# Описание кода всех *.cs файлов

Дата: 2026-01-28

## Общее устройство проекта
Проект состоит из трех частей:
- DeepBrain.Host — консольный TCP‑сервер, который рассылает логи и состояние «мозга».
- DeepBrain.Studio — Avalonia UI‑клиент, подключается к серверу, показывает логи и состояние.
- DeepBrain.Shared — общий слой с DTO и сетевым протоколом (сообщения, сериализация, фрейминг).

Протокол обмена:
- JSON‑конверты (Envelope) поверх TCP.
- Кадрирование (Framing): 4‑байтный little‑endian префикс длины + JSON‑payload.
- Типы сообщений фиксированы в Msg.

Ниже — подробное описание каждого файла.

---

## src/DeepBrain.Studio/Program.cs
Назначение: точка входа Avalonia‑приложения.
Ключевые элементы:
- `Main(string[] args)`: запускает Avalonia через `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.
- `BuildAvaloniaApp()`: конфигурирует приложение, включает авто‑детект платформы, шрифт Inter, логирование в Trace.

Поведение:
- Никакой UI‑логики здесь нет; только старт и конфигурация Avalonia.

---

## src/DeepBrain.Studio/App.axaml.cs
Назначение: корневой класс Avalonia Application.
Ключевые элементы:
- `Initialize()`: грузит XAML через `AvaloniaXamlLoader.Load(this)`.
- `OnFrameworkInitializationCompleted()`: при классическом десктоп‑лайфтайме создает и назначает `MainWindow`.

Поведение:
- Определяет, какое окно становится основным при запуске.

---

## src/DeepBrain.Studio/MainWindow.axaml.cs
Назначение: код‑бихайнд главного окна клиента.
Поля:
- `_tcp`: экземпляр `TcpClientService` для сети.
- `_logs`: `ObservableCollection<string>` для логов.

Инициализация:
- `LogsList.ItemsSource = _logs` — список логов бинден к коллекции.
- Подписка на события `_tcp`:
  - `OnLog` → `AddLog` через UI‑диспетчер.
  - `OnInfo` → обновление `StatusText.Text` через UI‑диспетчер.

UI‑обработчики кнопок:
- `ConnectBtn`: читает `HostBox` и `PortBox`, вызывает `ConnectAsync`.
- `PingBtn`: вызывает `PingAsync`.
- `SubLogsBtn`: вызывает `SubscribeLogsAsync`.
- `SubStateBtn`: вызывает `SubscribeStateAsync`.
- `ClearBtn`: очищает `_logs`.
- `OnState`: заполняет `BrainStateText` форматированной строкой состояния.

Методы:
- `AddLog(string text)`: ограничивает историю логов 2000 строками и добавляет новую запись.
- `Ui(Action a)`: прокидывает действие в UI‑поток через `Dispatcher.UIThread.Post`.

Примечание:
- В комментариях есть «битая» кодировка (не ASCII/UTF‑8). Это не влияет на исполнение, но усложняет чтение.

---

## src/DeepBrain.Studio/Net/TcpClientService.cs
Назначение: сетевой клиент TCP для DeepBrain.Studio.
Поля:
- `_client`, `_stream`: `TcpClient` и его `NetworkStream`.
- `_cts`: `CancellationTokenSource` для остановки read‑loop.

События:
- `OnLog(string)`: лог‑сообщения от сервера.
- `OnInfo(string)`: системные сообщения (подключение/ошибки/понг).
- `OnState(long tick, long uptime, string mode, string decision)`: состояние «мозга».

Публичные методы:
- `ConnectAsync(host, port)`: соединяется, создает stream, запускает `ReadLoopAsync`.
- `SubscribeLogsAsync()`: отправляет `Msg.LogsSubscribe`.
- `SubscribeStateAsync()`: отправляет `Msg.BrainStateSubscribe`.
- `PingAsync()`: отправляет `Msg.Ping`.
- `DisconnectAsync()`: отмена токена, закрытие stream/клиента.
- `DisposeAsync()`: вызывает `DisconnectAsync`, освобождает `_cts`.

Внутренние методы:
- `ReadLoopAsync(ct)`: читает фреймы через `Framing.ReadFrameAsync`, десериализует `Envelope`, передает в `HandleIncoming`.
- `HandleIncoming(Envelope)`: маршрутизирует входящие сообщения:
  - `Msg.LogAppend` → `OnLog` (читает JSON‑поле `text`).
  - `Msg.Pong` → `OnInfo("pong ✅")`.
  - `Msg.BrainState` → парсит `tick`, `uptimeMs`, `mode`, `lastDecision` и вызывает `OnState`.
- `SendAsync(Envelope, ct)`: сериализация и отправка по фреймингу.
- `NowMs()`: текущее время в мс UTC.

Поведение:
- Клиент не пытается переподключаться автоматически; только сообщает об ошибке и «Disconnected.» в `finally`.

---

## src/DeepBrain.Shared/Net/Msg.cs
Назначение: константы типов сообщений для протокола.
Содержит:
- `Ping`, `Pong`.
- `LogsSubscribe`, `LogAppend`.
- `BrainStateSubscribe`, `BrainState`.
- `BrainStart`, `BrainStop`.

Роль:
- Единая точка именования всех message‑type строк.

---

## src/DeepBrain.Shared/Net/Envelope.cs
Назначение: структура сетевого сообщения.
`record Envelope(Type, Id, Ts, Payload)`:
- `Type`: тип сообщения (строка из `Msg`).
- `Id`: идентификатор сообщения.
- `Ts`: timestamp (мс UTC).
- `Payload`: полезная нагрузка (объект/JSON).

---

## src/DeepBrain.Shared/Net/JsonWire.cs
Назначение: JSON‑сериализация и десериализация Envelope.
Ключевые элементы:
- `Options`: camelCase, без форматирования.
- `Serialize(Envelope)`: `SerializeToUtf8Bytes`.
- `Deserialize(byte[])`: `JsonSerializer.Deserialize<Envelope>`, при null — `InvalidDataException`.

---

## src/DeepBrain.Shared/Net/Framing.cs
Назначение: фрейминг TCP‑сообщений (длина + payload).
Ключевые элементы:
- `WriteFrameAsync(stream, payload, ct)`:
  - Пишет 4‑байтную длину (LE), затем payload, затем `FlushAsync`.
- `ReadFrameAsync(stream, maxBytes, ct)`:
  - Читает длину (4 байта), валидирует диапазон, затем читает payload указанной длины.
  - Возвращает `null` при чистом disconnect (0 байт на длине).
- `ReadUpToAsync` и `ReadExactlyAsync`: вспомогательные чтения до нужного количества байт.

Поведение:
- Ограничение `maxBytes` защищает от слишком больших фреймов.

---

## src/DeepBrain.Shared/Brain/BrainStateDto.cs
Назначение: DTO состояния «мозга».
`record BrainStateDto(Tick, UptimeMs, Mode, LastDecision)`:
- `Tick`: счетчик итераций.
- `UptimeMs`: время жизни процесса в мс.
- `Mode`: режим (например, idle/running).
- `LastDecision`: последняя «решение».

---

## src/DeepBrain.Host/Program.cs
Назначение: запуск TCP‑сервера и генерация «состояния мозга».
Потоки/задачи:
- `RunAcceptLoopAsync`: принимает клиентов и запускает их сессии.
- `heartbeatTask`: раз в 1 секунду отправляет `BroadcastLogAsync`.
- `stateTask`: раз в 250 мс инкрементирует `tick`, меняет `mode`, выбирает `lastDecision`, рассылает `BroadcastStateAsync`.

Логика состояния:
- Каждые 20 тиков переключает `mode` между `idle` и `running`.
- `lastDecision`: `observe` на четных тиках, `wait` на нечетных.

Завершение:
- Отмена по Ctrl+C → `cts.Cancel()`.
- `Task.WhenAll` ждет все задачи, затем `DisposeAsync` сервера.

---

## src/DeepBrain.Host/Net/TcpBrainServer.cs
Назначение: TCP‑сервер, управляющий пулом клиентов.
Поля:
- `_listener`: `TcpListener` на `IPAddress.Loopback`.
- `_clients`: список `ClientSession`.
- `_lock`: блокировка списка.

Методы:
- `Start()`: запуск listener.
- `RunAcceptLoopAsync(onInfo, ct)`:
  - Принимает клиентов, создает `ClientSession`, добавляет в список.
  - Запускает `session.RunAsync` в отдельной задаче, удаляет из списка при завершении.
- `BroadcastLogAsync(text, ct)`:
  - Снэпшот списка, для каждого клиента `SendLogAsync`.
- `BroadcastStateAsync(payload, ct)`:
  - Аналогично логам, вызывает `SendStateAsync`.
- `DisposeAsync()`:
  - Останавливает listener, отменяет сессии, освобождает клиентов.

Поведение:
- Любые исключения при отправке логов/состояния подавляются, чтобы не ронять сервер.

---

## src/DeepBrain.Host/Net/ClientSession.cs
Назначение: сессия одного TCP‑клиента.
Поля:
- `_client`, `_stream`: транспорт.
- `_cts`: локальная отмена сессии.
- `WantsLogs`, `WantsState`: флаги подписок.

Методы:
- `RunAsync(onInfo, serverCt)`:
  - Создает linked token и читает фреймы.
  - Десериализует `Envelope`, передает в `HandleAsync`.
  - Логирует подключение/отключение и ошибки.
- `HandleAsync(env, onInfo, ct)`:
  - `Msg.Ping` → `Msg.Pong`.
  - `Msg.LogsSubscribe` → `WantsLogs=true`, отправляет приветственное `LogAppend`.
  - `Msg.BrainStateSubscribe` → `WantsState=true`, отправляет log об успешной подписке.
  - `Msg.BrainStart/BrainStop` → логирование + `LogAppend`.
  - Иначе → лог «Unknown msg».
- `SendLogAsync(text, ct)`:
  - Отправляет `LogAppend`, но только если `WantsLogs`.
- `SendStateAsync(payload, ct)`:
  - Отправляет `BrainState`, но только если `WantsState`.
- `SendAsync(env, ct)`:
  - Сериализация `JsonWire.Serialize` + `Framing.WriteFrameAsync`.
- `Stop()` и `DisposeAsync()`:
  - Останавливает сессию, закрывает stream/client, освобождает `_cts`.

Поведение:
- Клиент управляет подписками через сообщения `logs.subscribe` и `brain.state.subscribe`.

---

## Итоговая картина взаимодействия
1) Host запускается и слушает `127.0.0.1:5555`.
2) Studio подключается, подписывается на логи/состояние.
3) Server периодически рассылает `log.append` и `brain.state`.
4) Client читает, десериализует, обновляет UI через events.

Если нужно дополнить описание конкретных частей, уточните раздел/файл.
