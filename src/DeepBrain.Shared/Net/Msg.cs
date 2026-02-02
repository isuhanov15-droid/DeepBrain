namespace DeepBrain.Shared.Net;

public static class Msg
{
    public const string Ping = "ping";
    public const string Pong = "pong";

    public const string LogsSubscribe = "logs.subscribe";
    public const string LogAppend = "log.append";

    public const string BrainStateSubscribe = "brain.state.subscribe";
    public const string BrainState = "brain.state";
    public const string BrainStart = "brain.start";
    public const string BrainStop = "brain.stop";
    public const string BrainStep = "brain.step";
    public const string TraceSubscribe = "trace.subscribe";
    public const string TraceAppend = "trace.append";
    public const string InputSet = "input.set";
    public const string InputGet = "input.get";          // опционально
    public const string InputSnapshot = "input.snapshot"; // опционально
    public const string EventPush = "event.push";


}
