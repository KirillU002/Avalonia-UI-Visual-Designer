using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private const string ContractSqlProfile = "Server=127.0.0.1,1;Database=contract;Integrated Security=true;Connect Timeout=1;Encrypt=false";

    private static void RunDataGridPreviewContracts(Action<string, Action> check)
    {
        foreach (var provider in new[] { "SqlServer", "PostgreSql" })
        {
            check("CONTRACT_" + provider + "_selection_json_cache_identity", () =>
            {
                var vm = DataGridContractModel(provider);
                var json = vm.ExportWorkspaceJson();
                var loaded = CreateViewModel("DataGridJsonReload"); loaded.LoadDocumentJson(json);
                var file = loaded.CreatePreviewDocumentSnapshot().BindingSources.Single();
                using var window = new DataGridRegressionPreview(new PreviewWindow(loaded.CreatePreviewDocumentSnapshot(), loaded.Registry));
                var runtime = RuntimePreviewSource(window.Window, file);
                if (runtime.SourceKind != provider || DatabaseProviderAdapters.For(provider).SourceKind != provider) throw new Exception("Provider changed after JSON reload.");
                if (DataGridSourceKey(file) != DataGridSourceKey(runtime)) throw new Exception("Runtime clone changed the cache identity.");
                if (runtime.Fields[0].DbType != "integer" || !runtime.Fields[0].IsPrimaryKey || runtime.Fields[0].IsNullable) throw new Exception("Database metadata was lost.");
            });
            foreach (var axaml in new[] { false, true })
            {
                check("CONTRACT_" + provider + (axaml ? "_AXAML" : "_Legacy") + "_quoted_rows_context_items", () =>
                {
                    var vm = DataGridContractModel(provider);
                    using var preview = ShowDataGridRegressionPreview(vm, axaml, window => SeedDataGridRows(window, vm.CreatePreviewDocumentSnapshot().BindingSources.Single(), ContractRows(provider)));
                    var grid = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                    var text = string.Join("|", grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                    if (grid.ItemsSource!.Cast<object>().Count() != 2 || !text.Contains("upper value") || !text.Contains("lower value") || !text.Contains("dot value")) throw new Exception("Exact quoted identifiers did not reach visible cells.");
                    if (grid.DataContext is not PreviewWindow.DataGridPreviewContext) throw new Exception("Grid context is missing.");
                    if (preview.Window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_QUERY_START")) throw new Exception("A seeded contract must not call a database.");
                    RequireDataGridDiagnostic(preview.Window, "DATAGRID_ITEMSSOURCE_ASSIGNED", 2);
                    RequireDataGridDiagnostic(preview.Window, "DATAGRID_PREVIEW_RENDERED", 2);
                });
            }
        }
        foreach (var axaml in new[] { false, true })
        {
            check("CONTRACT_" + (axaml ? "AXAML" : "Legacy") + "_schema_only_no_query", () =>
            {
                using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => throw new Exception("Schema-only must not execute SQL."));
                var vm = DataGridRegressionModel(ContractSqlProfile, "metadata"); vm.BindingSources.Single().PreviewRowMode = BindingSourceModel.PreviewRowModeSchemaOnly;
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var grid = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (grid.ItemsSource!.Cast<object>().Any() || grid.Columns.Count != 4 || preview.Window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_QUERY_START")) throw new Exception("Schema-only must keep columns with no query or rows.");
                vm.BindingSources.Single().Fields.Clear(); vm.BindingSources.Single().PreviewRowMode = BindingSourceModel.PreviewRowModeTopN;
                using var noColumns = ShowDataGridRegressionPreview(vm, axaml);
                var empty = noColumns.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (empty.ItemsSource!.Cast<object>().Any() || empty.Columns.Count != 0 || noColumns.Window.FindControl<Border>("DatabaseErrorBanner")!.IsVisible || noColumns.Window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_QUERY_START")) throw new Exception("A source awaiting schema must not query, generate fields, or report a connection failure.");
            });
            check("CONTRACT_" + (axaml ? "AXAML" : "Legacy") + "_unconfigured_sql_no_demo", () =>
            {
                var vm = DataGridRegressionModel("", "single"); var source = vm.BindingSources.Single();
                source.UseDemoData = source.AllowPreviewSampleFallback = true; source.PreviewRowMode = BindingSourceModel.PreviewRowModeSampleRows;
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                if (preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single().ItemsSource!.Cast<object>().Any()) throw new Exception("Unconfigured database must not generate fake rows.");
            });
            check("CONTRACT_" + (axaml ? "AXAML" : "Legacy") + "_no_source", () =>
            {
                var vm = CreateViewModel("DataGridWithoutSource"); vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
                vm.Controls.Add(DataGrid("EmptyGrid", "", 10, 10, 500, 200));
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                if (preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single().ItemsSource!.Cast<object>().Any()) throw new Exception("Unbound grid must be empty.");
            });
            check("CONTRACT_" + (axaml ? "AXAML" : "Legacy") + "_static_explicit_data", () =>
            {
                var vm = CreateViewModel("StaticDataGrid"); vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
                var source = ProductsSource(); source.UseDemoData = true; vm.BindingSources.Add(source);
                vm.Controls.Add(DataGrid("StaticGrid", source.Id, 10, 10, 700, 200));
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                if (!preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Keyboard")) throw new Exception("Explicit static data disappeared.");
            });
        }
        check("CONTRACT_async_dispatcher_reopen", () =>
        {
            var calls = 0;
            using var provider = UseSqlPreviewRowsProvider(async (_, _, _, _) => { Interlocked.Increment(ref calls); await Task.Delay(220); return new[] { new Dictionary<string, string> { ["Name"] = "async actual value" } }; });
            var vm = DataGridRegressionModel(ContractSqlProfile, "single");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var preview = ShowDataGridRegressionPreview(vm, attempt == 1);
                var grid = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (grid.ItemsSource!.Cast<object>().Count() != 1 || !grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "async actual value")) throw new Exception("Async completion did not update the visual grid.");
            }
            if (calls != 2) throw new Exception("Reopen must use a fresh load.");
        });
        check("CONTRACT_close_during_load", () =>
        {
            var pending = new TaskCompletionSource<IReadOnlyList<Dictionary<string, string>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => pending.Task);
            var vm = DataGridRegressionModel(ContractSqlProfile, "single");
            var window = new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
            window.Position = new Avalonia.PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false; window.Show();
            NativeItemsUiWait(260); window.Close(); NativeItemsUiWait(100);
            pending.TrySetResult(new[] { new Dictionary<string, string> { ["Name"] = "late row" } }); NativeItemsUiWait(100);
            if (window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_PREVIEW_RENDERED")) throw new Exception("Closed Preview must not render a late query.");
        });
        check("CONTRACT_multiple_grids_mixed_forms", RequireDataGridMixedFormContracts);
        check("CONTRACT_sql_failure_diagnostics_redacted", () =>
        {
            const string secret = "P0-secret-never-log";
            using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => throw new InvalidOperationException("Password=" + secret + ";SELECT '" + secret + "'"));
            var vm = DataGridRegressionModel(ContractSqlProfile, "single");
            using var preview = ShowDataGridRegressionPreview(vm, false);
            var serialized = JsonSerializer.Serialize(preview.Window.DataGridDiagnostics);
            if (serialized.Contains(secret)) throw new Exception("Diagnostics leaked a server error literal.");
            if (!preview.Window.FindControl<Border>("DatabaseErrorBanner")!.IsVisible) throw new Exception("SQL failure needs a visible error.");
        });
    }

    private static MainWindowViewModel DataGridContractModel(string provider)
    {
        var vm = DataGridRegressionModel(ContractSqlProfile, "metadata");
        var source = vm.BindingSources.Single(); source.SourceKind = provider;
        if (provider == "PostgreSql") { source.SourceConnectionString = "Host=127.0.0.1;Port=1;Database=contract;Username=contract;Timeout=1"; source.SourceSchemaName = "Case.Schema"; }
        source.Fields.Clear();
        foreach (var key in provider == "PostgreSql" ? new[] { "Id", "name", "Name", "display.name" } : new[] { "Id", "lower_name", "UpperName", "display.name" }) source.Fields.Add(new() { Path = key, Header = key, TypeName = key == "Id" ? "int" : "string", DbType = key == "Id" ? "integer" : "text", IsNullable = false, IsPrimaryKey = key == "Id", Width = "160", IsVisible = true });
        return vm;
    }
    private static IReadOnlyList<Dictionary<string, string>> ContractRows(string provider = "PostgreSql") => new[]
    {
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "1", [provider == "PostgreSql" ? "name" : "lower_name"] = "lower value", [provider == "PostgreSql" ? "Name" : "UpperName"] = "upper value", ["display.name"] = "dot value" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["Id"] = "2", [provider == "PostgreSql" ? "name" : "lower_name"] = "lower two", [provider == "PostgreSql" ? "Name" : "UpperName"] = "upper two", ["display.name"] = "dot two" }
    };
    private static BindingSourceModel RuntimePreviewSource(PreviewWindow window, BindingSourceFileModel file) => (BindingSourceModel)typeof(PreviewWindow).GetMethod("CreateRuntimeBindingSourceModel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { file })!;
    private static string DataGridSourceKey(object source)
    {
        var type = typeof(MainWindowViewModel).Assembly.GetType("FormDesigner.DesignerSystem.Binding.DataSourceIdentity")!;
        return (string)type.GetMethod("BuildKey", new[] { source.GetType() })!.Invoke(null, new[] { source })!;
    }
    private static void SeedDataGridRows(PreviewWindow window, BindingSourceFileModel file, IReadOnlyList<Dictionary<string, string>> rows)
    {
        var source = RuntimePreviewSource(window, file);
        var type = typeof(MainWindowViewModel).Assembly.GetType("FormDesigner.DesignerSystem.Binding.PreviewRowsLoader")!;
        var signature = (string)type.GetMethod("BuildSignature")!.Invoke(null, new object[] { source })!;
        var cache = (IDictionary)typeof(PreviewWindow).GetField("_previewRowsBySourceKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        cache[DataGridSourceKey(source)] = (signature, rows, "RealData", true, "deterministic row contract; no database called");
    }
    private static void RequireDataGridDiagnostic(PreviewWindow window, string code, int count)
    {
        if (!window.DataGridDiagnostics.Any(d => d.Event == code && d.ItemsSourceCount == count && d.ControlId.Length > 0 && d.FormId.Length > 0)) throw new Exception("Missing structured grid diagnostic: " + code);
    }
    private static void RequireDataGridMixedFormContracts()
    {
        var vm = DataGridContractModel("SqlServer");
        vm.CreateNewForm(DesignerLayoutModes.Absolute); vm.Controls.Clear(); vm.BindingSources.Clear();
        var source = DataGridContractModel("PostgreSql").BindingSources.Single().Clone(); vm.BindingSources.Add(source);
        vm.Controls.Add(DataGrid("PostgresGrid", source.Id, 10, 10, 760, 200));
        vm.CreateNewForm(DesignerLayoutModes.Absolute); vm.Controls.Clear(); vm.BindingSources.Clear(); vm.Controls.Add(DataGrid("UnboundGrid", "", 10, 10, 760, 200));
        foreach (var form in vm.CurrentProject.Forms.ToArray())
        {
            vm.OpenFormDocument(form.Id);
            foreach (var axaml in new[] { false, true })
            {
                var file = vm.CreatePreviewDocumentSnapshot().BindingSources.FirstOrDefault();
                using var preview = ShowDataGridRegressionPreview(vm, axaml, w => { if (file is not null) SeedDataGridRows(w, file, ContractRows(file.SourceKind)); });
                var grid = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (grid.ItemsSource!.Cast<object>().Count() != (file is null ? 0 : 2)) throw new Exception("Form data isolation failed.");
                if (preview.Window.DataGridDiagnostics.Any(d => d.FormId != form.Id)) throw new Exception("Form ID changed in runtime diagnostics.");
            }
        }
    }
}
