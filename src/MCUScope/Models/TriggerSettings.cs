namespace MCUScope.Models
{
    public enum TriggerMode
    {
        Auto,
        Single,
        Normal
    }

    public enum TriggerEdge
    {
        Rise,
        Fall,
        Both
    }

    public enum TriggerSource
    {
        EXT,
        CH1, CH2, CH3, CH4, CH5, CH6, CH7, CH8, CH9, CH10, CH11, CH12
    }

    public class TriggerSettings
    {
        public double Position { get; set; }    // percent 0-100
        public double Level { get; set; }
        public TriggerSource Source { get; set; } = TriggerSource.EXT;
        public TriggerMode Mode { get; set; } = TriggerMode.Single;
        public TriggerEdge Edge { get; set; } = TriggerEdge.Rise;
    }
}
