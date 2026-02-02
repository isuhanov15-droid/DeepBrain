# Описание кода всех *.cs файлов (полное)

Дата: 2026-02-02

Документ описывает все классы и модули проекта DeepBrain (Host / Shared / Studio) с учетом v0.3: инерция аффекта, память, анти‑петля и стратегия.

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
- `BrainLifeStateSubscribe`, `BrainLifeState`, `BrainLifeEpisodeAppend` — подписка/доставка жизненного состояния v0.2/v0.3.
- `BrainLifeOutputSubscribe`, `BrainLifeOutputAppend` — подписка/доставка внешнего вывода.
- `BrainLifeStart`, `BrainLifeStop`, `BrainLifeStep` — управляющие команды v0.2/v0.3.
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

### DTO жизненного цикла v0.2/v0.3
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
- расширения v0.3: `Policy`, `DominantDrive`, `MoodInertia`.

EpisodeDto (`EpisodeDto.cs`):
- `Tick`, `BeforeHomeostasis`, `AfterHomeostasis`, `Action`, `Reward`, `Ts`.

LifeOutputDto (`LifeOutputDto.cs`):
- `Tick`, `Ts`, `Message`, `ActionName`.

PolicyContextDto (`src/DeepBrain.Shared/BrainDtos/V2/PolicyContextDto.cs`):
- `Strategy`, `Reason`, `LoopCount`, `LoopPenalty`, `LastAction`, `SameActionStreak`, `AvgRewardShort`.

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
  - `LifeLoop` (v0.2/v0.3), включен по умолчанию (`useLifeLoop = true`).
- запуск TCP‑сервера `TcpBrainServer`.
- heartbeat‑цикл (обновляет `Console.Title`, отправляет log heartbeat).
- запуск одного из циклов:
  - `LifeLoop.RunAsync` (v0.2/v0.3),
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
- делегаты управления v0.1 (`_onBrainStart/Stop/Step`) и v0.2/v0.3 (`_onLifeStart/Stop/Step`).
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
- делегаты управления v0.1 и v0.2/v0.3.
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
- `brain.life.start/stop/step` → делегаты v0.2/v0.3.
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

## 4) Brain v0.3 (жизненный цикл с инерцией/памятью/анти‑петлей)

### src/DeepBrain.Host/BrainLife/LifeMath.cs
Назначение: утилита `Clamp01`.

### src/DeepBrain.Host/BrainLife/WorldSim.cs
Назначение: симуляция мира (Noise/Novelty/SocialPresence/Threat).
Особенности:
- медленные изменения + редкие всплески угрозы;
- фиксированный seed;
- `DampenNovelty` снижает новизну.

### src/DeepBrain.Host/BrainLife/HomeostasisEngine.cs
Назначение: обновление гомеостаза по миру и dt.
Правила:
- `Fatigue` растет, `Energy` падает;
- `Safety` падает при Threat, восстанавливается медленно;
- `Pain` растет при Fatigue/Threat;
- `Arousal` зависит от Noise и Safety.

### src/DeepBrain.Host/BrainLife/InstinctEngine.cs
Назначение: расчет инстинктов из homeostasis и мира.
Формулы: SelfPreservation, EnergyConservation, Exploration, Attachment, Agency (0..1).

### src/DeepBrain.Host/BrainLife/EmotionEngine.cs
Назначение: базовый расчет `Affect` (без инерции).

### src/DeepBrain.Host/BrainLife/MoodInertiaEngine.cs
Назначение: инерция аффекта.
Поведение:
- Valence/Arousal сглаживаются `lerp(prev, computed, alpha)` с alpha 0.15–0.35.
- Mood меняется только при устойчивости кандидата (>=3 тика) или при высоком SelfPreservation.
- Возвращает `MoodInertia` как 1‑alpha.

### src/DeepBrain.Host/BrainLife/DominantDriveResolver.cs
Назначение: определяет доминирующий драйв по максимальному инстинкту.

### src/DeepBrain.Host/BrainLife/EpisodeMemory.cs
Назначение: эпизодическая память.
Поведение:
- хранит ring‑buffer последних 1000 эпизодов;
- `QuerySimilar` ищет похожие по дистанции (Energy/Fatigue/Safety/Arousal).

### src/DeepBrain.Host/BrainLife/LoopDetector.cs
Назначение: анти‑петля.
Поведение:
- считает `SameActionStreak`;
- хранит окно наград (`AvgRewardShort`);
- при петле повышает `LoopPenalty` и `LoopCount`, иначе снижает.

### src/DeepBrain.Host/BrainLife/LearningEngine.cs
Назначение: EMA‑обучение по действиям.
Методы: `Update`, `GetEma`, `GetBest`.

### src/DeepBrain.Host/BrainLife/ActionSelector.cs
Назначение: выбор действия (System 1) с учетом стратегии, памяти и анти‑петли.
Алгоритм:
- определяет `strategy` (explore/regulate/rest/connect/focus) по драйвам и состоянию;
- формирует кандидатов по стратегии;
- скоринг: `base + EMA + externalBonus - loopPenalty - badMemory + goodMemory`;
- если `LoopPenalty > 0.4`, сильно штрафует повтор `LastAction`.

### src/DeepBrain.Host/BrainLife/Actuator.cs
Назначение: применение действия к homeostasis/affect.
Эффекты:
- `rest_short`, `breathe_slow`, `focus_narrow`, `focus_widen`, `reframe_negative`,
  `recall_safe_memory`, `explore_signal`, `emit_message`.

### src/DeepBrain.Host/BrainLife/RewardEngine.cs
Назначение: reward по изменению состояния.
Формула: энергия+безопасность − (fatigue+pain)*0.5.

### src/DeepBrain.Host/BrainLife/LifeLoop.cs
Назначение: главный цикл v0.3.
Шаги:
1) `world.Tick`.
2) `homeostasis.Update`.
3) `instincts.Compute`.
4) `emotion.Compute` → `MoodInertiaEngine.Apply`.
5) `DominantDriveResolver.Resolve`.
6) `ActionSelector.Choose` (strategy + anti‑петля + память).
7) `actuator.Apply`.
8) `reward.Compute`, `LearningEngine.Update`, `LoopDetector.Update`.
9) `EpisodeMemory.Add`.
10) формирование `LifeStateDto` с `Policy`, `DominantDrive`, `MoodInertia`.
11) broadcast `brain.life.state`.
12) trace по стадиям (homeostasis/instincts/affect/decision/action/reward/output).
13) диагностика: каждые 20 тиков (mood/drive/strategy/streak/avgR/loopPenalty).
14) если `SameActionStreak > 10` — `LOOP WARNING`.
15) если есть `Outcome.Message`, формируется `LifeOutputDto` и отправляется наружу (log + trace + output).

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
Heartbeat: переключает символ в UI при log‑строках содержащих "heartbeat".
v0.3 поля:
- strategy, dominantDrive, loopPenalty/streak, avgRewardShort, moodInertia.

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
- Проект содержит два параллельных мозга: v0.1 (старый) и v0.3 (LifeLoop с инерцией/памятью/анти‑петлей).
- Ветка v0.3 по умолчанию активна и вещает расширенную телеметрию.
- Studio — только наблюдение и вывод состояния, без управления мозгом.
