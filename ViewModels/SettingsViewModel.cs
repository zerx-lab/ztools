using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ztools.I18n;
using ztools.Models;
using ztools.Services;

namespace ztools.ViewModels;

/// <summary>Strongly-typed wrapper so ComboBox SelectedItem binding works cleanly.</summary>
public record LanguageOption(string Code, string DisplayName);

public partial class SettingsViewModel : ViewModelBase
{
    // ── Locale accessor ───────────────────────────────────────────────────────
    public LocaleManager L => LocaleManager.Instance;

    // ── Language ──────────────────────────────────────────────────────────────
    public List<LanguageOption> AvailableLocales { get; } =
        LocaleManager.AvailableLocales
            .Select(t => new LanguageOption(t.Code, t.DisplayName))
            .ToList();

    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is null) return;
        LocaleManager.Instance.Load(value.Code);
        var s = SettingsService.Load();
        s.Language = value.Code;
        SettingsService.Save(s);
    }

    // ── General ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _startWithWindows = false;

    partial void OnStartWithWindowsChanged(bool value)
    {
        // Write / remove registry entry
        var success = StartupService.SetEnabled(value);

        // If the registry write failed, revert the toggle silently
        if (!success)
        {
            SetProperty(ref _startWithWindows, !value, nameof(StartWithWindows));
            return;
        }

        var s = SettingsService.Load();
        s.StartWithWindows = value;
        SettingsService.Save(s);
    }

    [ObservableProperty]
    private bool _minimizeToTray = false;

    partial void OnMinimizeToTrayChanged(bool value)
    {
        TrayService.Instance.MinimizeToTray = value;

        var s = SettingsService.Load();
        s.MinimizeToTray = value;
        SettingsService.Save(s);
    }

    // ── About ─────────────────────────────────────────────────────────────────
    public string AppVersion => "0.1.0";
    public string BuildDate => "2025";
    public string Platform => "Windows (x64)";
    public string Runtime =>
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    [ObservableProperty]
    private string _copyBtnText = string.Empty;

    [RelayCommand]
    private async System.Threading.Tasks.Task CopyVersionInfoAsync()
    {
        var info = $"ZTools v{AppVersion} | {Platform} | {Runtime}";

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? Avalonia.Controls.TopLevel.GetTopLevel(desktop.MainWindow)
            : null;

        var clipboard = topLevel?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(info);

        CopyBtnText = L["Settings.About.Copied"];
        await System.Threading.Tasks.Task.Delay(2000);
        CopyBtnText = L["Settings.About.CopyVersion"];
    }

    // ── Constructor ───────────────────────────────────────────────────────────
    public SettingsViewModel()
    {
        var settings = SettingsService.Load();

        // Language
        var matchedLocale = AvailableLocales
            .FirstOrDefault(o => o.Code == settings.Language)
            ?? AvailableLocales[0];
        _selectedLanguageOption = matchedLocale;

        // General — read real state from registry / settings
        // StartWithWindows reflects the actual registry state, not just the saved setting
        _startWithWindows = StartupService.IsEnabled();

        _minimizeToTray = settings.MinimizeToTray;

        // Sync tray service with persisted state (window may not exist yet;
        // TrayService.Initialize is called from App after window creation)
        TrayService.Instance.MinimizeToTray = settings.MinimizeToTray;

        // Copy button label
        _copyBtnText = L["Settings.About.CopyVersion"];

        // Keep copy button label in sync when locale changes
        L.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "Item[]")
                CopyBtnText = L["Settings.About.CopyVersion"];
        };
    }
}
