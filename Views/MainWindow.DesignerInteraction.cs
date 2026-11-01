using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private DesignerInteractionMode _designerInteractionMode;
    private IPointer? _designerPointer;
    private InputElement? _designerCaptureOwner;
    private string _designerInteractionControlId = "";
    private string _designerInteractionSession = "";
    private Point _lastDesignerPointerPosition;
    private bool _lastDesignerLeftPressed;
    private string _lastDesignerHitTarget = "";
    private readonly List<DesignerPointerDiagnostic> _designerPointerDiagnostics = new();
    public DesignerInteractionMode CurrentDesignerInteractionMode => _designerInteractionMode;
    public IReadOnlyList<DesignerPointerDiagnostic> DesignerPointerDiagnostics => _designerPointerDiagnostics;

    private void InitializeDesignerInteraction()
    {
        DesignerCanvas.PointerCaptureLost += (_, _) => { _pendingContextMenuControlId = ""; _contextMenuPointer = null; };
        Deactivated += (_, _) => CancelPendingContextMenu();
        DesignerCanvas.AddHandler(InputElement.PointerPressedEvent, TraceDesignerPressed, RoutingStrategies.Tunnel, true);
        DesignerCanvas.AddHandler(InputElement.PointerReleasedEvent, TraceDesignerReleased, RoutingStrategies.Tunnel, true);
        DesignResizeHandle.AddHandler(InputElement.PointerPressedEvent, TraceDesignerPressed, RoutingStrategies.Tunnel, true);
        DesignResizeHandle.AddHandler(InputElement.PointerReleasedEvent, TraceDesignerReleased, RoutingStrategies.Tunnel, true);
        DesignerCanvas.AddHandler(DragDrop.DragLeaveEvent, (_, e) =>
        {
            var p = GetDesignCanvasPosition(e);
            if (!VM.UsesNativeLayout || p.X < 0 || p.Y < 0 || p.X >= VM.PreviewFormWidth || p.Y >= VM.PreviewFormHeight)
                FinishToolboxInteraction("toolbox-leave");
        }, RoutingStrategies.Bubble, true);
        Closing += (_, _) => CancelDesignerInteraction("window-closing", false);
        Closed += (_, _) => CancelDesignerInteraction("window-closed", false);
    }

    private void TraceDesignerPressed(object? sender, PointerPressedEventArgs e) => TraceDesignerInteraction("DESIGNER_POINTER_PRESSED", e);
    private void TraceDesignerReleased(object? sender, PointerReleasedEventArgs e) => TraceDesignerInteraction("DESIGNER_POINTER_RELEASED", e);

    private void RememberDesignerPointer(PointerEventArgs e)
    {
        _lastDesignerPointerPosition = GetDesignCanvasPosition(e);
        _lastDesignerLeftPressed = e.GetCurrentPoint(DesignerCanvas).Properties.IsLeftButtonPressed;
        _lastDesignerHitTarget = e.Source is Control control ? control.GetType().Name + (string.IsNullOrEmpty(control.Name) ? "" : "#" + control.Name) : e.Source?.GetType().Name ?? "None";
    }

    private void TraceDesignerInteraction(string code, PointerEventArgs? e = null, string reason = "", IPointer? pointer = null)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (e is not null) RememberDesignerPointer(e);
        var visual = e?.Source as Visual;
        var model = visual is null ? null : visual.GetVisualAncestors().Prepend(visual).OfType<Control>().Select(c => c.Tag).OfType<DesignControlModel>().FirstOrDefault();
        var record = new DesignerPointerDiagnostic(code, model?.Id ?? _designerInteractionControlId,
            string.Create(CultureInfo.InvariantCulture, $"{_lastDesignerPointerPosition.X:0.##},{_lastDesignerPointerPosition.Y:0.##}"),
            _lastDesignerHitTarget, _designerInteractionMode.ToString(), _lastDesignerLeftPressed,
            (e?.Pointer ?? pointer ?? _designerPointer)?.Captured is not null, vm.ActiveDocumentId, reason);
        _designerPointerDiagnostics.Add(record);
        if (_designerPointerDiagnostics.Count > 512) _designerPointerDiagnostics.RemoveAt(0);
        vm.TraceDocumentDebug(code, JsonSerializer.Serialize(record), toOutput: true);
    }

    private bool IsEmptyDesignerCanvasPress(PointerPressedEventArgs e) => !e.Handled
        && ReferenceEquals(e.Source, DesignerCanvas)
        && ReferenceEquals(DesignerCanvas.InputHitTest(e.GetPosition(DesignerCanvas)), DesignerCanvas)
        && e.GetCurrentPoint(DesignerCanvas).Properties.IsLeftButtonPressed;

    private void CaptureDesignerInteraction(DesignerInteractionMode mode, PointerEventArgs e, InputElement owner, string controlId = "")
    {
        _designerInteractionMode = mode;
        _designerInteractionControlId = controlId;
        _designerInteractionSession = VM.DocumentSessionId;
        _designerPointer = e.Pointer;
        _designerCaptureOwner = owner;
        owner.PointerCaptureLost += DesignerInteraction_CaptureLost;
        RememberDesignerPointer(e);
        // Surface relay sender is DesignerSurface; the actual input owner is explicit.
        e.Pointer.Capture(owner);
        TraceDesignerInteraction("DESIGNER_INTERACTION_STARTED", e);
    }

    private void DesignerInteraction_CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!ReferenceEquals(e.Source, _designerCaptureOwner)) return;
        TraceDesignerInteraction("DESIGNER_POINTER_CAPTURE_LOST", reason: "capture-lost");
        CancelDesignerInteraction("capture-lost");
    }

    private void FinishDesignerInteraction(string reason, PointerEventArgs? e = null)
    {
        var pointer = _designerPointer;
        var owner = _designerCaptureOwner;
        if (owner is not null) owner.PointerCaptureLost -= DesignerInteraction_CaptureLost;
        if (owner is not null && ReferenceEquals(pointer?.Captured, owner)) pointer!.Capture(null);
        TraceDesignerInteraction("DESIGNER_INTERACTION_COMPLETED", e, reason, pointer);
        _designerInteractionMode = DesignerInteractionMode.None;
        _designerCaptureOwner = null; _designerPointer = null;
        _designerInteractionControlId = ""; _designerInteractionSession = "";
    }

    private void CancelDesignerInteraction(string reason, bool scheduleRender = true)
    {
        CancelPendingContextMenu();
        if (_designerInteractionMode == DesignerInteractionMode.None) return;
        var mode = _designerInteractionMode;
        var sameSession = DataContext is MainWindowViewModel && _designerInteractionSession == VM.DocumentSessionId;
        if (IsGridControlInteraction || mode == DesignerInteractionMode.ToolboxDragDrop) CancelNativeGridDrag(reason);
        if (IsGridEditorPointerInteraction)
        {
            _attachedViewModel?.FinishGridTrackResize(false);
            _gridEditorPointerTarget = null;
        }
        if (mode is DesignerInteractionMode.Selecting or DesignerInteractionMode.MarqueeSelecting)
            TraceDesignerInteraction("MARQUEE_CANCELLED", reason: reason);
        FinishDesignerInteraction(reason);
        _isMarqueeSelecting = _isMarqueeAdditive = _isMarqueeToggle = false;
        _isDragging = _isResizing = _isResizingDesignSurface = false;
        _draggedBorder = null; _draggedModel = null; _resizingModel = null;
        _dragGestureSessionId = _resizeGestureSessionId = "";
        _dragSelectionRoots.Clear(); _dragRootStartPositions.Clear(); _marqueeBaseSelection.Clear();
        ClearSnapCandidateSnapshot(); ClearGuideOverlay(); ClearSelectionOverlay();
        if (sameSession && mode is DesignerInteractionMode.MovingControl or DesignerInteractionMode.ResizingControl or DesignerInteractionMode.ResizingSurface)
        {
            if (mode != DesignerInteractionMode.ResizingSurface)
            {
                VM.EndPropertyGridLiveGesture();
                VM.EndInspectorInteraction("Designer:" + reason, logToOutput: false, dumpState: false);
            }
            VM.CommitUndoBatch();
        }
        if (scheduleRender && DataContext is MainWindowViewModel) ScheduleDesignerRender();
    }

    private bool DesignerPointerCanContinue(PointerEventArgs e)
    {
        RememberDesignerPointer(e);
        if (!ReferenceEquals(e.Pointer, _designerPointer)) return false;
        if (!_lastDesignerLeftPressed || _designerInteractionSession != VM.DocumentSessionId)
        {
            CancelDesignerInteraction(!_lastDesignerLeftPressed ? "left-button-not-pressed" : "session-changed");
            return false;
        }
        return true;
    }

    private void BeginToolboxInteraction(PointerEventArgs? e = null)
    {
        if (_designerInteractionMode == DesignerInteractionMode.ToolboxDragDrop) return;
        CancelDesignerInteraction("toolbox-start", false);
        _designerInteractionMode = DesignerInteractionMode.ToolboxDragDrop;
        _designerInteractionSession = VM.DocumentSessionId;
        TraceDesignerInteraction("DESIGNER_INTERACTION_STARTED", e, "toolbox");
    }
    private void FinishToolboxInteraction(string reason)
    {
        if (_designerInteractionMode != DesignerInteractionMode.ToolboxDragDrop) return;
        CancelNativeGridDrag(reason);
        _lastDesignerLeftPressed = false;
        FinishDesignerInteraction(reason);
        ClearGuideOverlay();
    }
}
