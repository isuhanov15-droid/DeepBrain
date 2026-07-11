# Запуск DeepBrain в Linux

## Требования

- Ubuntu 24.04 или совместимый дистрибутив;
- .NET SDK 8.0 или новее.

## Сборка

```bash
dotnet build DeepBrain.sln
```

## Запуск Host

```bash
dotnet run --project src/DeepBrain.Host/DeepBrain.Host.csproj
```

Host запускает поведенческое ядро и встроенную консольную панель. Если стандартный ввод или вывод перенаправлен, интерактивная отрисовка и командный цикл безопасно отключаются.

Рабочую конфигурацию сервера рекомендуется хранить вне `bin` и передавать явно:

```bash
dotnet run --project src/DeepBrain.Host/DeepBrain.Host.csproj -- \
  --config /etc/deepbrain/brainconfig.json \
  --console logs
```

Альтернативно задайте переменную окружения `DEEPBRAIN_CONFIG_PATH`. Аргумент `--config` имеет приоритет над переменной окружения.

## Консольный режим

Консоль входит в `DeepBrain.Host`. Запустите Host в терминале и используйте встроенные команды:

- `trace`
- `mlstatus`
- `scenario.list`
- `scenario.set <name>`
- `curriculum.mode <mode>`
- `memory.status`
- `memory.recent [count]`
- `memory.search <scenario|mood|action>`
- `reloadconfig`

Команды сохраняют технические английские имена ради совместимости, а ответы и справка выводятся на русском языке.

## Запуск Studio

Studio на Avalonia работает в разных ОС, но требует графических зависимостей:

```bash
dotnet run --project src/DeepBrain.Studio/DeepBrain.Studio.csproj
```

На сервере без графической среды запускайте только `DeepBrain.Host`.

## Особенности Linux

- Пути формируются через `Path.Combine`.
- Журналы, трассировки, отчёты, контрольные точки и память создаются относительно рабочего каталога приложения.
- Файлы записываются в UTF-8 без BOM.
- TCP-транспорт и фрейминг не зависят от платформы.
- Отрисовка консоли защищена от неинтерактивных терминалов и ошибок определения размера окна.

## Быстрая проверка

```bash
dotnet build DeepBrain.sln
dotnet run --project src/DeepBrain.Host/DeepBrain.Host.csproj
```
