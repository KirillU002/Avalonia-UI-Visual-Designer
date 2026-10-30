using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Collections;
using System.Data;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Microsoft.Data.SqlClient;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunDataGridPreviewChecks()
    {
        Directory.CreateDirectory(Path.Combine(FindRepositoryRoot(), "artifacts", "datagrid-preview-p0"));
        var failures = 0;
        var total = 0;
        void Check(string name, Action test)
        {
            total++;
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        RunDataGridPreviewContracts(Check);
        Console.WriteLine($"DataGrid offline contracts: {total - failures}/{total}");
        var connection = Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.WriteLine("SKIP SQL Server live DataGrid Preview: FORMDESIGNER_TEST_SQLSERVER is not set.");
            return failures == 0 ? 0 : 1;
        }
        foreach (var axaml in new[] { false, true })
        foreach (var layout in new[] { "single", "metadata", "unbound-first", "empty", "invalid-query", "disconnected", "aliases", "wrapped", "filters", "json", "multiple" })
        {
            total++;
            var name = "LIVE_SQL_" + (axaml ? "Axaml" : "Legacy") + "_" + layout;
            try
            {
                var vm = DataGridRegressionModel(connection, layout);
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var grids = preview.Window.GetLogicalDescendants().OfType<DataGrid>().ToArray();
                var counts = grids.Select(g => (g.ItemsSource?.Cast<object>() ?? Array.Empty<object>()).Count()).ToArray();
                var texts = grids.Select(g => string.Join("|", g.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text))).ToArray();
                var banner = preview.Window.FindControl<Border>("DatabaseErrorBanner")!.IsVisible;
                Console.WriteLine("OBSERVED " + JsonSerializer.Serialize(new { name, counts, banner, context = grids.Select(g => g.DataContext?.GetType().Name).ToArray(), status = preview.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text }));
                if (axaml && preview.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text != "AXAML Preview ready") throw new Exception("AXAML Preview must not fall back to Legacy.");
                if (layout is "invalid-query" or "disconnected")
                {
                    if (!banner) throw new Exception("SQL error must be visible.");
                    if (counts.Any(c => c != 0)) throw new Exception("SQL error must not create demo rows.");
                }
                else if (layout == "empty")
                {
                    if (grids.Length != 1 || counts[0] != 0 || grids[0].Columns.Count != 2) throw new Exception("Empty table must keep its columns and zero rows.");
                }
                else if (layout == "unbound-first")
                {
                    if (grids.Length != 2 || counts[0] != 0 || counts[1] != 3) throw new Exception("Rows must reach the bound grid only.");
                }
                else if (layout == "multiple")
                {
                    if (grids.Length != 2 || counts[0] != 3 || counts[1] != 2 || !texts[0].Contains("Анна SQL") || !texts[1].Contains("19.50")) throw new Exception("Independent SQL sources were mixed between grids.");
                }
                else if (grids.Length != 1 || counts[0] != 3 || !texts[0].Contains("Анна SQL")) throw new Exception("Actual SQL rows must reach ItemsSource and visible cells.");
                if (layout is not ("empty" or "invalid-query" or "disconnected"))
                {
                    var actual = texts[layout == "unbound-first" ? 1 : 0];
                    if (!actual.Contains("Анна SQL") || !actual.Contains("Борис SQL") || !actual.Contains("Вера SQL")) throw new Exception("All three distinct SQL values must be visible; the first row must not be duplicated.");
                }
                if (layout == "filters")
                {
                    var filter = preview.Window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Watermark == (axaml ? "Фильтр: Name" : "Name"));
                    filter.Text = "Анна"; NativeItemsUiWait(100);
                    if (grids[0].ItemsSource!.Cast<object>().Count() != 1) throw new Exception("Live rows did not update after filtering.");
                    filter.Text = ""; NativeItemsUiWait(100);
                    if (grids[0].ItemsSource!.Cast<object>().Count() != 3) throw new Exception("Clearing the filter must restore actual rows.");
                }
                if (layout == "metadata")
                {
                    var content = (Avalonia.Controls.Control)preview.Window.Content!;
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height));
                    bitmap.Render(content);
                    bitmap.Save(Path.Combine(FindRepositoryRoot(), "artifacts", "datagrid-preview-p0", axaml ? "axaml-real-sql.png" : "legacy-real-sql.png"));
                    File.WriteAllText(Path.Combine(FindRepositoryRoot(), "artifacts", "datagrid-preview-p0", axaml ? "axaml-diagnostics.json" : "legacy-diagnostics.json"), JsonSerializer.Serialize(preview.Window.DataGridDiagnostics, new JsonSerializerOptions { WriteIndented = true }));
                    RequireDataGridDiagnostic(preview.Window, "DATAGRID_ITEMSSOURCE_ASSIGNED", 3);
                    RequireDataGridDiagnostic(preview.Window, "DATAGRID_PREVIEW_RENDERED", 3);
                }
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        Check("LIVE_SQL_reopen_and_close_during_query", () => RequireLiveSqlPreviewCancellation(connection));
        Check("LIVE_SQL_multi_form", () => RequireLiveSqlMultiForm(connection));
        Check("LIVE_SQL_command_timeout_both_modes", () => RequireLiveSqlCommandTimeout(connection));
        Console.WriteLine("SKIP PostgreSQL live Preview: no test PostgreSQL server; quoted identifiers, provider selection and row delivery were CONTRACT checks only.");
        Console.WriteLine($"DataGrid Preview checks (CONTRACT and LIVE labeled separately): {total - failures}/{total}");
        return failures == 0 ? 0 : 1;
    }

    private static MainWindowViewModel DataGridRegressionModel(string connection, string layout)
    {
        var vm = CreateViewModel("DataGridRegression");
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        var source = new BindingSourceModel
        {
            Id = "p0-customers", Name = "Customers", Path = "Customers", ItemTypeName = "Customer",
            SourceKind = "SqlServer", SourceConnectionString = connection, SourceSchemaName = "dbo",
            SourceTableName = layout == "empty" ? "EmptyTable" : "Customers", PreviewTopN = 50,
            SourceQuery = layout == "invalid-query" ? "SELECT MissingColumn FROM dbo.Customers" : layout == "aliases" ? "SELECT Id AS [Customer Id], Name AS [Customer Name], City AS [City.Name] FROM dbo.Customers" : "",
            UseRealPreviewRowsIfAvailable = true, UseDemoData = false, AllowPreviewSampleFallback = false
        };
        if (layout == "disconnected") source.SourceConnectionString = ContractSqlProfile;
        foreach (var column in layout == "empty" ? new[] { "Id", "Name" } : layout == "aliases" ? new[] { "Customer Id", "Customer Name", "City.Name" } : new[] { "Id", "Name", "Email", "City" })
            source.Fields.Add(new() { Path = column, Header = column, TypeName = column == "Id" ? "int" : "string", IsVisible = true, Width = "140",
                DbType = layout == "metadata" ? column == "Id" ? "int" : "nvarchar" : "",
                IsNullable = layout != "metadata" || column == "Email", IsPrimaryKey = layout == "metadata" && column == "Id" });
        vm.BindingSources.Add(source);
        if (layout == "unbound-first") vm.Controls.Add(DataGrid("UnboundGrid", "", 10, 10, 760, 140));
        vm.Controls.Add(DataGrid("CustomersGrid", source.Id, 10, layout == "unbound-first" ? 170 : 10, 760, 260));
        if (layout == "wrapped") source.Fields[1].TextWrapping = BindingFieldModel.TextWrappingWrap;
        if (layout == "filters") vm.Controls.Single().ShowFilterRow = true;
        if (layout == "multiple")
        {
            var orders = new BindingSourceModel { Id = "p0-orders", Name = "Orders", Path = "Orders", ItemTypeName = "Order", SourceKind = "SqlServer", SourceConnectionString = connection, SourceSchemaName = "dbo", SourceTableName = "Orders", UseRealPreviewRowsIfAvailable = true };
            foreach (var key in new[] { "Id", "CustomerId", "Amount" }) orders.Fields.Add(new() { Path = key, Header = key, TypeName = key == "Amount" ? "decimal" : "int", IsVisible = true, Width = "140" });
            vm.BindingSources.Add(orders); vm.Controls.Add(DataGrid("OrdersGrid", orders.Id, 10, 300, 760, 200));
        }
        if (layout == "json")
        {
            var loaded = CreateViewModel("RealSqlJsonReload"); loaded.LoadDocumentJson(vm.ExportWorkspaceJson());
            loaded.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal; return loaded;
        }
        return vm;
    }

    private static DataGridRegressionPreview ShowDataGridRegressionPreview(MainWindowViewModel vm, bool axaml, Action<PreviewWindow>? beforeShow = null)
    {
        var window = axaml ? new PreviewWindow(vm.GenerateExportedAxamlPreviewSnapshot(), vm.Registry)
            : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry, vm.CreatePreviewProjectFormsSnapshot(), vm.ActiveFormDocument!.Id);
        window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false;
        beforeShow?.Invoke(window); window.Show();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do { NativeItemsUiWait(40); } while (window.FindControl<Border>("LoadingOverlay")!.IsVisible && DateTime.UtcNow < deadline);
        NativeItemsUiWait(100);
        return new(window);
    }

    private static void RequireLiveSqlPreviewCancellation(string connection)
    {
        using (var lockConnection = new SqlConnection(connection))
        {
            lockConnection.Open();
            using var transaction = lockConnection.BeginTransaction();
            using var hold = new SqlCommand("SELECT * FROM dbo.Customers WITH (TABLOCKX,HOLDLOCK)", lockConnection, transaction);
            using (var reader = hold.ExecuteReader()) { while (reader.Read()) { } }
            var vm = DataGridRegressionModel(connection, "metadata");
            vm.BindingSources.Single().SourceQuery = "SELECT Id, Name, Email, City FROM dbo.Customers WITH (READCOMMITTEDLOCK)";
            var window = new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
            window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false;
            window.Show(); NativeItemsUiWait(350);
            if (!window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_QUERY_START")) throw new Exception("Live blocked query did not start.");
            if (!window.FindControl<Border>("LoadingOverlay")!.IsVisible) throw new Exception("The live fixture query must still be blocked before cancellation.");
            var responsive = false; Dispatcher.UIThread.Post(() => responsive = true); NativeItemsUiWait(50);
            if (!responsive) throw new Exception("UI blocked while SQL was running.");
            window.Close(); NativeItemsUiWait(300);
            if (window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_PREVIEW_RENDERED")) throw new Exception("Closed live Preview rendered rows.");
            if (typeof(PreviewWindow).GetField("_databasePreviewCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is not null) throw new Exception("SQL cancellation did not complete promptly.");
            transaction.Rollback();
        }
        foreach (var axaml in new[] { false, true })
        {
            using var reopened = ShowDataGridRegressionPreview(DataGridRegressionModel(connection, "metadata"), axaml);
            if (reopened.Window.GetLogicalDescendants().OfType<DataGrid>().Single().ItemsSource!.Cast<object>().Count() != 3) throw new Exception("Reopened SQL Preview did not reconnect.");
        }
    }

    private static void RequireLiveSqlMultiForm(string connection)
    {
        var vm = DataGridRegressionModel(connection, "metadata");
        vm.CreateNewForm(); vm.Controls.Clear(); vm.BindingSources.Clear();
        var orders = DataGridRegressionModel(connection, "multiple").BindingSources.Single(s => s.Id == "p0-orders").Clone();
        vm.BindingSources.Add(orders); vm.Controls.Add(DataGrid("OrdersGrid", orders.Id, 10, 10, 760, 240));
        foreach (var form in vm.CurrentProject.Forms.ToArray())
        {
            vm.OpenFormDocument(form.Id);
            foreach (var axaml in new[] { false, true })
            {
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var count = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single().ItemsSource!.Cast<object>().Count();
                if (count != (vm.BindingSources.Single().Id == "p0-orders" ? 2 : 3)) throw new Exception("Live SQL rows changed between forms.");
            }
        }
    }

    private static void RequireLiveSqlCommandTimeout(string connection)
    {
        using var owner = new SqlConnection(connection); owner.Open();
        using var transaction = owner.BeginTransaction();
        using var hold = new SqlCommand("SELECT * FROM dbo.Customers WITH (TABLOCKX,HOLDLOCK)", owner, transaction);
        using (var reader = hold.ExecuteReader()) { while (reader.Read()) { } }
        var windows = new List<PreviewWindow>();
        try
        {
            foreach (var axaml in new[] { false, true })
            {
                var vm = DataGridRegressionModel(connection, "metadata");
                vm.BindingSources.Single().SourceQuery = "SELECT Id,Name,Email,City FROM dbo.Customers WITH (READCOMMITTEDLOCK)";
                var window = axaml ? new PreviewWindow(vm.GenerateExportedAxamlPreviewSnapshot(), vm.Registry) : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry);
                windows.Add(window); window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false; window.Show();
            }
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (windows.Any(w => w.FindControl<Border>("LoadingOverlay")!.IsVisible) && DateTime.UtcNow < deadline) NativeItemsUiWait(50);
            foreach (var window in windows)
            {
                if (!window.FindControl<Border>("DatabaseErrorBanner")!.IsVisible || !window.FindControl<TextBlock>("DatabaseErrorText")!.Text!.Contains("время ожидания")) throw new Exception("Actual SQL command timeout must show a clear Russian error.");
                var grid = window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (grid.ItemsSource!.Cast<object>().Any() || grid.Columns.Count != 4) throw new Exception("Timed-out query must keep columns with zero rows.");
                if (!window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_QUERY_FAILED")) throw new Exception("Command timeout diagnostics missing.");
            }
        }
        finally { foreach (var window in windows) window.Close(); transaction.Rollback(); }
    }

    private sealed record DataGridRegressionPreview(PreviewWindow Window) : IDisposable
    {
        public void Dispose() => Window.Close();
    }
}
