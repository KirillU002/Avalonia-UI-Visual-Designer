using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunBetaPerformance()
    {
        var records = new List<object>();
        var folder = Path.Combine(BetaFolder, "performance"); Directory.CreateDirectory(folder);
        foreach (var count in new[] { 20, 50, 100, 300, 500 })
        {
            var vm = GridFirstVm(); var json = JsonSerializer.Serialize(DropSeed(count, "ToolboxGrid")); vm.LoadDocumentJson(json);
            var window = CreateSmokeWindow(vm); window.Width = 1600; window.Height = 1000;
            window.Position = new PixelPoint(-3500, -3500); window.ShowInTaskbar = false; window.ShowActivated = false;
            window.Show(); vm.CloseStartScreenCommand.Execute(null); GridFirstVisible(window, vm);
            try
            {
                for (var iteration = -3; iteration < 30; iteration++)
                {
                    void Measure(string operation, Action action, bool render = true)
                    {
                        var bytes = GC.GetTotalAllocatedBytes(false); var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                        var watch = Stopwatch.StartNew(); action(); var actionMs = watch.Elapsed.TotalMilliseconds;
                        if (render) GridFirstVisible(window, vm);
                        var visibleMs = watch.Elapsed.TotalMilliseconds;
                        records.Add(new { count, iteration, phase = iteration < 0 ? "Warmup" : "Steady", operation, actionMs, visibleMs,
                            allocatedBytes = GC.GetTotalAllocatedBytes(false) - bytes, gc = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray() });
                    }
                    Measure("NewProject", () => vm.NewProjectEditorCommand!.Execute(null));
                    Measure("OpenProject", () => vm.LoadDocumentJson(json));
                    vm.SelectSingleControl(vm.Controls.First());
                    Measure("Selection", () => vm.SelectSingleControl(vm.Controls.Last()));
                    Measure("InspectorRefresh", () => typeof(MainWindowViewModel).GetMethod("RebuildPropertyGrid", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(vm, new object[] { "BetaBenchmark" }));
                    PreviewWindow? measuredPreview = null;
                    try
                    {
                        Measure("AxamlPreview", () =>
                        {
                            var preview = new PreviewWindow(vm.GenerateExportedAxamlPreviewSnapshot(), vm.Registry);
                            measuredPreview = preview;
                            preview.Position = new PixelPoint(-3500, -3500); preview.ShowActivated = false; preview.ShowInTaskbar = false; preview.Show();
                            DropPumpUntil(() => !preview.FindControl<Border>("LoadingOverlay")!.IsVisible, "Preview ready");
                            preview.UpdateLayout(); var commit = ElementComposition.GetElementVisual(preview)!.Compositor.RequestCommitAsync();
                            DropPumpUntil(() => commit.IsCompleted, "Preview compositor"); commit.GetAwaiter().GetResult();
                        }, false);
                    }
                    finally { measuredPreview?.Close(); }
                    Measure("ExportFolder", () => Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(folder, "export-" + count))), false);
                }
                Console.WriteLine($"PERFORMANCE BETA N={count}: 3 warmup + 30 steady samples per operation; native compositor; no timing assertions.");
            }
            finally { window.CloseForExternalHost(); SmokeWindows.Remove(window); }
            File.WriteAllText(Path.Combine(folder, "operations.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        }
        return 0;
    }
}
