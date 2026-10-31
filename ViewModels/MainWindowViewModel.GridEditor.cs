using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyDictionary<string, AxamlLayoutProjection.GridMeasurement> NativeGridMeasurements { get; set; }
        = new Dictionary<string, AxamlLayoutProjection.GridMeasurement>();
    public ObservableCollection<string> GridLengthKinds { get; } = new() { "Auto", "Star", "Pixel" };
    public bool CanEditGridDefinitions => !IsAxamlRoundTripDocument;
    public bool IsGridDefinitionReadOnly => IsAxamlRoundTripDocument;
    public string ActiveGridEditorId => GetActiveLayoutHost()?.Id ?? "";
    public bool HasPendingGridRemoval => _pendingGridRemoval is not null;
    [ObservableProperty] private string gridRemovalWarning = "";
    [ObservableProperty] private bool selectedGridTrackIsRow = true;
    [ObservableProperty] private bool showGridEditorGuides = true;
    partial void OnSelectedLayoutGridRowChanged(LayoutDefinitionEditorItem? value) { if (!_isSyncingLayoutDefinitionEditor && value is not null) SelectedGridTrackIsRow = true; }
    partial void OnSelectedLayoutGridColumnChanged(LayoutDefinitionEditorItem? value) { if (!_isSyncingLayoutDefinitionEditor && value is not null) SelectedGridTrackIsRow = false; }
    private string _gridEditorContext = "";
    private GridRemoval? _pendingGridRemoval;
    private GridTrackResize? _gridTrackResize;
    private sealed record GridRemoval(string Context, bool Rows, int Index, string Signature);
    private sealed record GridTrackResize(string Context, bool Rows, int Index, string Original,
        List<LayoutDefinitionToken> Definitions, double FirstSize, double SecondSize)
    {
        public List<LayoutDefinitionToken> Preview { get; set; } = Definitions.ToList();
    }
    private string GridEditorContext => DocumentSessionId + ":" + ActiveGridEditorId;

    private void ResetGridEditorTransientState()
    {
        CancelGridTrackRemoval(); FinishGridTrackResize(false); _gridEditorContext = "";
        NativeGridMeasurements = new Dictionary<string, AxamlLayoutProjection.GridMeasurement>();
    }

    private void BeginGridEditorUndoBatch()
    {
        // Explicit structure/definition commands must not coalesce with earlier typing.
        if (_undoBatchDepth == 0) RegisterHistorySnapshot();
        _lastHistoryMutationUtc = DateTime.MinValue;
        BeginUndoBatch();
    }
    private void CommitGridEditorUndoBatch()
    {
        CommitUndoBatch();
        if (_undoBatchDepth == 0) _lastHistoryMutationUtc = DateTime.MinValue;
    }

    private bool EnsureGridEditable()
    {
        if (CanEditGridDefinitions) return true;
        StatusText = "Визуальное изменение структуры импортированного AXAML доступно только для чтения. Исходная разметка сохранена.";
        return false;
    }

    public void SelectGridTrack(bool rows, int index)
    {
        SelectedGridTrackIsRow = rows;
        if (rows) SelectedLayoutGridRow = LayoutGridRows.ElementAtOrDefault(index);
        else SelectedLayoutGridColumn = LayoutGridColumns.ElementAtOrDefault(index);
    }

    [RelayCommand] private void InsertGridRowBefore() => InsertGridTrack(true, SelectedLayoutGridRow?.Index ?? 0);
    [RelayCommand] private void InsertGridRowAfter() => InsertGridTrack(true, (SelectedLayoutGridRow?.Index ?? 0) + 1);
    [RelayCommand] private void InsertGridColumnBefore() => InsertGridTrack(false, SelectedLayoutGridColumn?.Index ?? 0);
    [RelayCommand] private void InsertGridColumnAfter() => InsertGridTrack(false, (SelectedLayoutGridColumn?.Index ?? 0) + 1);

    public void InsertGridTrack(bool rows, int index)
    {
        if (!EnsureGridEditable()) return;
        CancelGridTrackRemoval();
        MutateGridDefinitions(rows, definitions =>
        {
            index = Math.Clamp(index, 0, definitions.Count);
            foreach (var child in GetActiveLayoutChildren())
            {
                var start = rows ? child.GridRow : child.GridColumn;
                var span = Math.Max(1, rows ? child.GridRowSpan : child.GridColumnSpan);
                if (start >= index) start++;
                else if (start + span > index) span++;
                SetGridRange(child, rows, start, span);
            }
            definitions.Insert(index, LayoutDefinitionToken.Star(1));
            return definitions;
        });
        SelectGridTrack(rows, index);
    }

    private string GridRemovalSignature(bool rows) => GetActiveLayoutMode() + ":" + CountGridDefinitions(rows ? GetActiveLayoutRows() : GetActiveLayoutColumns(), rows ? GetActiveGridRowDefinitions() : GetActiveGridColumnDefinitions()) + ":" + (rows ? GetActiveGridRowDefinitions() : GetActiveGridColumnDefinitions())
        + ":" + string.Join(";", GetActiveLayoutChildren().Select(c => $"{c.Id}:{(rows ? c.GridRow : c.GridColumn)}:{(rows ? c.GridRowSpan : c.GridColumnSpan)}"));

    private void RequestGridTrackRemoval(bool rows, LayoutDefinitionEditorItem? item)
    {
        if (item is null || !EnsureGridEditable()) return;
        var definitions = ParseLayoutDefinitions(rows ? GetActiveGridRowDefinitions() : GetActiveGridColumnDefinitions(), rows ? GetActiveLayoutRows() : GetActiveLayoutColumns(), UsesNativeLayout);
        if (definitions.Count <= 1) { StatusText = "Последнюю строку или колонку удалить нельзя: Grid сохраняет ячейку 0/0."; return; }
        var index = Math.Clamp(item.Index, 0, definitions.Count - 1);
        var occupied = GetActiveLayoutChildren().Count(c =>
        {
            var start = Math.Clamp(rows ? c.GridRow : c.GridColumn, 0, definitions.Count - 1);
            var span = Math.Max(1, rows ? c.GridRowSpan : c.GridColumnSpan);
            return start <= index && start + span > index;
        });
        if (occupied == 0) { RemoveGridTrack(rows, index); return; }
        _pendingGridRemoval = new(GridEditorContext, rows, index, GridRemovalSignature(rows));
        GridRemovalWarning = $"{(rows ? "Строка" : "Колонка")} {index} затрагивает элементов: {occupied}. Перенести их в следующую ячейку (для последней — в предыдущую), уменьшить spans и удалить definition? Контролы будут сохранены.";
        OnPropertyChanged(nameof(HasPendingGridRemoval));
        StatusText = "Удаление требует выбора: перенести контролы или отменить. См. Grid Editor.";
    }

    [RelayCommand]
    private void ConfirmGridTrackRemoval()
    {
        var pending = _pendingGridRemoval;
        CancelGridTrackRemoval();
        if (pending is null || !EnsureGridEditable()) return;
        if (pending.Context != GridEditorContext || pending.Signature != GridRemovalSignature(pending.Rows))
        { StatusText = "Структура Grid изменилась. Повторите удаление для актуальной строки или колонки."; return; }
        RemoveGridTrack(pending.Rows, pending.Index);
    }

    [RelayCommand]
    private void CancelGridTrackRemoval()
    {
        _pendingGridRemoval = null; GridRemovalWarning = "";
        OnPropertyChanged(nameof(HasPendingGridRemoval));
    }

    private void RemoveGridTrack(bool rows, int index)
    {
        CancelGridTrackRemoval();
        MutateGridDefinitions(rows, definitions =>
        {
            if (definitions.Count <= 1 || index >= definitions.Count) return definitions;
            definitions.RemoveAt(index);
            foreach (var child in GetActiveLayoutChildren())
            {
                var start = rows ? child.GridRow : child.GridColumn;
                var span = Math.Max(1, rows ? child.GridRowSpan : child.GridColumnSpan);
                if (start > index) start--;
                else if (start <= index && start + span > index && span > 1) span--;
                start = Math.Clamp(start, 0, definitions.Count - 1);
                SetGridRange(child, rows, start, Math.Clamp(span, 1, definitions.Count - start));
            }
            return definitions;
        });
        SelectGridTrack(rows, Math.Max(0, index - 1));
    }

    private static void SetGridRange(DesignControlModel child, bool rows, int start, int span)
    {
        if (rows) { child.GridRow = start; child.GridRowSpan = span; }
        else { child.GridColumn = start; child.GridColumnSpan = span; }
    }

    private void MoveGridTrackSafely(bool rows, LayoutDefinitionEditorItem item, int direction)
    {
        if (!EnsureGridEditable()) return;
        var definitions = ParseLayoutDefinitions(rows ? GetActiveGridRowDefinitions() : GetActiveGridColumnDefinitions(), rows ? GetActiveLayoutRows() : GetActiveLayoutColumns(), UsesNativeLayout);
        var index = item.Index; var target = index + direction;
        if (index < 0 || index >= definitions.Count || target < 0 || target >= definitions.Count) return;
        var ranges = GetActiveLayoutChildren().Select(child =>
        {
            var start = Math.Clamp(rows ? child.GridRow : child.GridColumn, 0, definitions.Count - 1);
            var span = Math.Clamp(rows ? child.GridRowSpan : child.GridColumnSpan, 1, definitions.Count - start);
            var mapped = Enumerable.Range(start, span).Select(i => i == index ? target : i == target ? index : i).OrderBy(i => i).ToArray();
            return (child, start: mapped[0], span, contiguous: mapped[^1] - mapped[0] + 1 == span);
        }).ToArray();
        if (ranges.Any(r => !r.contiguous)) { StatusText = "Перестановка разорвёт span на несмежные ячейки. Сначала измените span через Inspector."; return; }
        MutateGridDefinitions(rows, list =>
        {
            (list[index], list[target]) = (list[target], list[index]);
            foreach (var range in ranges) SetGridRange(range.child, rows, range.start, range.span);
            return list;
        });
        SelectGridTrack(rows, target);
    }

    private void SetGridDefinitionText(bool rows, string value, DesignControlModel? host)
    {
        if (!EnsureGridEditable()) return;
        value = string.IsNullOrWhiteSpace(value) ? "*" : value;
        try
        {
            var count = rows ? RowDefinitions.Parse(value).Count : ColumnDefinitions.Parse(value).Count;
            if (count == 0) throw new FormatException();
            var children = GetChildControls(host?.Id).ToArray();
            if (children.Any(c => (rows ? c.GridRow : c.GridColumn) >= count || (rows ? c.GridRow : c.GridColumn) + Math.Max(1, rows ? c.GridRowSpan : c.GridColumnSpan) > count))
            { StatusText = "Definitions исключают ячейки с контролами или spans. Удалите их через Grid Editor с явным переносом."; return; }
            BeginGridEditorUndoBatch();
            try
            {
                if (host is null)
                { if (rows) { SurfaceGridRowDefinitions = value; SurfaceLayoutRows = count; } else { SurfaceGridColumnDefinitions = value; SurfaceLayoutColumns = count; } }
                else if (rows) { host.GridRowDefinitions = value; host.Rows = count; }
                else { host.GridColumnDefinitions = value; host.Columns = count; }
                RebuildLayoutDefinitionEditor(); RebuildPropertyGrid("GridDefinitionsText"); NotifyDesignerStateChanged();
            }
            finally { CommitGridEditorUndoBatch(); }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { StatusText = "Некорректные определения Grid. Используйте Auto, *, 2* или размер в пикселях."; }
    }

    public bool BeginGridTrackResize(bool rows, int index, double firstSize, double secondSize)
    {
        if (!UsesNativeLayout || !IsActiveLayoutGrid || _gridTrackResize is not null || !EnsureGridEditable()) return false;
        var original = rows ? GetActiveGridRowDefinitions() : GetActiveGridColumnDefinitions();
        var definitions = ParseLayoutDefinitions(original, rows ? GetActiveLayoutRows() : GetActiveLayoutColumns(), true);
        if (index < 0 || index + 1 >= definitions.Count) return false;
        var first = definitions[index]; var second = definitions[index + 1];
        if (first.Kind != "Fixed" && !(first.Kind == "Star" && second.Kind == "Star" && first.Value > 0 && second.Value > 0 && firstSize + secondSize > 0))
        { StatusText = "Resize: Pixel меняет размер; соседние Star меняют пропорции. Для Auto или смешанной пары используйте GridLength editor."; return false; }
        _gridTrackResize = new(GridEditorContext, rows, index, original, definitions, firstSize, secondSize);
        return true;
    }

    public void UpdateGridTrackResize(double delta)
    {
        if (_gridTrackResize is not { } gesture || !double.IsFinite(delta)) return;
        var list = gesture.Definitions.ToList(); var first = list[gesture.Index];
        if (first.Kind == "Fixed") list[gesture.Index] = new("Fixed", Math.Max(0, first.Value + delta));
        else
        {
            var totalWeight = first.Value + list[gesture.Index + 1].Value;
            var firstWeight = Math.Clamp(first.Value / totalWeight + delta / (gesture.FirstSize + gesture.SecondSize), 0.001, 0.999) * totalWeight;
            list[gesture.Index] = new("Star", firstWeight); list[gesture.Index + 1] = new("Star", totalWeight - firstWeight);
        }
        gesture.Preview = list;
    }

    public void ApplyGridTrackResizePreview(AxamlLayoutProjection.JsonLayoutTree tree)
    {
        if (_gridTrackResize is not { } gesture || gesture.Context != GridEditorContext || !tree.GridPanels.TryGetValue(ActiveGridEditorId, out var grid)) return;
        var text = string.Join(",", gesture.Preview.Select(t => t.ToXaml(true)));
        if (gesture.Rows) grid.RowDefinitions = RowDefinitions.Parse(text); else grid.ColumnDefinitions = ColumnDefinitions.Parse(text);
    }

    public void FinishGridTrackResize(bool commit)
    {
        var gesture = _gridTrackResize; _gridTrackResize = null;
        if (!commit || gesture is null || gesture.Context != GridEditorContext || !IsActiveLayoutGrid || !EnsureGridEditable()) return;
        if ((gesture.Rows ? GetActiveGridRowDefinitions() : GetActiveGridColumnDefinitions()) != gesture.Original)
        { StatusText = "Definitions изменились во время resize. Жест отменён."; return; }
        MutateGridDefinitions(gesture.Rows, _ => gesture.Preview);
    }
}
