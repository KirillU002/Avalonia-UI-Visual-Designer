using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.Views;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // Supplementary CPU/managed-allocation research, not a window/frame benchmark.
    // Invokes existing VM methods. Does not implement caching, sharing or commands.
    private static int RunHistoryCostBenchmark()
    {
        Directory.CreateDirectory(Layout72Folder);
        var n=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_COUNT")??"100");
        var history=int.Parse(Environment.GetEnvironmentVariable("HISTORY_BENCH_DEPTH")??"10");
        var kind=Environment.GetEnvironmentVariable("HISTORY_BENCH_KIND")??"Captions";
        if(kind=="RestoreProbe")return RunUndoRestoreProbe(n);
        RequireCapability(kind is "Captions" or "Commit" or "Dto" or "Serialize","Unknown history benchmark kind.");
        var vm=NestedModel();var seed=DropSeed(n,"ToolboxGrid");
        const BindingFlags instance=BindingFlags.Instance|BindingFlags.NonPublic;
        var options=(JsonSerializerOptions)typeof(MainWindowViewModel).GetField("JsonOptions",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var snapshots=new List<string>();
        for(var i=0;i<=history;i++){seed.Controls[0].Text="History step "+i;snapshots.Add(JsonSerializer.Serialize(seed,options));}
        vm.LoadDocumentJson(snapshots[^1]);
        vm.ActiveSession.SetHistoryState(snapshots.Take(history),Array.Empty<string>(),snapshots[^1],snapshots[0]);
        var dto=vm.CreatePreviewDocumentSnapshot();
        var method=typeof(MainWindowViewModel).GetMethod(kind=="Captions"?"RebuildUndoRedoHistoryItems":"CreateDocumentFileModel",instance)!;
        var observations=new List<object>();var durations=new List<double>();var allocations=new List<long>();
        for(var iteration=-4;iteration<30;iteration++)
        {
            if(kind=="Commit")
            {
                vm.LoadDocumentJson(snapshots[^1]);
                vm.ActiveSession.SetHistoryState(snapshots.Take(history),Array.Empty<string>(),snapshots[^1],snapshots[0]);
                vm.BeginUndoBatch();vm.Controls[0].Text="Committed new text";
            }
            NativeItemsUiWait(1); // Drain pre-existing diagnostics/command callbacks outside the timed call.
            if(kind=="Commit")vm.ActiveSession.LastHistoryMutationUtc=DateTime.MinValue;
            var bytes=GC.GetAllocatedBytesForCurrentThread();var cpu=BenchmarkThreadCpuMs();
            var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            var watch=Stopwatch.StartNew();
            object? result=kind switch
            {
                "Serialize"=>JsonSerializer.Serialize(dto,options),
                "Commit"=>Commit(),
                _=>method.Invoke(vm,null)
            };
            var ms=watch.Elapsed.TotalMilliseconds;var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            var cpuMs=BenchmarkThreadCpuMs()-cpu;
            observations.Add(new {Iteration=iteration,Phase=iteration==-4?"First":iteration<0?"Warmup":"Steady",Ms=ms,AllocatedBytes=allocated,UiCpuMs=cpuMs,
                Gc0=GC.CollectionCount(0)-gc[0],Gc1=GC.CollectionCount(1)-gc[1],Gc2=GC.CollectionCount(2)-gc[2]});
            if(iteration>=0){durations.Add(ms);allocations.Add(allocated);}
            GC.KeepAlive(result);
            if(kind=="Captions")RequireCapability(vm.UndoRedoHistoryItems.Count==history+1,"History captions lost entries.");
            if(kind=="Commit")RequireCapability(vm.ActiveSession.UndoSnapshots.Count==history+1,"Commit was not atomic.");
        }
        durations.Sort();allocations.Sort();
        var report=new {Kind=kind,N=n,History=history,Build=typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,Debugger=Debugger.IsAttached,
            SnapshotCharacters=snapshots[^1].Length,RetainedHistoryCharacters=snapshots.Sum(s=>(long)s.Length),
            Median=durations[14],P95=durations[28],Min=durations[0],Max=durations[^1],MedianAllocated=allocations[14],Observations=observations};
        File.WriteAllText(Path.Combine(Layout72Folder,$"history-{kind}-{n}-{history}.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"HISTORY {kind} N={n} H={history} median={report.Median:F2} p95={report.P95:F2} MiB={report.MedianAllocated/1048576d:F2}");return 0;
        object? Commit(){vm.CommitUndoBatch();return null;}
    }
    private static int RunUndoRestoreProbe(int n)
    {
        using var f=new PointerFixture();f.Window.Height=920;
        f.Vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(n,"ToolboxGrid")));f.Vm.SelectSingleControl(f.First);f.Refresh();
        var records=new List<object>();
        for(var iteration=0;iteration<3;iteration++)
        {
            var original=f.First;var editor=f.Window.GetVisualDescendants().OfType<DesignerPropertyInspector>().Single()
                .GetVisualDescendants().OfType<PropertyGridTextEditor>().Single(e=>e.Row?.Key=="Text");
            var text=original.Text;editor.Text="Restore probe";editor.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
            DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"probe edit");DropFrame(f.Window);
            using var profile=FormDesigner.Services.LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,original.Id);
            f.Vm.UndoCommand.Execute(null);DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"probe Undo");DropFrame(f.Window);profile.Dispose();
            RequireCapability(f.First.Text==text&&!ReferenceEquals(original,f.First)&&original.Id==f.First.Id,"Restore probe identity/values changed incorrectly.");
            records.Add(new{iteration,profile.ElapsedMs,profile.AllocatedBytes,Stages=profile.Stages.ToArray(),Counters=profile.Counters});
        }
        File.WriteAllText(Path.Combine(Layout72Folder,$"restore-probe-{n}.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Restore diagnostic N={n}: 3 successful commands, same IDs / new model instances.");return 0;
    }
}
