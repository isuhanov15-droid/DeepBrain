using System;
using System.Collections.ObjectModel;
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
    private bool _heartbeatOn;

    public MainWindow()
    {
        InitializeComponent();

        LogsList.ItemsSource = _logs;
        TraceList.ItemsSource = _traces;

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
        });

        DisconnectBtn.Click += async (_, _) => await RunSafeAsync(async () => await _tcp.DisconnectAsync());
        ClearBtn.Click += (_, _) =>
        {
            _logs.Clear();
            _traces.Clear();
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
