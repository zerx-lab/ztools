using Avalonia;
using System;

namespace ztools;

sealed class Program
{
    /// <summary>
    /// Set to <c>true</c> when the application is launched with the <c>--silent</c> flag.
    /// When silent, the main window is not shown on startup (background / tray-only mode).
    /// </summary>
    public static bool SilentStart { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        SilentStart = Array.Exists(args, a =>
            string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
