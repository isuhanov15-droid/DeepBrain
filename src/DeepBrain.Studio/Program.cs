using Avalonia;
using System;

namespace DeepBrain.Studio;

class Program
{
    // Код ранней инициализации. До вызова AppMain нельзя использовать Avalonia,
    // сторонние API и код, зависящий от SynchronizationContext: окружение ещё не готово.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Конфигурация Avalonia; используется также визуальным редактором.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
