using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ztools.Services;
using ztools.ViewModels;
using ztools.Views;

namespace ztools;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load persisted settings and apply locale before any UI is created
        var settings = ztools.Services.SettingsService.Load();
        ztools.I18n.LocaleManager.Instance.Load(settings.Language);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Note: Avalonia 12 disables DataAnnotations validation by default,
            // so the old BindingPlugins workaround is no longer needed.
            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            // Initialize tray icon — must happen after window is created
            TrayService.Instance.Initialize(mainWindow);

            // Keep tray menu labels in sync when locale changes
            ztools.I18n.LocaleManager.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is "Item[]")
                {
                    var l = ztools.I18n.LocaleManager.Instance;
                    TrayService.Instance.UpdateMenuLabels(l["Tray.Show"], l["Tray.Exit"]);
                }
            };

            // Silent start: keep the window hidden so only the tray icon is visible.
            // The user can restore the window via the tray menu at any time.
            if (Program.SilentStart && settings.SilentStart)
            {
                // Switch shutdown mode so the process doesn't exit when no
                // window is visible — the tray icon keeps the app alive until
                // the user explicitly chooses "Exit".
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

                // IMPORTANT: do NOT assign desktop.MainWindow here.
                // ClassicDesktopStyleApplicationLifetime.Start() automatically
                // calls MainWindow?.Show() after this method returns, which
                // would defeat the silent start. Assign it after the main loop
                // has started so TopLevel lookups (clipboard etc.) still work.
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => desktop.MainWindow = mainWindow,
                    Avalonia.Threading.DispatcherPriority.Background);
            }
            else
            {
                // The lifetime shows MainWindow automatically after init.
                desktop.MainWindow = mainWindow;
            }

            // Clean up tray icon on app exit
            desktop.Exit += (_, _) => TrayService.Instance.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
