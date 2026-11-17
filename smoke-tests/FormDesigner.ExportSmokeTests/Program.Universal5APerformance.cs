using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // This file can also run against the archived Phase 4B assembly: use the same
    // existing control types and measurements for an actual before/after pair.
    private static int RunUniversal5AProfile()
    {
        var types = (Environment.GetEnvironmentVariable("UNIVERSAL5A_PROFILE_TYPES") ??
            "Button,NumericUpDown,ScrollBar,GridSplitter,Rectangle,Ellipse,HyperlinkButton,RepeatButton").Split(',');
        var samples = new List<object>();
        foreach (var type in types)
        {
            var vm = GridFirstVm();
            var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout();
            try
            {
                for (var iteration = -5; iteration < 30; iteration++)
                {
                    vm.NewProjectEditorCommand!.Execute(null); window.UpdateLayout();
                    double selectionMs=0,layoutMs=0;
                    void Measure(string operation, Action action)
                    {
                        using var trace = Environment.GetEnvironmentVariable("UNIVERSAL5A_PROFILE_STAGES") == "1"
                            ? FormDesigner.Services.LayoutDropProfile.Begin(vm.ActiveDocumentId, operation) : null;
                        var bytes = GC.GetAllocatedBytesForCurrentThread();
                        var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                        var builds = AvaloniaMetadataCatalog.BuildCount;
                        using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
                        var timer = Stopwatch.StartNew(); action(); timer.Stop();
                        if (iteration >= 0) samples.Add(new { Type = type, Operation = operation, Iteration = iteration,
                            Ms = timer.Elapsed.TotalMilliseconds, SelectionMs=selectionMs, LayoutMs=layoutMs, Bytes = GC.GetAllocatedBytesForCurrentThread() - bytes,
                            CpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                            GC = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray(),
                            Stages = trace?.Stages.ToArray(), Counters = trace?.Counters,
                            MetadataBuilds = AvaloniaMetadataCatalog.BuildCount - builds,
                            PeakWorkingSet = process.PeakWorkingSet64 });
                    }
                    Measure("ToolboxCreate", () => { vm.CreateControl(type, 0, 0); window.UpdateLayout(); });
                    var target = vm.Controls.Single(); vm.CreateControl(Environment.GetEnvironmentVariable("UNIVERSAL5A_SELECTION_SOURCE") ?? "TextBox", 0, 0); window.UpdateLayout();
                    Measure("Inspector", () => { var start=Stopwatch.GetTimestamp(); vm.SelectSingleControl(target); var selected=Stopwatch.GetTimestamp(); window.UpdateLayout(); var laidOut=Stopwatch.GetTimestamp(); selectionMs=Stopwatch.GetElapsedTime(start,selected).TotalMilliseconds;layoutMs=Stopwatch.GetElapsedTime(selected,laidOut).TotalMilliseconds; });
                    Measure("SaveLoad", () => { var json = vm.ExportWorkspaceJson(); var copy = GridFirstVm();
                        try { copy.LoadDocumentJson(json); RequireCapability(copy.Controls.Any(c => c.Id == target.Id && c.Type == type), "Save/load identity lost."); }
                        finally { Dispose5AProfileVm(copy); } });
                    Measure("ExportAxaml", vm.GenerateXaml);
                    foreach (var mode in new[] { false, true })
                        Measure(mode ? "AxamlPreview" : "LegacyPreview", () => { using var preview = ShowDataGridRegressionPreview(vm, mode);
                            RequireCapability(preview.Window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == target.Name && c.GetType().Name == type), "Preview profile lacks native control."); });
                }
            }
            finally { window.CloseForExternalHost(); SmokeWindows.Clear(); Dispose5AProfileVm(vm); }
            Console.WriteLine("PROFILE5A " + type + " samples=30 warmup=5");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL5A_OUTPUT")!, JsonSerializer.Serialize(samples));
        return 0;
    }
    private static void Dispose5AProfileVm(FormDesigner.ViewModels.MainWindowViewModel vm)
    {
        // A benchmark owns these non-application VMs. Use the existing lifecycle
        // cleanup: merely closing the test view does not remove VM static traces/timers.
        typeof(FormDesigner.ViewModels.MainWindowViewModel).GetMethod("DisposeExportOnlyViewModel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, null);
        RequireCapability(FormDesigner.ViewModels.PropertyGridRowViewModel.ValueTrace?.GetInvocationList().Any(d => ReferenceEquals(d.Target, vm)) != true,
            "Profile VM still subscribed to static ValueTrace.");
    }
}
