using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.Services;
using FormDesigner.Models;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // Research harness only. Actual resize routing and the existing Inspector editor;
    // no alternative model mutation, projection cache or Undo implementation.
    private static int RunUndoFeasibilityBenchmark()
    {
        Directory.CreateDirectory(Layout72Folder);
        var scenario=Environment.GetEnvironmentVariable("UNDO_BENCH_SCENARIO")??"Property";
        var count=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_COUNT")??"20");
        var samples=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_SAMPLES")??"30");
        var directRestore=Environment.GetEnvironmentVariable("UNDO_BENCH_DIRECT_RESTORE")=="1";
        RequireCapability(scenario is "Resize" or "Property","Unknown Undo benchmark scenario.");
        using var f=new PointerFixture();
        f.Window.Height=920;
        var seed=DropSeed(count,"ToolboxGrid");
        // Give the selected first row real content height, keeping its resize grip
        // hit-testable at N=500. Other tracks retain the original Star semantics.
        seed.SurfaceGridRowDefinitions="Auto,"+string.Join(",",Enumerable.Repeat("*",seed.SurfaceLayoutRows-1));
        f.Vm.LoadDocumentJson(JsonSerializer.Serialize(seed));f.Vm.SelectSingleControl(f.First);f.Refresh();
        RequireCapability(f.Vm.PreviewFormWidth==1200&&f.Vm.PreviewFormHeight==800,"Unexpected viewport.");
        var observations=new List<DropObservation>();var passes=0;EventHandler onLayout=(_,_)=>passes++;
        f.Window.LayoutUpdated+=onLayout;
        try
        {
            for(var iteration=-4;iteration<samples;iteration++)
            {
                var originalWidth=f.First.Width;var originalHeight=f.First.Height;var originalText=f.First.Text;
                var pointer=scenario=="Resize"?f.ResizePoint():default;
                var editor=scenario=="Property"?f.Window.GetVisualDescendants().OfType<DesignerPropertyInspector>().Single()
                    .GetVisualDescendants().OfType<PropertyGridTextEditor>().Single(e=>e.Row?.Key=="Text"):null;
                var undoBefore=f.Vm.ActiveSession.UndoSnapshots.Count;
                var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var jit=JitInfo.GetCompiledMethodCount();
                var bytes=GC.GetTotalAllocatedBytes(false);passes=0;
                using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,f.First.Id);
                var cpuBefore=BenchmarkThreadCpuMs();
                var clock=Stopwatch.StartNew();
                using(LayoutDropProfile.Stage(scenario+"Event"))
                {
                    if(scenario=="Resize") {f.Down(pointer);f.Move(pointer+new Vector(16,8));f.Up(pointer+new Vector(16,8));}
                    else {editor!.Text="Measured property value";editor.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});}
                }
                var handler=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("DispatcherToVisual"))DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"authored visual update");
                var visual=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("AttachedUILayout"))f.Window.UpdateLayout();
                var layout=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("FirstCompositorCommit"))DropFrame(f.Window);
                var frame=clock.Elapsed.TotalMilliseconds;var input=false;
                using(LayoutDropProfile.Stage("NextInputReady")){Dispatcher.UIThread.Post(()=>input=true,DispatcherPriority.Input);DropPumpUntil(()=>input,"next input");}
                profile.Dispose();
                var record=new DropObservation(iteration,iteration==-4?"FirstDrop":iteration<0?"Warmup":"Steady",0,handler,visual,layout,frame,clock.Elapsed.TotalMilliseconds,
                    profile.AllocatedBytes,GC.GetTotalAllocatedBytes(false)-bytes,GC.CollectionCount(0)-gc[0],GC.CollectionCount(1)-gc[1],GC.CollectionCount(2)-gc[2],
                    JitInfo.GetCompiledMethodCount()-jit,passes,f.Vm.Controls.Count,DropWrappers(f.Window).Count,profile.Stages.ToArray(),new(profile.Counters))
                    {SnapshotCharacters=f.Vm.ActiveSession.CurrentSnapshot.Length,RetainedHistoryCharacters=RetainedHistoryCharacters(f.Vm),UndoEntryDelta=f.Vm.ActiveSession.UndoSnapshots.Count-undoBefore,
                        UiCpuMs=BenchmarkThreadCpuMs()-cpuBefore};
                observations.Add(record);
                RequireCapability(record.UndoEntryDelta==1,"A single edit did not produce one atomic Undo entry.");
                RequireCapability(scenario=="Property"?f.First.Text!=originalText:f.First.Width!=originalWidth||f.First.Height!=originalHeight,"The measured edit did not change the model.");
                var undoClock=Stopwatch.StartNew();var undoBytes=GC.GetAllocatedBytesForCurrentThread();
                if(directRestore){record.ToolbarUndoEnabledBeforeCommand=f.Vm.UndoEditorCommand!.CanExecute(null);f.Vm.UndoCommand.Execute(null);}else GridEditorUndo(f.Vm);
                DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"Undo visual restore");DropFrame(f.Window);
                record.UndoCommitMs=undoClock.Elapsed.TotalMilliseconds;record.UndoUiAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-undoBytes;
                RequireCapability(f.First.Text==originalText&&f.First.Width==originalWidth&&f.First.Height==originalHeight,"Undo failed to restore the measured edit.");
                if(directRestore)
                {
                    var redoClock=Stopwatch.StartNew();var redoBytes=GC.GetAllocatedBytesForCurrentThread();
                    record.ToolbarRedoEnabledBeforeCommand=f.Vm.RedoEditorCommand!.CanExecute(null);f.Vm.RedoCommand.Execute(null);
                    DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"Redo visual restore");DropFrame(f.Window);
                    record.RedoCommitMs=redoClock.Elapsed.TotalMilliseconds;record.RedoUiAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-redoBytes;
                    RequireCapability(scenario=="Property"?f.First.Text=="Measured property value":f.First.Width!=originalWidth||f.First.Height!=originalHeight,"Redo did not restore the edit.");
                    f.Vm.UndoCommand.Execute(null);DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"final reset");DropFrame(f.Window);
                    RequireCapability(f.First.Text==originalText&&f.First.Width==originalWidth&&f.First.Height==originalHeight,"Final Undo reset failed.");
                }
            }
            var steady=observations.Where(o=>o.Phase=="Steady").ToArray();
            double Q(Func<DropObservation,double> field,double q=.5){var sorted=steady.Select(field).Order().ToArray();return sorted[(int)Math.Ceiling(sorted.Length*q)-1];}
            var result=new {scenario,count,samples,directRestore,Build=typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
                Debugger=Debugger.IsAttached,Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,GCSettings.IsServerGC,
                Viewport="1200x800 DIP",f.Window.RenderScaling,f.Window.ClientSize,Median=Q(o=>o.FirstCommitMs),P95=Q(o=>o.FirstCommitMs,.95),
                Min=steady.Min(o=>o.FirstCommitMs),Max=steady.Max(o=>o.FirstCommitMs),MedianAllocated=Q(o=>o.UiAllocatedBytes),Observations=observations};
            File.WriteAllText(Path.Combine(Layout72Folder,$"{(directRestore?"direct":"undo")}-{scenario}-{count}.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"UNDO_BENCH {scenario} N={count} median={result.Median:F2} p95={result.P95:F2} samples={samples}");return 0;
        }
        finally {f.Window.LayoutUpdated-=onLayout;}
    }
}
