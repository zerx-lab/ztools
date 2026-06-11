using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ztools.I18n;
using ztools.Models;
using ztools.Services;

namespace ztools.ViewModels;

public partial class DevCleanerViewModel : ViewModelBase
{
    // ── Locale accessor ───────────────────────────────────────────────────────
    public LocaleManager L => LocaleManager.Instance;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<string> ScanPaths { get; } = [];
    public ObservableCollection<CleanItem> Results { get; } = [];
    public ObservableCollection<CleanGroup> Groups { get; } = [];
    public ObservableCollection<FilterChip> Chips { get; } = [];
    public ObservableCollection<CleanItem> GlobalCaches { get; } = [];

    // ── State ─────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string _scanStatus = "";

    [ObservableProperty]
    private string _resultsSummary = "";

    [ObservableProperty]
    private string _resultsSizeText = "";

    [ObservableProperty]
    private string _selectedSummary = "";

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _hasPaths;

    [ObservableProperty]
    private bool _hasVisible;

    [ObservableProperty]
    private string _filterText = "";

    partial void OnFilterTextChanged(string value) => ApplyFilters();

    private bool _isAllSelected;
    public bool IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (!SetProperty(ref _isAllSelected, value)) return;
            if (_syncingSelection) return;
            _syncingSelection = true;
            try
            {
                foreach (var g in Groups)
                    foreach (var item in g.Items)
                        if (!item.IsCleaned)
                            item.IsSelected = value;
            }
            finally { _syncingSelection = false; }
            UpdateSelection();
        }
    }

    private bool _syncingSelection;
    private bool _cachesLoaded;
    private CancellationTokenSource? _scanCts;
    private readonly SemaphoreSlim _sizeGate = new(4);

    // ── Constructor ───────────────────────────────────────────────────────────
    public DevCleanerViewModel()
    {
        var settings = SettingsService.Load();
        foreach (var p in settings.CleanerPaths)
            ScanPaths.Add(p);

        ScanPaths.CollectionChanged += OnScanPathsChanged;
        HasPaths = ScanPaths.Count > 0;

        // Existence checks only — sizes are computed lazily on first view.
        foreach (var hit in DevCleanService.GetKnownCaches())
            GlobalCaches.Add(new CleanItem { Path = hit.Path, Category = hit.Category });

        ResetChips();
    }

    private void OnScanPathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasPaths = ScanPaths.Count > 0;
        var s = SettingsService.Load();
        s.CleanerPaths = [.. ScanPaths];
        SettingsService.Save(s);
    }

    /// <summary>
    /// Called by the view every time the page is shown. Lazy work only happens
    /// here — never at app startup, since the VM is constructed eagerly.
    /// </summary>
    public void OnViewShown()
    {
        if (!_cachesLoaded)
        {
            _cachesLoaded = true;
            foreach (var item in GlobalCaches)
                _ = ComputeSizeAsync(item, updateSummary: false);
        }

        // Auto-scan configured paths on first entry (skip if user already
        // scanned or a scan is running).
        if (HasPaths && !HasResults && !IsScanning)
            _ = ScanAsync();
    }

    // ── Paths ─────────────────────────────────────────────────────────────────
    public void AddPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.TrimEnd('\\', '/');
        if (ScanPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            return;
        ScanPaths.Add(path);
    }

    [RelayCommand]
    private void RemovePath(string? path)
    {
        if (path is not null)
            ScanPaths.Remove(path);
    }

    // ── Scan ──────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || ScanPaths.Count == 0) return;

        Results.Clear();
        foreach (var g in Groups) g.Detach();
        Groups.Clear();
        ResetChips();
        FilterText = "";
        HasResults = false;
        HasVisible = false;
        ResultsSummary = "";
        ResultsSizeText = "";
        SelectedSummary = "";
        HasSelection = false;
        SetProperty(ref _isAllSelected, false, nameof(IsAllSelected));

        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;
        IsScanning = true;
        ScanStatus = "";

        var roots = ScanPaths.ToArray();
        try
        {
            await Task.Run(() => DevCleanService.Scan(
                roots,
                hit => Dispatcher.UIThread.Post(() => AddResult(hit)),
                dir => Dispatcher.UIThread.Post(() =>
                {
                    if (IsScanning)
                        ScanStatus = string.Format(L["Cleaner.ScanningFmt"], dir);
                }),
                ct), ct);
        }
        catch (OperationCanceledException) { }
        catch { /* swallow — partial results remain visible */ }
        finally
        {
            IsScanning = false;
            ScanStatus = "";
            _scanCts.Dispose();
            _scanCts = null;
            UpdateResultsSummary();
        }
    }

    [RelayCommand]
    private void StopScan() => _scanCts?.Cancel();

    private void AddResult(DevCleanService.ScanHit hit)
    {
        var item = new CleanItem { Path = hit.Path, Category = hit.Category };
        item.PropertyChanged += OnItemPropertyChanged;
        Results.Add(item);
        HasResults = true;
        BumpChip(item.Category);
        if (Passes(item))
        {
            GetOrCreateGroup(item.Category).Add(item);
            HasVisible = true;
        }
        UpdateResultsSummary();
        _ = ComputeSizeAsync(item, updateSummary: true);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CleanItem.IsSelected) && !_syncingSelection)
        {
            SyncAllSelected();
            UpdateSelection();
        }
    }

    private async Task ComputeSizeAsync(CleanItem item, bool updateSummary)
    {
        await _sizeGate.WaitAsync();
        try
        {
            var size = await Task.Run(() => DevCleanService.GetDirectorySize(item.Path));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                item.SizeBytes = size;
                if (updateSummary)
                {
                    UpdateResultsSummary();
                    UpdateSelection();
                }
            });
        }
        finally
        {
            _sizeGate.Release();
        }
    }

    // ── Clean ─────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task CleanSelectedAsync()
    {
        if (IsCleaning) return;
        var items = Results.Where(r => r.IsSelected && r.CanClean).ToList();
        if (items.Count == 0) return;

        IsCleaning = true;
        HasSelection = false;
        foreach (var item in items)
            await CleanOneAsync(item);
        IsCleaning = false;
        UpdateResultsSummary();
        UpdateSelection();
    }

    [RelayCommand]
    private async Task CleanItemAsync(CleanItem? item)
    {
        if (item is null || !item.CanClean) return;
        await CleanOneAsync(item);
        UpdateResultsSummary();
        UpdateSelection();
    }

    private static async Task CleanOneAsync(CleanItem item)
    {
        item.HasError = false;
        item.IsCleaning = true;
        try
        {
            await DevCleanService.DeleteDirectoryFastAsync(item.Path);
            item.IsCleaned = true;
            item.IsSelected = false;
            item.SizeBytes = 0;
        }
        catch
        {
            item.HasError = true;
        }
        finally
        {
            item.IsCleaning = false;
        }
    }

    // ── Filtering / grouping ────────────────────────────────────────────
    [RelayCommand]
    private void SelectChip(FilterChip? chip)
    {
        if (chip is null || chip.IsActive) return;
        foreach (var c in Chips) c.IsActive = false;
        chip.IsActive = true;
        ApplyFilters();
    }

    [RelayCommand]
    private static void ToggleGroup(CleanGroup? group)
    {
        if (group is not null)
            group.IsExpanded = !group.IsExpanded;
    }

    private void ResetChips()
    {
        Chips.Clear();
        Chips.Add(new FilterChip { Category = null, Label = L["Cleaner.Filter.All"], IsActive = true });
    }

    private bool Passes(CleanItem item)
    {
        FilterChip? active = null;
        foreach (var c in Chips)
            if (c.IsActive) { active = c; break; }
        if (active?.Category is CleanCategory cat && item.Category != cat)
            return false;
        return string.IsNullOrWhiteSpace(FilterText)
            || item.Path.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void BumpChip(CleanCategory category)
    {
        Chips[0].Count = Results.Count;

        FilterChip? chip = null;
        foreach (var c in Chips)
            if (c.Category == category) { chip = c; break; }

        if (chip is null)
        {
            chip = new FilterChip { Category = category, Label = CleanItem.LabelFor(category) };
            int i = 1;
            while (i < Chips.Count && Chips[i].Category!.Value < category) i++;
            Chips.Insert(i, chip);
        }
        chip.Count++;
    }

    private CleanGroup GetOrCreateGroup(CleanCategory category)
    {
        foreach (var g in Groups)
            if (g.Category == category) return g;

        var group = new CleanGroup
        {
            Category   = category,
            Label      = CleanItem.LabelFor(category),
            BadgeBrush = CleanItem.BrushFor(category),
            TileBrush  = CleanItem.TileBrushFor(category),
            IconData   = CleanItem.IconFor(category),
        };
        int i = 0;
        while (i < Groups.Count && Groups[i].Category < category) i++;
        Groups.Insert(i, group);
        return group;
    }

    private void ApplyFilters()
    {
        foreach (var g in Groups) g.Detach();
        Groups.Clear();
        foreach (var item in Results)
            if (Passes(item))
                GetOrCreateGroup(item.Category).Add(item);
        HasVisible = Groups.Count > 0;
        SyncAllSelected();
    }

    private void SyncAllSelected()
    {
        _syncingSelection = true;
        try
        {
            var active = Groups.SelectMany(g => g.Items).Where(r => !r.IsCleaned).ToList();
            SetProperty(ref _isAllSelected,
                        active.Count > 0 && active.All(r => r.IsSelected),
                        nameof(IsAllSelected));
        }
        finally { _syncingSelection = false; }
    }

    // ── Summaries ─────────────────────────────────────────────────────────────
    private void UpdateResultsSummary()
    {
        var active = Results.Where(r => !r.IsCleaned).ToList();
        long known = active.Where(r => r.SizeBytes > 0).Sum(r => r.SizeBytes);
        if (Results.Count == 0)
        {
            ResultsSummary = "";
            ResultsSizeText = "";
        }
        else
        {
            ResultsSummary = string.Format(L["Cleaner.FoundCountFmt"], active.Count);
            ResultsSizeText = CleanItem.FormatSize(known);
        }
    }

    private void UpdateSelection()
    {
        var selected = Results.Where(r => r.IsSelected && !r.IsCleaned).ToList();
        long size = selected.Where(r => r.SizeBytes > 0).Sum(r => r.SizeBytes);
        HasSelection = selected.Count > 0 && !IsCleaning;
        SelectedSummary = selected.Count == 0
            ? ""
            : string.Format(L["Cleaner.SelectedFmt"], selected.Count, CleanItem.FormatSize(size));
    }
}
