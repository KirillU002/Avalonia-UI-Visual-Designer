using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Services;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // ACK-only boundary: patch creation, transport and initial projection are outside
    // the timer. The 4C benchmark continues to measure the full structural commands.
    private static int RunUniversal4DAckProfile()
    {
        var output = Environment.GetEnvironmentVariable("UNIVERSAL4D_ACK_OUTPUT")!;
        RequireCapability(!File.Exists(output), "Fresh ACK profile output required");
        var samples = new List<object>();
        foreach (var (scene, count) in new[] { ("Small", 4), ("Medium", 100), ("Large", -1) })
        {
            var source = count < 0 ? File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "MainWindow.axaml"))
                : "<Window xmlns='https://github.com/avaloniaui'><StackPanel Name='Host'>"
                    + string.Concat(Enumerable.Range(0, count).Select(i => $"<Button Name='B{i}'/><!--keep-->"))
                    + "</StackPanel></Window>";
            for (var iteration = -5; iteration < 30; iteration++)
            {
                var vm = GridFirstVm();
                try
                {
                    vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source, "ack.axaml"));
                    var host = vm.ActiveAxamlSourceDocument!.SourceMap.Controls.First(r =>
                        r.Element.LocalName is "Grid" or "StackPanel" && vm.GetAxamlStructuralCapability(r.ControlId).CanAddChild);
                    var added = vm.InsertAxamlControl("Button", host.ControlId, 0, 0, index: int.MaxValue)!;
                    vm.MarkAxamlRoundTripSaved("ack.axaml", Text4C(vm), vm.ActiveSession.CurrentSnapshot);
                    RequireCapability(vm.ReorderAxamlControl(added.Id, 0), vm.StatusText);
                    var text = Text4C(vm);
                    var snapshot = vm.ActiveSession.CurrentSnapshot;
                    var map = vm.ActiveAxamlSourceDocument!.SourceMap;
                    var selected = vm.SelectedControl;
                    using var profile = LayoutDropProfile.Begin(vm.ActiveDocumentId, "ACK");
                    var bytes = GC.GetAllocatedBytesForCurrentThread();
                    var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                    var timer = Stopwatch.StartNew();
                    vm.MarkAxamlRoundTripSaved("ack.axaml", text, snapshot);
                    timer.Stop();
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    RequireCapability(ReferenceEquals(map, vm.ActiveAxamlSourceDocument!.SourceMap)
                        && ReferenceEquals(selected, vm.SelectedControl) && !vm.HasUnsavedChanges,
                        "Exact ACK replaced projection or selection");
                    if (iteration >= 0)
                    {
                        using var process = Process.GetCurrentProcess();
                        samples.Add(new { Scene = scene, Iteration = iteration, Ms = timer.Elapsed.TotalMilliseconds,
                            Bytes = allocated, GC = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray(),
                            PeakWorkingSet = process.PeakWorkingSet64, Stages = profile.Stages.ToArray() });
                    }
                }
                finally { Dispose5AProfileVm(vm); }
            }
            Console.WriteLine($"PROFILE4D_ACK {scene} warmup=5 samples=30");
        }
        File.WriteAllText(output, JsonSerializer.Serialize(samples));
        return 0;
    }
}
