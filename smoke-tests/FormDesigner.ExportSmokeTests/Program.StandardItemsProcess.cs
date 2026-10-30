using System.Diagnostics;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void RequireStandaloneStandardItemsProcess(SmokeContext context)
    {
        using var project = ExportToTemporaryProject(context);
        var app = Path.Combine(project.Path, "App.axaml.cs");
        var source = File.ReadAllText(app);
        const string startup = "desktop.MainWindow = new MainWindow();";
        if (!source.Contains(startup)) throw new Exception("Cannot attach the test-only process probe to exported app startup.");
        File.WriteAllText(app, source.Replace(startup, "{ " + startup + " ToolboxSmokeProbe.Attach(desktop, desktop.MainWindow); }"));
        File.WriteAllText(Path.Combine(project.Path, "ToolboxSmokeProbe.cs"), StandaloneItemsProbe.Replace("__NAMESPACE__", context.ViewModel.ExportProjectNamespace));
        DotnetBuild(project.Path);
        var name = Path.GetFileNameWithoutExtension(Directory.GetFiles(project.Path, "*.csproj").Single());
        var resultPath = Path.Combine(FindRepositoryRoot(), "artifacts", "toolbox-standard", "standalone-process.txt");
        File.WriteAllText(resultPath, "START");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = project.Path, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(project.Path, "bin", "Debug", "net6.0", name + ".dll"));
        start.Environment["FORMDESIGNER_TOOLBOX_PROBE_RESULT"] = resultPath;
        using var process = Process.Start(start) ?? throw new Exception("Exported application process failed to start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20000)) { process.Kill(entireProcessTree: true); throw new Exception("Standalone generated UI probe timed out."); }
        if (process.ExitCode != 0 || !File.Exists(resultPath) || !File.ReadAllText(resultPath).StartsWith("PASS", StringComparison.Ordinal))
            throw new Exception("Standalone generated UI failed: " + error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
    }

    private const string StandaloneItemsProbe = """
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace __NAMESPACE__;

internal static class ToolboxSmokeProbe
{
    internal static void Attach(IClassicDesktopStyleApplicationLifetime lifetime, Window window)
    {
        window.Position = new PixelPoint(-3000, -3000);
        window.ShowInTaskbar = false; window.ShowActivated = false;
        window.Opened += async (_, _) =>
        {
            var result = Environment.GetEnvironmentVariable("FORMDESIGNER_TOOLBOX_PROBE_RESULT")!;
            try
            {
                await Task.Delay(250);
                var split = window.FindControl<SplitButton>("ActionSplit")!;
                split.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_PrimaryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.FindControl<TextBlock>("ResultLabel")!.Text != "primary action") throw new Exception("Primary action failed.");
                split.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_SecondaryButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(80);
                if (split.Flyout is not Flyout { IsOpen: true, Content: StackPanel stack }) throw new Exception("Native split menu failed.");
                stack.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.FindControl<TextBlock>("ResultLabel")!.Text != "menu action" || split.Flyout.IsOpen) throw new Exception("Menu action/close failed.");
                var drop = window.FindControl<DropDownButton>("DropMenu")!;
                typeof(DropDownButton).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drop, null); await Task.Delay(50);
                if (!drop.Flyout!.IsOpen) throw new Exception("Native dropdown activation failed.");
                drop.Flyout.Hide();
                var list = window.FindControl<ListBox>("ChoiceList")!; list.SelectedIndex = 2;
                if (!Equals(list.SelectedItem, "Gamma")) throw new Exception("Native list selection failed.");
                File.WriteAllText(result, "PASS separate generated application process: DropDownButton/Flyout, SplitButton primary/menu/Logic/close, ListBox selection.");
                lifetime.Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(result, "FAIL " + ex.GetType().Name + ": " + ex.Message);
                lifetime.Shutdown(1);
            }
        };
    }
}
""";
}
