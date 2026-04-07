using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace ztools.I18n;

public sealed class LocaleManager : INotifyPropertyChanged
{
    public static readonly LocaleManager Instance = new();

    private Dictionary<string, string> _strings = new();
    private string _currentLocale = "en-US";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly List<(string Code, string DisplayName)> AvailableLocales =
    [
        ("system", "Follow System / 跟随系统"),
        ("en-US",  "English (United States)"),
        ("zh-CN",  "简体中文"),
    ];

    private LocaleManager() { }

    public string CurrentLocale => _currentLocale;

    /// <summary>Indexer — usage: LocaleManager.Instance["Key"]</summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var v) ? v : $"[{key}]";

    public void Load(string localeCode)
    {
        // "system" means detect from OS
        if (localeCode == "system" || string.IsNullOrWhiteSpace(localeCode))
            localeCode = DetectSystemLocale();

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = $"ztools.I18n.Locales.{localeCode}.json";

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            // Fallback to en-US
            localeCode = "en-US";
            var fallbackName = "ztools.I18n.Locales.en-US.json";
            using var fallback = asm.GetManifestResourceStream(fallbackName);
            if (fallback != null)
                _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(fallback) ?? new();
            else
                _strings = new();
        }
        else
        {
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }

        _currentLocale = localeCode;

        // Notify indexer consumers (bindings like L[Key])
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLocale)));
    }

    private static string DetectSystemLocale()
    {
        var culture = CultureInfo.CurrentUICulture;

        // Try exact match first (e.g. "zh-CN" == "zh-CN")
        foreach (var (code, _) in AvailableLocales)
        {
            if (code == "system") continue;
            if (culture.Name.Equals(code, StringComparison.OrdinalIgnoreCase))
                return code;
        }

        // Try language match (e.g. system "zh-TW" -> available "zh-CN")
        foreach (var (code, _) in AvailableLocales)
        {
            if (code == "system") continue;
            try
            {
                if (culture.TwoLetterISOLanguageName.Equals(
                        new CultureInfo(code).TwoLetterISOLanguageName,
                        StringComparison.OrdinalIgnoreCase))
                    return code;
            }
            catch
            {
                // Invalid culture code — skip
            }
        }

        return "en-US";
    }
}
