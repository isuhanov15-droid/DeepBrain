using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using DeepBrain.Studio.Net;

namespace DeepBrain.Studio;

public partial class MainWindow : Window
{
    private readonly TcpClientService _tcp = new();
    private readonly ObservableCollection<string> _logs = new();

    public MainWindow()
    {
        InitializeComponent();

        LogsList.ItemsSource = _logs;

        _tcp.OnLog += s => Ui(() => AddLog(s));
        _tcp.OnInfo += s => Ui(() => StatusText.Text = s);

        ConnectBtn.Click += async (_, _) =>
        {
            var host = HostBox.Text?.Trim() ?? "127.0.0.1";
            var port = int.TryParse(PortBox.Text, out var p) ? p : 5555;
            await _tcp.ConnectAsync(host, port);
        };

        PingBtn.Click += async (_, _) => await _tcp.PingAsync();
        SubLogsBtn.Click += async (_, _) => await _tcp.SubscribeLogsAsync();

        ClearBtn.Click += (_, _) => _logs.Clear();
    }

    private void AddLog(string text)
    {
        // кольцевой буфер, чтобы UI не умер
        if (_logs.Count > 2000) _logs.RemoveAt(0);
        _logs.Add(text);
    }

    private static void Ui(Action a) => Dispatcher.UIThread.Post(a);
}
