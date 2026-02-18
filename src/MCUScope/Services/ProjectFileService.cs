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
            // Header: Time, channel names
            writer.Write("Time");
            for (int i = 0; i < chartData.ChannelCount; i++)
            {
                string name = !string.IsNullOrEmpty(chartData.Channels[i].Name)
                    ? chartData.Channels[i].Name : $"CH{i + 1}";
                writer.Write($",{name}");
            }
            writer.WriteLine();

            // Find max data length across all channels
            int maxLength = 0;
            for (int c = 0; c < chartData.ChannelCount; c++)
                maxLength = System.Math.Max(maxLength, chartData.Channels[c].Data.Length);

            // Data rows - pad shorter channels with NaN
            for (int s = 0; s < maxLength; s++)
            {
                double time = s * chartData.SamplePeriod;
                writer.Write(time.ToString("G"));
                for (int c = 0; c < chartData.ChannelCount; c++)
                {
                    writer.Write(",");
                    if (s < chartData.Channels[c].Data.Length)
                        writer.Write(chartData.Channels[c].Data[s].ToString("G"));
                    else
                        writer.Write("NaN");
                }
                writer.WriteLine();
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
