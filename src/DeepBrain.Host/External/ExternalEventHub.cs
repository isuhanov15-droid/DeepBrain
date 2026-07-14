using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using DeepBrain.Host.Cognition;
using DeepBrain.Shared.Brain;

namespace DeepBrain.Host.External;

public static class ExternalContract
{
    public const string Version = "deepbrain.external.v1";
}

public sealed record ExternalEventEnvelope(
    string ContractVersion,
    long Sequence,
    string Type,
    DateTimeOffset Timestamp,
    long? Tick,
    JsonElement Payload
);

// Future MQTT, Home Assistant and ROS2 bridges implement this contract. The
// core event hub deliberately exposes observations, not unrestricted actions.
public interface IExternalOutputAdapter : IAsyncDisposable
{
    string Name { get; }
    ValueTask PublishAsync(ExternalEventEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class ExternalEventSubscription : IDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    internal ExternalEventSubscription(ChannelReader<ExternalEventEnvelope> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<ExternalEventEnvelope> Reader { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _dispose();
    }
}

public sealed class ExternalEventHub : IDisposable
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<long, Channel<ExternalEventEnvelope>> _subscribers = new();
    private readonly object _latestSync = new();
    private long _nextSubscriberId;
    private long _sequence;
    private LifeStateDto? _latestState;
    private LifeOutputDto? _latestOutput;
    private CognitiveInsightEnvelope? _latestInsight;
    private int _disposed;

    public void PublishState(LifeStateDto state, int publishEveryTicks)
    {
        lock (_latestSync)
            _latestState = state;

        var interval = Math.Clamp(publishEveryTicks, 1, 10_000);
        if (state.Tick % interval == 0)
            Publish("state", state.Tick, state);
    }

    public void PublishOutput(LifeOutputDto output)
    {
        lock (_latestSync)
            _latestOutput = output;
        Publish("output", output.Tick, output);
    }

    public void PublishInsight(CognitiveInsightEnvelope insight)
    {
        lock (_latestSync)
            _latestInsight = insight;
        Publish("cortex.insight", insight.AcceptedAtTick, insight);
    }

    public LifeStateDto? GetLatestState()
    {
        lock (_latestSync)
            return _latestState;
    }

    public LifeOutputDto? GetLatestOutput()
    {
        lock (_latestSync)
            return _latestOutput;
    }

    public CognitiveInsightEnvelope? GetLatestInsight()
    {
        lock (_latestSync)
            return _latestInsight;
    }

    public ExternalEventSubscription Subscribe(int capacity)
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(ExternalEventHub));
        var boundedCapacity = Math.Clamp(capacity, 8, 4096);
        var channel = Channel.CreateBounded<ExternalEventEnvelope>(new BoundedChannelOptions(boundedCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
        var id = Interlocked.Increment(ref _nextSubscriberId);
        _subscribers[id] = channel;
        return new ExternalEventSubscription(channel.Reader, () => Remove(id));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var id in _subscribers.Keys)
            Remove(id);
    }

    private void Publish(string type, long? tick, object payload)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;

        var envelope = new ExternalEventEnvelope(
            ExternalContract.Version,
            Interlocked.Increment(ref _sequence),
            type,
            DateTimeOffset.UtcNow,
            tick,
            JsonSerializer.SerializeToElement(payload, PayloadJsonOptions));

        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(envelope);
    }

    private void Remove(long id)
    {
        if (_subscribers.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }
}
