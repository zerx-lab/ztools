using System;
using System.Runtime.Versioning;
using Microsoft.Win32;


namespace ztools.Services;

/// <summary>
/// Manages the Windows "Start with Windows" registry entry under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ZTools";

    /// <summary>Returns true if the startup registry entry currently exists.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables or disables the startup entry.
    /// Pass <c>true</c> to add the entry, <c>false</c> to remove it.
    /// When <paramref name="silentStart"/> is true the entry will include the
    /// <c>--silent</c> flag so the app starts without showing its main window.
    /// </summary>
    /// <returns>True if the operation succeeded.</returns>
    public static bool SetEnabled(bool enable, bool silentStart = false)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key is null) return false;

            if (enable)
            {
                // Use the actual executable path so it survives being moved
                var exePath = Environment.ProcessPath
                              ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                              ?? string.Empty;

                if (string.IsNullOrWhiteSpace(exePath)) return false;

                // Quote the path in case it contains spaces; append --silent if requested
                var value = silentStart
                    ? $"\"{exePath}\" --silent"
                    : $"\"{exePath}\"";

                key.SetValue(AppName, value);
            }
            else
            {
                // Only delete if the value actually exists
                if (key.GetValue(AppName) is not null)
                    key.DeleteValue(AppName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Re-writes the startup entry (if it exists) to reflect the current
    /// <paramref name="silentStart"/> preference without toggling the enabled
    /// state.  No-op when startup is currently disabled.
    /// </summary>
    /// <returns>True if the operation succeeded or was a no-op.</returns>
    public static bool UpdateSilentFlag(bool silentStart)
    {
        if (!IsEnabled()) return true;          // nothing to update
        return SetEnabled(enable: true, silentStart: silentStart);
    }
}
