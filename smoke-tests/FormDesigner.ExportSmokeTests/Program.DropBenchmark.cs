using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string Layout72Folder => Environment.GetEnvironmentVariable("DROP_BENCH_OUTPUT_DIRECTORY")
        ?? Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase7a2");
    private sealed record DropObservation(int Iteration, string Phase, double HoverMs, double HandlerMs,
        double VisualMs, double LayoutMs, double FirstCommitMs, double ReadyMs, long UiAllocatedBytes,
        long ProcessAllocatedBytes, int Gen0, int Gen1, int Gen2, long JittedMethods, int LayoutPasses,
        int Controls, int Wrappers, LayoutDropProfile.StageResult[] Stages, Dictionary<string,long> Counters)
    {
        public long SnapshotCharacters {get;init;}
        public long RetainedHistoryCharacters {get;init;}
        public int UndoEntryDelta {get;init;}
        // Valid product restore timing only in directRestore=true datasets.
        // The legacy reset helper otherwise includes 160+100ms test pumps.
        public double UndoCommitMs {get;set;}
        public long UndoUiAllocatedBytes {get;set;}
        public double RedoCommitMs {get;set;}
        public long RedoUiAllocatedBytes {get;set;}
        public bool? ToolbarUndoEnabledBeforeCommand {get;set;}
        public bool? ToolbarRedoEnabledBeforeCommand {get;set;}
        public double? UiCpuMs {get;init;}
        public long HistoryCacheHits {get;init;}
        public long HistoryCacheMisses {get;init;}
        public int HistoryCacheCount {get;init;}
    }
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread,out long created,out long exited,out long kernel,out long user);
    private static double? BenchmarkThreadCpuMs()=>OperatingSystem.IsWindows()&&GetThreadTimes(new IntPtr(-2),out _,out _,out var kernel,out var user)
        ?(kernel+user)/10000d:null;
    private static long RetainedHistoryCharacters(MainWindowViewModel vm)
    {
        var session=vm.ActiveSession;
        var strings=new HashSet<string>(ReferenceEqualityComparer.Instance){session.CurrentSnapshot,session.SavedSnapshot};
        strings.UnionWith(session.UndoSnapshots);strings.UnionWith(session.RedoSnapshots);
        return strings.Sum(s=>(long)s.Length);
    }

    private static void DropPumpUntil(Func<bool> ready, string reason)
    {
        var deadline=Stopwatch.GetTimestamp()+Stopwatch.Frequency*15;
        while(!ready() && Stopwatch.GetTimestamp()<deadline) NativeItemsUiWait(1);
        RequireCapability(ready(),"Benchmark timeout: "+reason);
    }
    private static void DropFrame(MainWindow window)
    {
        window.UpdateLayout();
        var task=ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        DropPumpUntil(()=>task.IsCompleted,"compositor commit"); task.GetAwaiter().GetResult();
    }
    private static Dictionary<string,Border> DropWrappers(MainWindow window) =>
        (Dictionary<string,Border>)typeof(MainWindow).GetField("_wrapperByControlId",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
    private static bool DropSnapshotReady(MainWindow window,MainWindowViewModel vm)
    {
        if(vm.UsesNativeLayout&&vm.NativeGridMeasurementKey!=vm.GridDropLayoutKey)return false;
        var wrappers=DropWrappers(window);var visible=0;
        foreach(var model in vm.Controls)
        {
            if(vm.UsesNativeLayout&&(!vm.NativeLayoutBounds.TryGetValue(model.Id,out var bounds)||bounds.Width<=0||bounds.Height<=0))continue;
            visible++;if(!wrappers.TryGetValue(model.Id,out var wrapper)||!ReferenceEquals(wrapper.Tag,model))return false;
        }
        return visible==wrappers.Count;
    }

    private static DesignerDocumentFileModel DropSeed(int count,string scenario)
    {
        var canvas=scenario=="ToolboxCanvas";
        var document=new DesignerDocumentFileModel { LayoutSemanticsVersion=canvas?1:2, DesignWidth=1200, DesignHeight=800,
            SurfaceLayoutMode=canvas?DesignerLayoutModes.Absolute:DesignerLayoutModes.Grid, SurfaceLayoutColumns=1, SurfaceLayoutRows=1,
            SurfaceGridColumnDefinitions="*",SurfaceGridRowDefinitions="*",FormTitle="Drop benchmark" };
        var grid=scenario=="ToolboxGrid";
        if(grid){document.SurfaceGridColumnDefinitions=string.Join(",",Enumerable.Repeat("*",10));document.SurfaceLayoutColumns=10;
            document.SurfaceLayoutRows=(count+9)/10+1;document.SurfaceGridRowDefinitions=string.Join(",",Enumerable.Repeat("*",document.SurfaceLayoutRows));}
        var parent="";
        if(!canvas&&!grid)
        {
            var mode=scenario switch {"ToolboxStackPanel" or "Reparent"=>DesignerLayoutModes.Stack,"ToolboxDockPanel"=>DesignerLayoutModes.Dock,_=>DesignerLayoutModes.Flex};
            parent="host-a"; AddHost(parent,mode,0);
            if(scenario=="Reparent"){document.SurfaceGridColumnDefinitions="*,*";document.SurfaceLayoutColumns=2;AddHost("host-b",mode,1);}
        }
        var leaves=count-document.Controls.Count;
        for(var i=0;i<leaves;i++)document.Controls.Add(new DesignerControlFileModel {Id="leaf-"+i,Type="Button",DescriptorId="Button",Name="Item"+i,Text="Item "+i,
            ParentId=parent,Width=75,Height=28,WidthMode="Fixed",HeightMode="Fixed",StackOrder=i,GridColumn=grid?i%10:0,GridRow=grid?i/10:0,
            X=canvas?(i%14)*80:0,Y=canvas?(i/14)*32:0,HorizontalAlignment="Stretch",VerticalAlignment="Top"});
        return document;
        void AddHost(string id,string mode,int column)=>document.Controls.Add(new DesignerControlFileModel {Id=id,Name=id.Replace('-','_'),
            Type=mode==DesignerLayoutModes.Stack?"StackPanel":mode==DesignerLayoutModes.Flex?"WrapPanel":"Group",ChildLayoutMode=mode,
            WidthMode="Auto",HeightMode="Auto",HorizontalAlignment="Stretch",VerticalAlignment="Stretch",Padding=0,BorderThickness=0,
            GridColumn=column,LayoutOrientation=mode==DesignerLayoutModes.Flex?"Horizontal":"Vertical",LayoutSpacing=0});
    }

    private static int RunLayoutDropBenchmark()
    {
        Directory.CreateDirectory(Layout72Folder);
        var scenario=Environment.GetEnvironmentVariable("DROP_BENCH_SCENARIO")??"ToolboxWrapPanel";
        var count=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_COUNT")??"100");
        var samples=int.Parse(Environment.GetEnvironmentVariable("DROP_BENCH_SAMPLES")??"30");
        var label=Environment.GetEnvironmentVariable("DROP_BENCH_LABEL")??"current";
        var vm=NestedModel();vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(count,scenario)));
        int? preparedHistory=Environment.GetEnvironmentVariable("DROP_BENCH_HISTORY_PAIR") is {Length:>0} historyText?int.Parse(historyText):null;
        if(preparedHistory is not null)
        {
            RequireCapability(preparedHistory is 0 or 10 or 30,"Matched history fixture accepts H0/H10/H30.");
            var text=vm.Controls[0].Text;
            // Identical 30 actual VM edits in both arms, before showing the Window.
            // Both end in exactly the original model state; H0 only drops fixture history.
            for(var i=0;i<30;i++)
            {
                vm.ActiveSession.LastHistoryMutationUtc=DateTime.MinValue;
                vm.BeginUndoBatch();vm.Controls[0].Text=i==29?text:"Prepared history "+i;vm.CommitUndoBatch();
            }
            RequireCapability(vm.ActiveSession.UndoSnapshots.Count==30,"History fixture did not create thirty independent steps.");
            if(preparedHistory<30)vm.ActiveSession.SetHistoryState(vm.ActiveSession.UndoSnapshots.Reverse().TakeLast(preparedHistory.Value).ToArray(),Array.Empty<string>(),vm.ActiveSession.CurrentSnapshot,vm.ActiveSession.SavedSnapshot);
            RefreshHistory(vm);
        }
        var selectedId=vm.Controls.Last().Id;var originalParent=vm.Controls.Last().ParentId;vm.SelectSingleControl(vm.GetControl(selectedId)!);
        var setup=Stopwatch.StartNew();var window=CreateSmokeWindow(vm);window.Show();NativeItemsUiWait(150);DropFrame(window);
        var showMs=setup.Elapsed.TotalMilliseconds;
        var actualZoom=(double)typeof(MainWindow).GetField("_surfaceZoom",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        RequireCapability(actualZoom==1&&vm.PreviewFormWidth==1200&&vm.PreviewFormHeight==800,"Benchmark viewport/zoom differs from the fixed fixture.");
        NativeCrashDiagnostics.CaptureModules("benchmark-window-shown");
        var ablation=Environment.GetEnvironmentVariable("DROP_BENCH_ABLATION")??"None";
        var inspector=window.GetVisualDescendants().OfType<DesignerPropertyInspector>().Single();
        if(ablation=="InspectorHidden"){inspector.IsVisible=false;DropFrame(window);}
        var surface=window.GetVisualDescendants().OfType<DesignerSurface>().Single();
        var observations=new List<DropObservation>();
        var layoutPasses=0;EventHandler onLayout=(_,_)=>layoutPasses++;
        window.LayoutUpdated+=onLayout;
        try
        {
            for(var iteration=-4;iteration<samples;iteration++)
            {
                var moving=vm.GetControl(selectedId)!;var toolbox=scenario.StartsWith("Toolbox",StringComparison.Ordinal);
                Point point;
                if(scenario=="ToolboxCanvas")point=new(150,650);
                else if(scenario=="ToolboxGrid")point=new(4,4);
                else if(scenario=="Reparent")point=NestedPoint(vm,"host-b",.5,.1);
                else {var host=vm.NativeContainerMeasurements["host-a"];point=host.Bounds.TopLeft+new Vector(4,4);}
                var data=new DataObject();data.Set("formdesigner-control-type","Button");data.Set("formdesigner-source-document",vm.ActiveDocumentId);
                data.Set("formdesigner-toolbox-gesture",Guid.NewGuid().ToString("N"));
                DragEventArgs Route(RoutedEvent<DragEventArgs> routed)
                {
                    var e=(DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                        new object[]{routed,data,surface.Canvas,point,KeyModifiers.None},null)!;surface.Canvas.RaiseEvent(e);return e;
                }
                var hover=Stopwatch.StartNew();GridDropTarget? target=null;
                if(toolbox){var over=Route(DragDrop.DragOverEvent);RequireCapability(over.DragEffects==DragDropEffects.Copy,"Benchmark DragOver rejected "+scenario+": "+vm.StatusText);}
                else {target=vm.ResolveLayoutDropTarget(point,moving);RequireCapability(target.IsValid,"Benchmark move target invalid: "+target.Reason);}
                DropFrame(window);var hoverMs=hover.Elapsed.TotalMilliseconds;
                var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var jit=JitInfo.GetCompiledMethodCount();
                var total=GC.GetTotalAllocatedBytes(false);layoutPasses=0;var undoBefore=vm.ActiveSession.UndoSnapshots.Count;
                var cacheHits=vm.ActiveSession.HistoryDescriptions.HitCount;var cacheMisses=vm.ActiveSession.HistoryDescriptions.MissCount;
                using var profile=LayoutDropProfile.Begin(vm.ActiveDocumentId,toolbox?"toolbox":selectedId);
                var cpuBefore=BenchmarkThreadCpuMs();
                var clock=Stopwatch.StartNew();
                using(LayoutDropProfile.Stage(toolbox?"DropEvent":"MoveEvent"))
                {
                    if(toolbox){var drop=Route(DragDrop.DropEvent);RequireCapability(drop.Handled&&vm.Controls.Count==count+1
                        &&(scenario=="ToolboxCanvas"||drop.DragEffects==DragDropEffects.Copy),"Benchmark Drop rejected: "+vm.StatusText);}
                    else RequireCapability(vm.CommitGridControlDrop(moving,target!,point),"Benchmark move failed: "+vm.StatusText);
                }
                var handlerMs=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("DispatcherToVisual"))DropPumpUntil(()=>DropSnapshotReady(window,vm),"visual snapshot");
                var visualMs=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("AttachedUILayout"))window.UpdateLayout();
                var layoutMs=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("FirstCompositorCommit"))DropFrame(window);
                var firstMs=clock.Elapsed.TotalMilliseconds;var input=false;
                using(LayoutDropProfile.Stage("NextInputReady")){Dispatcher.UIThread.Post(()=>input=true,DispatcherPriority.Input);DropPumpUntil(()=>input,"next input");}
                var readyMs=clock.Elapsed.TotalMilliseconds;
                profile.Dispose();
                var record=new DropObservation(iteration,iteration==-4?"FirstDrop":iteration<0?"Warmup":"Steady",hoverMs,handlerMs,visualMs,layoutMs,firstMs,readyMs,
                    profile.AllocatedBytes,GC.GetTotalAllocatedBytes(false)-total,GC.CollectionCount(0)-gc[0],GC.CollectionCount(1)-gc[1],GC.CollectionCount(2)-gc[2],
                    JitInfo.GetCompiledMethodCount()-jit,layoutPasses,vm.Controls.Count,DropWrappers(window).Count,profile.Stages.ToArray(),new(profile.Counters))
                    {SnapshotCharacters=vm.ActiveSession.CurrentSnapshot.Length,RetainedHistoryCharacters=RetainedHistoryCharacters(vm),UndoEntryDelta=vm.ActiveSession.UndoSnapshots.Count-undoBefore,
                        UiCpuMs=BenchmarkThreadCpuMs()-cpuBefore,HistoryCacheHits=vm.ActiveSession.HistoryDescriptions.HitCount-cacheHits,
                        HistoryCacheMisses=vm.ActiveSession.HistoryDescriptions.MissCount-cacheMisses,HistoryCacheCount=vm.ActiveSession.HistoryDescriptions.Count};
                observations.Add(record);
                RequireCapability(vm.SelectedControl is {Type:"Button"} selected&&vm.PropertyGridCategories.SelectMany(g=>g.Rows).Any(r=>r.Key=="Name"&&r.ContextControlId==selected.Id),"Benchmark selection/Inspector mismatch.");
                RequireCapability(vm.Controls.Count==count+(toolbox?1:0),"Benchmark duplicated/lost controls.");
                var undoClock=Stopwatch.StartNew();var undoBytes=GC.GetAllocatedBytesForCurrentThread();
                if(Environment.GetEnvironmentVariable("DROP_BENCH_DIRECT_UNDO")=="1")vm.UndoCommand.Execute(null);else GridEditorUndo(vm);
                vm.SelectSingleControl(vm.GetControl(selectedId)!);
                DropPumpUntil(()=>DropSnapshotReady(window,vm),"reset layout");DropFrame(window);
                record.UndoCommitMs=undoClock.Elapsed.TotalMilliseconds;record.UndoUiAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-undoBytes;
                RequireCapability(vm.Controls.Count==count&&vm.GetControl(selectedId)!.ParentId==originalParent,"Undo corrupted benchmark seed.");
            }
            var steady=observations.Where(s=>s.Phase=="Steady").ToArray();
            double Quantile(Func<DropObservation,double> select,double q){var values=steady.Select(select).Order().ToArray();return values[Math.Clamp((int)Math.Ceiling(q*values.Length)-1,0,values.Length-1)];}
            var result=new {label,scenario,count,samples,ablation,preparedHistory,InspectorVisuals=inspector.GetVisualDescendants().Count(),Build=typeof(Program).Assembly.GetCustomAttribute<System.Reflection.AssemblyConfigurationAttribute>()?.Configuration,
                actualZoom,ActualRootWidth=vm.PreviewFormWidth,ActualRootHeight=vm.PreviewFormHeight,window.ClientSize,Environment.ProcessorCount,GCSettings.IsServerGC,
                ProfileSources=System.Diagnostics.Tracing.EventSource.GetSources().Where(s=>s.Name.Contains("FormDesigner")).Select(s=>new{s.Name,Enabled=s.IsEnabled(),Error=s.ConstructionException?.Message}).ToArray(),
                Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,Debugger=Debugger.IsAttached,ProcessId=Environment.ProcessId,
                Viewport="1200x800 DIP",window.RenderScaling,showMs,Median=Quantile(s=>s.FirstCommitMs,.5),P95=Quantile(s=>s.FirstCommitMs,.95),Min=steady.Min(s=>s.FirstCommitMs),Max=steady.Max(s=>s.FirstCommitMs),
                MedianAllocated=Quantile(s=>s.UiAllocatedBytes,.5),MedianReady=Quantile(s=>s.ReadyMs,.5),Gc0=steady.Sum(s=>s.Gen0),Gc1=steady.Sum(s=>s.Gen1),Gc2=steady.Sum(s=>s.Gen2),Observations=observations};
            File.WriteAllText(Path.Combine(Layout72Folder,$"{label}-{scenario}-{count}.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"BENCHMARK {label} {scenario} N={count} samples={samples} median={result.Median:F2} p95={result.P95:F2} min={result.Min:F2} max={result.Max:F2} MB={result.MedianAllocated/1e6:F2}");
            return 0;
        }
        finally{window.LayoutUpdated-=onLayout;window.CloseForExternalHost();}
    }
}
