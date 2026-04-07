# ZTools

A lightweight Windows desktop utility suite built with Avalonia UI.

## Features

### Window Manager
Move and resize any window on screen without grabbing its title bar or edge.

| Gesture | Action |
|---|---|
| Hold modifier + **left-drag** anywhere in a window | Move the window |
| Hold modifier + **right-drag** anywhere in a window | Resize the window from the bottom-right corner |

The default modifier key is **Alt**. You can switch it to `Ctrl`, `Shift`, or `Win` in the Window Manager settings page.

### System Tray
ZTools lives in the system tray. When *Minimize to Tray* is enabled, closing the main window hides it rather than exiting — click the tray icon to bring it back.

### Settings
- **Language** — English (United States) or 简体中文, or follow the system locale.
- **Start with Windows** — adds / removes a registry entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **Minimize to Tray** — controls close-to-hide behaviour.

---

## Requirements

- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (build only — the published binary is self-contained)

---

## Getting Started

```sh
git clone https://github.com/zerx-lab/ztools.git
cd ztools
dotnet run
```

---

## Developer Commands

```sh
dotnet restore                                          # restore NuGet packages
dotnet build                                            # debug build
dotnet run                                              # run in development
dotnet build -c Release                                 # release build
dotnet publish -c Release -r win-x64 --self-contained  # portable publish
```

> There is no solution file. Build with `dotnet build ztools.csproj` or just `dotnet build` from the project root.

During a **Debug** build, press **F12** inside the running app to open the Avalonia Inspector.

---

## Project Structure

```
ztools/
├── Assets/                  # Icons and style resources
│   └── Styles/
├── I18n/
│   ├── LocaleManager.cs     # Locale loading, switching, and indexer
│   └── Locales/
│       ├── en-US.json
│       └── zh-CN.json
├── Models/
│   ├── AppSettings.cs       # Persisted settings data model
│   └── NavItem.cs           # Sidebar navigation item
├── Services/
│   ├── SettingsService.cs   # Load / save settings.json
│   ├── StartupService.cs    # Windows registry startup entry
│   ├── TrayService.cs       # System tray icon and minimize-to-tray
│   └── WindowManagerService.cs  # Global mouse hook, move/resize logic
├── ViewModels/
│   ├── MainWindowViewModel.cs   # Sidebar, navigation, sidebar width
│   ├── SettingsViewModel.cs     # Settings page bindings
│   ├── WindowManagerViewModel.cs
│   └── ViewModelBase.cs
├── Views/
│   ├── MainWindow.axaml(.cs)
│   ├── SettingsView.axaml(.cs)
│   └── WindowManagerView.axaml(.cs)
├── App.axaml(.cs)           # Application entry, design tokens, theme
├── Program.cs               # Entry point ([STAThread])
├── ViewLocator.cs           # *ViewModel → *View name convention
└── ztools.csproj
```

---

## Architecture

```
Program.cs → App (Application)
  └── OnFrameworkInitializationCompleted()
        ├── SettingsService.Load()         → AppSettings (%APPDATA%\ztools\settings.json)
        ├── LocaleManager.Instance.Load()  → embedded locale JSON
        └── MainWindow { DataContext = MainWindowViewModel }
              ├── ViewLocator (IDataTemplate): *ViewModel → *View
              └── ContentControl switches between feature views
```

**MVVM** is provided by [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/). Source generators emit boilerplate from `[ObservableProperty]` and `[RelayCommand]` attributes on `partial` classes.

**Compiled bindings** (`AvaloniaUseCompiledBindingsByDefault=true`) — all XAML bindings are type-checked at build time. Binding errors are compiler errors, not runtime warnings.

---

## Internationalization

Locale strings are loaded from embedded JSON files (`I18n/Locales/*.json`) via `LocaleManager`. In XAML, use `{Binding L[key]}`. Switching the locale fires a `PropertyChanged` event for `"Item[]"`, which refreshes all bound strings automatically without a restart.

Available locale codes: `en-US`, `zh-CN`, `system` (auto-detect from OS).

To add a new locale:
1. Create `I18n/Locales/<code>.json` with all the same keys as `en-US.json`.
2. The `EmbeddedResource` glob in `ztools.csproj` (`I18n/Locales/*.json`) picks it up automatically.
3. Add a `(code, displayName)` entry to `LocaleManager.AvailableLocales`.

---

## Settings Persistence

Settings are stored at `%APPDATA%\ztools\settings.json`. I/O errors are silently swallowed — the app falls back to defaults.

| Key | Type | Default | Description |
|---|---|---|---|
| `Language` | string | `"system"` | `"system"` \| `"en-US"` \| `"zh-CN"` |
| `Theme` | string | `"dark"` | `"dark"` \| `"light"` \| `"system"` |
| `StartWithWindows` | bool | `false` | Registry startup entry |
| `MinimizeToTray` | bool | `false` | Close hides window instead of exiting |
| `SidebarWidth` | double | `220` | Last sidebar width in pixels (52–320) |
| `WindowManagerEnabled` | bool | `true` | Global mouse hook active |
| `WindowManagerHotkey` | string | `"Alt"` | Modifier key (`Alt`, `Ctrl`, `Shift`, `Win`) |

---

## Stack

| Package | Version |
|---|---|
| [Avalonia](https://avaloniaui.net/) | 11.3.12 |
| Avalonia.Themes.Fluent | 11.3.12 |
| Avalonia.Fonts.Inter | 11.3.12 |
| Avalonia.Diagnostics | 11.3.12 (Debug only) |
| CommunityToolkit.Mvvm | 8.2.1 |
| Target framework | net10.0 |

---

## License

MIT