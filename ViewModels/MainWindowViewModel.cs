using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ztools.I18n;
using ztools.Models;
using ztools.ViewModels;

namespace ztools.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // ── Icon geometry paths ──────────────────────────────────────────────────
    private const string IconGrid = "M3,3H11V11H3V3M13,3H21V11H13V3M3,13H11V21H3V13M13,13H21V21H13V13";
    private const string IconSettings = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z";

    // ── Locale accessor ──────────────────────────────────────────────────────
    public LocaleManager L => LocaleManager.Instance;

    // ── Page VMs (created once) ──────────────────────────────────────────────
    private readonly WindowManagerViewModel _windowManagerViewModel = new();
    private readonly SettingsViewModel _settingsViewModel = new();

    // ── Nav items ────────────────────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<NavItem> _navItems = [];

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    // ── Sidebar state ────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapseArrowData))]
    private bool _isSidebarExpanded = true;

    private double _sidebarWidth = 220;
    public double SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            if (SetProperty(ref _sidebarWidth, value))
                OnSidebarWidthChanged(value);
        }
    }

    // ── Current page ─────────────────────────────────────────────────────────
    /// <summary>The VM for the page currently shown in the main content area.</summary>
    [ObservableProperty]
    private object? _currentPageContent;

    // ── App version ──────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _appVersion = "ZTools v0.1.0";

    // ── Constants ────────────────────────────────────────────────────────────
    public const double SidebarCollapsedWidth = 52;
    public const double SidebarMinWidth = 160;
    public const double SidebarMaxWidth = 320;

    // Re-entrancy guard for width <-> expanded sync
    private bool _syncingState = false;

    // ── Derived ──────────────────────────────────────────────────────────────
    public string CollapseArrowData => IsSidebarExpanded
        ? "M15,18L9,12L15,6"   // chevron-left
        : "M9,18L15,12L9,6";   // chevron-right

    // ── Sidebar sync ─────────────────────────────────────────────────────────
    private void OnSidebarWidthChanged(double value)
    {
        if (_syncingState) return;
        _syncingState = true;
        try
        {
            IsSidebarExpanded = value > SidebarCollapsedWidth + 10;
        }
        finally
        {
            _syncingState = false;
        }
    }

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        if (_syncingState) return;
        _syncingState = true;
        try
        {
            if (!value && _sidebarWidth > SidebarCollapsedWidth + 10)
                _sidebarWidth = SidebarCollapsedWidth;
            else if (value && _sidebarWidth < SidebarMinWidth)
                _sidebarWidth = SidebarMinWidth;
            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(CollapseArrowData));
        }
        finally
        {
            _syncingState = false;
        }
    }

    // ── Constructor ──────────────────────────────────────────────────────────
    public MainWindowViewModel()
    {
        BuildNavItems();

        // Default page: Window Manager
        CurrentPageContent = _windowManagerViewModel;

        // Rebuild nav labels whenever locale changes
        LocaleManager.Instance.PropertyChanged += OnLocaleChanged;
    }

    private void OnLocaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Item[]")
        {
            var previousTag = SelectedNavItem?.Tag ?? "windowmanager";
            BuildNavItems();

            // Restore selection
            foreach (var nav in NavItems)
                nav.IsSelected = nav.Tag == previousTag;
            SelectedNavItem = null;
            foreach (var nav in NavItems)
                if (nav.IsSelected) { SelectedNavItem = nav; break; }
            if (SelectedNavItem is null && NavItems.Count > 0)
            {
                NavItems[0].IsSelected = true;
                SelectedNavItem = NavItems[0];
            }

            OnPropertyChanged(nameof(L));
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void ToggleSidebar()
    {
        SidebarWidth = IsSidebarExpanded ? SidebarCollapsedWidth : SidebarMinWidth;
    }

    [RelayCommand]
    private void SelectNav(NavItem? item)
    {
        if (item is null) return;

        foreach (var nav in NavItems)
            nav.IsSelected = false;

        item.IsSelected = true;
        SelectedNavItem = item;

        CurrentPageContent = item.Tag switch
        {
            "settings" => _settingsViewModel,
            "windowmanager" => _windowManagerViewModel,
            _ => _windowManagerViewModel,
        };
    }

    // ── Data builders ─────────────────────────────────────────────────────────
    private void BuildNavItems()
    {
        var L = LocaleManager.Instance;

        // Remember current tag before rebuild
        var currentTag = SelectedNavItem?.Tag ?? "windowmanager";

        NavItems =
        [
            new NavItem
            {
                Name       = L["Nav.WindowManager"],
                IconPath   = IconGrid,
                Tag        = "windowmanager",
                IsSelected = currentTag == "windowmanager",
            },
            new NavItem
            {
                Name       = L["Nav.Settings"],
                IconPath   = IconSettings,
                Tag        = "settings",
                IsSelected = currentTag == "settings",
            },
        ];

        // Keep SelectedNavItem in sync
        SelectedNavItem = null;
        foreach (var nav in NavItems)
            if (nav.IsSelected) { SelectedNavItem = nav; break; }

        if (SelectedNavItem is null && NavItems.Count > 0)
        {
            NavItems[0].IsSelected = true;
            SelectedNavItem = NavItems[0];
        }
    }
}
