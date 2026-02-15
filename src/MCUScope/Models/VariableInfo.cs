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

        public string DisplayName => string.IsNullOrEmpty(Alias) ? Name : Alias;

        public int ByteSize => ModifiedType switch
        {
            VariableType.UInt8 or VariableType.Int8 or VariableType.Bool or VariableType.Logic => 1,
            VariableType.UInt16 or VariableType.Int16 => 2,
            VariableType.UInt32 or VariableType.Int32 or VariableType.Float32 => 4,
            _ => 4
        };

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
            };
        }
    }
}
