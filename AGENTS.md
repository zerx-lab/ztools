# AGENTS.md

## Project

Single-project Avalonia desktop app (`WinExe`, `net10.0`). One `.csproj`, no `.sln`, no test project, no CI, no task runner. All developer commands are raw `dotnet` CLI.

## Commands

```sh
dotnet restore                                          # restore NuGet packages
dotnet build                                            # debug build
dotnet run                                              # run in development
dotnet build -c Release                                 # release build
dotnet publish -c Release -r win-x64 --self-contained  # portable publish
```

No `dotnet test`, no linter, no formatter config — skip those steps.

## Architecture

```
Program.cs → App (Application)
  └── OnFrameworkInitializationCompleted()
        ├── SettingsService.Load()        → AppSettings (from %APPDATA%\ztools\settings.json)
        ├── LocaleManager.Instance.Load() → loads embedded locale JSON
        └── MainWindow { DataContext = MainWindowViewModel }
              ├── ViewLocator (IDataTemplate): *ViewModel → *View by name convention
              └── ContentControl switches between home grid and SettingsView
```

- **MVVM via CommunityToolkit.Mvvm** — use `[ObservableProperty]`, `[RelayCommand]` on `partial` classes; the source generator emits the boilerplate.
- **`AvaloniaUseCompiledBindingsByDefault=true`** — all XAML bindings are compiled and type-checked at build time. Binding errors are **compiler errors**, not runtime warnings.
- **ViewLocator** resolves views by replacing `ViewModel` → `View` in the fully-qualified type name. New VM/View pairs must follow the exact `*ViewModel` / `*View` naming convention or they will not resolve.

## Key Conventions

- **Locale strings** are loaded from embedded resources (`I18n/Locales/*.json`) via `LocaleManager`. In XAML use `{Binding L[key]}`. `LocaleManager` fires `"Item[]"` `PropertyChanged` on locale switch, refreshing all bound strings automatically. Locale codes: `en-US`, `zh-CN`, `system`.
- **Settings persistence** is at `%APPDATA%\ztools\settings.json` via `SettingsService` (static Load/Save). I/O errors are silently swallowed — do not rely on exceptions there.
- **Design tokens** (colors, sizes, brushes, gradients) live entirely in `App.axaml` as application-level resources. Do not hardcode colors in control-level XAML; reference existing resource keys.
- **`Nullable=enable`** is enforced. All new code must be null-safe.
- **No `.editorconfig`** in source root — the one in `obj/` is auto-generated; ignore it.

## Gotchas

- **No solution file.** Open/build with `dotnet build ztools.csproj` or just `dotnet build` from the project root.
- **`[STAThread]` on `Main`** is required for Win32 clipboard and COM interop — do not remove it.
- **Avalonia Diagnostics** (`Avalonia.Diagnostics` package) is included unconditionally but only useful in Debug. Press F12 inside the running app to open the inspector.
- **Locale JSON files are `EmbeddedResource`**, not `Content`. Adding a new locale file requires updating the `EmbeddedResource` glob in `.csproj` (currently `I18n/Locales/*.json`) and adding the code to `LocaleManager`.
- **Sidebar width** is clamped 52–320 px in `MainWindowViewModel`. The re-entrancy guard on `SidebarWidth`/`IsSidebarExpanded` setters is intentional — do not simplify it away.
- **Theme switching** (light/dark/system) is persisted but not yet fully implemented in the VM — the `ThemeLight`/`ThemeSystem` toggles save the setting but do not change the running theme. `RequestedThemeVariant="Dark"` is hardcoded in `App.axaml`.
- **`StartWithWindows` / `MinimizeToTray`** are persisted but OS-level registration is not yet implemented.
