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

        // ── GCC/arm-none-eabi-gcc MAP patterns ─────────────────────────────────────
        // Section line: " .data.varName" or " .bss.varName" with no address yet
        private static readonly Regex GccSectionOnlyRx = new(
            @"^\s+\.(data|bss|noinit|ccmram|dtcmram|sram2)\.([\w$]+)\s*$",
            RegexOptions.Compiled);

        // Section line WITH address+size inline (single-line variant):
        // " .bss.varName    0x20000100    0x4    build/foo.o"
        private static readonly Regex GccSectionInlineRx = new(
            @"^\s+\.(data|bss|noinit|ccmram|dtcmram|sram2)\.([\w$]+)\s+(0x[0-9A-Fa-f]+)\s+(0x[0-9A-Fa-f]+)\s+\S",
            RegexOptions.Compiled);

        // Address+size continuation line (follows GccSectionOnlyRx or GccSectionInlineRx):
        // "                0x0000000020000004        0x4 build/Core/Src/foo.o"
        private static readonly Regex GccAddrSizeLine = new(
            @"^\s+(0x[0-9A-Fa-f]+)\s+(0x[0-9A-Fa-f]+)\s+\S",
            RegexOptions.Compiled);

        // Optional symbol-name confirmation line:
        // "                0x0000000020000004                varName"
        private static readonly Regex GccSymbolLine = new(
            @"^\s+(0x[0-9A-Fa-f]+)\s+([A-Za-z_][\w$]*)\s*$",
            RegexOptions.Compiled);

        // ── Renesas CCRX MAP patterns ───────────────────────────────────────────
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

        // Keil/ARM MDK execution-region row — supports BOTH 6-column and 7-column layouts:
        //   6-col (standard MDK):   0x20000000  0x00000004  Data  RW  1  .data.varName  main.o
        //   7-col (load+exec addr): 0x20000000  0x08005c64  0x00000004  Data  RW  25  .data.varName  main.o
        // The optional second address column is the flash load address and is consumed non-greedily;
        // regex backtracks correctly so size is always captured in group 2.
        private static readonly Regex KeilExecRowRx = new(
            @"^\s*0x([0-9A-Fa-f]{8})\s+(?:0x[0-9A-Fa-f]{8}\s+)?0x([0-9A-Fa-f]+)\s+Data\s+\w+\s+\d+\s+(\S+)\s+\S+\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Keil image symbol table row:
        // com_u1_system_mode  0x20000070  Data  1  main.o(.bss..L_MergedGlobals)
        private static readonly Regex KeilSymbolRowRx = new(
            @"^\s*(\S+)\s+0x([0-9A-Fa-f]{8})\s+(\w+)\s+([0-9A-Fa-f]+)\s+(.+)$",
            RegexOptions.Compiled);

        /// <summary>
        /// Parse GCC/arm-none-eabi-gcc linker MAP files (STM32, LPC, nRF, etc.).
        /// Extracts RAM-resident variables from .data and .bss sections.
        /// </summary>
        private static List<VariableInfo> ParseGccMap(string[] lines, HashSet<(string, uint)> seen)
        {
            var result = new List<VariableInfo>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // ── Try single-line format first ──────────────────────────────
                // " .bss.varName    0x20000100    0x4    build/foo.o"
                var inlineM = GccSectionInlineRx.Match(line);
                if (inlineM.Success)
                {
                    string varName = inlineM.Groups[2].Value;
                    uint address = GccParseHex(inlineM.Groups[3].Value);
                    int size = (int)GccParseHex(inlineM.Groups[4].Value);

                    if (IsValidGccRamVar(varName, address, size, seen))
                        result.Add(MakeGccVar(varName, address, size));
                    continue;
                }

                // ── Multi-line format ─────────────────────────────────────────
                // Line 1: " .data.varName"
                var sectionM = GccSectionOnlyRx.Match(line);
                if (!sectionM.Success) continue;

                string candidateName = sectionM.Groups[2].Value;
                if (string.IsNullOrEmpty(candidateName)) continue;

                // Line 2: "                0x20000100    0x4    build/foo.o"
                if (i + 1 >= lines.Length) continue;
                var addrM = GccAddrSizeLine.Match(lines[i + 1]);
                if (!addrM.Success) continue;

                uint addr2 = GccParseHex(addrM.Groups[1].Value);
                int size2 = (int)GccParseHex(addrM.Groups[2].Value);

                // Optional Line 3: "                0x20000100    varName"  (symbol confirmation)
                // If present, prefer the confirmed name from that line.
                string finalName = candidateName;
                if (i + 2 < lines.Length)
                {
                    var symM = GccSymbolLine.Match(lines[i + 2]);
                    if (symM.Success && symM.Groups[2].Value.Length > 0)
                        finalName = symM.Groups[2].Value;
                }

                if (IsValidGccRamVar(finalName, addr2, size2, seen))
                    result.Add(MakeGccVar(finalName, addr2, size2));
            }

            return result;
        }

        private static bool IsValidGccRamVar(string name, uint address, int size,
            HashSet<(string, uint)> seen)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (size <= 0 || size > 4096) return false;                         // skip arrays
            if (address < 0x20000000U || address >= 0x60000000U) return false;  // RAM only
            if (IsInternalSymbolName(name)) return false;
            // Skip very short or purely numeric names
            if (name.Length < 2) return false;
            return seen.Add((name, address));
        }

        private static VariableInfo MakeGccVar(string name, uint address, int size)
        {
            var type = InferTypeFromName(name) ?? InferTypeFromSize(name, size);
            return new VariableInfo
            {
                Name = name,
                Address = address,
                OriginalType = type,
                ModifiedType = type,
                DeclaredSize = size,
                IsGlobal = true,
                Category = CategorizeVariable(name)
            };
        }

        // For GCC, when no naming convention hint exists, use size + heuristics
        private static VariableType InferTypeFromSize(string name, int size)
        {
            string lower = name.ToLowerInvariant();
            // Names ending in common float-related words → float
            if (size == 4 && (lower.Contains("rpm") || lower.Contains("current") ||
                lower.Contains("voltage") || lower.Contains("speed") || lower.Contains("torque") ||
                lower.Contains("flux") || lower.Contains("angle") || lower.Contains("freq") ||
                lower.Contains("gain") || lower.Contains("ref") || lower.Contains("err") ||
                lower.Contains("ratio") || lower.Contains("duty") || lower.Contains("power") ||
                lower.Contains("temp") || lower.Contains("coeff") || lower.Contains("scale") ||
                lower.Contains("limit") || lower.Contains("omega") || lower.Contains("zeta") ||
                lower.Contains("vbus") || lower.Contains("vdc") || lower.Contains("param")))
                return VariableType.Float32;
            return size switch
            {
                1 => VariableType.UInt8,
                2 => VariableType.Int16,
                4 => VariableType.Float32,  // Most 4-byte MCU vars are float in motor control
                _ => VariableType.UInt32
            };
        }

        private static uint GccParseHex(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static bool IsGccMapFile(string[] lines)
        {
            // GCC MAP files contain "Linker script and memory map" and GNU LD signature lines
            int checked_ = 0;
            foreach (var line in lines)
            {
                if (line.Contains("Linker script and memory map")) return true;
                if (line.Contains("arm-none-eabi")) return true;
                if (line.Contains("GNU ld") || line.Contains("GNU Binutils")) return true;
                if (++checked_ > 30) break;
            }
            return false;
        }

        private static List<VariableInfo> LoadMapFormat(string filePath)
        {
            var variables = new List<VariableInfo>();
            var lines = File.ReadAllLines(filePath);
            var seen = new HashSet<(string, uint)>();

            int globalCount = 0, localCount = 0, structCount = 0;
            int keilCount = 0, keilSymbolCount = 0;

            // 0) Try GCC/arm-none-eabi format FIRST if the file looks like a GNU LD map.
            if (IsGccMapFile(lines))
            {
                var gccVars = ParseGccMap(lines, seen);
                if (gccVars.Count > 0)
                {
                    LogService.Info($"GCC MAP: {gccVars.Count} RAM variables from {Path.GetFileName(filePath)}");
                    return gccVars;
                }
            }

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

            // 3) Keil image symbol table parser.
            foreach (var line in lines)
            {
                var row = KeilSymbolRowRx.Match(line);
                if (!row.Success)
                    continue;

                string symbolName = row.Groups[1].Value;
                if (symbolName.StartsWith("[", StringComparison.Ordinal)) // [Anonymous Symbol]
                    continue;
                if (symbolName.Contains("$$", StringComparison.Ordinal)) // linker/system symbols
                    continue;
                if (!uint.TryParse(row.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address))
                    continue;
                if (address < 0x20000000U || address >= 0x60000000U) // RAM data only
                    continue;

                // Keep function-static/compiler-local symbols out of GUI lists.
                // For Keil maps, names containing '.' are almost always internal locals.
                if (symbolName.Contains('.', StringComparison.Ordinal))
                    continue;
                if (IsInternalSymbolName(symbolName))
                    continue;

                string ovType = row.Groups[3].Value;
                if (!ovType.Equals("Data", StringComparison.OrdinalIgnoreCase))
                    continue;

                string sizeToken = row.Groups[4].Value;
                int size = 0;
                if (!int.TryParse(sizeToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                {
                    if (!int.TryParse(sizeToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size))
                        continue;
                }
                if (size <= 0)
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
                keilSymbolCount++;
            }

            // 4) Add known synthetic struct members when Keil map only exposes base struct symbols.
            int syntheticCount = AddKnownSyntheticMembers(variables, seen);

            LogService.Info($"MAP loaded: {globalCount} globals, {structCount} struct members, " +
                $"{localCount} locals, {keilCount} keil exec, {keilSymbolCount} keil symbol, {syntheticCount} synthetic = {variables.Count} total from {Path.GetFileName(filePath)}");

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

        private static int AddKnownSyntheticMembers(List<VariableInfo> variables, HashSet<(string, uint)> seen)
        {
            int added = 0;

            VariableInfo? sensorlessBase = variables.FirstOrDefault(v =>
                v.Name.Equals("g_st_sensorless_vector", StringComparison.Ordinal));
            if (sensorlessBase != null)
            {
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.f4_vdc_ad", sensorlessBase.Address + 0x00U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.f4_iu_ad", sensorlessBase.Address + 0x04U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.f4_iv_ad", sensorlessBase.Address + 0x08U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.f4_iw_ad", sensorlessBase.Address + 0x0CU, VariableType.Float32, 4);

                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_speed_output.f4_speed_rad_lpf", sensorlessBase.Address + 0x10U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_speed_output.f4_ref_speed_rad_ctrl", sensorlessBase.Address + 0x14U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_speed_output.f4_id_ref", sensorlessBase.Address + 0x18U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_speed_output.f4_iq_ref", sensorlessBase.Address + 0x1CU, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_speed_output.f4_speed_err_rad", sensorlessBase.Address + 0x20U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_speed_output.f4_torque_est_nm", sensorlessBase.Address + 0x24U, VariableType.Float32, 4);

                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.u1_flag_offset_calc", sensorlessBase.Address + 0x28U, VariableType.UInt8, 1);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.u1_flag_charge_bootstrap", sensorlessBase.Address + 0x29U, VariableType.UInt8, 1);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_ref_id_ctrl", sensorlessBase.Address + 0x2CU, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_speed_rad", sensorlessBase.Address + 0x30U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_ed", sensorlessBase.Address + 0x34U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_eq", sensorlessBase.Address + 0x38U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_phase_err_rad", sensorlessBase.Address + 0x3CU, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_bus_current_a", sensorlessBase.Address + 0x40U, VariableType.Float32, 4);
                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_current_output.f4_bus_power_w", sensorlessBase.Address + 0x44U, VariableType.Float32, 4);

                added += AddSyntheticMember(variables, seen, "g_st_sensorless_vector.st_stm.u1_status", sensorlessBase.Address + 0x48U, VariableType.UInt8, 1);
            }

            return added;
        }

        private static int AddSyntheticMember(
            List<VariableInfo> variables,
            HashSet<(string, uint)> seen,
            string name,
            uint address,
            VariableType type,
            int declaredSize)
        {
            if (!seen.Add((name, address)))
                return 0;

            variables.Add(new VariableInfo
            {
                Name = name,
                Address = address,
                OriginalType = type,
                ModifiedType = type,
                DeclaredSize = declaredSize,
                IsGlobal = false,
                Category = "Struct"
            });
            return 1;
        }

        // HAL/BSP/RTOS symbol prefixes to exclude from variable list
        private static readonly string[] _internalPrefixes = {
            ".L_", "Region$$", "HEAP", "STACK",
            "__", "$",                               // compiler-generated
            "g_ics2", "g_lpuart",                   // ICS/UART HAL internals
            "hdma_", "hlpuart", "huart", "hspi", "hi2c", "htim", "hadc", "hcan",
            "hiwdg", "hrng",                        // STM32 HAL handles
            "LL_Init", "HAL_",                      // HAL function pointers sometimes appear
        };

        private static readonly string[] _internalExactNames = {
            "uwTick", "SystemCoreClock", "uwTickPrio",
            "uwTickFreq", "AHBPrescTable", "APBPrescTable",
        };

        private static bool IsInternalSymbolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            foreach (var prefix in _internalPrefixes)
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var exact in _internalExactNames)
                if (name.Equals(exact, StringComparison.OrdinalIgnoreCase)) return true;
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
            // Renesas conventions
            if (lower.StartsWith("com_")) return "COM";
            if (lower.StartsWith("g_st_")) return "Struct";
            if (lower.StartsWith("g_")) return "Global";
            if (lower.StartsWith("bsp_")) return "BSP";
            if (lower.StartsWith("r_")) return "Driver";
            // STM32/GCC conventions
            if (lower.StartsWith("foc_")) return "FOC";
            if (lower.StartsWith("motor_")) return "Motor";
            if (lower.StartsWith("pid_")) return "PID";
            if (lower.StartsWith("hal_")) return "HAL";
            if (lower.StartsWith("h") && (lower.StartsWith("huart") || lower.StartsWith("hspi") ||
                lower.StartsWith("hi2c") || lower.StartsWith("htim") || lower.StartsWith("hadc")))
                return "HAL";
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
