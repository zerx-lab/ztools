using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ztools.ViewModels;

namespace ztools.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    // ── Splitter drag state ──────────────────────────────────────────────────
    private bool _isDraggingSplitter = false;
    private double _dragStartX = 0;
    private double _dragStartSidebarWidth = 0;

    public MainWindow()
    {
        InitializeComponent();
        this.Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        SetupWindowControls();
        SetupResizeBorders();
        SetupSidebarSplitter();
    }

    // ── Window chrome buttons ────────────────────────────────────────────────
    private void SetupWindowControls()
    {
        var dragRegion = this.FindControl<Border>("TitleBarDragRegion");
        if (dragRegion != null)
            dragRegion.PointerPressed += OnTitleBarPointerPressed;

        var btnMinimize = this.FindControl<Button>("BtnMinimize");
        var btnMaximize = this.FindControl<Button>("BtnMaximize");
        var btnClose = this.FindControl<Button>("BtnClose");

        if (btnMinimize != null) btnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        if (btnMaximize != null) btnMaximize.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        if (btnClose != null) btnClose.Click += (_, _) => Close();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ── Window edge resize grips ─────────────────────────────────────────────
    private void SetupResizeBorders()
    {
        AttachResize("ResizeTop", WindowEdge.North);
        AttachResize("ResizeBottom", WindowEdge.South);
        AttachResize("ResizeLeft", WindowEdge.West);
        AttachResize("ResizeRight", WindowEdge.East);
        AttachResize("ResizeTopLeft", WindowEdge.NorthWest);
        AttachResize("ResizeTopRight", WindowEdge.NorthEast);
        AttachResize("ResizeBottomLeft", WindowEdge.SouthWest);
        AttachResize("ResizeBottomRight", WindowEdge.SouthEast);
    }

    private void AttachResize(string controlName, WindowEdge edge)
    {
        var border = this.FindControl<Border>(controlName);
        if (border is null) return;

        border.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginResizeDrag(edge, e);
        };
    }

    // ── Sidebar drag-to-resize splitter ──────────────────────────────────────
    private void SetupSidebarSplitter()
    {
        var splitter = this.FindControl<Border>("SidebarSplitter");
        if (splitter is null) return;

        splitter.PointerPressed += OnSplitterPointerPressed;
        splitter.PointerMoved += OnSplitterPointerMoved;
        splitter.PointerReleased += OnSplitterPointerReleased;
        splitter.PointerCaptureLost += OnSplitterCaptureLost;
    }

    private void OnSplitterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var vm = ViewModel;
        if (vm is null) return;

        _isDraggingSplitter = true;
        _dragStartX = e.GetPosition(this).X;
        _dragStartSidebarWidth = vm.SidebarWidth;

        e.Pointer.Capture(sender as Border);
        e.Handled = true;
    }

    private void OnSplitterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingSplitter) return;

        var vm = ViewModel;
        if (vm is null) return;

        var currentX = e.GetPosition(this).X;
        var delta = currentX - _dragStartX;
        var newWidth = _dragStartSidebarWidth + delta;

        // Clamp between collapsed minimum and maximum
        newWidth = Math.Max(MainWindowViewModel.SidebarCollapsedWidth, newWidth);
        newWidth = Math.Min(MainWindowViewModel.SidebarMaxWidth, newWidth);

        // Snap to collapsed if dragged close to minimum
        if (newWidth < MainWindowViewModel.SidebarCollapsedWidth + 20)
            newWidth = MainWindowViewModel.SidebarCollapsedWidth;

        vm.SidebarWidth = newWidth;
        e.Handled = true;
    }

    private void OnSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishSplitterDrag(e.Pointer);
        e.Handled = true;
    }

    private void OnSplitterCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDraggingSplitter = false;
    }

    private void FinishSplitterDrag(IPointer pointer)
    {
        if (!_isDraggingSplitter) return;
        _isDraggingSplitter = false;
        pointer.Capture(null);
    }
}
