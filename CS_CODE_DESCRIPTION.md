# Описание кода всех *.cs файлов (полное)

Дата: 2026-02-02

Документ описывает все классы и модули проекта DeepBrain (Host / Shared / Studio) с учетом v0.4: суточный цикл, сон, консолидация памяти и микро‑планы.

---

## 1) DeepBrain.Shared (общий слой DTO + протокол)

### src/DeepBrain.Shared/Net/Msg.cs
Назначение: единый справочник строковых типов сообщений протокола.
Содержит:
- `Ping`, `Pong` — проверка соединения.
- `LogsSubscribe`, `LogAppend` — подписка на логи и доставка логов.
- `BrainStateSubscribe`, `BrainState` — подписка и доставка состояния v0.1.
- `BrainStart`, `BrainStop`, `BrainStep` — управляющие команды v0.1.
- `TraceSubscribe`, `TraceAppend` — подписка и доставка трассировки.
- `BrainLifeStateSubscribe`, `BrainLifeState`, `BrainLifeEpisodeAppend` — подписка/доставка жизненного состояния v0.2/v0.3/v0.4.
- `BrainLifeOutputSubscribe`, `BrainLifeOutputAppend` — подписка/доставка внешнего вывода.
- `BrainLifeStart`, `BrainLifeStop`, `BrainLifeStep` — управляющие команды v0.2+.
- `InputSet`, `InputGet`, `InputSnapshot` — установка/запрос входов.
- `EventPush` — внешнее событие (строка).

### src/DeepBrain.Shared/Net/Envelope.cs
Назначение: универсальный сетевой конверт.
Поля:
- `Type` — строковый тип сообщения (`Msg.*`).
- `Id` — идентификатор сообщения.
- `Ts` — timestamp (ms UTC).
- `Payload` — DTO (любой объект).
Методы/конструкторы:
- `NowMs()`, `NewId()` — генераторы времени и id.
- `[JsonConstructor] Envelope(string type, string id, long ts, object? payload)` — основной конструктор для JSON.
- Доп. конструкторы:
  - `(type, id, payload)`
  - `(type, payload)`
  - `(type, id)`

### src/DeepBrain.Shared/Net/JsonWire.cs
Назначение: сериализация/десериализация `Envelope`.
Ключевое:
- `Options`: `camelCase`, `WriteIndented=false`.
- `Serialize(Envelope)` → `byte[]`.
- `Deserialize(byte[])` → `Envelope`, ошибка при `null`.

### src/DeepBrain.Shared/Net/Framing.cs
Назначение: фрейминг TCP‑сообщений.
WriteFrameAsync:
- Пишет 4‑байтную длину (LE), затем payload.
- Flush.
ReadFrameAsync:
- Читает длину, валидирует `maxBytes`.
- Возвращает `null` при clean disconnect.
ReadEnvelopeAsync/WriteEnvelopeAsync:
- Работают через JsonWire.

### src/DeepBrain.Shared/Net/PayloadReader.cs
Назначение: безопасное извлечение DTO из `Envelope.Payload`.
Алгоритм:
1) если `payload is T` — возвращает сразу;
2) если `JsonElement` — десериализует;
3) если строка — десериализует;
4) иначе сериализует объект и десериализует обратно.

### src/DeepBrain.Shared/Net/PayloadWriter.cs
Назначение: упаковка DTO в payload.
Поведение: сейчас просто возвращает объект как есть (`object?`).

### src/DeepBrain.Shared/Net/LogAppendDto.cs
Назначение: DTO лог‑строки.
Поля: `Text`.

### src/DeepBrain.Shared/Trace/TraceDto.cs
Назначение: DTO трассировки.
Поля: `Tick`, `Stage`, `Data`.

### src/DeepBrain.Shared/Input/BrainInputDto.cs
Назначение: входы v0.1.
Поля:
- `Stress`, `Energy`, `Focus` (float)
- `Goal`, `Command` (string)

### src/DeepBrain.Shared/Brain/BrainStateDto.cs
Назначение: состояние v0.1.
Поля: `Tick`, `UptimeMs`, `Mode`, `LastDecision`.

### src/DeepBrain.Shared/Brain/BrainEventDto.cs
Назначение: DTO события для `event.push`.
Поле: `Name`.

### DTO жизненного цикла v0.2/v0.3/v0.4
ActionDto (`src/DeepBrain.Shared/Brain/ActionDto.cs`):
- `Kind` (internal|external), `Name`, `Strength`, `Note`.

AffectDto (`AffectDto.cs`):
- `Mood`, `Valence`, `Arousal`.

HomeostasisDto (`HomeostasisDto.cs`):
- `Energy`, `Fatigue`, `Arousal`, `Pain`, `Safety`.

InstinctsDto (`InstinctsDto.cs`):
- `SelfPreservation`, `EnergyConservation`, `Exploration`, `Attachment`, `Agency`.

OutcomeDto (`OutcomeDto.cs`):
- `Action`, `Reward`, `Message`.

LifeStateDto (`LifeStateDto.cs`):
- `Tick`, `Ts`, `Homeostasis`, `Instincts`, `Affect`, `LastDecision`, `LastReward`,
- расширения v0.3: `Policy`, `DominantDrive`, `MoodInertia`,
- расширения v0.4: `Circadian`, `Goals`, `ActivePlan`.

EpisodeDto (`EpisodeDto.cs`):
- `Tick`, `BeforeHomeostasis`, `AfterHomeostasis`, `Action`, `Reward`, `Ts`.

LifeOutputDto (`LifeOutputDto.cs`):
- `Tick`, `Ts`, `Message`, `ActionName`.

PolicyContextDto (`src/DeepBrain.Shared/BrainDtos/V2/PolicyContextDto.cs`):
- `Strategy`, `Reason`, `LoopCount`, `LoopPenalty`, `LastAction`, `SameActionStreak`, `AvgRewardShort`.

CircadianDto (`src/DeepBrain.Shared/BrainDtos/V3/CircadianDto.cs`):
- `Phase`, `TimeOfDay`, `IsSleeping`, `SleepPressure`.

GoalDto (`src/DeepBrain.Shared/BrainDtos/V3/GoalDto.cs`):
- `Id`, `Type`, `Urgency`, `Satisfaction`, `Source`.

PlanDto (`src/DeepBrain.Shared/BrainDtos/V3/PlanDto.cs`):
- `Strategy`, `RemainingTicks`, `GoalId`, `Rationale`.

---

## 2) DeepBrain.Host (сервер и мозг)

### src/DeepBrain.Host/Program.cs
Назначение: главный процесс.
Функции:
- глобальные обработчики исключений (`AppDomain`, `TaskScheduler`).
- `CancellationTokenSource` для Ctrl+C.
- инициализация:
  - `InputStore`, `BrainEngine` (v0.1);
  - `FileBatchWriter` для логов и трасс (`Logs/` и `Trace/`);
  - `LifeLoop` (v0.2/v0.3/v0.4), включен по умолчанию (`useLifeLoop = true`).
- запуск TCP‑сервера `TcpBrainServer`.
- heartbeat‑цикл (обновляет `Console.Title`, отправляет log heartbeat).
- запуск одного из циклов:
  - `LifeLoop.RunAsync` (v0.2/v0.3/v0.4),
  - либо `RunBrainLoopAsync` (v0.1).
- командный цикл:
  - `trace` — вкл/выкл отображение trace в консоли;
  - `start/stop` — управляют v0.1;
  - `logs` — печатает лог‑буфер;
  - `death/exit` — завершение.

RunBrainLoopAsync (v0.1):
- раз в 250 мс вызывает `brain.TickWithTrace`;
- шлет `trace.append` и `brain.state`;
- пишет trace в файл и в консоль, если включен `trace`.

FileBatchWriter:
- пишет батчами по 1000 строк;
- отдельные файлы логов/трасс;
- в начало записывает время запуска.

### src/DeepBrain.Host/Net/TcpBrainServer.cs
Назначение: TCP‑сервер, пул клиентских сессий.
Поля:
- `_listener` (`TcpListener`), `_sessions` (`ConcurrentDictionary`).
- `_lifeOutputs` — буфер последних output‑сообщений (до 50) для повторной синхронизации Studio.
- делегаты управления v0.1 (`_onBrainStart/Stop/Step`) и v0.2+ (`_onLifeStart/Stop/Step`).
Методы:
- `StartAsync` → запускает accept‑loop.
- `AcceptLoopAsync` → принимает клиентов и создает `ClientSession`.
- `BroadcastLogAsync`, `BroadcastStateAsync`, `BroadcastTraceAsync`, `BroadcastLifeStateAsync`.
- `BroadcastLifeOutputAsync` → добавляет output в буфер и рассылает клиентам.
Особенности:
- ошибки отправки подавляются, чтобы не ломать сервер.

### src/DeepBrain.Host/Net/ClientSession.cs
Назначение: одна TCP‑сессия.
Поля:
- `_client`, `_stream`.
- делегаты управления v0.1 и v0.2+.
- флаги подписок: `_wantsLogs`, `_wantsState`, `_wantsTrace`, `_wantsLifeState`, `_wantsLifeOutput`.
RunAsync:
- читает `Envelope` через `Framing.ReadEnvelopeAsync`;
- ошибки чтения логируются;
- вызывает `HandleAsync`.
HandleAsync:
- `ping` → `pong`.
- `logs.subscribe`, `brain.state.subscribe`, `trace.subscribe`, `brain.life.state.subscribe`, `brain.life.output.subscribe` → включают флаги и логируют.
- при `brain.life.output.subscribe` отправляет накопленные outputs из буфера сервера.
- `brain.start/stop/step` → вызывают делегаты v0.1.
- `brain.life.start/stop/step` → делегаты v0.2+.
- `input.set` → десериализация `BrainInputDto`.
- `event.push` → десериализация `BrainEventDto`.
Отправка:
- `SendLogAsync`, `SendStateAsync`, `SendTraceAsync`, `SendLifeStateAsync`, `SendLifeOutputAsync`.

---

## 3) Brain v0.1 (старый мозг)

### src/DeepBrain.Host/Brain/BrainEngine.cs
Назначение: основной цикл v0.1.
Состав:
- `InputStore`, `PerceptionEngine`, `StateEstimator`, `PolicyEngine`, `Actuator`.
Цикл `TickWithTrace`:
1) проверка режима (`running` или forced);
2) увеличение тика;
3) обработка очереди событий;
4) `PerceptionEngine.Sense` → `Percept` + `PerceptionDrives`;
5) `StateEstimator.UpdateHomeostasis`;
6) `PolicyEngine.Decide` → решение;
7) `Actuator.Act` → результат;
8) формирует trace‑события для каждой стадии.

### src/DeepBrain.Host/Brain/Act/Actuator.cs
Назначение: исполнитель v0.1.
Логика: всегда возвращает `ActResult(true)` (заглушка).

### src/DeepBrain.Host/Brain/Act/ActResult.cs
Назначение: DTO результата действия.
Поле: `Done`.

### src/DeepBrain.Host/Brain/Input/InputStore.cs
Назначение: хранение входов v0.1.
Функции:
- `GetSnapshot()` — потокобезопасное чтение.
- `Set()` — полная замена.
- `Patch()` — частичное обновление.

### src/DeepBrain.Host/Brain/Input/BrainInput.cs
Назначение: пустой файл (резерв/плейсхолдер).

### src/DeepBrain.Host/Brain/Perception/PerceptionEngine.cs
Назначение: преобразование входов в drives.
Алгоритм:
- вычисляет `threat`, `fatigue`, `goalPull` из входов;
- усиливает drives по событиям (`scare`, `noise`, `rest`, `focus`).

### src/DeepBrain.Host/Brain/Perception/Percept.cs
Назначение: слепок восприятия (time, tick, input).

### src/DeepBrain.Host/Brain/Perception/PerceptionDrives.cs
Назначение: DTO drives (Threat/Fatigue/GoalPull).

### src/DeepBrain.Host/Brain/State/StateEstimator.cs
Назначение: оценка внутреннего состояния v0.1.
Алгоритм:
- обновляет стресс/энергию/фокус;
- плавно возвращает к baseline;
- вычисляет `Mood` (tired/anxious/focused/calm).

### src/DeepBrain.Host/Brain/State/BrainStateInternal.cs
Назначение: внутреннее состояние (baseline + текущие значения).

### src/DeepBrain.Host/Brain/Policy/PolicyEngine.cs
Назначение: выбор действия v0.1.
Приоритеты:
1) `Command` (stop/step).
2) стресс/энергия.
3) цель (explore/work).
4) fallback по четности тика.

### src/DeepBrain.Host/Brain/Policy/Decision.cs
Назначение: DTO решения (Name/Confidence/Reason).

### src/DeepBrain.Host/Brain/Policy/DecisionNames.cs
Назначение: набор строк‑идентификаторов решений.

---

## 4) Brain v0.4 (жизненный цикл с временем/сном/планами)

### src/DeepBrain.Host/BrainLife/LifeMath.cs
Назначение: утилита `Clamp01`.

### src/DeepBrain.Host/BrainLife/CircadianClock.cs
Назначение: суточные фазы и давление сна.
Поведение:
- `TimeOfDay` 0..1, циклично по DayLengthSeconds.
- `Phase` определяется по TimeOfDay: morning/active/evening/night.
- `SleepPressure` растет при бодрствовании, падает во сне.

### src/DeepBrain.Host/BrainLife/SleepEngine.cs
Назначение: вход/выход из сна и восстановление.
Условия входа:
- `Phase == night`, `SleepPressure > 0.6`, `Safety > 0.5`.
Во сне:
- `Energy` растет, `Fatigue/Pain/Arousal` снижаются.
Условия выхода:
- `Phase == morning` и `SleepPressure < 0.2`.
Флаги: `EnteredSleep`, `WokeUp`.

### src/DeepBrain.Host/BrainLife/GoalResolver.cs
Назначение: формирует цели из драйвов и состояния.
Поведение:
- генерирует 2–4 цели;
- поддерживает `Satisfaction` и ее медленное падение;
- цели учитывают фазу суток (explore не в night).

### src/DeepBrain.Host/BrainLife/PlanEngine.cs
Назначение: микро‑планы.
Поведение:
- создает план, если нет активного или цель изменилась;
- уменьшает `RemainingTicks`;
- прерывает план при SelfPreservation, LoopPenalty или при сне;
- логирует `PLAN CREATED`/`PLAN INTERRUPTED`.

### src/DeepBrain.Host/BrainLife/MoodInertiaEngine.cs
Назначение: инерция аффекта.
Поведение:
- Valence/Arousal сглаживаются `lerp(prev, computed, alpha)` (alpha 0.15–0.35).
- Mood меняется при устойчивом кандидате (>=3 тика) или при высоком SelfPreservation.

### src/DeepBrain.Host/BrainLife/DominantDriveResolver.cs
Назначение: определяет доминирующий драйв по максимальному инстинкту.

### src/DeepBrain.Host/BrainLife/EpisodeMemory.cs
Назначение: эпизодическая память.
Поведение:
- ring‑buffer последних 1000 эпизодов;
- `QuerySimilar` ищет похожие по дистанции;
- `Consolidate` суммирует reward по действиям для сна.

### src/DeepBrain.Host/BrainLife/LoopDetector.cs
Назначение: анти‑петля.
Поведение:
- считает `SameActionStreak`;
- окно наград → `AvgRewardShort`;
- `LoopPenalty` растет при петле;
- `ResetShortTerm` очищает счетчики (используется во сне).

### src/DeepBrain.Host/BrainLife/LearningEngine.cs
Назначение: EMA‑обучение по действиям.
Методы: `Update`, `GetEma`, `GetBest`, `AddBias`.

### src/DeepBrain.Host/BrainLife/ActionSelector.cs
Назначение: выбор действия (System 1) в рамках стратегии/плана.
Поведение:
- стратегия берется из плана, иначе вычисляется по драйву;
- анти‑петля штрафует повтор `LastAction`;
- эпизодическая память добавляет бонус/штраф по действиям.

### src/DeepBrain.Host/BrainLife/Actuator.cs
Назначение: применение действия к homeostasis/affect.
Эффекты:
- `rest_short`, `breathe_slow`, `focus_narrow`, `focus_widen`, `reframe_negative`,
  `recall_safe_memory`, `explore_signal`, `emit_message`.

### src/DeepBrain.Host/BrainLife/RewardEngine.cs
Назначение: reward по изменению состояния.
Формула: энергия+безопасность − (fatigue+pain)*0.5.

### src/DeepBrain.Host/BrainLife/LifeLoop.cs
Назначение: главный цикл v0.4.
Шаги:
1) `CircadianClock.Tick`.
2) `SleepEngine.Update`.
3) если сон:
   - восстановление и консолидация памяти;
   - пропуск выбора действий;
   - broadcast state с `IsSleeping=true`.
4) если бодрствование:
   - `GoalResolver.Resolve`.
   - `PlanEngine.Update`.
   - `ActionSelector.Choose` (учет плана).
   - `Actuator / Reward / Learning / Memory` как в v0.3.
5) LifeState включает `Circadian`, `Goals`, `ActivePlan`.
6) Диагностика каждые 50 тиков: phase/sleeping/drive/goals/plan/action/reward.
7) Логи: `ENTER SLEEP`, `WAKE UP`, `PLAN CREATED`, `PLAN INTERRUPTED`.

---

## 5) DeepBrain.Studio (UI‑телеметрия)

### src/DeepBrain.Studio/Program.cs
Назначение: старт Avalonia приложения.
Особенности: `.UsePlatformDetect()`, `.WithInterFont()`, `.LogToTrace()`.

### src/DeepBrain.Studio/App.axaml.cs
Назначение: загрузка XAML и создание окна.
Особенности: ловит UI‑исключения, пишет `studio_crash.log`.

### src/DeepBrain.Studio/MainWindow.axaml
Назначение: визуальная структура.
Блоки:
- верхняя панель (heartbeat, host/port, connect/disconnect, статус, кнопки);
- слева Trace (последние 50);
- справа Life‑телеметрия + Outputs + Logs.
Примечание: кнопки Start/Stop отключены — UI не управляет мозгом.

### src/DeepBrain.Studio/MainWindow.axaml.cs
Назначение: логика UI.
События:
- `OnLog` — добавление логов;
- `OnTrace` — буфер 50 строк;
- `OnLifeState` — обновление Life‑панели;
- `OnLifeOutput` — добавление output‑сообщений (последние 50).
Connect: ping + подписки logs/state/trace/life/output.
Поля v0.4:
- phase/sleepPressure/isSleeping;
- active plan (strategy/goal/ttl);
- goals (id/urgency/satisfaction).

### src/DeepBrain.Studio/Net/TcpClientService.cs
Назначение: TCP‑клиент.
Ключевое:
- `ConnectAsync` → подключение и запуск `ReadLoopAsync`.
- `ReadLoopAsync` → чтение фреймов, `JsonWire.Deserialize`, `HandleIncoming`.
- `HandleIncoming`:
  - `LogAppend` → `OnLog`;
  - `Pong` → `OnInfo`;
  - `BrainState` → `OnState`;
  - `BrainLifeState` → `OnLifeState`;
  - `BrainLifeOutputAppend` → `OnLifeOutput`;
  - `TraceAppend` → `OnTrace`.
- `Subscribe*Async` → подписки.
- `PingWithTimeoutAsync` → ping с таймаутом.
- `SendAsync` защищен `SemaphoreSlim`.

---

## Итог
- Проект содержит два параллельных мозга: v0.1 (старый) и v0.4 (LifeLoop с временем/сном/планами).
- Ветка v0.4 по умолчанию активна и вещает расширенную телеметрию.
- Studio — только наблюдение и вывод состояния, без управления мозгом.
