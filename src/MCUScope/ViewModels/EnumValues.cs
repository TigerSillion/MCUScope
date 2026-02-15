using MCUScope.Models;
using System;

namespace MCUScope.ViewModels
{
    public static class EnumValues
    {
        public static TriggerSource[] TriggerSources { get; } = (TriggerSource[])Enum.GetValues(typeof(TriggerSource));
        public static TriggerMode[] TriggerModes { get; } = (TriggerMode[])Enum.GetValues(typeof(TriggerMode));
        public static TriggerEdge[] TriggerEdges { get; } = (TriggerEdge[])Enum.GetValues(typeof(TriggerEdge));
        public static VariableType[] VariableTypes { get; } = (VariableType[])Enum.GetValues(typeof(VariableType));
    }
}
