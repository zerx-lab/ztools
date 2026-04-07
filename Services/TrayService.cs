using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ztools.Services;

/// <summary>
/// Manages the system tray icon and minimize-to-tray behaviour.
/// Call <see cref="Initialize"/> once after the main window is created.
/// </summary>
public sealed class TrayService : IDisposable
{
    public static readonly TrayService Instance = new();

    private Window? _mainWindow;
    private TrayIcon? _trayIcon;
    private bool _minimizeToTray;
    private bool _disposed;

    private TrayService() { }

    // ── Public API ───────────────────────────────────────────────────────────

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set
        {
            _minimizeToTray = value;

            // Tray icon is always visible; MinimizeToTray only controls
            // whether closing the window hides it instead of exiting.
            if (!value)
                ShowWindow();
        }
    }

    /// <summary>Call once the main window exists.</summary>
    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;

        _trayIcon = new TrayIcon
        {
            ToolTipText = "ZTools",
            IsVisible = true,
            Icon = LoadTrayIcon(),
        };

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show ZTools");
        showItem.Click += (_, _) => ShowWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();
        menu.Add(exitItem);

        _trayIcon.Menu = menu;
        _trayIcon.Clicked += (_, _) => ShowWindow();

        // Intercept window close when minimize-to-tray is active
        mainWindow.Closing += OnMainWindowClosing;
    }

    /// <summary>
    /// Update the tray icon's menu labels after a locale change.
    /// </summary>
    public void UpdateMenuLabels(string showLabel, string exitLabel)
    {
        if (_trayIcon?.Menu is not NativeMenu menu) return;
        if (menu.Items.Count >= 1 && menu.Items[0] is NativeMenuItem showItem)
            showItem.Header = showLabel;
        if (menu.Items.Count >= 3 && menu.Items[2] is NativeMenuItem exitItem)
            exitItem.Header = exitLabel;
    }

    public void ShowWindow()
    {
        if (_mainWindow is null) return;

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
        _mainWindow.BringIntoView();
    }

    // ── Icon loading ─────────────────────────────────────────────────────────

    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("avares://ztools/Assets/ztools_256.png");
            using var stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_minimizeToTray) return;

        // Cancel the close, hide instead
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private static void ExitApp()
    {
        if (Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime lifetime)
        {
            // Detach the closing handler so the window actually closes
            if (lifetime.MainWindow is { } win)
                win.Closing -= Instance.OnMainWindowClosing;

            lifetime.Shutdown();
        }
    }
}
