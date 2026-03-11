using System;
using System.IO;
using System.Text.Json;

namespace DeepBrain.Host.BrainLife;

public sealed class EpisodeReportWriter
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = false
    };

    public void Write(EpisodeReport report)
    {
        var date = report.EndTs.ToString("yyyy-MM-dd");
        var dir = Path.Combine("reports", "episodes", date);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "episodes.jsonl");
        var json = JsonSerializer.Serialize(report, _options);
        File.AppendAllText(path, json + Environment.NewLine);
    }
}
