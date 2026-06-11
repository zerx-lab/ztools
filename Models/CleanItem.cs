using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ztools.Models;

/// <summary>Kind of development artifact / cache directory.</summary>
public enum CleanCategory
{
    NodeModules,
    RustTarget,
    GoModCache,
    GoBuildCache,
    NpmCache,
    PnpmStore,
    YarnCache,
    BunCache,
    CargoRegistry,
}

/// <summary>A scan hit: one directory that can be cleaned.</summary>
public partial class CleanItem : ObservableObject
{
    public required string Path { get; init; }
    public required CleanCategory Category { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>-1 = size not computed yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeDisplay))]
    private long _sizeBytes = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    private bool _isCleaning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    private bool _isCleaned;

    [ObservableProperty]
    private bool _hasError;

    public bool CanClean => !IsCleaning && !IsCleaned;
    public double RowOpacity => IsCleaned ? 0.4 : 1.0;

    public string SizeDisplay => SizeBytes < 0 ? "…" : FormatSize(SizeBytes);

    public string CategoryLabel => LabelFor(Category);

    /// <summary>Project folder name for project artifacts, leaf name otherwise.</summary>
    public string DisplayName
    {
        get
        {
            string? name = Category is CleanCategory.NodeModules or CleanCategory.RustTarget
                ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path))
                : System.IO.Path.GetFileName(Path);
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }

    public static string LabelFor(CleanCategory category) => category switch
    {
        CleanCategory.NodeModules   => "node_modules",
        CleanCategory.RustTarget    => "Rust target",
        CleanCategory.GoModCache    => "Go modules",
        CleanCategory.GoBuildCache  => "Go build",
        CleanCategory.NpmCache      => "npm cache",
        CleanCategory.PnpmStore     => "pnpm store",
        CleanCategory.YarnCache     => "Yarn cache",
        CleanCategory.BunCache      => "Bun cache",
        CleanCategory.CargoRegistry => "Cargo registry",
        _                           => "?",
    };

    // ── Category accent brushes (cached, immutable usage) ───────────────────
    private static readonly Color ColorNode  = Color.Parse("#8CC84B");
    private static readonly Color ColorRust  = Color.Parse("#FF7043");
    private static readonly Color ColorGo    = Color.Parse("#00ADD8");
    private static readonly Color ColorNpm   = Color.Parse("#E5484D");
    private static readonly Color ColorPnpm  = Color.Parse("#F69220");
    private static readonly Color ColorYarn  = Color.Parse("#3FA9F5");
    private static readonly Color ColorBun   = Color.Parse("#F472B6");
    private static readonly Color ColorCargo = Color.Parse("#DEA584");

    private static readonly IBrush BrushNode  = new SolidColorBrush(ColorNode);
    private static readonly IBrush BrushRust  = new SolidColorBrush(ColorRust);
    private static readonly IBrush BrushGo    = new SolidColorBrush(ColorGo);
    private static readonly IBrush BrushNpm   = new SolidColorBrush(ColorNpm);
    private static readonly IBrush BrushPnpm  = new SolidColorBrush(ColorPnpm);
    private static readonly IBrush BrushYarn  = new SolidColorBrush(ColorYarn);
    private static readonly IBrush BrushBun   = new SolidColorBrush(ColorBun);
    private static readonly IBrush BrushCargo = new SolidColorBrush(ColorCargo);

    // 14% alpha tinted tile backgrounds
    private const byte TileAlpha = 0x24;
    private static readonly IBrush TileNode  = new SolidColorBrush(ColorNode,  TileAlpha / 255.0);
    private static readonly IBrush TileRust  = new SolidColorBrush(ColorRust,  TileAlpha / 255.0);
    private static readonly IBrush TileGo    = new SolidColorBrush(ColorGo,    TileAlpha / 255.0);
    private static readonly IBrush TileNpm   = new SolidColorBrush(ColorNpm,   TileAlpha / 255.0);
    private static readonly IBrush TilePnpm  = new SolidColorBrush(ColorPnpm,  TileAlpha / 255.0);
    private static readonly IBrush TileYarn  = new SolidColorBrush(ColorYarn,  TileAlpha / 255.0);
    private static readonly IBrush TileBun   = new SolidColorBrush(ColorBun,   TileAlpha / 255.0);
    private static readonly IBrush TileCargo = new SolidColorBrush(ColorCargo, TileAlpha / 255.0);

    public IBrush BadgeBrush => BrushFor(Category);
    public IBrush TileBrush  => TileBrushFor(Category);
    public string IconData   => IconFor(Category);

    public static IBrush BrushFor(CleanCategory category) => category switch
    {
        CleanCategory.NodeModules   => BrushNode,
        CleanCategory.RustTarget    => BrushRust,
        CleanCategory.GoModCache    => BrushGo,
        CleanCategory.GoBuildCache  => BrushGo,
        CleanCategory.NpmCache      => BrushNpm,
        CleanCategory.PnpmStore     => BrushPnpm,
        CleanCategory.YarnCache     => BrushYarn,
        CleanCategory.BunCache      => BrushBun,
        CleanCategory.CargoRegistry => BrushCargo,
        _                           => BrushNode,
    };

    public static IBrush TileBrushFor(CleanCategory category) => category switch
    {
        CleanCategory.NodeModules   => TileNode,
        CleanCategory.RustTarget    => TileRust,
        CleanCategory.GoModCache    => TileGo,
        CleanCategory.GoBuildCache  => TileGo,
        CleanCategory.NpmCache      => TileNpm,
        CleanCategory.PnpmStore     => TilePnpm,
        CleanCategory.YarnCache     => TileYarn,
        CleanCategory.BunCache      => TileBun,
        CleanCategory.CargoRegistry => TileCargo,
        _                           => TileNode,
    };

    // ── Category icons (Material Design path data) ──────────────────────────
    private const string IconPackage  = "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15M5,15.91L11,19.29V12.58L5,9.21V15.91M19,15.91V9.21L13,12.58V19.29L19,15.91Z";
    private const string IconTarget   = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,6A6,6 0 0,1 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6M12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8M12,10A2,2 0 0,1 14,12A2,2 0 0,1 12,14A2,2 0 0,1 10,12A2,2 0 0,1 12,10Z";
    private const string IconLayers   = "M12,16L19.36,10.27L21,9L12,2L3,9L4.63,10.27M12,18.54L4.62,12.81L3,14.07L12,21.07L21,14.07L19.37,12.8L12,18.54Z";
    private const string IconWrench   = "M22.7,19L13.6,9.9C14.5,7.6 14,4.9 12.1,3C10.1,1 7.1,0.6 4.7,1.7L9,6L6,9L1.6,4.7C0.4,7.1 0.9,10.1 2.9,12.1C4.8,14 7.5,14.5 9.8,13.6L18.9,22.7C19.3,23.1 19.9,23.1 20.3,22.7L22.6,20.4C23.1,20 23.1,19.3 22.7,19Z";
    private const string IconArchive  = "M3,3H21V7H3V3M4,8H20V21H4V8M9.5,11A0.5,0.5 0 0,0 9,11.5V13H15V11.5A0.5,0.5 0 0,0 14.5,11H9.5Z";
    private const string IconGrid     = "M3,11H11V3H3M3,21H11V13H3M13,21H21V13H13M13,3V11H21V3";
    private const string IconRefresh  = "M19,8L15,12H18A6,6 0 0,1 12,18C11,18 10.03,17.75 9.2,17.3L7.74,18.76C8.97,19.54 10.43,20 12,20A8,8 0 0,0 20,12H23M6,12A6,6 0 0,1 12,6C13,6 13.97,6.25 14.8,6.7L16.26,5.24C15.03,4.46 13.57,4 12,4A8,8 0 0,0 4,12H1L5,16L9,12";
    private const string IconCookie   = "M12,3A9,9 0 0,0 3,12A9,9 0 0,0 12,21A9,9 0 0,0 21,12C21,11.5 20.96,11 20.87,10.5C20.6,10 20,10 20,10H18V9C18,8 17,8 17,8H15V7C15,6 14,6 14,6H13V4C13,3 12,3 12,3M9.5,6A1.5,1.5 0 0,1 11,7.5A1.5,1.5 0 0,1 9.5,9A1.5,1.5 0 0,1 8,7.5A1.5,1.5 0 0,1 9.5,6M6.5,10A1.5,1.5 0 0,1 8,11.5A1.5,1.5 0 0,1 6.5,13A1.5,1.5 0 0,1 5,11.5A1.5,1.5 0 0,1 6.5,10M11.5,11A1.5,1.5 0 0,1 13,12.5A1.5,1.5 0 0,1 11.5,14A1.5,1.5 0 0,1 10,12.5A1.5,1.5 0 0,1 11.5,11M16.5,12A1.5,1.5 0 0,1 18,13.5A1.5,1.5 0 0,1 16.5,15A1.5,1.5 0 0,1 15,13.5A1.5,1.5 0 0,1 16.5,12M11,16A1.5,1.5 0 0,1 12.5,17.5A1.5,1.5 0 0,1 11,19A1.5,1.5 0 0,1 9.5,17.5A1.5,1.5 0 0,1 11,16Z";
    private const string IconDatabase = "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z";

    public static string IconFor(CleanCategory category) => category switch
    {
        CleanCategory.NodeModules   => IconPackage,
        CleanCategory.RustTarget    => IconTarget,
        CleanCategory.GoModCache    => IconLayers,
        CleanCategory.GoBuildCache  => IconWrench,
        CleanCategory.NpmCache      => IconArchive,
        CleanCategory.PnpmStore     => IconGrid,
        CleanCategory.YarnCache     => IconRefresh,
        CleanCategory.BunCache      => IconCookie,
        CleanCategory.CargoRegistry => IconDatabase,
        _                           => IconPackage,
    };

    public static string FormatSize(long bytes) => bytes switch
    {
        < 0                      => "—",
        < 1024                   => $"{bytes} B",
        < 1024L * 1024           => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024    => $"{bytes / (1024.0 * 1024):0.#} MB",
        < 1024L * 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
        _                        => $"{bytes / (1024.0 * 1024 * 1024 * 1024):0.##} TB",
    };
}
