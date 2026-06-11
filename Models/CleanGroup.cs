using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ztools.Models;

/// <summary>A collapsible category group of scan results, sorted by size desc.</summary>
public partial class CleanGroup : ObservableObject
{
    public required CleanCategory Category { get; init; }
    public required string Label { get; init; }
    public required IBrush BadgeBrush { get; init; }
    public required IBrush TileBrush { get; init; }
    public required string IconData { get; init; }

    public ObservableCollection<CleanItem> Items { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private string _countText = "";

    [ObservableProperty]
    private string _sizeText = "";

    // Tri-state: true = all selected, false = none, null = mixed.
    private bool? _isChecked = false;
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (!SetProperty(ref _isChecked, value)) return;
            if (value is not bool b) return;
            foreach (var item in Items)
                if (!item.IsCleaned)
                    item.IsSelected = b;
        }
    }

    public void Add(CleanItem item)
    {
        Items.Insert(FindIndex(item.SizeBytes), item);
        item.PropertyChanged += OnItemChanged;
        Refresh();
    }

    /// <summary>Unsubscribe from all items (call before discarding the group).</summary>
    public void Detach()
    {
        foreach (var item in Items)
            item.PropertyChanged -= OnItemChanged;
    }

    private int FindIndex(long size)
    {
        int i = 0;
        while (i < Items.Count && Items[i].SizeBytes >= size) i++;
        return i;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanItem.SizeBytes) && sender is CleanItem item)
        {
            // Re-sort: remove + insert at the correct position.
            int old = Items.IndexOf(item);
            if (old >= 0)
            {
                Items.RemoveAt(old);
                Items.Insert(FindIndex(item.SizeBytes), item);
            }
            Refresh();
        }
        else if (e.PropertyName is nameof(CleanItem.IsSelected) or nameof(CleanItem.IsCleaned))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var active = Items.Where(x => !x.IsCleaned).ToList();
        long size = active.Where(x => x.SizeBytes > 0).Sum(x => x.SizeBytes);
        CountText = active.Count.ToString();
        SizeText = CleanItem.FormatSize(size);

        bool? state = active.Count == 0
            ? false
            : active.All(x => x.IsSelected) ? true
            : active.Any(x => x.IsSelected) ? null
            : false;
        SetProperty(ref _isChecked, state, nameof(IsChecked));
    }
}
