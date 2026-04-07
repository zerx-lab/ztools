using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
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
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
            desktop.MainWindow = mainWindow;

            // Initialize tray icon — must happen after window is assigned
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
            }
            else
            {
                mainWindow.Show();
            }

            // Clean up tray icon on app exit
            desktop.Exit += (_, _) => TrayService.Instance.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
