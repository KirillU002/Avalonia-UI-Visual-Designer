using Avalonia;
using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly string[] NativeContainerModes = { DesignerLayoutModes.Absolute, DesignerLayoutModes.Stack, DesignerLayoutModes.Grid, DesignerLayoutModes.Flex, DesignerLayoutModes.Dock };
    private static readonly string[] DockSideChoices = { "Left", "Right", "Top", "Bottom" };

    private IEnumerable<PropertyGridRowViewModel> NativeContainerRows(DesignControlModel control, bool layoutOnly)
    {
        var mode = GetLayoutModeForParent(control.ParentId);
        if (DesignerLayoutModes.IsOrdered(mode) && !layoutOnly)
            yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.StackOrder), "Children order", control.StackOrder,
                "Порядок среди дочерних элементов текущего контейнера.", value => control.StackOrder = Math.Max(0, (int)value));
        if (mode == DesignerLayoutModes.Dock)
        {
            if (LayoutPlacementProperties.ProtectedPlacement(control) is not null)
                yield return CreateReadOnlyRow(PropertyGridCategoryLayout, LayoutPlacementProperties.DockKey, LayoutPlacementProperties.DockKey,
                    LayoutPlacementProperties.ReadString(control, LayoutPlacementProperties.DockKey) ?? "выражение", "Выражение сохранено; визуальный перенос не заменяет его литералом.");
            else yield return CreateEnumRow(PropertyGridCategoryLayout, LayoutPlacementProperties.DockKey, LayoutPlacementProperties.DockKey,
                LayoutPlacementProperties.GetDock(control).ToString(), DockSideChoices, "Сторона размещения в настоящем DockPanel; последний элемент может заполнять свободное место.",
                value => SetNativeLayoutLiteral(control, LayoutPlacementProperties.DockKey, value));
        }
        foreach (var key in LayoutPlacementProperties.SizeConstraintKeys)
        {
            if (LayoutPlacementProperties.ReadString(control, key) is { } expression)
                yield return CreateReadOnlyRow(PropertyGridCategoryLayout, key, key, expression, "Выражение ограничения размера сохранено.");
            else yield return CreateNumberRow(PropertyGridCategoryLayout, key, key, key.StartsWith("Max", StringComparison.Ordinal) ? LayoutPlacementProperties.Maximum(control, key) : LayoutPlacementProperties.Minimum(control, key),
                "Настоящее ограничение Avalonia в DIP; Max=Infinity снимает верхнее ограничение. Авторский Width/Height не меняется.", value =>
                {
                    if (double.IsPositiveInfinity(value) && key.StartsWith("Max", StringComparison.Ordinal))
                    {
                        var existing = control.CustomProperties.FirstOrDefault(p => p.Key == key); if (existing is not null) control.CustomProperties.Remove(existing);
                        NotifyDesignerStateChanged(); RebuildPropertyGrid("NativeConstraint");
                    }
                    else if (double.IsFinite(value) && value >= 0) SetNativeLayoutLiteral(control, key, value);
                    else StatusText = "Введите неотрицательный размер; Infinity допустим только для MaxWidth/MaxHeight.";
                });
        }
        if (!CanHostChildren(control)) yield break;
        if (!layoutOnly)
        {
            yield return CreateEnumRow(PropertyGridCategoryLayout, nameof(control.ChildLayoutMode), "Children Layout", GetLayoutModeForControl(control), NativeContainerModes,
                "Grid, StackPanel, WrapPanel, DockPanel или Canvas внутри этого контейнера. Dock доступен без нового компонента Toolbox.", value => SetNativeContainerMode(control, value));
            if (DesignerLayoutModes.IsFlow(GetLayoutModeForControl(control)))
                yield return CreateEnumRow(PropertyGridCategoryLayout, nameof(control.LayoutOrientation), "Orientation", control.LayoutOrientation,
                    AvailableLayoutOrientations, "Ориентация StackPanel или WrapPanel.", value => control.LayoutOrientation = value);
            if (GetLayoutModeForControl(control) == DesignerLayoutModes.Stack)
                yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.LayoutSpacing), "Spacing", control.LayoutSpacing,
                    "Расстояние между элементами StackPanel в DIP.", value => control.LayoutSpacing = Math.Max(0, value));
        }
        if (GetLayoutModeForControl(control) == DesignerLayoutModes.Dock)
        {
            var fill = control.CustomProperties.FirstOrDefault(p => p.Key == LayoutPlacementProperties.LastChildFillKey);
            if (fill is not null && fill.ValueJson is not ("true" or "false"))
                yield return CreateReadOnlyRow(PropertyGridCategoryLayout, LayoutPlacementProperties.LastChildFillKey, "LastChildFill", fill.ValueJson,
                    "Выражение сохранено. Сначала задайте LastChildFill явно для визуального размещения.");
            else yield return CreateBoolRow(PropertyGridCategoryLayout, LayoutPlacementProperties.LastChildFillKey, "LastChildFill",
                LayoutPlacementProperties.LastChildFill(control), "Последний дочерний элемент заполняет оставшееся место. Drag & Drop не меняет это значение.",
                value => SetNativeLayoutLiteral(control, LayoutPlacementProperties.LastChildFillKey, value));
        }
    }

    private void SetNativeLayoutLiteral(DesignControlModel control, string key, object value)
    {
        if (value is double number && !double.IsFinite(number)) { StatusText = "Введите конечное числовое значение размера."; return; }
        LayoutPlacementProperties.SetLiteral(control, key, value);
        NotifyDesignerStateChanged(); RebuildPropertyGrid("NativeLayoutProperty");
    }

    private void SetNativeContainerMode(DesignControlModel control, string value)
    {
        if (GetChildControls(control.Id).Any(c => LayoutPlacementProperties.ProtectedPlacement(c) is not null))
        { StatusText = "Layout не изменён: placement дочернего элемента содержит binding/CustomProperty. Измените его явно."; return; }
        control.ChildLayoutMode = value;
    }

    private static string NativeDockFillAttribute(DesignControlModel control)
        => LayoutPlacementProperties.ReadString(control, LayoutPlacementProperties.LastChildFillKey)
            ?? LayoutPlacementProperties.LastChildFill(control).ToString();

    // Value-only native geometry. No retained offscreen controls, timers or subscriptions.
    public IReadOnlyDictionary<string, AxamlLayoutProjection.ContainerMeasurement> NativeContainerMeasurements { get; private set; }
        = new Dictionary<string, AxamlLayoutProjection.ContainerMeasurement>();
    public IReadOnlyList<AxamlLayoutProjection.LayoutVisualHit> NativeVisualHits { get; private set; } = Array.Empty<AxamlLayoutProjection.LayoutVisualHit>();
    private IReadOnlyDictionary<string, string> _nativeContainerGeometry = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _nativeContainerDropKeys = new Dictionary<string, string>();
    private string _nativeGeometryFingerprint = "";
    private long _nativeGeometryVersion;

    public void UpdateNativeLayoutMeasurements(AxamlLayoutProjection.JsonLayoutTree tree, double width, double height)
    {
        NativeLayoutBounds = tree.Measure(width, height);
        NativeGridMeasurements = tree.MeasuredGrids;
        NativeContainerMeasurements = tree.MeasuredContainers;
        NativeVisualHits = tree.VisualHits;
        object RectValues(Rect rect) => new { rect.X, rect.Y, rect.Width, rect.Height };
        var geometry = tree.MeasuredContainers.ToDictionary(p => p.Key, p => JsonSerializer.Serialize(new
        {
            p.Value.Mode, Bounds = RectValues(p.Value.Bounds), Clip = RectValues(p.Value.Clip), p.Value.Horizontal, p.Value.LastChildFill,
            Tracks = tree.MeasuredGrids.TryGetValue(p.Key,out var grid) ? new { grid.Rows, grid.Columns } : null,
            Children = p.Value.Children.Select(c => new { c.Id, Bounds = RectValues(c.Bounds), c.Visible,
                Margin = new { c.Margin.Left, c.Margin.Top, c.Margin.Right, c.Margin.Bottom } })
        }));
        var fingerprint=GridDropLayoutKey;
        // Keys are small version tokens. Reuse is decided by exact input + geometry
        // comparisons, never hashes; unchanged redraws do not invalidate a live gesture.
        _nativeContainerDropKeys=geometry.ToDictionary(p=>p.Key,p=>_nativeGeometryFingerprint==fingerprint
            && _nativeContainerGeometry.TryGetValue(p.Key,out var previous) && previous==p.Value && _nativeContainerDropKeys.TryGetValue(p.Key,out var key)
                ? key : (++_nativeGeometryVersion).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _nativeContainerGeometry=geometry; _nativeGeometryFingerprint=fingerprint;
        NativeGridMeasurementKey = fingerprint;
    }

    public GridDropTarget ResolveLayoutDropTarget(Point point, DesignControlModel? moving = null, Point grabOffset = default, bool bypassSnap = false)
    {
        GridDropTarget Invalid(string reason, string parent = "", string mode = DesignerLayoutModes.Grid, Rect bounds = default)
            => new(DocumentSessionId, parent, -1, -1, moving?.GridRowSpan ?? 1, moving?.GridColumnSpan ?? 1,
                bounds, default, NativeGridMeasurementKey, false, reason, LayoutMode: mode,
                GrabOffset: grabOffset, BypassSnap: bypassSnap, UsesContainerHitTest: true);
        if (!UsesNativeLayout) return Invalid("Изменение иерархии исходного AXAML недоступно. Исходная разметка сохранена.");
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !ContainsDropPoint(new Rect(0, 0, PreviewFormWidth, PreviewFormHeight), point))
            return Invalid("Указатель вне формы. Drop отменён.");
        if (Controls.Count == 0 && NativeContainerMeasurements.Count == 0)
            UpdateNativeLayoutMeasurements(BuildNativeLayout(_ => throw new InvalidOperationException("Unexpected leaf.")), PreviewFormWidth, PreviewFormHeight);
        if (NativeGridMeasurementKey != GridDropLayoutKey || NativeContainerMeasurements.Count == 0)
            return Invalid("Layout изменился. Дождитесь перерисовки и повторите наведение.");

        // Native visual paint order includes nested branches, clipping and layout/scroll offsets.
        // A foreground leaf resolves to its own nearest container, never to a deeper sibling behind it.
        var hit = NativeVisualHits.LastOrDefault(h => h.Id != moving?.Id && ContainsDropPoint(h.Bounds, point) && ContainsDropPoint(h.Clip, point));
        var hostId = hit?.Id ?? "";
        var seen = new HashSet<string>();
        while (hostId.Length != 0 && seen.Add(hostId))
        {
            if (NativeContainerMeasurements.TryGetValue(hostId, out var candidate)
                && ContainsDropPoint(candidate.Bounds, point) && ContainsDropPoint(candidate.Clip, point)) break;
            hostId = GetControl(hostId)?.ParentId ?? "";
        }
        if (!NativeContainerMeasurements.TryGetValue(hostId, out var host) || !ContainsDropPoint(host.Bounds, point) || !ContainsDropPoint(host.Clip, point))
            return Invalid("Под указателем нет видимого поддерживаемого layout-контейнера.");
        var hostModel = GetControl(hostId);
        if (hostModel is not null && (hostModel.IsLocked || AncestorsOf(hostModel).Any(c => c.IsLocked)))
            return Invalid("Целевой контейнер или его родитель заблокирован.", hostId, host.Mode, host.Bounds);
        if (moving is not null && (hostId == moving.Id || IsDescendant(hostId, moving.Id)))
            return Invalid("Нельзя переместить контейнер в себя или своего потомка.", hostId, host.Mode, host.Bounds);
        if (moving is not null && LayoutPlacementProperties.ProtectedPlacement(moving) is { } protectedProperty)
            return Invalid($"Перенос отменён: {protectedProperty} задано через binding или отдельное CustomProperty. Измените размещение явно; значение сохранено.", hostId, host.Mode, host.Bounds);

        GridDropTarget Target(int index, string dock, Rect area, Rect? indicator = null, string note = "", Point canvas = default)
            => new(DocumentSessionId, hostId, -1, -1, moving?.GridRowSpan ?? 1, moving?.GridColumnSpan ?? 1,
                host.Bounds, area, _nativeContainerDropKeys[hostId], true, note, LayoutMode: host.Mode, InsertionIndex: index,
                DockSide: dock, CanvasPosition: canvas, InsertionIndicator: indicator, GrabOffset: grabOffset,
                BypassSnap: bypassSnap, UsesContainerHitTest: true);

        if (host.Mode == DesignerLayoutModes.Grid)
            return ResolveGridCellDropTarget(point, moving, hostId) with { UsesContainerHitTest = true, GrabOffset = grabOffset, BypassSnap = bypassSnap };
        if (host.Mode == DesignerLayoutModes.Absolute)
        {
            var local = point - host.Bounds.Position - new Vector(grabOffset.X, grabOffset.Y);
            var position = new Point(bypassSnap ? local.X : Snap(local.X), bypassSnap ? local.Y : Snap(local.Y));
            return Target(-1, "Left", host.Bounds, canvas: position);
        }
        var siblings = host.Children.Where(c => c.Id != moving?.Id).ToList();
        if (host.Mode == DesignerLayoutModes.Dock)
        {
            var property = hostModel?.CustomProperties.FirstOrDefault(p => p.Key == LayoutPlacementProperties.LastChildFillKey);
            if (property is not null && property.ValueJson is not ("true" or "false"))
                return Invalid("LastChildFill содержит выражение. Безопасный Dock Drop недоступен; значение сохранено.", hostId, host.Mode, host.Bounds);
            var x = (point.X - host.Bounds.X) / host.Bounds.Width; var y = (point.Y - host.Bounds.Y) / host.Bounds.Height;
            var center = x is > 0.25 and < 0.75 && y is > 0.25 and < 0.75;
            if (host.LastChildFill && center)
            {
                var last = host.Children.LastOrDefault();
                if (last is not null && last.Id != moving?.Id)
                    return Invalid("Центр занят последним элементом LastChildFill. Выберите сторону или явно отключите LastChildFill в Inspector.", hostId, host.Mode, host.Bounds);
                return Target(siblings.Count, "Fill", host.Bounds, note: "Последний элемент заполняет доступное место (LastChildFill=True).");
            }
            var side = new (double Distance, string Side)[] { (x, "Left"), (1 - x, "Right"), (y, "Top"), (1 - y, "Bottom") }.OrderBy(p => p.Distance).First().Side;
            var bounds = host.Bounds;
            var area = side switch
            {
                "Left" => new Rect(bounds.X, bounds.Y, bounds.Width / 4, bounds.Height),
                "Right" => new Rect(bounds.X + bounds.Width * 3 / 4, bounds.Y, bounds.Width / 4, bounds.Height),
                "Top" => new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height / 4),
                _ => new Rect(bounds.X, bounds.Y + bounds.Height * 3 / 4, bounds.Width, bounds.Height / 4)
            };
            var keepFill = host.LastChildFill && host.Children.LastOrDefault() is { } fill && fill.Id != moving?.Id;
            return Target(keepFill ? Math.Max(0, siblings.Count - 1) : siblings.Count, side, area,
                note: host.LastChildFill && siblings.Count == 0 ? "Первый элемент также является последним: LastChildFill заполняет свободное место." : "");
        }
        if (host.Mode is DesignerLayoutModes.Stack or DesignerLayoutModes.Flex)
        {
            var visible = siblings.Where(c => c.Visible).ToList();
            var line = visible;
            Rect Outer(AxamlLayoutProjection.LayoutChildMeasurement child) => child.Bounds.Inflate(child.Margin);
            double CrossStart(Rect rect) => host.Horizontal ? rect.Top : rect.Left;
            double CrossEnd(Rect rect) => host.Horizontal ? rect.Bottom : rect.Right;
            if (host.Mode == DesignerLayoutModes.Flex && visible.Count > 0)
            {
                var lines = new List<List<AxamlLayoutProjection.LayoutChildMeasurement>>();
                double end = double.NegativeInfinity;
                foreach (var child in visible)
                {
                    var outer = Outer(child);
                    if (lines.Count == 0 || CrossStart(outer) >= end - 0.1) { lines.Add(new()); end = CrossEnd(outer); }
                    else end = Math.Max(end, CrossEnd(outer));
                    lines[^1].Add(child);
                }
                var cross = host.Horizontal ? point.Y : point.X;
                line = lines.OrderBy(items =>
                {
                    var start = items.Min(c => CrossStart(Outer(c))); var finish = items.Max(c => CrossEnd(Outer(c)));
                    return cross < start ? start - cross : cross > finish ? cross - finish : 0;
                }).First();
            }
            var axis = host.Horizontal ? point.X : point.Y;
            var next = line.FirstOrDefault(c => axis < (host.Horizontal ? c.Bounds.Center.X : c.Bounds.Center.Y));
            var index = next is not null ? siblings.IndexOf(next) : line.Count == 0 ? siblings.Count : siblings.IndexOf(line[^1]) + 1;
            var previous = next is not null ? line.TakeWhile(c => c != next).LastOrDefault() : line.LastOrDefault();
            var before = previous is null ? (host.Horizontal ? host.Bounds.Left : host.Bounds.Top)
                : host.Horizontal ? Outer(previous).Right : Outer(previous).Bottom;
            var after = next is null ? before : host.Horizontal ? Outer(next).Left : Outer(next).Top;
            var location = next is not null && previous is not null ? (before + after) / 2 : next is not null ? after : before;
            var crossStart = line.Count == 0 ? CrossStart(host.Bounds) : line.Min(c => CrossStart(Outer(c)));
            var crossEnd = line.Count == 0 ? CrossEnd(host.Bounds) : line.Max(c => CrossEnd(Outer(c)));
            var indicator = host.Horizontal ? new Rect(location - 1, crossStart, 2, Math.Max(2, crossEnd - crossStart))
                : new Rect(crossStart, location - 1, Math.Max(2, crossEnd - crossStart), 2);
            return Target(index, "Left", host.Bounds, indicator);
        }
        return Invalid("Этот тип layout пока не поддерживает визуальный Drop.", hostId, host.Mode, host.Bounds);
    }

    private static void ApplyNativeDropPlacement(DesignControlModel model, GridDropTarget target, IEnumerable<DesignControlModel> models)
    {
        ApplyGridDropPlacement(model, target);
        if (!DesignerLayoutModes.IsOrdered(target.LayoutMode)) return;
        var siblings = models.Where(c => c.Id != model.Id && c.ParentId == target.ParentId).OrderBy(c => c.StackOrder).ToList();
        siblings.Insert(Math.Clamp(target.InsertionIndex, 0, siblings.Count), model);
        for (var index = 0; index < siblings.Count; index++) siblings[index].StackOrder = index;
    }

    public GridDropTarget TranslateCanvasDropPreview(GridDropTarget previous, GridDropTarget target, DesignControlModel model)
    {
        var position = target.CanvasPosition + new Vector(previous.CanvasHostShift.X, previous.CanvasHostShift.Y);
        var delta = position - previous.CanvasPosition;
        var preview = previous.PreviewBounds!.Value.Translate(delta);
        var clip = previous.PreviewClip ?? target.GridBounds;
        var overlap = NativeContainerMeasurements[target.ParentId].Children.Where(c => c.Visible && c.Id != model.Id)
            .Any(c =>
            {
                var bounds = c.Bounds.Translate(new Vector(-previous.CanvasHostShift.X, -previous.CanvasHostShift.Y));
                var intersection = bounds.Intersect(clip).Intersect(preview.Intersect(clip));
                return intersection.Width > .1 && intersection.Height > .1;
            });
        var pointer = target.GridBounds.Position + new Vector(target.CanvasPosition.X + target.GrabOffset.X, target.CanvasPosition.Y + target.GrabOffset.Y);
        var valid = ContainsDropPoint(clip, pointer);
        return target with { CanvasPosition = position, CanvasHostShift = previous.CanvasHostShift, PreviewClip = clip, PreviewBounds = preview,
            HasOverlap = overlap, IsValid = valid, Reason = valid ? target.Reason : "После переноса Canvas сместится; указатель вне его будущей области. Выберите другое место." };
    }

    public static string DescribeNativeDrop(GridDropTarget target)
    {
        if (!target.IsValid) return target.Reason;
        return (target.LayoutMode switch
        {
            DesignerLayoutModes.Stack => $"StackPanel · позиция {target.InsertionIndex + 1}",
            DesignerLayoutModes.Flex => $"WrapPanel · позиция {target.InsertionIndex + 1}",
            DesignerLayoutModes.Dock => $"DockPanel.Dock = {target.DockSide}",
            DesignerLayoutModes.Absolute => $"Canvas · X={target.CanvasPosition.X:0.#}, Y={target.CanvasPosition.Y:0.#}",
            _ => $"Grid · строка {target.Row}, столбец {target.Column} · span {target.RowSpan}×{target.ColumnSpan}"
        }) + (target.Reason.Length > 0 ? " · " + target.Reason : "");
    }
}
