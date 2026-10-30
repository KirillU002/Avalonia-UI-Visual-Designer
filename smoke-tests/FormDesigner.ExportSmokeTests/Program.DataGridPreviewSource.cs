using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FormDesigner.ViewModels;
using Microsoft.Data.SqlClient;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunDataGridPreviewSourceWorkflow()
    {
        var connection = Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection)) { Console.WriteLine("SKIP live SQL Settings/schema/BindingSource workflow: no test server configured."); return 0; }
        try
        {
            var options = new SqlConnectionStringBuilder(connection);
            var vm = DataGridRegressionModel(connection, "metadata");
            vm.UseGlobalSqlServerSettings = true; vm.SqlServerName = options.DataSource; vm.SqlDatabaseName = options.InitialCatalog;
            vm.SqlAuthenticationMode = "Windows"; vm.SqlEncryptConnection = false; vm.SqlTrustServerCertificate = true; vm.SqlConnectionTimeoutSeconds = 5;
            var source = vm.BindingSources.Single(); var originalId = source.Id;
            source.Fields.Clear(); source.SourceConnectionString = ""; vm.SelectedBindingSource = source;
            Pump(vm.RefreshSelectedBindingSourceFromDatabaseAsync());
            if (source.Id != originalId || vm.Controls.Single().BindingSourceId != originalId || source.Fields.Count != 4 || source.Fields.Any(f => string.IsNullOrEmpty(f.DbType))) throw new Exception("SQL source discovery lost its binding identity or actual column metadata.");
            foreach (var axaml in new[] { false, true })
            {
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var grid = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                if (grid.ItemsSource!.Cast<object>().Count() != 3 || !grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Борис SQL")) throw new Exception("Schema-discovered SQL source did not reach the visual DataGrid.");
            }
            Console.WriteLine("PASS LIVE_SQL_settings_schema_bindingsource_both_previews: global profile, actual discovered columns, stable BindingSourceId, three distinct real rows.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL LIVE_SQL_settings_schema_bindingsource_both_previews: " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }
}
