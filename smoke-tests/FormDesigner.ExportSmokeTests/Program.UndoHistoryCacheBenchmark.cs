using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // VM/CPU family: actual history commands, no claims about visible/render latency.
    private static int RunUndoHistoryCacheBenchmark()
    {
        if(Environment.GetEnvironmentVariable("HISTORY_CACHE_PAIRS_ONLY")=="1")return RunHistoryCaptionPairsBenchmark();
        var n=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_COUNT")??"100");
        var h=int.Parse(Environment.GetEnvironmentVariable("HISTORY_BENCH_DEPTH")??"30");
        var samples=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_SAMPLES")??"30");
        var vm=NestedModel();vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(n,"Reparent")));
        var seed=vm.CreatePreviewDocumentSnapshot();var snapshots=new List<string>();
        var options=(JsonSerializerOptions)typeof(MainWindowViewModel).GetField("JsonOptions",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        for(var i=0;i<=h;i++){seed.Controls[^1].Text="History "+i;snapshots.Add(JsonSerializer.Serialize(seed,options));}
        vm.LoadDocumentJson(snapshots[^1]);
        vm.ActiveSession.SetHistoryState(snapshots.Take(h),Array.Empty<string>(),snapshots[^1],snapshots[0]);
        var records=new List<object>();
        void Measure(string operation,int iteration,Action action)
        {
            var c=vm.ActiveSession.HistoryDescriptions;var hit=c.HitCount;var miss=c.MissCount;
            var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var bytes=GC.GetAllocatedBytesForCurrentThread();
            var watch=Stopwatch.StartNew();action();var ms=watch.Elapsed.TotalMilliseconds;var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            records.Add(new {Operation=operation,Iteration=iteration,Phase=iteration==-4?"First":iteration<0?"Warmup":"Steady",Ms=ms,AllocatedBytes=allocated,
                Gc0=GC.CollectionCount(0)-gc[0],Gc1=GC.CollectionCount(1)-gc[1],Gc2=GC.CollectionCount(2)-gc[2],Hits=c.HitCount-hit,Misses=c.MissCount-miss,CacheCount=c.Count});
        }
        Measure("ColdCaptions",-4,()=>RefreshHistory(vm));
        foreach(var operation in new[]{"HistoryRefresh","Add","Move","Reparent","Resize","Property"})
        {
            for(var iteration=-4;iteration<samples;iteration++)
            {
                NativeItemsUiWait(1);var before=vm.ActiveSession.CurrentSnapshot;
                Measure(operation,iteration,()=>
                {
                    if(operation=="HistoryRefresh"){RefreshHistory(vm);return;}
                    var c=vm.GetControl("leaf-0")!;
                    HistoryEdit(vm,()=>
                    {
                        switch(operation)
                        {
                            case "Add":vm.CreateControl("Button",0,0);break;
                            case "Move":c.StackOrder+=2;break;
                            case "Reparent":c.ParentId="host-b";break;
                            case "Resize":c.Width+=16;break;
                            case "Property":c.Text="Measured text";break;
                        }
                    });
                });
                if(operation=="HistoryRefresh")continue;
                RequireCapability(vm.ActiveSession.UndoSnapshots.Count==h+1,"Benchmark edit not atomic.");
                var changed=vm.ActiveSession.CurrentSnapshot;
                if(operation=="Property")
                {
                    Measure("Undo",iteration,()=>vm.UndoCommand.Execute(null));
                    RequireCapability(vm.ActiveSession.CurrentSnapshot==before,"Undo did not restore the original snapshot.");
                    Measure("Redo",iteration,()=>vm.RedoCommand.Execute(null));
                    RequireCapability(vm.ActiveSession.CurrentSnapshot==changed,"Redo did not restore the edited snapshot.");
                }
                vm.UndoCommand.Execute(null);
                RequireCapability(vm.ActiveSession.CurrentSnapshot==before&&vm.ActiveSession.UndoSnapshots.Count==h,"Benchmark reset changed history.");
            }
        }
        var result=new {N=n,History=h,Samples=samples,CacheEnabled=vm.ActiveSession.HistoryDescriptions.Enabled,
            Build=typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,Debugger=Debugger.IsAttached,
            Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,Family="VM-only; no Window/compositor",Records=records};
        Directory.CreateDirectory(Layout72Folder);
        File.WriteAllText(Path.Combine(Layout72Folder,$"vm-{(result.CacheEnabled?"on":"off")}-{n}-{h}.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"HISTORY_CACHE_VM N={n} H={h} cache={result.CacheEnabled} records={records.Count}");return 0;
    }

    // Exclusive pair formatting/cache lookup family: no UI rows, no reflection inside the clock.
    private static int RunHistoryCaptionPairsBenchmark()
    {
        var n=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_COUNT")??"100");
        var h=int.Parse(Environment.GetEnvironmentVariable("HISTORY_BENCH_DEPTH")??"30");
        var samples=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_SAMPLES")??"30");
        var vm=NestedModel();vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(n,"Reparent")));var seed=vm.CreatePreviewDocumentSnapshot();
        var options=(JsonSerializerOptions)typeof(MainWindowViewModel).GetField("JsonOptions",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var describe=typeof(MainWindowViewModel).GetMethod("DescribeHistoryTransition",BindingFlags.Static|BindingFlags.NonPublic)!
            .CreateDelegate<Func<string,string,(string Title,string Description)>>();
        var snapshots=new List<string>();
        for(var i=0;i<=h;i++){seed.Controls[^1].Text="History "+i;snapshots.Add(JsonSerializer.Serialize(seed,options));}
        vm.ActiveSession.SetHistoryState(snapshots.Take(h),Array.Empty<string>(),snapshots[^1],snapshots[0]);
        var c=vm.ActiveSession.HistoryDescriptions;var records=new List<object>();
        for(var iteration=-4;iteration<samples;iteration++)
        {
            NativeItemsUiWait(1);var hit=c.HitCount;var miss=c.MissCount;var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            var bytes=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
            c.Prepare(snapshots,"caption-pairs-v1");
            for(var i=1;i<snapshots.Count;i++)c.GetOrCreate(snapshots[i-1],snapshots[i],describe);
            var ms=clock.Elapsed.TotalMilliseconds;var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            records.Add(new{Operation="CaptionPairs",Iteration=iteration,Phase=iteration==-4?"First":iteration<0?"Warmup":"Steady",Ms=ms,AllocatedBytes=allocated,
                Gc0=GC.CollectionCount(0)-gc[0],Gc1=GC.CollectionCount(1)-gc[1],Gc2=GC.CollectionCount(2)-gc[2],Hits=c.HitCount-hit,Misses=c.MissCount-miss,CacheCount=c.Count});
        }
        var result=new{N=n,History=h,Samples=samples,CacheEnabled=c.Enabled,Family="Caption pairs only; no history UI rows",
            Build=typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,Debugger=Debugger.IsAttached,Records=records};
        Directory.CreateDirectory(Layout72Folder);
        File.WriteAllText(Path.Combine(Layout72Folder,$"captions-{(c.Enabled?"on":"off")}-{n}-{h}.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"HISTORY_CACHE_CAPTIONS N={n} H={h} cache={c.Enabled} records={records.Count}");return 0;
    }
}
