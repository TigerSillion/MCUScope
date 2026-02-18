namespace MCUScope.Models
{
    public enum VariableType
    {
        UInt8,
        Int8,
        UInt16,
        Int16,
        UInt32,
        Int32,
        Float32,
        Bool,
        Logic
    }

    public class VariableInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;
        public uint Address { get; set; }
        public VariableType OriginalType { get; set; }
        public VariableType ModifiedType { get; set; }
        public double Scale { get; set; } = 1.0;
        public bool ReadEnabled { get; set; } = true;
        public bool WriteEnabled { get; set; }
        public string Comment { get; set; } = string.Empty;
        public bool IsGlobal { get; set; } = true;
        public string Category { get; set; } = string.Empty;
        public int DeclaredSize { get; set; }

        public string DisplayName => string.IsNullOrEmpty(Alias) ? Name : Alias;

        public int ByteSize => ModifiedType switch
        {
            VariableType.UInt8 or VariableType.Int8 or VariableType.Bool or VariableType.Logic => 1,
            VariableType.UInt16 or VariableType.Int16 => 2,
            VariableType.UInt32 or VariableType.Int32 or VariableType.Float32 => 4,
            _ => 4
        };

        public int EffectiveSize => DeclaredSize > 0 ? DeclaredSize : ByteSize;

        public bool IsProtocolScalar => EffectiveSize is 1 or 2 or 4;

        public bool IsLikelyInternal =>
            Name.StartsWith(".L_", System.StringComparison.Ordinal) ||
            Name.StartsWith("Region$$", System.StringComparison.Ordinal) ||
            Name.StartsWith("g_ics2", System.StringComparison.OrdinalIgnoreCase) ||
            Name.StartsWith("g_lpuart", System.StringComparison.OrdinalIgnoreCase) ||
            Name.StartsWith("hdma_", System.StringComparison.OrdinalIgnoreCase) ||
            Name.StartsWith("hlpuart", System.StringComparison.OrdinalIgnoreCase) ||
            Name.Equals("uwTick", System.StringComparison.OrdinalIgnoreCase) ||
            Name.Equals("SystemCoreClock", System.StringComparison.OrdinalIgnoreCase);

        public VariableInfo Clone()
        {
            return new VariableInfo
            {
                Name = Name,
                Alias = Alias,
                Address = Address,
                OriginalType = OriginalType,
                ModifiedType = ModifiedType,
                Scale = Scale,
                ReadEnabled = ReadEnabled,
                WriteEnabled = WriteEnabled,
                Comment = Comment
                ,
                DeclaredSize = DeclaredSize
            };
        }
    }
}
