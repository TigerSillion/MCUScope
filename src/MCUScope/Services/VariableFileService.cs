using MCUScope.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCUScope.Services
{
    /// <summary>
    /// Parses variable information files (map files, symbol files, etc.)
    /// Supports multiple formats used by Renesas compilers and common map file formats.
    /// </summary>
    public static class VariableFileService
    {
        public static List<VariableInfo> LoadVariableFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".csv" => LoadCsvFormat(filePath),
                ".map" => LoadMapFormat(filePath),
                ".sym" => LoadSymFormat(filePath),
                ".xml" => LoadXmlFormat(filePath),
                _ => LoadAutoDetect(filePath)
            };
        }

        private static List<VariableInfo> LoadCsvFormat(string filePath)
        {
            var variables = new List<VariableInfo>();
            var lines = File.ReadAllLines(filePath);

            bool headerFound = false;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (!headerFound)
                {
                    // Skip header line
                    if (parts.Any(p => p.Trim().Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                                      p.Trim().Equals("Address", StringComparison.OrdinalIgnoreCase)))
                    {
                        headerFound = true;
                        continue;
                    }
                    headerFound = true;
                }

                if (parts.Length >= 3)
                {
                    var name = parts[0].Trim().Trim('"');
                    if (string.IsNullOrEmpty(name)) continue;

                    uint address = ParseAddress(parts[1].Trim().Trim('"'));
                    var type = ParseVariableType(parts[2].Trim().Trim('"'));

                    var variable = new VariableInfo
                    {
                        Name = name,
                        Address = address,
                        OriginalType = type,
                        ModifiedType = type,
                        DeclaredSize = type switch
                        {
                            VariableType.UInt8 or VariableType.Int8 or VariableType.Bool or VariableType.Logic => 1,
                            VariableType.UInt16 or VariableType.Int16 => 2,
                            VariableType.UInt32 or VariableType.Int32 or VariableType.Float32 => 4,
                            _ => 4
                        },
                        Scale = parts.Length > 3 && double.TryParse(parts[3].Trim(), out var s) ? s : 1.0
                    };
                    variables.Add(variable);
                }
            }

            return variables;
        }

        // Cached compiled regex for Renesas MAP format
        // Symbol name line: leading spaces + underscore + name (may contain dots for struct members)
        private static readonly Regex SymbolNameRx = new(
            @"^\s+_([A-Za-z][\w.]*)\s*$", RegexOptions.Compiled);

        // Metadata line for globals/locals: address(8hex) size(hex) "data" ,scope(g/l)
        private static readonly Regex DataMetaRx = new(
            @"^\s*([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]+)\s+data\s+,([gl])\s+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Metadata line for struct members with explicit type: address(8hex) size(hex) type_string
        private static readonly Regex TypedMetaRx = new(
            @"^\s*([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]+)\s+(float|double|unsigned\s+\w+|signed\s+\w+|char|short|int|long)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Fallback generic pattern
        private static readonly Regex GenericMapRx = new(
            @"^\s*([0-9a-fA-F]{4,8})\s+(_?\w+)\s+([0-9a-fA-F]+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Keil/ARM map execution-region row:
        // 0x2000000c   0x08005c64   0x00000004   Data   RW   25   .data.com_f4_speed_rate_limit_rpm  main.o
        private static readonly Regex KeilExecRowRx = new(
            @"^\s*0x([0-9A-Fa-f]{8})\s+(?:0x[0-9A-Fa-f]{8}|-)\s+0x([0-9A-Fa-f]+)\s+\w+\s+\w+\s+\d+\s+(\S+)\s+\S+\s*$",
            RegexOptions.Compiled);

        private static List<VariableInfo> LoadMapFormat(string filePath)
        {
            var variables = new List<VariableInfo>();
            var lines = File.ReadAllLines(filePath);
            var seen = new HashSet<(string, uint)>();

            int globalCount = 0, localCount = 0, structCount = 0;
            int keilCount = 0;

            // 1) Renesas CCRX symbol-list style parser.
            for (int i = 0; i < lines.Length; i++)
            {
                var symbolMatch = SymbolNameRx.Match(lines[i]);
                if (!symbolMatch.Success) continue;

                string symbolName = symbolMatch.Groups[1].Value;
                if (symbolName.StartsWith("__", StringComparison.Ordinal)) continue;
                if (IsInternalSymbolName(symbolName)) continue;

                bool isStructMember = symbolName.Contains('.');

                // Search next few lines for metadata
                for (int j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
                {
                    // Try "data ,g/l" format first (globals and locals)
                    var dataMeta = DataMetaRx.Match(lines[j]);
                    if (dataMeta.Success)
                    {
                        uint address = ParseAddress(dataMeta.Groups[1].Value);
                        if (!int.TryParse(dataMeta.Groups[2].Value, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out var size) || size <= 0)
                            break;

                        if (!seen.Add((symbolName, address))) break;

                        bool isGlobal = string.Equals(dataMeta.Groups[3].Value, "g",
                            StringComparison.OrdinalIgnoreCase);

                        // Use naming convention for type inference, fall back to size
                        var type = InferTypeFromName(symbolName) ?? SizeToType(size);
                        string category = CategorizeVariable(symbolName);

                        variables.Add(new VariableInfo
                        {
                            Name = symbolName,
                            Address = address,
                            OriginalType = type,
                            ModifiedType = type,
                            DeclaredSize = size,
                            IsGlobal = isGlobal,
                            Category = category
                        });

                        if (isGlobal) globalCount++; else localCount++;
                        break;
                    }

                    // Try typed format (struct members: "00006848   4   float")
                    var typedMeta = TypedMetaRx.Match(lines[j]);
                    if (typedMeta.Success)
                    {
                        uint address = ParseAddress(typedMeta.Groups[1].Value);
                        if (!int.TryParse(typedMeta.Groups[2].Value, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out var size) || size <= 0)
                            break;

                        if (!seen.Add((symbolName, address))) break;

                        // Use explicit type string from MAP
                        string typeStr = typedMeta.Groups[3].Value.Trim();
                        var type = ParseVariableType(typeStr);
                        string category = CategorizeVariable(symbolName);

                        variables.Add(new VariableInfo
                        {
                            Name = symbolName,
                            Address = address,
                            OriginalType = type,
                            ModifiedType = type,
                            DeclaredSize = size,
                            IsGlobal = false, // struct members are not independently global
                            Category = category
                        });

                        structCount++;
                        break;
                    }
                }
            }

            // 2) Keil/ARM execution-region table parser (STM32 maps).
            foreach (var line in lines)
            {
                var keil = KeilExecRowRx.Match(line);
                if (!keil.Success) continue;

                uint address = ParseAddress(keil.Groups[1].Value);
                if (!int.TryParse(keil.Groups[2].Value, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var size) || size <= 0)
                    continue;

                // Keep RAM variables only.
                if (address < 0x20000000U || address >= 0x60000000U)
                    continue;

                string sectionName = keil.Groups[3].Value;
                if (!TryExtractKeilVariableName(sectionName, out var symbolName))
                    continue;
                if (IsInternalSymbolName(symbolName))
                    continue;

                if (!seen.Add((symbolName, address)))
                    continue;

                var type = InferTypeFromName(symbolName) ?? SizeToType(size);
                variables.Add(new VariableInfo
                {
                    Name = symbolName,
                    Address = address,
                    OriginalType = type,
                    ModifiedType = type,
                    DeclaredSize = size,
                    IsGlobal = true,
                    Category = CategorizeVariable(symbolName)
                });
                keilCount++;
            }

            LogService.Info($"MAP loaded: {globalCount} globals, {structCount} struct members, " +
                $"{localCount} locals, {keilCount} keil = {variables.Count} total from {Path.GetFileName(filePath)}");

            if (variables.Count > 0)
                return variables;

            // Fallback for simpler generic map formats (reuse lines, no re-read)
            string content = string.Join("\n", lines);
            foreach (Match match in GenericMapRx.Matches(content))
            {
                uint address = ParseAddress(match.Groups[1].Value);
                string name = match.Groups[2].Value;
                if (name.StartsWith("__", StringComparison.Ordinal)) continue;
                if (IsInternalSymbolName(name)) continue;
                if (!int.TryParse(match.Groups[3].Value, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var size) || size <= 0) continue;

                // Strip single leading underscore only
                if (name.StartsWith("_") && !name.StartsWith("__"))
                    name = name[1..];

                if (!seen.Add((name, address))) continue;

                var type = InferTypeFromName(name) ?? SizeToType(size);
                variables.Add(new VariableInfo
                {
                    Name = name,
                    Address = address,
                    OriginalType = type,
                    ModifiedType = type,
                    DeclaredSize = size,
                    Category = CategorizeVariable(name)
                });
            }

            LogService.Info($"MAP fallback: {variables.Count} variables from {Path.GetFileName(filePath)}");
            return variables;
        }

        private static bool IsInternalSymbolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;
            if (name.StartsWith(".L_", StringComparison.Ordinal))
                return true;
            if (name.StartsWith("Region$$", StringComparison.Ordinal))
                return true;
            if (name.StartsWith("g_ics2", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.StartsWith("g_lpuart", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.StartsWith("hdma_", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.StartsWith("hlpuart", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("uwTick", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("SystemCoreClock", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static bool TryExtractKeilVariableName(string sectionName, out string symbolName)
        {
            symbolName = string.Empty;
            if (string.IsNullOrWhiteSpace(sectionName))
                return false;

            string[] prefixes = { ".data.", ".bss.", ".noinit.", ".zidata." };
            string candidate = sectionName;
            foreach (var prefix in prefixes)
            {
                if (candidate.StartsWith(prefix, StringComparison.Ordinal))
                {
                    candidate = candidate[prefix.Length..];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(candidate))
                return false;
            if (candidate.StartsWith(".", StringComparison.Ordinal)) // linker internals like .L_MergedGlobals
                return false;
            if (candidate.Equals("HEAP", StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals("STACK", StringComparison.OrdinalIgnoreCase))
                return false;
            if (candidate.StartsWith("Region$$", StringComparison.Ordinal))
                return false;

            symbolName = candidate;
            return true;
        }

        /// <summary>
        /// Infer variable type from Renesas naming convention.
        /// com_f4_ = float32, com_u1_ = uint8, com_u2_ = uint16, com_s2_ = int16, etc.
        /// Also handles g_ prefixed: g_f4_ = float, g_u1_ = uint8, etc.
        /// </summary>
        private static VariableType? InferTypeFromName(string name)
        {
            // Check for type markers in the name segments (split by _ or .)
            string lower = name.ToLowerInvariant();

            // Pattern: prefix_f4_ or .f4_ = float32
            if (lower.Contains("_f4_") || lower.Contains(".f4_") || lower.Contains("_f4."))
                return VariableType.Float32;
            if (lower.Contains("_u4_") || lower.Contains(".u4_"))
                return VariableType.UInt32;
            if (lower.Contains("_s4_") || lower.Contains(".s4_"))
                return VariableType.Int32;
            if (lower.Contains("_u2_") || lower.Contains(".u2_"))
                return VariableType.UInt16;
            if (lower.Contains("_s2_") || lower.Contains(".s2_"))
                return VariableType.Int16;
            if (lower.Contains("_u1_") || lower.Contains(".u1_"))
                return VariableType.UInt8;
            if (lower.Contains("_s1_") || lower.Contains(".s1_"))
                return VariableType.Int8;

            // Also check suffix patterns like f4_varname
            if (lower.StartsWith("f4_")) return VariableType.Float32;
            if (lower.StartsWith("u1_")) return VariableType.UInt8;
            if (lower.StartsWith("u2_")) return VariableType.UInt16;
            if (lower.StartsWith("u4_")) return VariableType.UInt32;
            if (lower.StartsWith("s1_")) return VariableType.Int8;
            if (lower.StartsWith("s2_")) return VariableType.Int16;
            if (lower.StartsWith("s4_")) return VariableType.Int32;

            return null;
        }

        /// <summary>
        /// Categorize variable by naming convention for filtering.
        /// </summary>
        private static string CategorizeVariable(string name)
        {
            if (name.Contains('.')) return "Struct";
            string lower = name.ToLowerInvariant();
            if (lower.StartsWith("com_")) return "COM";
            if (lower.StartsWith("g_st_")) return "Struct";
            if (lower.StartsWith("g_")) return "Global";
            if (lower.StartsWith("bsp_")) return "BSP";
            if (lower.StartsWith("r_")) return "Driver";
            return "Other";
        }

        private static List<VariableInfo> LoadSymFormat(string filePath)
        {
            var variables = new List<VariableInfo>();
            var lines = File.ReadAllLines(filePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    uint address = ParseAddress(parts[0]);
                    string name = parts[1].TrimStart('_');
                    int size = parts.Length > 2 ? int.Parse(parts[2]) : 4;

                    variables.Add(new VariableInfo
                    {
                        Name = name,
                        Address = address,
                        OriginalType = SizeToType(size),
                        ModifiedType = SizeToType(size),
                        DeclaredSize = size
                    });
                }
            }

            return variables;
        }

        private static List<VariableInfo> LoadXmlFormat(string filePath)
        {
            var variables = new List<VariableInfo>();
            try
            {
                var doc = System.Xml.Linq.XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null) return variables;

                foreach (var elem in root.Elements("Variable"))
                {
                    var name = elem.Attribute("Name")?.Value ?? elem.Element("Name")?.Value ?? "";
                    var addr = elem.Attribute("Address")?.Value ?? elem.Element("Address")?.Value ?? "0";
                    var typeStr = elem.Attribute("Type")?.Value ?? elem.Element("Type")?.Value ?? "Int32";
                    var scaleStr = elem.Attribute("Scale")?.Value ?? elem.Element("Scale")?.Value ?? "1.0";

                    if (string.IsNullOrEmpty(name)) continue;

                    variables.Add(new VariableInfo
                    {
                        Name = name,
                        Address = ParseAddress(addr),
                        OriginalType = ParseVariableType(typeStr),
                        ModifiedType = ParseVariableType(typeStr),
                        DeclaredSize = 0,
                        Scale = double.TryParse(scaleStr, CultureInfo.InvariantCulture, out var s) ? s : 1.0
                    });
                }
            }
            catch { }
            return variables;
        }

        private static List<VariableInfo> LoadAutoDetect(string filePath)
        {
            var firstLine = File.ReadLines(filePath).FirstOrDefault() ?? "";
            if (firstLine.Contains(',')) return LoadCsvFormat(filePath);
            if (firstLine.Contains("<?xml")) return LoadXmlFormat(filePath);
            return LoadMapFormat(filePath);
        }

        private static uint ParseAddress(string addr)
        {
            addr = addr.Trim();
            if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                addr = addr[2..];
            return uint.TryParse(addr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result)
                ? result : 0;
        }

        public static VariableType ParseVariableType(string typeStr)
        {
            var lower = typeStr.ToLowerInvariant().Trim();
            return lower switch
            {
                "uint8" or "unsigned char" or "u8" or "byte" => VariableType.UInt8,
                "int8" or "signed char" or "char" or "s8" or "sbyte" => VariableType.Int8,
                "uint16" or "unsigned short" or "u16" or "ushort" or "word" => VariableType.UInt16,
                "int16" or "signed short" or "short" or "s16" => VariableType.Int16,
                "uint32" or "unsigned int" or "u32" or "uint" or "dword" or "unsigned long" => VariableType.UInt32,
                "int32" or "signed int" or "int" or "s32" or "long" or "signed long" => VariableType.Int32,
                "float32" or "float" or "ieee754" or "single" => VariableType.Float32,
                "bool" or "boolean" => VariableType.Bool,
                "logic" => VariableType.Logic,
                _ when lower.Contains("float") => VariableType.Float32,
                _ when lower.Contains("unsigned") && lower.Contains("32") => VariableType.UInt32,
                _ when lower.Contains("unsigned") && lower.Contains("16") => VariableType.UInt16,
                _ when lower.Contains("unsigned") && lower.Contains("8") => VariableType.UInt8,
                _ when lower.Contains("32") => VariableType.Int32,
                _ when lower.Contains("16") => VariableType.Int16,
                _ when lower.Contains("8") => VariableType.Int8,
                _ => VariableType.Int32
            };
        }

        private static VariableType SizeToType(int byteSize) => byteSize switch
        {
            1 => VariableType.UInt8,
            2 => VariableType.UInt16,
            4 => VariableType.Float32, // Most MCU 4-byte vars are float in motor control
            _ => VariableType.UInt32
        };

        public static void SaveVariableFile(string filePath, List<VariableInfo> variables)
        {
            using var writer = new StreamWriter(filePath);
            writer.WriteLine("Name,Address,Type,Scale,Alias,Comment,ReadEnabled,WriteEnabled");
            foreach (var v in variables)
            {
                writer.WriteLine($"\"{v.Name}\",\"0x{v.Address:X8}\",\"{v.ModifiedType}\",{v.Scale}," +
                    $"\"{v.Alias}\",\"{v.Comment}\",{v.ReadEnabled},{v.WriteEnabled}");
            }
        }
    }
}
