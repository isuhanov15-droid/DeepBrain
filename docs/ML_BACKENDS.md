# ML-бэкенды (заглушка / локальный / удалённый)

Этот документ описывает режимы ML‑подсистемы DeepBrain и как включить локальный ML.

## Режимы

### 1) `stub` — заглушка (по умолчанию без ML)
- ML отключён, работает только эвристика.
- Ничего не ломается, Host/Studio запускаются всегда.

Причины использования заглушки:
- `ml.enable=false` в `brainconfig.json`.
- `ml.backend="local"`, но ML.Core не подключён (нет `ML_CORE_PATH`).
- `ml.backend="remote"`, но нет соединения с ML.Host.

### 2) `local` — ML.Core внутри DeepBrain.Host
- Основной режим для разработки.
- Использует ML.Core через `ML_CORE_PATH`.

Как включить локальный режим:
```powershell
dotnet build DeepBrain.slnx -p:ML_CORE_PATH="C:\path\to\ML.Core\ML.Core.csproj"
```
И в `brainconfig.json`:
```json
"ml": { "enable": true, "backend": "local" }
```

### 3) `remote` — ML.Host по TCP
- Опциональный режим, использует ML.Host на другом процессе/машине.
- Включается через:
```json
"ml": {
  "enable": true,
  "backend": "remote",
  "remote": { "host": "127.0.0.1", "port": 7777 }
}
```

## Почему используется `backend=stub`?
1) `ml.enable=false (brainconfig)`
2) `ml.backend="local"`, но ML.Core не подключён  
   → "ML.Core not linked: set ML_CORE_PATH to ML.Core.csproj"
3) `ml.backend="remote"` и нет соединения  
   → "remote not connected: run mlconnect or check host/port"

## Команды
- `mlstatus` — подключение, режим, источник политики, replay-буфер, шаги обучения, ε, вес сети, loss и причина отключения.
- `mlconnect` / `mldisconnect` — ручное подключение/отключение remote backend.

После загрузки checkpoint вес сети учитывает сохранённые шаги обучения, даже если replay-буфер нового процесса ещё пуст. Снижение ε для новой политики начинается после накопления как минимум одного batch опыта.
