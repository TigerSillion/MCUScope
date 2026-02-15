using MCUScope.Models;
using Newtonsoft.Json;
using System.IO;

namespace MCUScope.Services
{
    public static class ProjectFileService
    {
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static void SaveProject(string filePath, ProjectSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, JsonSettings);
            File.WriteAllText(filePath, json);
        }

        public static ProjectSettings LoadProject(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<ProjectSettings>(json) ?? new ProjectSettings();
        }

        public static void SaveChartData(string filePath, ChartDataFile chartData)
        {
            var json = JsonConvert.SerializeObject(chartData, JsonSettings);
            File.WriteAllText(filePath, json);
        }

        public static ChartDataFile LoadChartData(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<ChartDataFile>(json) ?? new ChartDataFile();
        }

        public static void SaveChartDataCsv(string csvPath, ChartDataFile chartData)
        {
            using var writer = new StreamWriter(csvPath);
            // Header: Time, CH1, CH2, ...
            writer.Write("Time");
            for (int i = 0; i < chartData.ChannelCount; i++)
                writer.Write($",CH{i + 1}");
            writer.WriteLine();

            // Data rows
            if (chartData.ChannelCount > 0 && chartData.Channels[0].Data.Length > 0)
            {
                int length = chartData.Channels[0].Data.Length;
                for (int s = 0; s < length; s++)
                {
                    double time = s * chartData.SamplePeriod;
                    writer.Write(time.ToString("G"));
                    for (int c = 0; c < chartData.ChannelCount; c++)
                    {
                        writer.Write(",");
                        if (s < chartData.Channels[c].Data.Length)
                            writer.Write(chartData.Channels[c].Data[s].ToString("G"));
                    }
                    writer.WriteLine();
                }
            }
        }
    }

    public class ChartDataFile
    {
        public double SamplePeriod { get; set; }
        public int ChannelCount => Channels.Count;
        public System.Collections.Generic.List<ChannelData> Channels { get; set; } = new();
    }

    public class ChannelData
    {
        public string Name { get; set; } = string.Empty;
        public double[] Data { get; set; } = System.Array.Empty<double>();
    }
}
