using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FormDesigner.DesignerSystem;
using FormDesigner.ViewModels;
using System;

namespace FormDesigner.Views;

/// <summary>
/// Переиспользуемая визуальная поверхность конструктора. Она не знает о Window
/// и передаёт host-specific действия через узкий набор событий.
/// </summary>
public partial class DesignerSurface : UserControl
{
    public static readonly StyledProperty<object?> ContextProperty =
        AvaloniaProperty.Register<DesignerSurface, object?>(nameof(Context));

    public static readonly StyledProperty<DesignerDocumentSession?> SessionProperty =
        AvaloniaProperty.Register<DesignerSurface, DesignerDocumentSession?>(nameof(Session));

    private readonly DesignerSurfaceViewModel _viewModel = new();
    private DesignerDocumentSession? _attachedSession;

    public DesignerSurface()
    {
        InitializeComponent();

        DesignerViewportScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnViewportPointerWheelChanged, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerPressedEvent, OnViewportPointerPressed, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerMovedEvent, OnViewportPointerMoved, RoutingStrategies.Tunnel, true);
        DesignerViewportScrollViewer.AddHandler(InputElement.PointerReleasedEvent, OnViewportPointerReleased, RoutingStrategies.Tunnel, true);
        DesignerCanvas.AddHandler(InputElement.PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Bubble, true);
        DesignerCanvas.AddHandler(InputElement.PointerMovedEvent, OnCanvasPointerMoved, RoutingStrategies.Bubble, true);
        DesignerCanvas.AddHandler(InputElement.PointerReleasedEvent, OnCanvasPointerReleased, RoutingStrategies.Bubble, true);
        DesignerCanvas.AddHandler(DragDrop.DragOverEvent, OnCanvasDragOver);
        DesignerCanvas.AddHandler(DragDrop.DropEvent, OnCanvasDrop);
        DesignResizeHandle.AddHandler(InputElement.PointerPressedEvent, OnDesignResizeHandlePointerPressed, RoutingStrategies.Bubble, true);
        DesignResizeHandle.AddHandler(InputElement.PointerMovedEvent, OnDesignResizeHandlePointerMoved, RoutingStrategies.Bubble, true);
        DesignResizeHandle.AddHandler(InputElement.PointerReleasedEvent, OnDesignResizeHandlePointerReleased, RoutingStrategies.Bubble, true);
        MiniMapCanvas.AddHandler(InputElement.PointerPressedEvent, OnMiniMapPointerPressed, RoutingStrategies.Bubble, true);
        MiniMapCanvas.AddHandler(InputElement.PointerMovedEvent, OnMiniMapPointerMoved, RoutingStrategies.Bubble, true);
        MiniMapCanvas.AddHandler(InputElement.PointerReleasedEvent, OnMiniMapPointerReleased, RoutingStrategies.Bubble, true);

        AttachedToVisualTree += (_, _) =>
        {
            AttachSession(Session);
            ReportDiagnostic("DESIGNER_SURFACE_CREATED", "surface attached");
        };
        DetachedFromVisualTree += (_, _) => DetachSession();
    }

    public object? Context
    {
        get => GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public DesignerDocumentSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public DesignerSurfaceViewModel ViewModel => _viewModel;
    public ScrollViewer ViewportScrollViewer => DesignerViewportScrollViewer;
    public Canvas ViewportRoot => DesignViewportRoot;
    public Canvas SurfaceHost => DesignSurfaceHost;
    public Border SurfaceTitleBar => DesignSurfaceTitleBar;
    public Border SurfaceBorder => DesignSurfaceBorder;
    public Canvas GridOverlay => GridOverlayCanvas;
    public Canvas GuideOverlay => GuideOverlayCanvas;
    public Canvas Canvas => DesignerCanvas;
    public Canvas SelectionOverlay => SelectionOverlayCanvas;
    public Border ResizeHandle => DesignResizeHandle;
    public Border MiniMap => MiniMapHost;
    public Canvas MiniMapCanvasControl => MiniMapCanvas;
    public TextBlock MiniMapStatus => MiniMapStatusTextBlock;
    public ComboBox ZoomPreset => ZoomPresetComboBox;

    public event EventHandler<DesignerSurfaceDiagnosticEventArgs>? DiagnosticReported;
    public event EventHandler<PointerWheelEventArgs>? ViewportPointerWheelChanged;
    public event EventHandler<PointerPressedEventArgs>? ViewportPointerPressed;
    public event EventHandler<PointerEventArgs>? ViewportPointerMoved;
    public event EventHandler<PointerReleasedEventArgs>? ViewportPointerReleased;
    public event EventHandler<PointerPressedEventArgs>? CanvasPointerPressed;
    public event EventHandler<PointerEventArgs>? CanvasPointerMoved;
    public event EventHandler<PointerReleasedEventArgs>? CanvasPointerReleased;
    public event EventHandler<DragEventArgs>? CanvasDragOver;
    public event EventHandler<DragEventArgs>? CanvasDrop;
    public event EventHandler<PointerPressedEventArgs>? DesignResizeHandlePointerPressed;
    public event EventHandler<PointerEventArgs>? DesignResizeHandlePointerMoved;
    public event EventHandler<PointerReleasedEventArgs>? DesignResizeHandlePointerReleased;
    public event EventHandler<PointerPressedEventArgs>? MiniMapPointerPressed;
    public event EventHandler<PointerEventArgs>? MiniMapPointerMoved;
    public event EventHandler<PointerReleasedEventArgs>? MiniMapPointerReleased;
    public event EventHandler<RoutedEventArgs>? ZoomOutRequested;
    public event EventHandler<RoutedEventArgs>? ResetZoomRequested;
    public event EventHandler<RoutedEventArgs>? ZoomInRequested;
    public event EventHandler<SelectionChangedEventArgs>? ZoomPresetChanged;

    /// <summary>Позволяет переходному host bridge записать lifecycle-событие surface.</summary>
    public void ReportHostDiagnostic(string eventName, string details) => ReportDiagnostic(eventName, details);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContextProperty)
        {
            _viewModel.Context = Context;
            ReportDiagnostic("DESIGNER_SURFACE_CONTEXT_ATTACHED", Context?.GetType().FullName ?? "null");
        }
        else if (change.Property == SessionProperty)
        {
            DetachSession();
            _viewModel.Session = Session;
            AttachSession(Session);
            ReportDiagnostic(Session is null ? "DESIGNER_SURFACE_SESSION_DETACHED" : "DESIGNER_SURFACE_SESSION_ATTACHED", Session?.DocumentId ?? "-");
        }
    }

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e) => ViewportPointerWheelChanged?.Invoke(this, e);
    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e) => ViewportPointerPressed?.Invoke(this, e);
    private void OnViewportPointerMoved(object? sender, PointerEventArgs e) => ViewportPointerMoved?.Invoke(this, e);
    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e) => ViewportPointerReleased?.Invoke(this, e);
    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e) => CanvasPointerPressed?.Invoke(this, e);
    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e) => CanvasPointerMoved?.Invoke(this, e);
    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e) => CanvasPointerReleased?.Invoke(this, e);
    private void OnCanvasDragOver(object? sender, DragEventArgs e) => CanvasDragOver?.Invoke(this, e);
    private void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        ReportDiagnostic("DESIGNER_SURFACE_DROP", "Canvas drop requested");
        CanvasDrop?.Invoke(this, e);
    }

    private void OnDesignResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ReportDiagnostic("DESIGNER_SURFACE_RESIZE", "design surface resize started");
        DesignResizeHandlePointerPressed?.Invoke(this, e);
    }
    private void OnDesignResizeHandlePointerMoved(object? sender, PointerEventArgs e) => DesignResizeHandlePointerMoved?.Invoke(this, e);
    private void OnDesignResizeHandlePointerReleased(object? sender, PointerReleasedEventArgs e) => DesignResizeHandlePointerReleased?.Invoke(this, e);
    private void OnMiniMapPointerPressed(object? sender, PointerPressedEventArgs e) => MiniMapPointerPressed?.Invoke(this, e);
    private void OnMiniMapPointerMoved(object? sender, PointerEventArgs e) => MiniMapPointerMoved?.Invoke(this, e);
    private void OnMiniMapPointerReleased(object? sender, PointerReleasedEventArgs e) => MiniMapPointerReleased?.Invoke(this, e);
    private void ZoomOutButton_Click(object? sender, RoutedEventArgs e) => ZoomOutRequested?.Invoke(this, e);
    private void ResetZoomButton_Click(object? sender, RoutedEventArgs e) => ResetZoomRequested?.Invoke(this, e);
    private void ZoomInButton_Click(object? sender, RoutedEventArgs e) => ZoomInRequested?.Invoke(this, e);
    private void ZoomPresetComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ZoomPresetChanged?.Invoke(this, e);

    private void ReportDiagnostic(string eventName, string details) =>
        DiagnosticReported?.Invoke(this, new DesignerSurfaceDiagnosticEventArgs(eventName, details));

    private void AttachSession(DesignerDocumentSession? session)
    {
        if (session is null || ReferenceEquals(_attachedSession, session))
            return;

        DetachSession();
        _attachedSession = session;
        session.SelectionChanged += Session_SelectionChanged;
    }

    private void DetachSession()
    {
        if (_attachedSession is null)
            return;

        _attachedSession.SelectionChanged -= Session_SelectionChanged;
        _attachedSession = null;
        ReportDiagnostic("DESIGNER_SURFACE_SESSION_DETACHED", "previous session detached");
    }

    private void Session_SelectionChanged(object? sender, DesignerDocumentSessionSelectionChangedEventArgs e)
    {
        ReportDiagnostic(
            "DESIGNER_SURFACE_SELECTION_SYNC",
            $"old={e.OldSelectedControl?.Id ?? "-"}; new={e.SelectedControl?.Id ?? "-"}; count={e.SelectedControlIds.Count}");
    }
}

public sealed class DesignerSurfaceDiagnosticEventArgs : EventArgs
{
    public DesignerSurfaceDiagnosticEventArgs(string eventName, string details)
    {
        EventName = eventName;
        Details = details;
    }

    public string EventName { get; }
    public string Details { get; }
}
