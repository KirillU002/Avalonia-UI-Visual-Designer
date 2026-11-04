using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FormDesigner.Views;

public partial class PreviewWindow
{
    private readonly List<DataGridPreviewDiagnostic> _dataGridDiagnostics = new();
    private readonly long _dataGridPreviewStarted = Stopwatch.GetTimestamp();
    private int _databasePreviewGeneration;
    private int _databasePreviewCompletedGeneration = -1;
    private bool DatabasePreviewReady => !_databasePreviewClosed && _databasePreviewCompletedGeneration == _databasePreviewGeneration;
    public IReadOnlyList<DataGridPreviewDiagnostic> DataGridDiagnostics => _dataGridDiagnostics;
    public event EventHandler<DataGridPreviewDiagnostic>? DataGridDiagnosticEmitted;

    private string PreviewControlName(DesignerControlFileModel control) =>
        _exportedAxamlPreview is not null && _exportedAxamlPreview.StandardControlNames.TryGetValue(control.Id, out var name) ? name : control.Name;

    // Generated bindings target DTO properties. Preview shares the dictionary row
    // contract with Legacy Preview. Adapt only the in-memory preview and its templates.
    private static string PrepareDataGridPreviewAxaml(string axaml)
    {
        var document = XDocument.Parse(axaml, LoadOptions.PreserveWhitespace);
        foreach (var grid in document.Descendants().Where(e => e.Name.LocalName == "DataGrid"))
        foreach (var columns in grid.Elements().Where(e => e.Name.LocalName == "DataGrid.Columns"))
        foreach (var attribute in columns.Descendants().Attributes())
        {
            var match = Regex.Match(attribute.Value, @"^\{Binding\s+(?<path>[\p{L}_][\p{L}\p{N}_]*)(?<options>\s*(?:,[^}]*)?)\}$", RegexOptions.CultureInvariant);
            if (match.Success) attribute.Value = "{Binding [" + match.Groups["path"].Value + "]" + match.Groups["options"].Value + "}";
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private IEnumerable<DesignerControlFileModel> PreviewDataGrids => _document.Controls.Where(c => c.Type == DesignerControlTypes.DataGrid);

    private DataGridRuntimeDataModeResolution ResolveDataGridPreviewMode(BindingSourceFileModel source)
    {
        if (!DataSourceIdentity.IsDatabase(source.SourceKind)) return DataGridRuntimeDataModeResolver.Resolve(source, _exportedAxamlPreview?.IncludeDemoData ?? false);
        return source.UseRealPreviewRowsIfAvailable && source.PreviewRowMode != BindingSourceModel.PreviewRowModeSchemaOnly && SqlPreviewDataLoader.CanLoad(source)
            ? new(DataGridRuntimeDataMode.Sql, "preview-database-source", true, false)
            : new(DataGridRuntimeDataMode.Empty, "preview-database-not-configured", false, false);
    }

    private void TraceDataGrid(string code, DesignerControlFileModel control, int loaded = 0, DataGrid? grid = null, string details = "")
    {
        if (code == "DATAGRID_QUERY_FAILED")
            ReportPreviewFailure(control, "BindingFailed", $"Не удалось загрузить данные компонента {control.Name}. Подробности показаны в сообщении подключения.", code: "PREVIEW_BINDING_FAILED");
        var source = GetBindingSource(control.BindingSourceId);
        var record = new DataGridPreviewDiagnostic(code, string.IsNullOrEmpty(_currentFormId) ? "standalone-preview" : _currentFormId,
            control.Id, control.BindingSourceId, source?.SourceKind ?? "None",
            source is null ? "None" : !source.UseRealPreviewRowsIfAvailable || source.PreviewRowMode == BindingSourceModel.PreviewRowModeSchemaOnly || !source.Fields.Any(f => f.IsVisible && !string.IsNullOrWhiteSpace(f.Path)) ? "SchemaOnly" : !string.IsNullOrWhiteSpace(source.SourceQuery) ? "Query" : !string.IsNullOrWhiteSpace(source.SourceTableName) ? "Table" : "Static",
            loaded, grid?.ItemsSource?.Cast<object>().Count() ?? 0, grid?.DataContext?.GetType().FullName ?? "None",
            _exportedAxamlPreview is null ? "Legacy" : "AXAML", (long)Stopwatch.GetElapsedTime(_dataGridPreviewStarted).TotalMilliseconds, details);
        _dataGridDiagnostics.Add(record);
        Debug.WriteLine(code + " " + JsonSerializer.Serialize(record));
        DataGridDiagnosticEmitted?.Invoke(this, record);
    }

    private void TraceSourceGrids(string code, string sourceId, int loaded = 0, string details = "")
    {
        foreach (var control in PreviewDataGrids.Where(c => c.BindingSourceId == sourceId)) TraceDataGrid(code, control, loaded, details: details);
    }

    private void AttachDataGridPreviewContext(DataGrid grid, DesignerControlFileModel control, int loaded)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (grid.Name != PreviewControlName(control)) grid.Name = PreviewControlName(control);
        var rows = grid.ItemsSource ?? Array.Empty<object>();
        grid.DataContext = new DataGridPreviewContext(rows);
        TraceDataGrid("DATAGRID_DATACONTEXT_ASSIGNED", control, loaded, grid);
        grid.Bind(DataGrid.ItemsSourceProperty, new Binding(nameof(DataGridPreviewContext.Rows)));
        TraceDataGrid("DATAGRID_ITEMSSOURCE_ASSIGNED", control, loaded, grid);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_databasePreviewClosed) TraceDataGrid("DATAGRID_PREVIEW_RENDERED", control, loaded, grid);
        }, DispatcherPriority.Loaded);
    }

    private Control EmptyRuntimeDataGrid(DesignerControlFileModel control, string status)
    {
        var grid = new DataGrid { Width = control.Width, Height = control.Height, AutoGenerateColumns = false, ItemsSource = Array.Empty<object>(), IsReadOnly = true };
        grid.ColumnHeaderHeight = Math.Max(24, control.DataGridHeaderHeight);
        DataGridVisualStyle.Create(CreateRuntimeControlModel(control)).Apply(grid);
        ToolTip.SetTip(grid, status);
        AttachDataGridPreviewContext(grid, control, 0);
        return grid;
    }

    private void WireAxamlDataGridView(DataGrid grid, DesignerControlFileModel control, BindingSourceFileModel source, ObservableCollection<Dictionary<string, string>> rows)
    {
        var names = _exportedAxamlPreview?.DataGridFieldPaths.GetValueOrDefault(source.Id);
        var fields = OrderBindingFieldsForDisplay(source.Fields.Where(f => f.IsVisible)).Select(f => new BindingFieldFileModel
        {
            Path = names is not null && names.TryGetValue(f.Path, out var name) ? name : f.Path,
            Header = f.Header, TypeName = f.TypeName, AllowFilter = f.AllowFilter
        }).ToList();
        for (var index = 0; index < Math.Min(fields.Count, grid.Columns.Count); index++)
            grid.Columns[index].CustomSortComparer = new PreviewDictionaryDataGridSortComparer(fields[index]);
        if (!control.ShowFilterRow || grid.Parent is not Control host) return;
        var original = rows.ToList();
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var textBox in host.GetLogicalDescendants().OfType<TextBox>().Where(t => t.Watermark?.StartsWith("Фильтр: ", StringComparison.Ordinal) == true))
        {
            if (textBox.Parent is not Border border) continue;
            var index = Grid.GetColumn(border);
            if (index >= fields.Count) continue;
            var field = fields[index];
            textBox.TextChanged += (_, _) =>
            {
                filters[field.Path] = textBox.Text ?? "";
                var filtered = ApplyPreviewWindowFilter(original, fields, filters, control.FilterMode);
                rows.Clear();
                foreach (var row in filtered) rows.Add(row);
                TraceDataGrid("DATAGRID_ITEMSSOURCE_ASSIGNED", control, original.Count, grid, "filter-changed");
            };
        }
    }

    private void ShowDataGridSourceError(string sourceId, string message)
    {
        if (_databasePreviewClosed) return;
        DatabaseErrorBanner.IsVisible = true;
        DatabaseErrorText.Text += message + " ";
        TraceSourceGrids("DATAGRID_QUERY_FAILED", sourceId, details: message);
        TraceSourceGrids("DATAGRID_PREVIEW_FAILED", sourceId, details: message);
    }

    public sealed class DataGridPreviewContext
    {
        public DataGridPreviewContext(IEnumerable rows) => Rows = rows;
        public IEnumerable Rows { get; }
    }
}
