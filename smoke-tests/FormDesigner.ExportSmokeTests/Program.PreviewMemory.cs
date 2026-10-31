using System.Diagnostics;
using System.Text.Json;
using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using Microsoft.Data.SqlClient;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunPreviewMemoryChecks()
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action test)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { test(); passed++; Console.WriteLine("PASS MEMORY_" + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL MEMORY_" + name + ": " + ex); }
            finally { foreach (var window in SmokeWindows) window.CloseForExternalHost(); SmokeWindows.Clear(); }
        }
        foreach (var axaml in new[] { false, true })
        {
            var mode = axaml ? "AXAML" : "Legacy";
            foreach (var scenario in new[] { "NoDataGrid", "EmptyGrid", "StaticRows", "MultipleGrids", "Reopen", "TwentyCycles", "MultiForm", "Eremex" })
                Check(mode + "_" + scenario, () =>
                {
                    var vm = MemoryPreviewModel(scenario);
                    var clockBefore = MemoryAnimationObservers(); var refs = new List<PreviewMemoryReferences>();
                    var cycles = scenario == "TwentyCycles" ? 20 : 3;
                    for (var cycle = 0; cycle < cycles; cycle++)
                    {
                        if (scenario == "MultiForm")
                        {
                            var dataForms = vm.CurrentProject.Forms.Where(f => f.Document.Controls.Any(c => c.Type == "DataGrid")).ToArray();
                            vm.SetActiveForm(dataForms[cycle % dataForms.Length].Id, "memory-multi-form");
                        }
                        refs.Add(CloseMemoryPreview(vm, axaml, scenario != "NoDataGrid" && scenario != "EmptyGrid" && scenario != "Eremex"));
                        if (scenario == "TwentyCycles" && (cycle + 1) % 5 == 0)
                        { VerifyMemoryReleased(refs); Console.WriteLine($"OBSERVED mode={mode}; cycles={cycle + 1}; aliveWindows={refs.Count(r => r.Window.IsAlive)}; clockObservers={MemoryAnimationObservers()}"); }
                    }
                    VerifyMemoryReleased(refs);
                    RequireCapability(MemoryAnimationObservers() == clockBefore, "Animation subscriptions accumulated after closing previews.");
                    Console.WriteLine($"OBSERVED mode={mode}; scenario={scenario}; windows={refs.Count}; contexts={refs.Sum(r => r.Contexts.Count)}; itemsSources={refs.Sum(r => r.Items.Count)}; aliveWindows=0; activeWindows={PreviewWindow.ActivePreviewWindows}; clockBefore={clockBefore}; clockAfter={MemoryAnimationObservers()}");
                });
            Check(mode + "_CloseDuringInitialDelay", () => VerifyMemoryReleased(new[] { CloseMemoryPreviewEarly(axaml) }));
            Check(mode + "_CancelledClose", () => VerifyMemoryReleased(new[] { CloseMemoryPreviewAfterCancelledClose(axaml) }));
            Check(mode + "_CancelledOwnerClose", () => VerifyMemoryReleased(CloseMemoryPreviewsAfterCancelledOwnerClose(axaml)));
            if (OperatingSystem.IsWindows())
                Check(mode + "_CloseFromSendMessage", () => VerifyMemoryReleased(new[] { CloseMemoryPreviewFromSendMessage(axaml) }));
            Check(mode + "_MainWindowOwnerReleasesClosedPreview", () =>
            {
                var vm = MemoryPreviewModel("StaticRows"); var owner = CreateSmokeWindow(vm);
                VerifyMemoryReleased(new[] { CloseMemoryPreviewWithOwner(vm, axaml, owner) }); GC.KeepAlive(owner);
            });
            Check(mode + "_CloseDuringAsyncContractLoad", () =>
            {
                var pending = new TaskCompletionSource<IReadOnlyList<Dictionary<string, string>>>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var provider = UseSqlPreviewRowsProvider((_, _, _, _) => pending.Task);
                var refs = CloseMemoryPreviewDuringQuery(DataGridRegressionModel(ContractSqlProfile, "single"), axaml);
                VerifyMemoryReleased(new[] { refs });
                RequireCapability(refs.Events.Any(e => e.Event == "PREVIEW_DATA_LOAD_CANCELLED"), "Data cancellation lifecycle diagnostic is missing.");
                pending.SetResult(new[] { new Dictionary<string, string> { ["Name"] = "late contract row" } }); NativeItemsUiWait(100);
                RequireCapability(!refs.Window.IsAlive, "Late contract completion revived a closed Preview.");
            });
            var connection = Environment.GetEnvironmentVariable("FORMDESIGNER_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(connection)) { Console.WriteLine("SKIP " + mode + " live LocalDB memory checks: FORMDESIGNER_TEST_SQLSERVER not set."); continue; }
            foreach (var layout in new[] { "single", "empty", "multiple" })
                Check(mode + "_LIVE_SQL_" + layout, () =>
                {
                    var vm = DataGridRegressionModel(connection, layout); var refs = new List<PreviewMemoryReferences>();
                    for (var cycle = 0; cycle < 3; cycle++) refs.Add(CloseMemoryPreview(vm, axaml, layout != "empty", liveSql: layout != "empty"));
                    VerifyMemoryReleased(refs);
                });
            Check(mode + "_LIVE_SQL_CloseDuringQuery", () =>
            {
                using var owner = new SqlConnection(connection); owner.Open(); using var transaction = owner.BeginTransaction();
                using var hold = new SqlCommand("SELECT * FROM dbo.Customers WITH (TABLOCKX,HOLDLOCK)", owner, transaction);
                using (var reader = hold.ExecuteReader()) { while (reader.Read()) { } }
                var vm = DataGridRegressionModel(connection, "metadata"); vm.BindingSources.Single().SourceQuery = "SELECT Id, Name, Email, City FROM dbo.Customers WITH (READCOMMITTEDLOCK)";
                var refs = CloseMemoryPreviewDuringQuery(vm, axaml); VerifyMemoryReleased(new[] { refs });
                RequireCapability(refs.Events.Any(e => e.Event == "PREVIEW_DATA_LOAD_CANCELLED"), "Live SQL cancellation is missing.");
                transaction.Rollback();
            });
        }
        Console.WriteLine($"Preview memory checks: {passed}/{passed + failed}"); return failed == 0 && passed > 0 ? 0 : 1;
    }

    private static MainWindowViewModel MemoryPreviewModel(string scenario)
    {
        var vm = NativeGridControls(); vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        if (scenario == "NoDataGrid") { vm.Controls.Remove(vm.Controls.Single(c => c.Type == "DataGrid")); return vm; }
        if (scenario == "EmptyGrid") return vm;
        if (scenario == "Eremex") { vm = CreateViewModel("MemoryEremex"); ConfigureNativeGridEremexExport(vm); return vm; }
        var source = ProductsSource(); source.UseDemoData = true; vm.BindingSources.Add(source); vm.Controls.Single(c => c.Type == "DataGrid").BindingSourceId = source.Id;
        if (scenario == "MultipleGrids") { var second = vm.CreateControl("DataGrid", 0, 0); second.GridRow = 1; second.BindingSourceId = source.Id; }
        if (scenario == "MultiForm")
        {
            vm.CreateNewForm("Grid"); vm.CreateControl("Button", 0, 0); var secondSource = ProductsSource(); secondSource.Id = "memory-second-source"; secondSource.UseDemoData = true;
            vm.BindingSources.Add(secondSource); vm.CreateControl("DataGrid", 0, 0).BindingSourceId = secondSource.Id;
        }
        return vm;
    }

    private sealed record PreviewMemoryReferences(WeakReference Window, List<WeakReference> Contexts, List<WeakReference> Items,
        List<WeakReference> Grids, List<PreviewLifecycleDiagnostic> Events, string Id, string Mode, int ClosingAttempts = 1);

    private static PreviewMemoryReferences CaptureMemoryReferences(PreviewWindow window)
    {
        var contexts = new List<object>(); var items = new List<object>(); var grids = new List<object>();
        if (window.DataContext is { } context) contexts.Add(context);
        if (window.FindControl<Border>("PreviewSurfaceBorder")?.Child?.DataContext is { } rootContext) contexts.Add(rootContext);
        var service = (PreviewRuntimeService)typeof(PreviewWindow).GetField("_previewRuntimeService", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        if (service.Current is { } runtime) contexts.Add(runtime);
        foreach (var control in window.GetLogicalDescendants().OfType<Control>().Where(c => c is DataGrid || c.GetType().FullName?.EndsWith(".DataGridControl", StringComparison.Ordinal) == true))
        {
            grids.Add(control); if (control.DataContext is { } gridContext) contexts.Add(gridContext);
            var rows = control.GetType().GetProperty("ItemsSource")!.GetValue(control);
            // Shared Array.Empty instances have process lifetime; per-preview collections do not.
            if (rows is not null && !ReferenceEquals(rows, Array.Empty<object>()) && !ReferenceEquals(rows, Array.Empty<Dictionary<string, string>>())) items.Add(rows);
        }
        var events = window.LifecycleDiagnostics.ToList(); window.LifecycleDiagnosticEmitted += (_, entry) => events.Add(entry);
        return new(new WeakReference(window), contexts.Distinct(ReferenceEqualityComparer.Instance).Select(o => new WeakReference(o)).ToList(),
            items.Distinct(ReferenceEqualityComparer.Instance).Select(o => new WeakReference(o)).ToList(), grids.Select(o => new WeakReference(o)).ToList(),
            events, window.PreviewId, window.LifecycleDiagnostics.First().Mode);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences CloseMemoryPreview(MainWindowViewModel vm, bool axaml, bool expectRows, bool liveSql = false)
    {
        using var scope = ShowDataGridRegressionPreview(vm, axaml); var window = scope.Window;
        RequireCapability(!window.FindControl<ProgressBar>("LoadingProgress")!.IsIndeterminate, "Hidden loading indicator still animates.");
        if (axaml) RequireCapability(window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text == "AXAML Preview ready", "Memory test silently fell back to Legacy.");
        var grids = window.GetLogicalDescendants().OfType<DataGrid>().ToArray();
        if (expectRows)
        {
            RequireCapability(grids.Length > 0 && grids.All(g => g.ItemsSource!.Cast<object>().Any()), "Memory test did not load visible rows.");
            if (liveSql) RequireCapability(grids[0].GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Анна SQL"), "Live SQL values did not reach visible cells.");
        }
        var refs = CaptureMemoryReferences(window); window.Close(); return refs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences CloseMemoryPreviewEarly(bool axaml)
    {
        var vm = NativeGridControls(); var window = NewMemoryPreview(vm, axaml); window.Show(); NativeItemsUiWait(30);
        var refs = CaptureMemoryReferences(window); window.Close(); return refs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences CloseMemoryPreviewAfterCancelledClose(bool axaml)
    {
        using var scope = ShowDataGridRegressionPreview(MemoryPreviewModel("StaticRows"), axaml);
        var window = scope.Window;
        EventHandler<WindowClosingEventArgs> cancel = (_, args) => args.Cancel = true;
        window.Closing += cancel; window.Close();
        RequireCapability(window.IsVisible && PreviewWindow.ActivePreviewWindows == 1, "Cancelled Close ended the live preview lifetime.");
        RequireCapability(!window.LifecycleDiagnostics.Any(d => d.Event is "PREVIEW_AUTOMATION_RELEASED" or "PREVIEW_LIFECYCLE_DISPOSED"), "Cancelled Close disconnected a live preview.");
        window.Closing -= cancel;
        var refs = CaptureMemoryReferences(window); window.Close(); return refs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences[] CloseMemoryPreviewsAfterCancelledOwnerClose(bool axaml)
    {
        var vm = MemoryPreviewModel("StaticRows"); var owner = NewMemoryPreview(vm, axaml); var child = NewMemoryPreview(vm, axaml);
        owner.Show(); child.Show(owner); NativeItemsUiWait(700);
        EventHandler<WindowClosingEventArgs> cancel = (_, args) => args.Cancel = true;
        owner.Closing += cancel; owner.Close();
        RequireCapability(owner.IsVisible && child.IsVisible && PreviewWindow.ActivePreviewWindows == 2, "Cancelled owner Close ended a preview lifetime.");
        RequireCapability(!child.LifecycleDiagnostics.Any(d => d.Event == "PREVIEW_AUTOMATION_RELEASED"), "Cancelled owner Close disconnected a live child provider.");
        owner.Closing -= cancel;
        // Avalonia asks the child about closing before the owner's cancellable event.
        // Two genuine child Closing attempts are expected, not two subscriptions.
        var refs = new[] { CaptureMemoryReferences(owner), CaptureMemoryReferences(child) with { ClosingAttempts = 2 } };
        owner.Close(); return refs;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences CloseMemoryPreviewFromSendMessage(bool axaml)
    {
        using var scope = ShowDataGridRegressionPreview(MemoryPreviewModel("StaticRows"), axaml);
        var window = scope.Window;
        Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(window);
        var refs = CaptureMemoryReferences(window); var handle = window.TryGetPlatformHandle()!.Handle;
        var send = Task.Run(() => SendMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero)); // cross-thread WM_CLOSE
        var deadline = Stopwatch.StartNew();
        while (!send.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5)) NativeItemsUiWait(20);
        RequireCapability(send.IsCompleted, "Cross-thread WM_CLOSE did not return."); send.GetAwaiter().GetResult(); NativeItemsUiWait(100);
        RequireCapability(!window.IsVisible, "Cross-thread WM_CLOSE did not finish deferred closing.");
        return refs;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences CloseMemoryPreviewDuringQuery(MainWindowViewModel vm, bool axaml)
    {
        var window = NewMemoryPreview(vm, axaml); window.Show(); var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_QUERY_START") && DateTime.UtcNow < deadline) NativeItemsUiWait(30);
        RequireCapability(window.FindControl<Border>("LoadingOverlay")!.IsVisible && window.FindControl<ProgressBar>("LoadingProgress")!.IsIndeterminate, "Close-during-load fixture was not loading.");
        var status = window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text; var refs = CaptureMemoryReferences(window); window.Close(); NativeItemsUiWait(250);
        RequireCapability(!window.FindControl<ProgressBar>("LoadingProgress")!.IsIndeterminate, "Close did not stop the loading animation.");
        RequireCapability(window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text == status && !window.DataGridDiagnostics.Any(d => d.Event == "DATAGRID_PREVIEW_RENDERED"), "Closed window was updated after cancellation.");
        return refs;
    }

    private static PreviewWindow NewMemoryPreview(MainWindowViewModel vm, bool axaml)
    {
        var window = axaml ? new PreviewWindow(vm.GenerateExportedAxamlPreviewSnapshot(), vm.Registry)
            : new PreviewWindow(vm.CreatePreviewDocumentSnapshot(), vm.Registry, vm.CreatePreviewProjectFormsSnapshot(), vm.ActiveFormDocument!.Id);
        window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false; return window;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static PreviewMemoryReferences CloseMemoryPreviewWithOwner(MainWindowViewModel vm, bool axaml, MainWindow owner)
    {
        var field = typeof(MainWindow).GetField("_launchPreviewWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var scope = ShowDataGridRegressionPreview(vm, axaml, child =>
        {
            field.SetValue(owner, child);
            child.Closed += (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), owner, typeof(MainWindow).GetMethod("LaunchPreviewWindow_Closed", BindingFlags.Instance | BindingFlags.NonPublic)!);
            child.LifecycleDiagnosticEmitted += (EventHandler<PreviewLifecycleDiagnostic>)Delegate.CreateDelegate(typeof(EventHandler<PreviewLifecycleDiagnostic>), owner, typeof(MainWindow).GetMethod("LogPreviewLifecycleDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic)!);
        });
        var references = CaptureMemoryReferences(scope.Window); scope.Window.Close();
        RequireCapability(field.GetValue(owner) is null, "MainWindow kept its closed Preview reference."); return references;
    }

    private static void VerifyMemoryReleased(IEnumerable<PreviewMemoryReferences> references)
    {
        NativeItemsUiWait(1000); ForceFullGc();
        foreach (var refs in references)
        {
            if (refs.Window.IsAlive)
            {
                Console.WriteLine("RETAINED_LIFECYCLE " + JsonSerializer.Serialize(refs.Events));
                DumpRetainedPreviewIfRequested();
            }
            RequireCapability(!refs.Window.IsAlive, "Closed PreviewWindow is still alive: " + refs.Mode);
            RequireCapability(refs.Contexts.All(r => !r.IsAlive), "A closed preview DataContext/runtime context is retained.");
            RequireCapability(refs.Items.All(r => !r.IsAlive), "A closed preview ItemsSource is retained.");
            RequireCapability(refs.Grids.All(r => !r.IsAlive), "A closed preview DataGrid is retained.");
            foreach (var code in new[] { "CREATED", "OPENED", "CLOSING", "CLOSED", "DISPOSED" })
                RequireCapability(refs.Events.Count(e => e.Event == "PREVIEW_LIFECYCLE_" + code) == (code == "CLOSING" ? refs.ClosingAttempts : 1), "Lifecycle callback accumulated or missing: " + code);
            RequireCapability(refs.Events.All(e => e.PreviewId == refs.Id && e.Mode == refs.Mode), "Lifecycle diagnostic identity changed.");
            RequireCapability(!refs.Events.Any(e => e.Event == "PREVIEW_AUTOMATION_RELEASE_FAILED"), "Closed-window UIA disconnect failed.");
        }
        RequireCapability(PreviewWindow.ActivePreviewWindows == 0, "Closed windows remain in the lifecycle active count.");
    }

    private static void DumpRetainedPreviewIfRequested()
    {
        if (Environment.GetEnvironmentVariable("FORMDESIGNER_MEMORY_DUMP_ON_RETAINED") != "1") return;
        var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "preview-memory");
        var start = new ProcessStartInfo(Path.Combine(folder, "tools", "dotnet-dump.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "collect", "-p", Environment.ProcessId.ToString(), "--type", "Heap", "-o", Path.Combine(folder, "retained-" + Guid.NewGuid().ToString("N") + ".dmp") }) start.ArgumentList.Add(arg);
        using var dump = Process.Start(start)!;
        if (!dump.WaitForExit(30000)) dump.Kill();
        else Console.WriteLine(dump.StandardOutput.ReadToEnd());
    }

    private static int MemoryAnimationObservers()
    {
        var type = typeof(AvaloniaObject).Assembly.GetType("Avalonia.Media.MediaContext")!;
        var context = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var clock = type.GetField("_clock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        return new[] { "_observers", "_newObservers" }.Sum(name => ((ICollection)clock.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(clock)!).Count);
    }
    private static int RunPreviewMemoryDump()
    {
        var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "preview-memory"); Directory.CreateDirectory(folder);
        var readyPath = Path.Combine(folder, "dump-ready.json"); var releasePath = Path.Combine(folder, "release-dump.txt");
        if (File.Exists(releasePath)) File.Delete(releasePath);
        var vm = NativeGridControls(); var references = new List<WeakReference>();
        var owned = Environment.GetEnvironmentVariable("FORMDESIGNER_MEMORY_DUMP_OWNED") == "1";
        foreach (var axaml in new[] { false, true })
            for (var i = 0; i < 3; i++)
            {
                if (owned) references.AddRange(CloseMemoryPreviewsAfterCancelledOwnerClose(axaml).Select(r => r.Window));
                else references.Add(AuditClosedPreview(vm, axaml));
            }
        NativeItemsUiWait(1000); ForceFullGc();
        var state = new { ProcessId = Environment.ProcessId, ClosedWindows = references.Count, Alive = references.Count(r => r.IsAlive) };
        File.WriteAllText(readyPath, JsonSerializer.Serialize(state)); Console.WriteLine(JsonSerializer.Serialize(state));
        var watch = Stopwatch.StartNew();
        while (!File.Exists(releasePath) && watch.Elapsed < TimeSpan.FromMinutes(5)) NativeItemsUiWait(50);
        GC.KeepAlive(references); GC.KeepAlive(vm);
        return 0;
    }
}
