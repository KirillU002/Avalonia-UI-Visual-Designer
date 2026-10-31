using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    public const string NewFormCanvasChoice = "Canvas — свободное размещение";
    public const string NewFormGridChoice = "Grid — адаптивный";
    public ObservableCollection<string> NewFormLayoutChoices { get; } = new() { NewFormCanvasChoice, NewFormGridChoice };
    public ObservableCollection<string> AvailableSizeModes { get; } = new() { "Fixed", "Auto" };
    [ObservableProperty] private string newFormLayoutChoice = NewFormCanvasChoice;
    [ObservableProperty] private int layoutSemanticsVersion = 1;
    public bool UsesNativeLayout => !IsAxamlRoundTripDocument && LayoutSemanticsVersion == 2;
    public IReadOnlyDictionary<string, Rect> NativeLayoutBounds { get; set; } = new Dictionary<string, Rect>();
    private IReadOnlyDictionary<string, Rect> DesignerLayoutBounds => UsesNativeLayout ? NativeLayoutBounds : AxamlLayoutBounds;
    public AxamlLayoutProjection.JsonLayoutTree BuildNativeLayout(Func<DesignControlModel, Control> leafFactory, bool includeHidden = false)
        => AxamlLayoutProjection.BuildJson(CreateDocumentFileModel(), Controls.ToArray(), leafFactory, includeHidden);

    private void ConfigureNativeControl(DesignControlModel model)
    {
        if (!UsesNativeLayout) return;
        model.WidthMode = model.HeightMode = "Auto";
        if (CanHostChildren(model) || model.Type == DesignerControlTypes.DataGrid || !string.IsNullOrEmpty(model.PluginId))
            model.VerticalAlignment = "Stretch";
        if (CanHostChildren(model))
        {
            model.LayoutSpacing = 0;
            // Real authored minimums keep a new empty Auto panel targetable inside flow.
            // They grow with content and are exported; no fake designer coordinates/sizes.
            if (!model.CustomProperties.Any(p => p.Key == LayoutPlacementProperties.MinWidthKey)) LayoutPlacementProperties.SetLiteral(model, LayoutPlacementProperties.MinWidthKey, 48d);
            if (!model.CustomProperties.Any(p => p.Key == LayoutPlacementProperties.MinHeightKey)) LayoutPlacementProperties.SetLiteral(model, LayoutPlacementProperties.MinHeightKey, 32d);
        }
    }

    private void PlaceInNativeGrid(DesignControlModel control)
    {
        control.GridRow = control.GridColumn = 0;
        control.GridRowSpan = control.GridColumnSpan = 1;
        control.X = control.Y = 0;
        StatusText = "Элемент добавлен в Grid: строка 0, столбец 0. Размещение и spans настраиваются в Inspector.";
    }

    private IEnumerable<PropertyGridRowViewModel> NativePlacementRows(DesignControlModel control, bool layoutOnly)
    {
        yield return CreateReadOnlyRow(PropertyGridCategoryLayout, nameof(control.ParentId), "ParentId", string.IsNullOrEmpty(control.ParentId) ? "(корень)" : control.ParentId, "Id родительского контейнера. Пустой ParentId означает корневой Grid формы.");
        yield return CreateEnumRow(PropertyGridCategoryLayout, nameof(control.WidthMode), "Width mode", control.WidthMode, AvailableSizeModes, "Auto — размер определяет Avalonia layout; Fixed — поле Width.", v => control.WidthMode = v);
        yield return CreateEnumRow(PropertyGridCategoryLayout, nameof(control.HeightMode), "Height mode", control.HeightMode, AvailableSizeModes, "Auto — размер определяет Avalonia layout; Fixed — поле Height.", v => control.HeightMode = v);
        foreach (var row in NativeContainerRows(control, layoutOnly)) yield return row;
        if (GetLayoutModeForParent(control.ParentId) != DesignerLayoutModes.Grid) yield break;
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.GridRow), "Grid.Row", control.GridRow, "Индекс строки родительского Grid.", v => control.GridRow = Math.Max(0, (int)v));
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.GridColumn), "Grid.Column", control.GridColumn, "Индекс столбца родительского Grid.", v => control.GridColumn = Math.Max(0, (int)v));
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.GridRowSpan), "Grid.RowSpan", control.GridRowSpan, "Число занимаемых строк.", v => control.GridRowSpan = Math.Max(1, (int)v));
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.GridColumnSpan), "Grid.ColumnSpan", control.GridColumnSpan, "Число занимаемых столбцов.", v => control.GridColumnSpan = Math.Max(1, (int)v));
    }

    private void SetNativeDefinitions(bool rows, string value)
    {
        SetGridDefinitionText(rows, value, null);
    }

    private void SetNativeGridCount(bool rows, int count, DesignControlModel? host = null)
    {
        var text = rows ? host?.GridRowDefinitions ?? SurfaceGridRowDefinitions : host?.GridColumnDefinitions ?? SurfaceGridColumnDefinitions;
        var parts = AxamlLayoutProjection.Definitions(text, rows ? host?.Rows ?? SurfaceLayoutRows : host?.Columns ?? SurfaceLayoutColumns).Split(',').ToList();
        count = Math.Max(1, count);
        while (parts.Count < count) parts.Add("*");
        if (parts.Count > count) parts.RemoveRange(count, parts.Count - count);
        SetGridDefinitionText(rows, string.Join(",", parts), host);
    }

    private void SetNativeMargin(DesignControlModel control, string value)
    {
        if (!UsesNativeLayout) { control.Margin = value; return; }
        try { _ = Thickness.Parse(value); control.Margin = value; }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { StatusText = "Некорректный Margin. Используйте одно, два или четыре числовых значения."; }
    }

    private string NativeSizeAttributes(DesignControlModel control)
    {
        return (control.WidthMode == "Auto" ? "" : $" Width=\"{ToInvariant(control.Width)}\"")
            + (control.HeightMode == "Auto" ? "" : $" Height=\"{ToInvariant(control.Height)}\"")
            + string.Concat(new[] { LayoutPlacementProperties.MinWidthKey, LayoutPlacementProperties.MinHeightKey }
                .Where(key => control.CustomProperties.Any(p => p.Key == key))
                .Select(key => $" {key}=\"{EscapeXml(LayoutPlacementProperties.ReadString(control, key) ?? ToInvariant(LayoutPlacementProperties.Minimum(control, key)))}\""));
    }

    private string NativePlacementAttributes(DesignControlModel control)
    {
        var result = new StringBuilder(NativeSizeAttributes(control));
        var mode = GetLayoutModeForParent(control.ParentId);
        if (mode == DesignerLayoutModes.Grid)
            result.Append($" Grid.Row=\"{control.GridRow}\" Grid.Column=\"{control.GridColumn}\" Grid.RowSpan=\"{control.GridRowSpan}\" Grid.ColumnSpan=\"{control.GridColumnSpan}\"");
        else if (mode == DesignerLayoutModes.Absolute)
            result.Append($" Canvas.Left=\"{ToInvariant(control.X)}\" Canvas.Top=\"{ToInvariant(control.Y)}\"");
        else if (mode == DesignerLayoutModes.Dock)
            result.Append($" DockPanel.Dock=\"{EscapeXml(LayoutPlacementProperties.ReadString(control, LayoutPlacementProperties.DockKey) ?? "Left")}\"");
        result.Append(AlignmentAttributes(control));
        result.Append($" Margin=\"{EscapeXml(control.Margin)}\"");
        return result.ToString();
    }

    private string ExportGridDefinitions(int count, string value) => UsesNativeLayout
        ? AxamlLayoutProjection.Definitions(value, count) : BuildGridDefinitions(count, value);

    private static void ValidateNativeDocument(DesignerDocumentFileModel document)
    {
        if (document.LayoutSemanticsVersion is < 1 or > 2)
            throw new InvalidOperationException("Версия layout документа не поддерживается. Файл не изменён.");
        if (document.LayoutSemanticsVersion != 2) return;
        if (document.SurfaceLayoutMode != DesignerLayoutModes.Grid)
            throw new InvalidOperationException("Phase 2 поддерживает native layout с корневым Grid.");
        _ = RowDefinitions.Parse(AxamlLayoutProjection.Definitions(document.SurfaceGridRowDefinitions, document.SurfaceLayoutRows));
        _ = ColumnDefinitions.Parse(AxamlLayoutProjection.Definitions(document.SurfaceGridColumnDefinitions, document.SurfaceLayoutColumns));
        var ids = document.Controls.ToDictionary(c => c.Id);
        foreach (var control in document.Controls)
        {
            if (control.WidthMode is not ("Auto" or "Fixed") || control.HeightMode is not ("Auto" or "Fixed"))
                throw new InvalidOperationException("Неизвестный режим размера элемента: " + control.Name);
            var seen = new HashSet<string> { control.Id };
            var parent = control.ParentId;
            while (!string.IsNullOrEmpty(parent))
            {
                if (!seen.Add(parent) || !ids.TryGetValue(parent, out var node))
                    throw new InvalidOperationException("Некорректное дерево ParentId: " + control.Name);
                parent = node.ParentId;
            }
        }
    }
}
