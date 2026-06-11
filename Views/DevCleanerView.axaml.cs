using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ztools.ViewModels;

namespace ztools.Views;

public partial class DevCleanerView : UserControl
{
    public DevCleanerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        (DataContext as DevCleanerViewModel)?.OnViewShown();
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevCleanerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = true,
        });

        foreach (var folder in folders)
        {
            var path = folder.TryGetLocalPath();
            if (path is not null)
                vm.AddPath(path);
        }
    }
}
