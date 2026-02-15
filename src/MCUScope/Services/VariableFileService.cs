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
                        Scale = parts.Length > 3 && double.TryParse(parts[3].Trim(), out var s) ? s : 1.0
                    };
                    variables.Add(variable);
                }
            }

            return variables;
        }

        private static List<VariableInfo> LoadMapFormat(string filePath)
        {
            var variables = new List<VariableInfo>();
            var content = File.ReadAllText(filePath);

            // Match patterns like: address name size
            var pattern = new Regex(@"^[\s]*([0-9a-fA-F]{4,8})[\s]+(_?\w+)[\s]+([0-9a-fA-F]+)",
                RegexOptions.Multiline);

            foreach (Match match in pattern.Matches(content))
            {
                uint address = ParseAddress(match.Groups[1].Value);
                string name = match.Groups[2].Value;
                int size = int.Parse(match.Groups[3].Value, NumberStyles.HexNumber);

                // Skip compiler-generated symbols
                if (name.StartsWith("__")) continue;

                var type = SizeToType(size);
                variables.Add(new VariableInfo
                {
                    Name = name.TrimStart('_'),
                    Address = address,
                    OriginalType = type,
                    ModifiedType = type
                });
            }

            return variables;
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
                        ModifiedType = SizeToType(size)
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
            2 => VariableType.Int16,
            4 => VariableType.Int32,
            _ => VariableType.Int32
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
