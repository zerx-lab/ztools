using CommunityToolkit.Mvvm.ComponentModel;

namespace ztools.Models;

/// <summary>A single-select category filter chip. Category null = "All".</summary>
public partial class FilterChip : ObservableObject
{
    public CleanCategory? Category { get; init; }

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    private int _count;

    [ObservableProperty]
    private bool _isActive;

    public bool HasCount => Count > 0;
}
