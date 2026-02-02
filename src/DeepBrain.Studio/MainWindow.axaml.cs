using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DeepBrain.Studio.Net;
using DeepBrain.Shared.Brain;

namespace DeepBrain.Studio;

public partial class MainWindow : Window
{
    private readonly TcpClientService _tcp = new();
    private readonly ObservableCollection<string> _logs = new();
    private readonly ObservableCollection<string> _traces = new();
    private readonly ObservableCollection<string> _outputs = new();
    private bool _heartbeatOn;

    public MainWindow()
    {
        InitializeComponent();

        LogsList.ItemsSource = _logs;
        TraceList.ItemsSource = _traces;
        OutputsList.ItemsSource = _outputs;

        _tcp.OnLog += s => Ui(() => HandleLog(s));
        _tcp.OnInfo += s => Ui(() =>
        {
            StatusText.Text = s;
            AddLog(s);
        });

        ConnectBtn.Click += async (_, _) => await RunSafeAsync(async () =>
        {
            var host = HostBox.Text?.Trim() ?? "127.0.0.1";
            var port = int.TryParse(PortBox.Text, out var p) ? p : 5555;
            await _tcp.ConnectAsync(host, port);
            await Task.Delay(200);
            var pong = await _tcp.PingWithTimeoutAsync(1000);
            if (!pong)
            {
                StatusText.Text = "ping timeout";
                AddLog("ping timeout");
                return;
            }
            await _tcp.SubscribeLogsAsync();
            await _tcp.SubscribeStateAsync();
            await _tcp.SubscribeTraceAsync();
            await _tcp.SubscribeLifeStateAsync();
            await _tcp.SubscribeLifeOutputAsync();
        });

        DisconnectBtn.Click += async (_, _) => await RunSafeAsync(async () => await _tcp.DisconnectAsync());
        ClearBtn.Click += (_, _) =>
        {
            _logs.Clear();
            _traces.Clear();
            _outputs.Clear();
        };
        BrainStartBtn.IsEnabled = false;
        BrainStopBtn.IsEnabled = false;
        _tcp.OnState += (tick, uptime, mode, decision) => Ui(() =>
        {
            StatusText.Text = $"tick={tick}  uptime={uptime}ms  mode={mode}  decision={decision}";
        });
        BrainStartBtn.Click += async (_, _) => await RunSafeAsync(async () => await _tcp.BrainStartAsync());
        BrainStopBtn.Click += async (_, _) => await RunSafeAsync(async () => await _tcp.BrainStopAsync());

        _tcp.OnTrace += s => Ui(() => AddTrace(s));
        _tcp.OnLifeState += state => Ui(() => UpdateLife(state));
        _tcp.OnLifeOutput += output => Ui(() => AddOutput(output));
    }

    private void HandleLog(string text)
    {
        if (IsHeartbeat(text))
        {
            ToggleHeartbeat();
            return;
        }

        AddLog(text);
    }

    private void AddLog(string text)
    {
        if (_logs.Count > 2000) _logs.RemoveAt(0);
        _logs.Add(text);
    }

    private void AddTrace(string text)
    {
        while (_traces.Count >= 50) _traces.RemoveAt(0);
        _traces.Add(text);
    }

    private void AddOutput(LifeOutputDto output)
    {
        while (_outputs.Count >= 50) _outputs.RemoveAt(0);
        var prefix = output.ActionName == "selftalk"
            ? "selftalk"
            : output.ActionName;
        _outputs.Add($"[{output.Ts:HH:mm:ss}] {prefix}: {output.Message}");
    }

    private static bool IsHeartbeat(string text)
    {
        return text.Contains("heartbeat", StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleHeartbeat()
    {
        _heartbeatOn = !_heartbeatOn;
        HeartbeatText.Text = _heartbeatOn ? "❤" : "♡";
    }

    private void UpdateLife(LifeStateDto state)
    {
        LifeMoodText.Text = $"mood={state.Affect.Mood} valence={state.Affect.Valence:0.00} arousal={state.Affect.Arousal:0.00}";
        LifeHomeostasisText.Text = $"energy={state.Homeostasis.Energy:0.00} fatigue={state.Homeostasis.Fatigue:0.00} safety={state.Homeostasis.Safety:0.00} pain={state.Homeostasis.Pain:0.00}";
        LifeInstinctsText.Text = $"instincts: self={state.Instincts.SelfPreservation:0.00} energy={state.Instincts.EnergyConservation:0.00} explore={state.Instincts.Exploration:0.00} attach={state.Instincts.Attachment:0.00} agency={state.Instincts.Agency:0.00}";
        LifeDecisionText.Text = $"lastDecision={state.LastDecision}";
        LifeRewardText.Text = $"lastReward={state.LastReward:0.000}  tick={state.Tick}";
        var policy = state.Policy;
        LifeStrategyText.Text = $"strategy={policy?.Strategy ?? "n/a"} reason={policy?.Reason ?? ""}";
        LifeDriveText.Text = $"drive={state.DominantDrive}";
        LifeLoopText.Text = $"loopPenalty={policy?.LoopPenalty:0.00} streak={policy?.SameActionStreak} loops={policy?.LoopCount}";
        LifeAvgRewardText.Text = $"avgRewardShort={policy?.AvgRewardShort:0.000}";
        LifeInertiaText.Text = $"moodInertia={state.MoodInertia:0.00}";
        if (state.Circadian is not null)
        {
            LifeCircadianText.Text = $"circadian={state.Circadian.Phase} tod={state.Circadian.TimeOfDay:0.00} sleep={state.Circadian.IsSleeping} pressure={state.Circadian.SleepPressure:0.00}";
        }
        else
        {
            LifeCircadianText.Text = "circadian=n/a";
        }

        if (state.ActivePlan is not null)
        {
            LifePlanText.Text = $"plan={state.ActivePlan.Strategy}/{state.ActivePlan.GoalId} ttl={state.ActivePlan.RemainingTicks}";
        }
        else
        {
            LifePlanText.Text = "plan=n/a";
        }

        if (state.Goals is not null)
        {
            var parts = state.Goals
                .Take(4)
                .Select(g => $"{g.Id}:{g.Urgency:0.00}/{g.Satisfaction:0.00}")
                .ToArray();
            LifeGoalsText.Text = $"goals={string.Join(", ", parts)}";
        }
        else
        {
            LifeGoalsText.Text = "goals=n/a";
        }

        if (state.Attention is not null)
        {
            var focus2 = string.IsNullOrWhiteSpace(state.Attention.Focus2) ? "" : $"/{state.Attention.Focus2}";
            LifeAttentionText.Text = $"attention={state.Attention.Focus1}{focus2} intensity={state.Attention.Intensity:0.00} reason={state.Attention.Reason}";
        }
        else
        {
            LifeAttentionText.Text = "attention=n/a";
        }

        if (state.RecentEvents is not null && state.RecentEvents.Count > 0)
        {
            var eventsLine = state.RecentEvents
                .TakeLast(5)
                .Select(e => $"{e.Type}:{e.Severity:0.00} {e.Payload}")
                .ToArray();
            LifeEventsText.Text = $"events={string.Join(" | ", eventsLine)}";
        }
        else
        {
            LifeEventsText.Text = "events=n/a";
        }

        if (state.SemanticNotesTop is not null && state.SemanticNotesTop.Count > 0)
        {
            var notesLine = state.SemanticNotesTop
                .Select(n => $"{n.Key}->{n.BestAction} {n.Score:0.00} ({n.Samples})")
                .ToArray();
            LifeSemanticText.Text = $"semantic={string.Join(" | ", notesLine)}";
        }
        else
        {
            LifeSemanticText.Text = "semantic=n/a";
        }
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"error: {ex.Message}";
            AddLog($"error: {ex.Message}");
        }
    }

    private static void Ui(Action a) => Dispatcher.UIThread.Post(a);
}
