using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ztools.ViewModels;
using ztools.Views;

namespace ztools;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// Uses a static registry instead of reflection so it is trimming/AOT
/// compatible and avoids per-navigation reflection cost.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Registry = new()
    {
        [typeof(SettingsViewModel)] = static () => new SettingsView(),
        [typeof(WindowManagerViewModel)] = static () => new WindowManagerView(),
    };

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        if (Registry.TryGetValue(param.GetType(), out var factory))
            return factory();

        return new TextBlock { Text = "Not Found: " + param.GetType().FullName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
