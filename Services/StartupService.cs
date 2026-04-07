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
    /// </summary>
    /// <returns>True if the operation succeeded.</returns>
    public static bool SetEnabled(bool enable)
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

                // Quote the path in case it contains spaces
                key.SetValue(AppName, $"\"{exePath}\"");
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
}
