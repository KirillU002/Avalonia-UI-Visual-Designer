using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUiaStability()
    {
        var scenario = Environment.GetEnvironmentVariable("UIA_STRESS_SCENARIO") ?? "plain";
        var cycles = int.Parse(Environment.GetEnvironmentVariable("UIA_STRESS_CYCLES") ?? "100");
        if (cycles is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(cycles));
        var folder = Path.GetFullPath(Environment.GetEnvironmentVariable("UIA_STRESS_OUTPUT") ?? Path.Combine(FindRepositoryRoot(), "artifacts", "universal-phase3b1", scenario));
        Directory.CreateDirectory(folder);
        var external = Environment.GetEnvironmentVariable("UIA_STRESS_CLIENT");
        using var client = string.IsNullOrEmpty(external) ? null : StartUiaStressClient(external);
        var observations = new List<object>(); var weak = new List<WeakReference>();
        var watch = Stopwatch.StartNew();
        var vm = scenario.StartsWith("preview") ? MemoryPreviewModel("NoDataGrid") : GridFirstVm();
        if (scenario is "editor" or "mutations" or "undo" or "forms" or "project") vm.CreateControl("TreeView", 0, 0);
        string? alternateForm = null;
        if (scenario == "forms")
        {
            var first = vm.ActiveDocumentId; vm.CreateNewForm("Grid"); alternateForm = vm.ActiveDocumentId; vm.SetActiveForm(first, "uia-stress-setup");
        }
        MainWindow? main = scenario is "editor" or "mutations" or "undo" or "forms" or "project" ? Phase3Show(vm) : null;
        NativeCrashDiagnostics.CaptureModules("uia-stress-start");
        Write("START", new { scenario, cycles, ExternalClient = client is not null, AmbientUiaClient = "uncontrolled", Apartment = Thread.CurrentThread.GetApartmentState().ToString(), UiAccess = Dispatcher.UIThread.CheckAccess(), Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription });
        try
        {
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                Write("CYCLE_START", new { cycle });
                weak.AddRange(UiaStressCycle(scenario, vm, client, alternateForm));
                if ((cycle + 1) % 25 == 0)
                {
                    NativeItemsUiWait(100); ForceFullGc();
                    using var process = Process.GetCurrentProcess(); process.Refresh();
                    var observation = new { Cycle = cycle + 1, ElapsedMs = watch.ElapsedMilliseconds, Alive = weak.Count(r => r.IsAlive), ManagedBytes = GC.GetTotalMemory(false), process.PrivateMemorySize64, process.HandleCount, Threads = process.Threads.Count, PreviewWindow.ActivePreviewWindows };
                    observations.Add(observation); Write("CHECKPOINT", observation);
                }
                Write("CYCLE_END", new { cycle });
            }
            if (client is not null && Environment.GetEnvironmentVariable("UIA_STRESS_RELEASE_CLIENT_FIRST") == "1") StopClient();
            NativeItemsUiWait(1000); ForceFullGc();
            if (scenario.StartsWith("preview") && weak.Any(r => r.IsAlive)) DumpRetainedPreviewIfRequested();
            if (scenario.StartsWith("preview")) RequireCapability(weak.All(r => !r.IsAlive), "Closed Preview retained after stress.");
            var hasEditor = scenario is "editor" or "mutations" or "undo" or "forms" or "project";
            var aliveSessions = hasEditor ? weak.Where((r, index) => index % 2 == 1).Count(r => r.IsAlive) : 0;
            RequireCapability(aliveSessions == 0, "Closed editor sessions retained after stress.");
            RequireCapability(vm.ActiveCollectionEditor is null, "An editor session remains active.");
            if (Environment.GetEnvironmentVariable("UIA_STRESS_HOLD_MEMORY") == "1")
            {
                File.WriteAllText(Path.Combine(folder, "dump-ready.json"), JsonSerializer.Serialize(new { Pid = Environment.ProcessId, Alive = weak.Count(r => r.IsAlive) }));
                var hold = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(folder, "release.txt")) && hold.Elapsed < TimeSpan.FromSeconds(60)) NativeItemsUiWait(20);
            }
            if (client is not null && !client.HasExited) StopClient();
            var result = new { scenario, Completed = cycles, ElapsedMs = watch.ElapsedMilliseconds, Alive = weak.Count(r => r.IsAlive), AliveSessions = aliveSessions, Observations = observations, Success = true };
            File.WriteAllText(Path.Combine(folder, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Write("COMPLETE", result); return 0;
        }
        finally
        {
            main?.CloseForExternalHost();
            foreach (var window in SmokeWindows) window.CloseForExternalHost(); SmokeWindows.Clear();
            if (client is not null && !client.HasExited) { client.Kill(); client.WaitForExit(); }
            NativeItemsUiWait(100);
        }
        void Write(string code, object value)
        {
            File.AppendAllText(Path.Combine(folder, "checkpoints.jsonl"), JsonSerializer.Serialize(new { Code = code, Utc = DateTimeOffset.UtcNow, Pid = Environment.ProcessId, Value = value }) + Environment.NewLine);
            NativeCrashDiagnostics.Write("UIA_STRESS_" + code, reason: scenario);
        }
        void StopClient()
        {
            client!.StandardInput.WriteLine("STOP"); client.StandardInput.Flush();
            DropPumpUntil(() => client.HasExited, "UIA client shutdown");
            RequireCapability(client.ExitCode == 0, "External UIA client failed.");
        }
    }

    private static Process StartUiaStressClient(string path)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.GetFullPath(path)); start.ArgumentList.Add(Environment.ProcessId.ToString());
        var process = Process.Start(start)!;
        var ready = process.StandardOutput.ReadLineAsync(); DropPumpUntil(() => ready.IsCompleted, "UIA client ready");
        RequireCapability(ready.GetAwaiter().GetResult() == "READY", "UIA client did not initialize.");
        return process;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] UiaStressCycle(string scenario, MainWindowViewModel vm, Process? client, string? alternateForm)
    {
        Window window; CollectionEditorSession? session = null; IDisposable? preview = null;
        if (scenario.StartsWith("preview"))
        {
            var shown = ShowDataGridRegressionPreview(vm, scenario == "preview-axaml"); preview = shown; window = shown.Window;
        }
        else if (scenario is "editor" or "mutations" or "undo" or "forms" or "project")
        {
            vm.SelectSingleControl(vm.Controls.Single(c => c.Type == "TreeView"));
            session = vm.OpenCollectionEditor("Items")!;
            window = new StandardItemsEditorWindow(vm, session) { ShowInTaskbar = false };
            window.Show();
        }
        else
        {
            var body = new StackPanel(); body.Children.Add(new TextBox { Text = "UIA test" });
            if (scenario == "menu")
            {
                var menu = new Menu(); var item = new MenuItem { Header = "File" }; item.Items.Add(new MenuItem { Header = "Nested" }); menu.Items.Add(item); body.Children.Add(menu);
                var context = new ContextMenu(); context.Items.Add(new MenuItem { Header = "Context" }); body.ContextMenu = context;
            }
            if (scenario == "popup")
            {
                body.Children.Add(new ComboBox { ItemsSource = new[] { "one", "two" }, Width = 180 });
                body.Children.Add(new ListBox { ItemsSource = new[] { "one", "two" } });
                var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Tab", Content = "Content" }); body.Children.Add(tabs);
                var tree = new TreeView(); var node = new TreeViewItem { Header = "Root" }; node.Items.Add(new TreeViewItem { Header = "Child" }); tree.Items.Add(node); node.IsExpanded = true; body.Children.Add(tree);
                body.Children.Add(new Button { Content = "Flyout", Flyout = new Flyout { Content = new TextBox { Text = "Popup" } } });
            }
            window = new Window { Width = 400, Height = 450, ShowInTaskbar = false, Content = body }; window.Show();
        }
        NativeItemsUiWait(20); window.UpdateLayout();
        if (scenario == "popup")
        {
            var body = (StackPanel)window.Content!;
            body.Children.OfType<ComboBox>().Single().IsDropDownOpen = true;
            body.Children.OfType<Button>().Single().Flyout!.ShowAt(body.Children.OfType<Button>().Single());
        }
        if (scenario == "menu")
        {
            var body = (StackPanel)window.Content!;
            ((MenuItem)body.Children.OfType<Menu>().Single().Items[0]!).IsSubMenuOpen = true;
            body.ContextMenu!.Open(body);
        }
        Task<string?>? completed = null;
        if (client is not null)
        {
            client.StandardInput.WriteLine(window.TryGetPlatformHandle()!.Handle.ToInt64() + " 100"); client.StandardInput.Flush();
            var acquired = client.StandardOutput.ReadLineAsync(); DropPumpUntil(() => acquired.IsCompleted, "UIA node acquisition");
            RequireCapability(acquired.Result?.StartsWith("ACQUIRED 00000000") == true, "External UIA node acquisition failed: " + acquired.Result);
            NativeCrashDiagnostics.Write("UIA_STRESS_CLIENT_ACQUIRED", reason: acquired.Result!);
            completed = client.StandardOutput.ReadLineAsync();
        }
        if (scenario is "mutations" or "undo")
        {
            RequireCapability(session!.Add(), "Add failed"); var root = session.SelectedItemId;
            RequireCapability(session.Add(nested: true), "Nested add failed");
            RequireCapability(session.Add(nested: true), "Deep add failed");
            session.Select(root); RequireCapability(session.Add(), "Sibling add failed");
            RequireCapability(session.Move(-1), "Reorder failed");
            RequireCapability(session.Remove(), "Subtree removal failed");
            if (scenario == "undo") { GridEditorUndo(vm); GridEditorUndo(vm, true); }
            foreach (var id in session.Roots.Select(n => n.Id).ToArray()) { session.Select(id); session.Remove(); }
        }
        var references = new List<WeakReference> { new(window) };
        if (session is not null) references.Add(new(session));
        if (scenario == "forms") { var form = vm.ActiveDocumentId; vm.SetActiveForm(alternateForm!, "uia-stress"); vm.SetActiveForm(form, "uia-stress"); }
        if (scenario == "project") { vm.NewProjectEditorCommand!.Execute(null); vm.CreateControl("TreeView", 0, 0); }
        // Intentionally close with popup/menu open; do not hide lifecycle defects.
        window.Close(); preview?.Dispose();
        if (completed is not null) { DropPumpUntil(() => completed.IsCompleted, "UIA query completion"); RequireCapability(completed.Result?.StartsWith("DONE ") == true, "UIA query client lost completion."); NativeCrashDiagnostics.Write("UIA_STRESS_CLIENT_DONE", reason: completed.Result!); }
        RequireCapability(!window.IsVisible, "Window is still visible after Close.");
        return references.ToArray();
    }
}
