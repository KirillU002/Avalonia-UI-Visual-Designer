using FormDesigner.DesignerSystem.AxamlRoundTrip;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal4DMemory()
    {
        var references = ExerciseAndClose4DSource();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        var retained = references.Count(r => r.IsAlive);
        Console.WriteLine($"MEMORY4D after-close weak-references={references.Length}; alive={retained}; managed-heap={GC.GetTotalMemory(false)}");
        RequireCapability(retained == 0, "Closed source map/models remain rooted after source session replacement");
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ExerciseAndClose4DSource()
    {
        var vm = Scene4C();
        var references = new List<WeakReference> { new(vm.ActiveAxamlSourceDocument!) };
        var host = Named4C(vm, "Host").Id;
        var bytes = GC.GetTotalAllocatedBytes(true);
        var heap = GC.GetTotalMemory(false);
        var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var timer = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var handles = process.HandleCount;
        var threads = process.Threads.Count;
        for (var i = 0; i < 50; i++)
        {
            var item = vm.InsertAxamlControl("Button", host, 0, 0)!;
            references.Add(new(item));
            RequireCapability(vm.RemoveAxamlControls(new[] { item.Id }), vm.StatusText);
            if (i % 10 == 0) vm.MarkAxamlRoundTripSaved("stress.axaml", Text4C(vm), vm.ActiveSession.CurrentSnapshot);
        }
        timer.Stop();
        references.Add(new(vm.ActiveAxamlSourceDocument!));
        RequireCapability(vm.Controls.Count == 4 && vm.CreateActiveAxamlPatch().CanApply, "100-operation source/model divergence");
        process.Refresh();
        Console.WriteLine("MEMORY4D " + JsonSerializer.Serialize(new
        {
            Operations = 100, Ms = timer.Elapsed.TotalMilliseconds,
            AllocatedBytes = GC.GetTotalAllocatedBytes(true) - bytes,
            HeapBefore = heap, HeapAfter = GC.GetTotalMemory(false),
            GC = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray(),
            HandlesBefore = handles, HandlesAfter = process.HandleCount, ThreadsBefore = threads, ThreadsAfter = process.Threads.Count,
            ArchiveEntries = ((System.Collections.IDictionary)typeof(AxamlRoundTripDocument)
                .GetProperty("RemovedSources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(vm.ActiveAxamlSourceDocument)!).Count,
            SourceCharacters = Text4C(vm).Length
        }));
        vm.NewProjectEditorCommand!.Execute(null);
        RequireCapability(vm.ActiveAxamlSourceDocument is null, "New project kept old AXAML session");
        Dispose5AProfileVm(vm);
        return references.ToArray();
    }
}
