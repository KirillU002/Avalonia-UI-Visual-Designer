using FormDesigner.Models;
using System.Diagnostics;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunDataGridPreviewLiveExport()
    {
        Directory.CreateDirectory(Path.Combine(FindRepositoryRoot(), "artifacts", "datagrid-preview-p0"));
        var connection = Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_SQLSERVER");
        if (string.IsNullOrEmpty(connection)) { Console.WriteLine("SKIP SQL Server generated runtime: no test connection configured."); return 0; }
        try
        {
            var vm = DataGridRegressionModel(connection, "metadata");
            vm.CreateNewForm(); vm.Controls.Clear(); vm.BindingSources.Clear();
            var orders = DataGridRegressionModel(connection, "multiple").BindingSources.Single(s => s.Id == "p0-orders").Clone();
            vm.BindingSources.Add(orders); vm.Controls.Add(DataGrid("OrdersGrid", orders.Id, 10, 10, 760, 240));
            vm.OpenFormDocument(vm.CurrentProject.Forms.First().Id);
            // This fixture uses Windows authentication and contains no password. Use the
            // existing opt-in SQL export setting; no runtime export implementation changes.
            vm.ExportSqlConnectionString = true;
            vm.UseCustomNuGetSource = false; vm.CustomNuGetSource = ""; vm.GenerateXaml();
            var scenario = new SmokeScenario("DataGridPreviewLiveExport", _ => { }, _ => { }, RequiresRealDataGrid: true);
            var context = new SmokeContext(scenario, vm, FindRepositoryRoot(), vm.GeneratedXaml, vm.GeneratedCSharp, vm.GeneratedFiles.ToList(), "", "");
            using var project = ExportToTemporaryProject(context);
            if (vm.RequiredPackages.Any(p => p.Id == "Npgsql")) throw new Exception("SQL Server export unexpectedly requires PostgreSQL.");
            var app = Path.Combine(project.Path, "App.axaml.cs");
            var source = File.ReadAllText(app);
            const string startup = "desktop.MainWindow = new MainWindow();";
            if (!source.Contains(startup)) throw new Exception("Generated startup was not found.");
            File.WriteAllText(app, source.Replace(startup, "{ " + startup + " DataGridRuntimeProbe.Attach(desktop, desktop.MainWindow); }"));
            File.WriteAllText(Path.Combine(project.Path, "DataGridRuntimeProbe.cs"), DataGridRuntimeProbeSource.Replace("__NAMESPACE__", vm.ExportProjectNamespace));
            var restore = RunProcess("dotnet", "restore --verbosity quiet", project.Path);
            if (restore.ExitCode != 0) throw new Exception("Generated restore failed: " + restore.Output);
            DotnetBuild(project.Path);
            var name = Path.GetFileNameWithoutExtension(Directory.GetFiles(project.Path, "*.csproj").Single());
            var resultPath = Path.Combine(FindRepositoryRoot(), "artifacts", "datagrid-preview-p0", "generated-runtime.txt");
            File.WriteAllText(resultPath, "START");
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = project.Path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(project.Path, "bin", "Debug", "net6.0", name + ".dll"));
            start.Environment["FORMDESIGNER_DATAGRID_PROBE_RESULT"] = resultPath;
            using var process = Process.Start(start) ?? throw new Exception("Generated application did not start.");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30000)) { process.Kill(entireProcessTree: true); throw new Exception("Generated SQL application timed out."); }
            if (process.ExitCode != 0 || !File.ReadAllText(resultPath).StartsWith("PASS")) throw new Exception("Generated runtime failed: " + error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
            Console.WriteLine(File.ReadAllText(resultPath));
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL LIVE_SQL_generated_restore_build_run: " + ex.Message); return 1; }
    }

    private const string DataGridRuntimeProbeSource = """
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace __NAMESPACE__;
internal static class DataGridRuntimeProbe
{
    internal static void Attach(IClassicDesktopStyleApplicationLifetime lifetime, Window window)
    {
        window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false;
        window.Opened += async (_, _) =>
        {
            var result = Environment.GetEnvironmentVariable("FORMDESIGNER_DATAGRID_PROBE_RESULT")!;
            try
            {
                await Task.Delay(200);
                Require(window, 3, "Анна SQL");
                var secondaryType = typeof(MainWindow).Assembly.GetTypes().Single(t => typeof(Window).IsAssignableFrom(t) && t != typeof(MainWindow) && t.GetConstructor(Type.EmptyTypes) is not null);
                var secondary = (Window)Activator.CreateInstance(secondaryType)!;
                secondary.Position = new PixelPoint(-3000, -3000); secondary.ShowInTaskbar = false; secondary.ShowActivated = false;
                secondary.Show(); await Task.Delay(200);
                Require(secondary, 2, "19.50");
                secondary.Close();
                File.WriteAllText(result, "PASS LIVE_SQL_generated_restore_build_run: separate process, Customers=3, Orders=2, real visible values and independent ViewModels.");
                lifetime.Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(result, "FAIL " + ex.GetType().Name); lifetime.Shutdown(1); }
        };
    }
    private static void Require(Window window, int expected, string expectedValue)
    {
        var grid = window.GetLogicalDescendants().OfType<DataGrid>().Single();
        if (window.DataContext is null || grid.ItemsSource is null || grid.ItemsSource.Cast<object>().Count() != expected) throw new Exception("Generated binding failed.");
        if (!grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == expectedValue || t.Text == expectedValue.Replace('.', ','))) throw new Exception("Actual SQL values are not visible in the generated DataGrid.");
    }
}
""";
}
