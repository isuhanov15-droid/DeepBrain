# DeepBrain Linux Run

## Target
- Ubuntu 24.04 or similar
- .NET SDK 8.0+

## Build
```bash
dotnet build DeepBrain.slnx
```

## Run Host
```bash
dotnet run --project src/DeepBrain.Host/DeepBrain.Host.csproj
```

Host runs the behavioral core and the built-in console view. If stdout or stdin is redirected, the interactive console renderer and command loop fall back safely.

## Run Console Mode
Console mode is part of `DeepBrain.Host`. Start the host in a terminal and use the built-in commands:
- `trace`
- `mlstatus`
- `scenario.list`
- `scenario.set <name>`
- `curriculum.mode <mode>`
- `reloadconfig`

## Run Studio
Avalonia Studio is cross-platform, but requires desktop dependencies:

```bash
dotnet run --project src/DeepBrain.Studio/DeepBrain.Studio.csproj
```

If the target machine is headless, run only `DeepBrain.Host`.

## Linux Notes
- Paths are created via `Path.Combine`.
- Logs, traces, reports and checkpoints are created relative to the application base directory.
- File output uses UTF-8 without BOM.
- TCP transport and framing are platform-neutral.
- Console rendering is guarded against non-interactive terminals and console size exceptions.

## Smoke
```bash
dotnet build DeepBrain.slnx
dotnet run --project src/DeepBrain.Host/DeepBrain.Host.csproj
```
