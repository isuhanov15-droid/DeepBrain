using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DeepBrain.Studio.Net;
using DeepBrain.Shared.Brain;
using DeepBrain.Shared.Localization;

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
                StatusText.Text = "Истекло время ожидания ответа";
                AddLog("Истекло время ожидания ответа от Host");
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
            StatusText.Text = $"тик={tick}  работа={uptime} мс  " +
                              $"режим={RussianDisplay.Token(mode)}  " +
                              $"решение={RussianDisplay.Token(decision)}";
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
        var prefix = RussianDisplay.Token(output.ActionName);
        _outputs.Add($"[{output.Ts:HH:mm:ss}] {prefix}: {output.Message}");
    }

    private static bool IsHeartbeat(string text)
    {
        return text.Contains("heartbeat", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("пульс", StringComparison.OrdinalIgnoreCase);
    }

    private void ToggleHeartbeat()
    {
        _heartbeatOn = !_heartbeatOn;
        HeartbeatText.Text = _heartbeatOn ? "❤" : "♡";
    }

    private void UpdateLife(LifeStateDto state)
    {
        LifeMoodText.Text = $"настроение={RussianDisplay.Token(state.Affect.Mood)} " +
                            $"валентность={state.Affect.Valence:0.00} возбуждение={state.Affect.Arousal:0.00}";
        LifeHomeostasisText.Text = $"энергия={state.Homeostasis.Energy:0.00} " +
                                   $"усталость={state.Homeostasis.Fatigue:0.00} " +
                                   $"безопасность={state.Homeostasis.Safety:0.00} " +
                                   $"боль={state.Homeostasis.Pain:0.00}";
        LifeInstinctsText.Text = $"инстинкты: самосохранение={state.Instincts.SelfPreservation:0.00} " +
                                 $"энергосбережение={state.Instincts.EnergyConservation:0.00} " +
                                 $"исследование={state.Instincts.Exploration:0.00} " +
                                 $"привязанность={state.Instincts.Attachment:0.00} " +
                                 $"самостоятельность={state.Instincts.Agency:0.00}";
        LifeDecisionText.Text = $"последнее решение={RussianDisplay.Token(state.LastDecision)}";
        LifeRewardText.Text = $"последняя награда={state.LastReward:0.000}  тик={state.Tick}";
        if (state.Reward is not null)
        {
            LifeRewardBreakdownText.Text = $"награда=всего:{state.Reward.Total:+0.000;-0.000} " +
                                           $"гомеостаз:{state.Reward.Homeostasis:+0.000;-0.000} " +
                                           $"исследование:{state.Reward.Explore:+0.000;-0.000} " +
                                           $"социальная:{state.Reward.Social:+0.000;-0.000} " +
                                           $"петля:{state.Reward.LoopPenalty:+0.000;-0.000} " +
                                           $"недопустимое:{state.Reward.InvalidActionPenalty:+0.000;-0.000}";
        }
        else
        {
            LifeRewardBreakdownText.Text = "награда=нет данных";
        }

        if (state.Episode is not null)
        {
            LifeEpisodeText.Text = $"эпизод={state.Episode.EpisodeId} " +
                                   $"тик={state.Episode.EpisodeTick}/{state.Episode.EpisodeLengthTicks} " +
                                   $"причина={RussianDisplay.Token(state.Episode.ResetReason)}";
        }
        else
        {
            LifeEpisodeText.Text = "эпизод=нет данных";
        }
        if (state.Scenario is not null)
        {
            LifeScenarioText.Text = $"сценарий={RussianDisplay.Token(state.Scenario.Name)} " +
                                    $"режим={RussianDisplay.Token(state.Scenario.CurriculumMode)} " +
                                    $"индекс={state.Scenario.Index}";
        }
        else
        {
            LifeScenarioText.Text = "сценарий=нет данных";
        }
        if (state.Evaluation is not null)
        {
            var calmRatio = ClampRatio(state.Evaluation.CalmRatio);
            var anxiousRatio = ClampRatio(state.Evaluation.AnxiousRatio);
            var curiousRatio = ClampRatio(state.Evaluation.CuriousRatio);
            var diversity = ClampRatio(state.Evaluation.ActionDiversity);
            LifeEvalText.Text = $"оценка: награда={state.Evaluation.AvgReward:0.000} " +
                                $"петли={Math.Max(0, state.Evaluation.LoopCount)} " +
                                $"разнообразие={diversity:0.00} спокойствие={calmRatio:0.00} " +
                                $"тревога={anxiousRatio:0.00} любопытство={curiousRatio:0.00}";
        }
        else
        {
            LifeEvalText.Text = "оценка=нет данных";
        }
        var policy = state.Policy;
        LifeStrategyText.Text = $"стратегия={RussianDisplay.Token(policy?.Strategy)} " +
                                $"причина={RussianDisplay.Token(policy?.Reason)}";
        LifeDriveText.Text = $"ведущий мотив={RussianDisplay.Token(state.DominantDrive)}";
        if (state.LoopInfo is not null)
        {
            LifeLoopText.Text = $"петля: активна={RussianDisplay.YesNo(state.LoopInfo.IsInLoop)} " +
                                $"тип={RussianDisplay.Token(state.LoopInfo.Type)} " +
                                $"сила={state.LoopInfo.Strength:0.00} " +
                                $"обнаружено={state.LoopInfo.ObservedCount} " +
                                $"штраф={state.LoopInfo.CurrentPenalty:0.00}";
        }
        else
        {
            LifeLoopText.Text = $"петля: активна=нет тип=нет сила=0.00 " +
                                $"обнаружено={policy?.LoopCount ?? 0} " +
                                $"штраф={Math.Abs(state.Reward?.LoopPenalty ?? 0):0.00}";
        }
        LifeAvgRewardText.Text = $"средняя краткая награда={policy?.AvgRewardShort:0.000}";
        LifeInertiaText.Text = $"инерция настроения={state.MoodInertia:0.00}";
        if (state.Circadian is not null)
        {
            LifeCircadianText.Text = $"суточная фаза={RussianDisplay.Token(state.Circadian.Phase)} " +
                                     $"время суток={state.Circadian.TimeOfDay:0.00} " +
                                     $"сон={RussianDisplay.YesNo(state.Circadian.IsSleeping)} " +
                                     $"потребность во сне={state.Circadian.SleepPressure:0.00}";
        }
        else
        {
            LifeCircadianText.Text = "суточный цикл=нет данных";
        }

        if (state.ActivePlan is not null)
        {
            LifePlanText.Text = $"план={RussianDisplay.Token(state.ActivePlan.Strategy)} / " +
                                $"{RussianDisplay.Token(state.ActivePlan.GoalId)} " +
                                $"осталось тиков={state.ActivePlan.RemainingTicks}";
        }
        else
        {
            LifePlanText.Text = "план=нет данных";
        }

        if (state.Goals is not null && state.Goals.Count > 0)
        {
            var parts = state.Goals
                .Take(4)
                .Select(g => $"{RussianDisplay.Token(g.Id)}:{g.Urgency:0.00}/{g.Satisfaction:0.00}")
                .ToArray();
            LifeGoalsText.Text = $"цели={string.Join(", ", parts)}";
        }
        else
        {
            LifeGoalsText.Text = "цели=нет данных";
        }

        if (state.Attention is not null)
        {
            var focus2 = string.IsNullOrWhiteSpace(state.Attention.Focus2)
                ? string.Empty
                : $" / {RussianDisplay.Token(state.Attention.Focus2)}";
            LifeAttentionText.Text = $"внимание={RussianDisplay.Token(state.Attention.Focus1)}{focus2} " +
                                     $"интенсивность={state.Attention.Intensity:0.00} " +
                                     $"причина={RussianDisplay.Token(state.Attention.Reason)}";
        }
        else
        {
            LifeAttentionText.Text = "внимание=нет данных";
        }

        if (state.RecentEvents is not null && state.RecentEvents.Count > 0)
        {
            var eventsLine = state.RecentEvents
                .TakeLast(5)
                .Select(e => $"{RussianDisplay.Token(e.Type)}:{e.Severity:0.00} {RussianDisplay.Token(e.Payload)}")
                .ToArray();
            LifeEventsText.Text = $"события={string.Join(" | ", eventsLine)}";
        }
        else
        {
            LifeEventsText.Text = "события=нет данных";
        }

        if (state.SemanticNotesTop is not null && state.SemanticNotesTop.Count > 0)
        {
            var notesLine = state.SemanticNotesTop
                .Select(n => $"{n.Key} → {RussianDisplay.Token(n.BestAction)} {n.Score:0.00} ({n.Samples})")
                .ToArray();
            LifeSemanticText.Text = $"семантическая память={string.Join(" | ", notesLine)}";
        }
        else
        {
            LifeSemanticText.Text = "семантическая память=нет данных";
        }

        if (state.Climate is not null)
        {
            LifeClimateText.Text = $"климат=спокойствие:{state.Climate.Calm:0.00} " +
                                   $"стресс:{state.Climate.Stress:0.00} " +
                                   $"напряжение:{state.Climate.Tension:0.00} " +
                                   $"база:{state.Climate.BaselineTension:0.00}";
        }
        else
        {
            LifeClimateText.Text = "климат=нет данных";
        }

        if (state.PainSource is not null)
        {
            LifePainSourceText.Text = $"источник боли=угроза:{state.PainSource.Threat:0.00} " +
                                      $"усталость:{state.PainSource.Fatigue:0.00} " +
                                      $"сон:{state.PainSource.Sleep:0.00} " +
                                      $"восстановление:{state.PainSource.Recovery:0.00}";
        }
        else
        {
            LifePainSourceText.Text = "источник боли=нет данных";
        }

        LifeConfigText.Text = $"конфигурация={state.ConfigVersion ?? RussianDisplay.NotAvailable}";

        if (state.Appraisal is not null)
        {
            LifeAppraisalText.Text = $"оценка состояния=угроза:{state.Appraisal.Threat:0.00} " +
                                     $"новизна:{state.Appraisal.Novelty:0.00} " +
                                     $"социальное:{state.Appraisal.Social:0.00} " +
                                     $"усталость:{state.Appraisal.Fatigue:0.00}";
        }
        else
        {
            LifeAppraisalText.Text = "оценка состояния=нет данных";
        }

        if (state.Stats is not null)
        {
            LifeStatsText.Text = $"статистика=тревога:{ClampRatio(state.Stats.AnxiousScore):0.00} " +
                                 $"спокойствие:{ClampRatio(state.Stats.CalmScore):0.00} " +
                                 $"любопытство:{ClampRatio(state.Stats.CuriousScore):0.00} " +
                                 $"боль p95:{state.Stats.P95Pain:0.00}";
        }
        else
        {
            LifeStatsText.Text = "статистика=нет данных";
        }

        if (state.Ml is not null)
        {
            var loops = policy?.LoopCount ?? 0;
            var err = string.IsNullOrWhiteSpace(state.Ml.LastRemoteError)
                ? string.Empty
                : $" ошибка:{state.Ml.LastRemoteError}";
            var reason = !state.Ml.Enabled && !string.IsNullOrWhiteSpace(state.Ml.ReasonIfDisabled)
                ? $" причина:{RussianDisplay.Token(state.Ml.ReasonIfDisabled)}"
                : string.Empty;
            LifeMlText.Text = $"ML: включён={RussianDisplay.YesNo(state.Ml.Enabled)} " +
                              $"режим={RussianDisplay.Token(state.Ml.MlMode)} " +
                              $"обучение={RussianDisplay.YesNo(state.Ml.TrainEnabled)} " +
                              $"backend={RussianDisplay.Token(state.Ml.BackendKind)} " +
                              $"ядро={RussianDisplay.YesNo(state.Ml.CoreAvailable)} " +
                              $"удалённый={RussianDisplay.YesNo(state.Ml.RemoteConnected)} " +
                              $"RTT={state.Ml.RttMs:0} мс ε={state.Ml.Epsilon:0.000} " +
                              $"ошибка обучения={state.Ml.LastLoss:0.000} среднее Q={state.Ml.AvgQ:0.000} " +
                              $"недопустимых={state.Ml.InvalidActionFallbackCount} петли={loops} " +
                              $"эпизоды обучения={state.Ml.TrainingEpisodeCount} " +
                              $"эпизоды оценки={state.Ml.EvalEpisodeCount}{err}{reason}";
        }
        else
        {
            LifeMlText.Text = "ML=нет данных";
        }

        if (state.Character is not null)
        {
            var p = state.Character.Personality;
            LifeVoiceModeText.Text = $"режим голоса={RussianDisplay.Token(state.Character.VoiceMode)}";
            LifePersonalityText.Text = $"личность={p.PersonaId} теплота={p.Warmth:0.00} " +
                                       $"огонь={p.Fire:0.00} юмор={p.Humor:0.00}";
            if (state.Character.HabitsTop is not null && state.Character.HabitsTop.Count > 0)
            {
                var habitsLine = state.Character.HabitsTop
                    .Select(h => $"{RussianDisplay.Token(h.Id)}:{h.Strength:0.00} " +
                                 $"использований={h.Uses} средняя награда={h.AvgReward:0.00}")
                    .ToArray();
                LifeHabitsText.Text = $"привычки={string.Join(" | ", habitsLine)}";
            }
            else
            {
                LifeHabitsText.Text = "привычки=нет данных";
            }

            LifeActiveHabitText.Text = $"активная привычка={RussianDisplay.Token(state.Character.ActiveHabitId)} " +
                                       $"влияние={state.Character.HabitInfluence:0.00}";
            LifeEmitText.Text = $"задержка сигнала={state.Character.EmitCooldownRemaining} " +
                                $"последний сигнал, тик={state.Character.LastEmitTick}";
            LifeConsumedText.Text = $"обработано событий={state.Character.ConsumedEventsCount}";
        }
        else
        {
            LifeVoiceModeText.Text = "режим голоса=нет данных";
            LifePersonalityText.Text = "личность=нет данных";
            LifeHabitsText.Text = "привычки=нет данных";
            LifeActiveHabitText.Text = "активная привычка=нет данных";
            LifeEmitText.Text = "задержка сигнала=нет данных";
            LifeConsumedText.Text = "обработано событий=нет данных";
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
            StatusText.Text = $"Ошибка: {ex.Message}";
            AddLog($"Ошибка: {ex.Message}");
        }
    }

    private static double ClampRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.0;

        return Math.Clamp(value, 0.0, 1.0);
    }

    private static void Ui(Action a) => Dispatcher.UIThread.Post(a);
}
