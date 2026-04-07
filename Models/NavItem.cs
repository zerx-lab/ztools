using CommunityToolkit.Mvvm.ComponentModel;

namespace ztools.Models;

public partial class NavItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private string _tag = string.Empty;

    [ObservableProperty]
    private bool _isSelected = false;
}
