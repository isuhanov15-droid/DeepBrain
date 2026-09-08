using System;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace DeepBrain.Host.BrainLife;

public sealed class EpisodeReportWriter
{
    private readonly Channel<EpisodeReport> _channel = Channel.CreateUnbounded<EpisodeReport>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = false
    };

    private readonly Task _worker;

    public EpisodeReportWriter()
    {
        _worker = Task.Run(ProcessAsync);
    }

    public void Write(EpisodeReport report)
    {
        _channel.Writer.TryWrite(report);
    }

    public Task CompleteAsync()
    {
        _channel.Writer.TryComplete();
        return _worker;
    }

    private async Task ProcessAsync()
    {
        await foreach (var report in _channel.Reader.ReadAllAsync())
        {
            var date = report.EndTs.ToString("yyyy-MM-dd");
            var dir = Path.Combine("reports", "episodes", date);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "episodes.jsonl");
            var json = JsonSerializer.Serialize(report, _options);
            await File.AppendAllTextAsync(path, json + Environment.NewLine);
        }
    }
}
