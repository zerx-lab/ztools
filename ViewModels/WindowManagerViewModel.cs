using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ztools.I18n;
using ztools.Services;

namespace ztools.ViewModels;

public partial class WindowManagerViewModel : ViewModelBase
{
    // ── Locale accessor ───────────────────────────────────────────────────────
    public LocaleManager L => LocaleManager.Instance;

    // ── Available modifier keys ───────────────────────────────────────────────
    public List<string> AvailableModifiers { get; } = ["Alt", "Ctrl", "Shift", "Win"];

    // ── IsEnabled ─────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        // Sync to service
        if (value)
            WindowManagerService.Instance.Enable();
        else
            WindowManagerService.Instance.Disable();

        // Persist
        var s = SettingsService.Load();
        s.WindowManagerEnabled = value;
        SettingsService.Save(s);
    }

    // ── SelectedModifier ──────────────────────────────────────────────────────
    [ObservableProperty]
    private string _selectedModifier;

    partial void OnSelectedModifierChanged(string value)
    {
        // Sync to service
        WindowManagerService.Instance.HotkeyModifier = value;

        // Persist
        var s = SettingsService.Load();
        s.WindowManagerHotkey = value;
        SettingsService.Save(s);
    }

    // ── Constructor ───────────────────────────────────────────────────────────
    public WindowManagerViewModel()
    {
        var settings = SettingsService.Load();

        // Read hotkey first so that when we toggle IsEnabled the service already
        // has the correct modifier configured.
        var hotkey = settings.WindowManagerHotkey;
        if (string.IsNullOrWhiteSpace(hotkey) || !AvailableModifiers.Contains(hotkey))
            hotkey = "Alt";

        // Initialise backing fields directly to avoid triggering the partial
        // callbacks during construction (service / settings would be written
        // redundantly and — for IsEnabled — could call Enable() before the
        // modifier is set).
        _selectedModifier = hotkey;
        WindowManagerService.Instance.HotkeyModifier = hotkey;

        _isEnabled = settings.WindowManagerEnabled;
        if (_isEnabled)
            WindowManagerService.Instance.Enable();
        else
            WindowManagerService.Instance.Disable();
    }
}
