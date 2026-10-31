using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private const string ToolboxGestureDataFormat = "formdesigner-toolbox-gesture";
    private sealed record GridDragOrigin(DesignControlModel Model, string Session, string DocumentId, string Parent, int Row, int Column, int RowSpan, int ColumnSpan);
    private GridDragOrigin? _gridDragOrigin;
    private Point _gridDragStart, _gridDropPoint;
    private GridDropTarget? _gridDropTarget;
    private DesignControlModel? _gridToolboxPrototype;
    private string _gridToolboxType = "", _gridToolboxGesture = "", _cancelledGridToolboxGesture = "", _gridDropTraceKey = "";
    private bool _gridDragAnnounced, _gridDropCommitted;
    private Point _nativeDragGrabOffset;
    private string _nativeDragSourcePlacement = "";
    private string _gridToolboxSourceDocument = "";
    private readonly List<GridDragDiagnostic> _gridDragDiagnostics = new();
    public IReadOnlyList<GridDragDiagnostic> GridDragDiagnostics => _gridDragDiagnostics;
    private bool IsGridControlInteraction => _designerInteractionMode is DesignerInteractionMode.GridControlPending or DesignerInteractionMode.MovingGridControl;

    private bool IsSourceGridDropBlocked(Point point) => VM.IsAxamlRoundTripDocument && VM.Controls.Where(VM.CanHostChildren)
        .Any(c =>
        {
            if (!VM.AxamlLayoutBounds.TryGetValue(c.Id, out var frame)) return false;
            var p = VM.GetAbsolutePosition(c);
            return point.X >= p.X && point.Y >= p.Y && point.X < p.X + frame.Width && point.Y < p.Y + frame.Height;
        });

    private void TraceGridDrag(string code, GridDropTarget? target = null, string reason = "", string? controlId = null)
    {
        if (_attachedViewModel is not { } vm) return;
        var origin = _gridDragOrigin;
        var record = new GridDragDiagnostic(code, origin?.DocumentId ?? (_gridToolboxSourceDocument.Length > 0 ? _gridToolboxSourceDocument : vm.ActiveDocumentId), controlId ?? origin?.Model.Id ?? "",
            origin?.Parent ?? "", target?.ParentId ?? "", origin?.Row ?? -1, origin?.Column ?? -1,
            target?.Row ?? -1, target?.Column ?? -1, _surfaceZoom, RenderScaling, reason);
        _gridDragDiagnostics.Add(record);
        if (_gridDragDiagnostics.Count > 512) _gridDragDiagnostics.RemoveAt(0);
        vm.TraceDocumentDebug(code, JsonSerializer.Serialize(record), toOutput: true);
    }

    private void BeginGridControlInteraction(DesignControlModel model, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (!VM.Controls.Contains(model)) { VM.StatusText = "Элемент уже не принадлежит активной форме. Перемещение отменено."; return; }
        if (VM.HasMultipleSelection && VM.IsControlSelected(model))
        { VM.StatusText = "Для переноса по ячейкам выберите один элемент. Групповое выделение сохранено."; return; }
        VM.SelectSingleControl(model);
        _gridDragOrigin = new(model, VM.DocumentSessionId, VM.ActiveDocumentId, model.ParentId, model.GridRow, model.GridColumn, model.GridRowSpan, model.GridColumnSpan);
        _gridDragStart = _gridDropPoint = GetDesignCanvasPosition(e);
        var origin = VM.GetAbsolutePosition(model);
        _nativeDragGrabOffset = new Point(_gridDragStart.X - origin.X, _gridDragStart.Y - origin.Y);
        _nativeDragSourcePlacement = NativeDragPlacementKey(model);
        _gridDropTarget = null; _gridDragAnnounced = _gridDropCommitted = false; _gridDropTraceKey = "";
        CaptureDesignerInteraction(DesignerInteractionMode.GridControlPending, e, DesignerCanvas, model.Id);
    }

    private void GridControl_PointerMoved(PointerEventArgs e)
    {
        if (!IsGridControlInteraction || !DesignerPointerCanContinue(e) || _gridDragOrigin is not { } origin) return;
        e.Handled = true;
        if (!GridDragSourceUnchanged(origin)) { CancelDesignerInteraction("grid-source-changed"); return; }
        var point = GetDesignCanvasPosition(e);
        if (_designerInteractionMode == DesignerInteractionMode.GridControlPending)
        {
            var delta = point - _gridDragStart;
            if (Math.Abs(delta.X) < MarqueeActivationThreshold && Math.Abs(delta.Y) < MarqueeActivationThreshold) return;
            _designerInteractionMode = DesignerInteractionMode.MovingGridControl;
            _gridDragAnnounced = true; TraceGridDrag("GRID_DRAG_START");
        }
        UpdateNativeGridDropPreview(point, origin.Model, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
    }

    private bool GridDragSourceUnchanged(GridDragOrigin origin) => origin.Session == VM.DocumentSessionId && VM.Controls.Contains(origin.Model)
        && origin.Model.ParentId == origin.Parent && origin.Model.GridRow == origin.Row && origin.Model.GridColumn == origin.Column
        && origin.Model.GridRowSpan == origin.RowSpan && origin.Model.GridColumnSpan == origin.ColumnSpan && !origin.Model.IsLocked
        && NativeDragPlacementKey(origin.Model) == _nativeDragSourcePlacement;

    private static string NativeDragPlacementKey(DesignControlModel model) => JsonSerializer.Serialize(new
        { model.ParentId, model.X, model.Y, model.StackOrder, Dock = LayoutPlacementProperties.ReadString(model, LayoutPlacementProperties.DockKey) });

    private void GridControl_PointerReleased(PointerReleasedEventArgs e)
    {
        if (!IsGridControlInteraction || !ReferenceEquals(e.Pointer, _designerPointer)) return;
        e.Handled = true;
        var origin = _gridDragOrigin; var target = _gridDropTarget;
        var moved = _designerInteractionMode == DesignerInteractionMode.MovingGridControl;
        var committed = false;
        if (moved && origin is not null && target is not null && GridDragSourceUnchanged(origin))
        {
            var reparent = origin.Parent != target.ParentId;
            if (reparent) TraceGridDrag("GRID_REPARENT_START", target);
            committed = VM.CommitGridControlDrop(origin.Model, target, GetDesignCanvasPosition(e));
            if (reparent) TraceGridDrag(committed ? "GRID_REPARENT_SUCCESS" : "GRID_REPARENT_FAILED", target, committed ? "" : VM.StatusText);
            if (committed) TraceGridDrag("GRID_DROP_COMMITTED", target, target.HasOverlap ? "overlap-warning" : "");
        }
        if (moved && !committed) TraceGridDrag("GRID_DROP_CANCELLED", target, target?.Reason ?? "no-valid-target");
        ClearNativeGridDragState(); FinishDesignerInteraction("grid-control-release", e); ClearGuideOverlay();
        // A plain click uses the existing deferred selection refresh. Rebuilding every
        // ruler here replaces the visuals before Avalonia's next input scene commits.
        if (moved) RenderDesigner();
    }

    private bool TryNativeGridToolboxPreview(DragEventArgs e)
    {
        if (!VM.UsesNativeLayout || !e.Data.Contains(ControlTypeDataFormat)) return false;
        var point = GetDesignCanvasPosition(e);
        BeginToolboxInteraction();
        var type = e.Data.Get(ControlTypeDataFormat) as string ?? "";
        var source = e.Data.Get(ControlSourceDocumentDataFormat) as string ?? VM.ActiveDocumentId;
        _gridToolboxSourceDocument = source;
        var gesture = e.Data.Get(ToolboxGestureDataFormat) as string ?? ("legacy:" + source + ":" + type);
        _gridToolboxGesture = gesture;
        if (type.Length == 0) return false;
        if (_gridToolboxPrototype is null || _gridToolboxType != type)
        {
            _gridToolboxType = type; _gridDropTarget = null;
            try { _gridToolboxPrototype = VM.CreateGridDropPrototype(type); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                _gridToolboxPrototype = null;
                _gridDropTarget = new(VM.DocumentSessionId, "", -1, -1, 1, 1, default, default, "", false, "Компонент больше недоступен в Toolbox. Начните перетаскивание заново.");
                _gridDropPoint = point; RenderNativeGridDropPreview(_gridDropTarget); e.Handled = true; e.DragEffects = DragDropEffects.None; return true;
            }
        }
        if (!_gridDragAnnounced) { _gridDragAnnounced = true; TraceGridDrag("GRID_DRAG_START"); }
        if (source != VM.ActiveDocumentId || gesture == _cancelledGridToolboxGesture)
        {
            _gridDropTarget = new(VM.DocumentSessionId, "", -1, -1, 1, 1, default, default, "", false,
                source != VM.ActiveDocumentId ? "Активная форма изменилась. Начните перетаскивание заново." : "Жест отменён. Начните новое перетаскивание из Toolbox.");
            _gridDropPoint = point; RenderNativeGridDropPreview(_gridDropTarget);
        }
        else UpdateNativeGridDropPreview(point, _gridToolboxPrototype, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
        e.DragEffects = _gridDropTarget?.IsValid == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        return true;
    }

    private bool TryNativeGridToolboxDrop(DragEventArgs e)
    {
        if (!VM.UsesNativeLayout || !e.Data.Contains(ControlTypeDataFormat)) return false;
        e.Handled = true; e.DragEffects = DragDropEffects.None;
        var source = e.Data.Get(ControlSourceDocumentDataFormat) as string ?? VM.ActiveDocumentId;
        var type = e.Data.Get(ControlTypeDataFormat) as string ?? "";
        var gesture = e.Data.Get(ToolboxGestureDataFormat) as string ?? ("legacy:" + source + ":" + type);
        var target = _gridDropTarget;
        try
        {
            if (source != VM.ActiveDocumentId || gesture == _cancelledGridToolboxGesture || gesture != _gridToolboxGesture || target is null || type != _gridToolboxType)
            { VM.StatusText = "Drop отменён: нет актуальной подсветки или изменилась форма."; return true; }
            var created = VM.CommitGridToolboxDrop(type, target, GetDesignCanvasPosition(e), source);
            if (created is null) { TraceGridDrag("GRID_DROP_CANCELLED", target, VM.StatusText); _gridDragAnnounced = false; return true; }
            _gridDropCommitted = true; e.DragEffects = DragDropEffects.Copy;
            TraceGridDrag("GRID_DROP_COMMITTED", target, target.HasOverlap ? "overlap-warning" : "", created.Id);
            return true;
        }
        finally { FinishToolboxInteraction("grid-toolbox-drop"); RenderDesigner(); }
    }

    private void UpdateNativeGridDropPreview(Point point, DesignControlModel model, bool bypassSnap = false)
    {
        _gridDropPoint = point;
        var target = VM.ResolveLayoutDropTarget(point, model, _gridDragOrigin is null ? default : _nativeDragGrabOffset, bypassSnap);
        try
        {
            if (_gridDropTarget is { IsValid: true, LayoutMode: DesignerLayoutModes.Absolute, PreviewBounds: not null } canvas
                && target.IsValid && target.LayoutMode == DesignerLayoutModes.Absolute && canvas.ParentId == target.ParentId && canvas.GeometryKey == target.GeometryKey)
                target = VM.TranslateCanvasDropPreview(canvas, target, model);
            else target = _gridDropTarget is { } prior && prior.GeometryKey == target.GeometryKey && prior.ParentId == target.ParentId
                && prior.Row == target.Row && prior.Column == target.Column && prior.IsValid == target.IsValid && prior.Reason == target.Reason
                && prior.LayoutMode == target.LayoutMode && prior.InsertionIndex == target.InsertionIndex && prior.DockSide == target.DockSide
                && prior.CanvasPosition == target.CanvasPosition && prior.BypassSnap == target.BypassSnap
                ? prior : VM.EvaluateGridDropPreview(target, model, CreatePreviewControl);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        { target = target with { IsValid = false, Reason = "Не удалось построить preview компонента: " + ex.GetType().Name }; }
        _gridDropTarget = target;
        var key = $"{target.ParentId}:{target.LayoutMode}:{target.Row}:{target.Column}:{target.InsertionIndex}:{target.DockSide}:{target.IsValid}:{target.HasOverlap}:{target.Reason}";
        if (key != _gridDropTraceKey)
        {
            _gridDropTraceKey = key;
            TraceGridDrag("GRID_CELL_HIT_TEST", target, target.Reason);
            TraceGridDrag("GRID_DROP_TARGET_RESOLVED", target, target.Reason);
            TraceGridDrag("GRID_DROP_PREVIEW", target, target.HasOverlap ? "overlap-warning" : target.Reason);
        }
        RenderNativeGridDropPreview(target);
    }

    private void RenderNativeGridDropPreview(GridDropTarget target)
    {
        ClearGuideOverlay();
        // This layer precedes DesignerCanvas in the surface tree. During a native
        // drop it must also cover opaque nested containers, without intercepting input.
        GuideOverlayCanvas.ZIndex = Math.Max(DesignerCanvas.ZIndex, SelectionOverlayCanvas.ZIndex) + 1;
        var color = target.IsValid ? (target.HasOverlap ? Colors.DarkOrange : Colors.DodgerBlue) : Colors.Crimson;
        void Box(Rect bounds, bool ghost)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var box = new Rectangle { Width = bounds.Width, Height = bounds.Height, Stroke = new SolidColorBrush(color), StrokeThickness = ghost ? 2 : 1,
                Fill = new SolidColorBrush(Color.FromArgb(ghost ? (byte)42 : (byte)12, color.R, color.G, color.B)), IsHitTestVisible = false };
            if (ghost) box.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 5, 3 };
            Canvas.SetLeft(box, bounds.X); Canvas.SetTop(box, bounds.Y); GuideOverlayCanvas.Children.Add(box);
        }
        Box(target.GridBounds, false);
        if (target.LayoutMode is DesignerLayoutModes.Grid or DesignerLayoutModes.Dock) Box(target.CellBounds, false);
        if (target.InsertionIndicator is { } insertion)
        {
            var line = new Rectangle { Width = insertion.Width, Height = insertion.Height, Fill = new SolidColorBrush(color), IsHitTestVisible = false };
            Canvas.SetLeft(line, insertion.X); Canvas.SetTop(line, insertion.Y); GuideOverlayCanvas.Children.Add(line);
        }
        if (target.IsValid && target.PreviewBounds is { } preview) Box(preview, true);
        var label = MainWindowViewModel.DescribeNativeDrop(target)
            + (target.HasOverlap ? " · Внимание: элементы перекрываются" : "");
        AddLayoutDropLabel(label, Math.Clamp(_gridDropPoint.X + 10, 0, Math.Max(0, VM.PreviewFormWidth - 240)),
            Math.Clamp(_gridDropPoint.Y + 12, 0, Math.Max(0, VM.PreviewFormHeight - 70)));
        if (GuideOverlayCanvas.Children.LastOrDefault() is Border { Child: TextBlock text } labelView)
        { labelView.MaxWidth = Math.Min(360, VM.PreviewFormWidth); labelView.CornerRadius = new CornerRadius(6); text.TextWrapping = TextWrapping.Wrap; }
        VM.StatusText = label;
    }

    private void RestoreNativeGridDropOverlay()
    {
        if (!_gridDragAnnounced || _gridDropTarget is not { } target) return;
        if (_gridDragOrigin is { } origin && !GridDragSourceUnchanged(origin))
        { CancelDesignerInteraction("drop-source-removed-or-changed", false); return; }
        if (target.ParentId.Length > 0 && VM.GetControl(target.ParentId) is null)
        { CancelDesignerInteraction("drop-target-removed", false); return; }
        var model = _gridDragOrigin?.Model ?? _gridToolboxPrototype;
        if (model is not null && target.IsValid && VM.ResolveLayoutDropTarget(_gridDropPoint, model, target.GrabOffset, target.BypassSnap).GeometryKey != target.GeometryKey)
            _gridDropTarget = target = target with { IsValid = false, Reason = "Grid изменился. Повторите наведение на ячейку." };
        RenderNativeGridDropPreview(target);
    }

    private void CancelNativeGridDrag(string reason)
    {
        if (_gridDragAnnounced && !_gridDropCommitted) TraceGridDrag("GRID_DROP_CANCELLED", _gridDropTarget, reason);
        if (_gridToolboxGesture.Length > 0 && reason is not "toolbox-leave" and not "toolbox-drag-completed" and not "grid-toolbox-drop")
            _cancelledGridToolboxGesture = _gridToolboxGesture;
        ClearNativeGridDragState();
    }
    private void ClearNativeGridDragState()
    {
        _gridDragOrigin = null; _gridDropTarget = null; _gridToolboxPrototype = null;
        _nativeDragGrabOffset = default; _nativeDragSourcePlacement = "";
        _gridToolboxType = _gridToolboxGesture = _gridDropTraceKey = _gridToolboxSourceDocument = ""; _gridDragAnnounced = _gridDropCommitted = false;
    }
}
