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
    public string NativeGridMeasurementKey { get; set; } = "";
    public string GridDropLayoutKey => JsonSerializer.Serialize(new
    {
        Width = PreviewFormWidth, Height = PreviewFormHeight, SurfaceLayoutMode,
        SurfaceGridRowDefinitions, SurfaceGridColumnDefinitions, SurfaceLayoutRows, SurfaceLayoutColumns,
        Controls = Controls.Select(c => new { c.Id, c.Type, c.ParentId, c.X, c.Y, c.Width, c.Height, c.WidthMode, c.HeightMode,
            c.GridRow, c.GridColumn, c.GridRowSpan, c.GridColumnSpan, c.Rows, c.Columns, c.GridRowDefinitions, c.GridColumnDefinitions,
            c.Margin, c.Padding, c.BorderThickness, c.HorizontalAlignment, c.VerticalAlignment, c.IsVisible, c.IsLocked, c.ChildLayoutMode })
    });

    public DesignControlModel CreateGridDropPrototype(string type)
    {
        if (!_registry.TryGetControl(type, out _)) throw new InvalidOperationException("Компонент недоступен в Toolbox.");
        return CreateDefaultControl(type);
    }

    public GridDropTarget ResolveGridDropTarget(Point point, DesignControlModel? moving = null)
    {
        GridDropTarget Invalid(string reason) => new(DocumentSessionId, "", -1, -1,
            moving?.GridRowSpan ?? 1, moving?.GridColumnSpan ?? 1, default, default, "", false, reason);
        if (!UsesNativeLayout) return Invalid("Размещение по ячейкам доступно в native JSON Grid. Исходный AXAML не изменён.");
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !ContainsDropPoint(new Rect(0, 0, PreviewFormWidth, PreviewFormHeight), point))
            return Invalid("Указатель вне формы. Размещение отменено.");
        // An empty form can be measured without a preview factory or synthetic controls.
        if (NativeGridMeasurements.Count == 0 && Controls.Count == 0)
        {
            var tree = BuildNativeLayout(_ => throw new InvalidOperationException("Unexpected leaf in empty form."));
            NativeLayoutBounds = tree.Measure(PreviewFormWidth, PreviewFormHeight);
            NativeGridMeasurements = tree.MeasuredGrids; NativeGridMeasurementKey = GridDropLayoutKey;
        }
        if (NativeGridMeasurementKey != GridDropLayoutKey) return Invalid("Геометрия Grid обновляется. Повторите Drop после перерисовки.");

        var candidates = NativeGridMeasurements.Where(pair => ContainsDropPoint(pair.Value.Bounds, point))
            .Select(pair => (pair.Key, Grid: pair.Value, Host: GetControl(pair.Key)))
            .Where(pair => IsGridDropHostVisible(pair.Host, point))
            .OrderByDescending(pair => pair.Host is null ? 0 : GetControlDepth(pair.Host) + 1)
            .ThenByDescending(pair => pair.Host is null ? -1 : GetCanvasVisualZIndex(pair.Host)).ToArray();
        if (candidates.Length == 0) return Invalid("Под указателем нет доступного Grid.");
        var hit = candidates[0]; var grid = hit.Grid;
        var row = HitGridTrack(grid.Rows, point.Y - grid.Bounds.Y);
        var column = HitGridTrack(grid.Columns, point.X - grid.Bounds.X);
        if (row < 0 || column < 0) return Invalid("Указатель вне фактических tracks Grid. Нулевые tracks выбираются через Grid Editor.");
        var rowSpan = moving?.GridRowSpan ?? 1; var columnSpan = moving?.GridColumnSpan ?? 1;
        var reason = "";
        if (hit.Host is not null && (hit.Host.IsLocked || AncestorsOf(hit.Host).Any(c => c.IsLocked))) reason = "Целевой Grid или его родитель заблокирован.";
        if (moving is not null && (hit.Key == moving.Id || IsDescendant(hit.Key, moving.Id))) reason = "Нельзя переместить контейнер в себя или своего потомка.";
        if (rowSpan < 1 || columnSpan < 1 || rowSpan > grid.Rows.Count - row || columnSpan > grid.Columns.Count - column)
            reason = "RowSpan/ColumnSpan не помещается в целевую ячейку. Выберите другую ячейку или явно измените span в Inspector.";
        var x = grid.Bounds.X + grid.Columns.Take(column).Sum(t => t.Size);
        var y = grid.Bounds.Y + grid.Rows.Take(row).Sum(t => t.Size);
        var cell = new Rect(x, y, grid.Columns.Skip(column).Take(Math.Max(1, columnSpan)).Sum(t => t.Size), grid.Rows.Skip(row).Take(Math.Max(1, rowSpan)).Sum(t => t.Size));
        var geometry = NativeGridMeasurementKey + ":" + JsonSerializer.Serialize(grid);
        return new(DocumentSessionId, hit.Key, row, column, rowSpan, columnSpan, grid.Bounds, cell, geometry, reason.Length == 0, reason);
    }

    private static int HitGridTrack(IReadOnlyList<AxamlLayoutProjection.GridTrackMeasurement> tracks, double coordinate)
    {
        double offset = 0;
        for (var index = 0; index < tracks.Count; index++)
        {
            var next = offset + tracks[index].Size;
            if (tracks[index].Size > 0 && coordinate >= offset && coordinate < next) return index;
            offset = next;
        }
        return -1;
    }
    private static bool ContainsDropPoint(Rect bounds, Point point) => bounds.Width > 0 && bounds.Height > 0
        && point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    private IEnumerable<DesignControlModel> AncestorsOf(DesignControlModel model)
    {
        var seen = new HashSet<string> { model.Id }; var parent = GetControl(model.ParentId);
        while (parent is not null && seen.Add(parent.Id)) { yield return parent; parent = GetControl(parent.ParentId); }
    }
    private bool IsGridDropHostVisible(DesignControlModel? host, Point point)
    {
        if (host is null) return true;
        if (!host.IsVisible) return false;
        foreach (var ancestor in AncestorsOf(host))
        {
            if (!ancestor.IsVisible || !NativeLayoutBounds.TryGetValue(ancestor.Id, out var frame)) return false;
            var origin = GetAbsolutePosition(ancestor);
            // Designer child hosts clip their descendants to the measured container frame.
            if (!ContainsDropPoint(new Rect(origin.X, origin.Y, frame.Width, frame.Height), point)) return false;
        }
        return true;
    }

    public GridDropTarget EvaluateGridDropPreview(GridDropTarget target, DesignControlModel model, Func<DesignControlModel, Control> leafFactory)
    {
        if (!target.IsValid) return target;
        var models = Controls.Select(c => { var clone = c.Clone(); clone.Id = c.Id; return clone; }).ToList();
        var proposed = models.FirstOrDefault(c => c.Id == model.Id);
        if (proposed is null) { proposed = model.Clone(); proposed.Id = model.Id; models.Add(proposed); }
        ApplyGridDropPlacement(proposed, target);
        var tree = AxamlLayoutProjection.BuildJson(CreatePreviewDocumentSnapshot(), models, leafFactory, includeHidden: true);
        tree.Measure(PreviewFormWidth, PreviewFormHeight);
        Rect Bounds(string id)
        {
            var view = tree.Controls[id]; return new Rect(view.TranslatePoint(default, tree.Root) ?? default, view.Bounds.Size);
        }
        var preview = Bounds(proposed.Id);
        var clip = target.ParentId.Length == 0 ? new Rect(0, 0, PreviewFormWidth, PreviewFormHeight) : Bounds(target.ParentId);
        var visible = preview.Intersect(clip);
        var overlap = model.Opacity > 0.001 && models.Where(c => c.Id != model.Id && c.ParentId == target.ParentId && c.IsVisible && c.Opacity > 0.001)
            .Any(c => { var intersection = Bounds(c.Id).Intersect(clip).Intersect(visible); return intersection.Width > 0.1 && intersection.Height > 0.1; });
        return target with { PreviewBounds = preview, HasOverlap = overlap };
    }

    private static void ApplyGridDropPlacement(DesignControlModel model, GridDropTarget target)
    {
        model.ParentId = target.ParentId; model.GridRow = target.Row; model.GridColumn = target.Column;
        model.GridRowSpan = target.RowSpan; model.GridColumnSpan = target.ColumnSpan; model.X = model.Y = 0;
    }
    private bool ValidateGridDrop(GridDropTarget expected, Point point, DesignControlModel model)
    {
        var current = ResolveGridDropTarget(point, model);
        if (!expected.IsValid || expected.SessionId != DocumentSessionId || !current.IsValid
            || current.ParentId != expected.ParentId || current.Row != expected.Row || current.Column != expected.Column
            || current.RowSpan != expected.RowSpan || current.ColumnSpan != expected.ColumnSpan || current.GeometryKey != expected.GeometryKey)
        {
            StatusText = !current.IsValid ? current.Reason : "Grid изменился после подсветки. Drop отменён; повторите перемещение.";
            return false;
        }
        return true;
    }

    public bool CommitGridControlDrop(DesignControlModel control, GridDropTarget target, Point point)
    {
        if (!Controls.Contains(control) || control.IsLocked || !ValidateGridDrop(target, point, control)) return false;
        BeginGridEditorUndoBatch();
        try
        {
            ApplyGridDropPlacement(control, target); SelectSingleControl(control);
            RebuildPropertyGrid("GridDropMove"); NotifyDesignerStateChanged(); SetGridDropStatus(target);
        }
        finally { CommitGridEditorUndoBatch(); }
        return true;
    }

    public DesignControlModel? CommitGridToolboxDrop(string type, GridDropTarget target, Point point, string documentId)
    {
        if (!target.IsValid) { StatusText = target.Reason; return null; }
        if (!_registry.TryGetControl(type, out _)) { StatusText = "Компонент больше недоступен в Toolbox. Drop отменён."; return null; }
        var prototype = CreateDefaultControl(type);
        if (!IsActiveDocumentTarget(documentId, out _) || !ValidateGridDrop(target, point, prototype)) return null;
        BeginGridEditorUndoBatch();
        try
        {
            var created = CreateControlCore(type, point.X, point.Y, target.ParentId, false, ActiveDocumentId, "GridToolboxDrop", target);
            RebuildPropertyGrid("GridDropCreate"); NotifyDesignerStateChanged(); SetGridDropStatus(target); return created;
        }
        finally { CommitGridEditorUndoBatch(); }
    }
    private void SetGridDropStatus(GridDropTarget target) => StatusText = $"Элемент размещён в Grid: строка {target.Row}, столбец {target.Column}."
        + (target.HasOverlap ? " Внимание: элементы перекрываются. Существующие элементы не перемещены." : "");
}
