namespace ztools.Models;

public class AppSettings
{
    public string Language { get; set; } = "system";   // "system" | "en-US" | "zh-CN"
    public string Theme { get; set; } = "dark";         // "dark" | "light" | "system"
    public bool StartWithWindows { get; set; } = false;
    public bool SilentStart { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
    public double SidebarWidth { get; set; } = 220;
    public bool WindowManagerEnabled { get; set; } = true;
    public string WindowManagerHotkey { get; set; } = "Alt";
}
