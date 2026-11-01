using FormDesigner.DesignerSystem;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using System.Diagnostics;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void RefreshHistory(MainWindowViewModel vm) => typeof(MainWindowViewModel)
        .GetMethod("RebuildUndoRedoHistoryItems", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
    private static (string Title, string Description) BaselineCaption(string a, string b) =>
        ((string, string))typeof(MainWindowViewModel).GetMethod("DescribeHistoryTransition", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[]{a,b})!;
    private static void AssertHistoryCaptions(MainWindowViewModel vm)
    {
        RefreshHistory(vm);
        var items = vm.UndoRedoHistoryItems.ToArray();
        for (var i=1;i<items.Length;i++)
            RequireCapability((items[i].Title,items[i].Description)==BaselineCaption(items[i-1].Snapshot,items[i].Snapshot),"Cached caption differs from the original formatter.");
        RequireCapability(items.Count(i=>i.IsCurrent)==1 && items[vm.ActiveSession.UndoSnapshots.Count].IsCurrent,"Current row moved incorrectly.");
        RequireCapability(vm.ActiveSession.HistoryDescriptions.Count<=Math.Max(0,items.Length-1),"Cache exceeds reachable history pairs.");
    }
    private static void HistoryEdit(MainWindowViewModel vm, Action change)
    {
        vm.ActiveSession.LastHistoryMutationUtc=DateTime.MinValue;
        vm.BeginUndoBatch(); change(); vm.CommitUndoBatch();
    }
    private static void CanonicalHistoryBaseline(MainWindowViewModel vm)
    {
        var options=(JsonSerializerOptions)typeof(MainWindowViewModel).GetField("JsonOptions",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        vm.ActiveSession.ResetHistory(JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot(),options),true);
    }
    private static int RunUndoHistoryCacheChecks()
    {
        var passed=0;var failed=0;
        void Check(string name,Action test)
        {
            try { test();passed++;Console.WriteLine("PASS HISTORY_CACHE_"+name); }
            catch(Exception e){failed++;Console.WriteLine("FAIL HISTORY_CACHE_"+name+": "+e);}
        }
        Check("UnchangedPairHitAndNewPairMiss",()=>
        {
            using var session=new DesignerDocumentSession(null,new());var c=session.HistoryDescriptions;
            var a=new string('a',80);var b=new string('b',80);c.Prepare(new[]{a,b},"v1");var calls=0;
            (string,string) Format(string x,string y){calls++;return(x[..1],y[..1]);}
            c.GetOrCreate(a,b,Format);c.GetOrCreate(a,b,Format);
            RequireCapability(c.HitCount==1&&c.MissCount==1&&calls==1,"Unchanged pair was recomputed.");
            var copy=new string(b.ToCharArray());c.Prepare(new[]{a,copy},"v1");c.GetOrCreate(a,copy,Format);
            RequireCapability(c.Count==1&&calls==2,"Reference identity was replaced by value/index identity.");
        });
        Check("PreviousSnapshotIsPartOfKey",()=>
        {
            using var s=new DesignerDocumentSession(null,new());var c=s.HistoryDescriptions;
            var a=new string('a',70);var b=new string('b',70);var d=new string('d',70);
            c.Prepare(new[]{a,b,d},"v1");c.GetOrCreate(a,b,static(x,y)=>(x,y));
            c.Prepare(new[]{d,b},"v1");var answer=c.GetOrCreate(d,b,static(x,y)=>(x,y));
            RequireCapability(answer.Title==d&&c.MissCount==2&&c.Count==1,"A changed predecessor reused a stale caption.");
        });
        Check("CaptionContextInvalidation",()=>
        {
            using var s=new DesignerDocumentSession(null,new());var c=s.HistoryDescriptions;var pair=new[]{"a","b"};
            c.Prepare(pair,"ru");c.GetOrCreate(pair[0],pair[1],static(_,_) =>("ru","ru"));
            c.Prepare(pair,"en");var result=c.GetOrCreate(pair[0],pair[1],static(_,_) =>("en","en"));
            RequireCapability(result.Title=="en"&&c.MissCount==2&&c.Count==1,"Caption context did not invalidate the cache.");
        });
        Check("FormatterFailureIsNotCached",()=>
        {
            using var s=new DesignerDocumentSession(null,new());var c=s.HistoryDescriptions;c.Prepare(new[]{"a","b"},"v1");
            try{c.GetOrCreate("a","b",static(_,_)=>throw new InvalidOperationException());}catch(InvalidOperationException){}
            RequireCapability(c.Count==0,"Failed caption was cached.");
        });
        foreach(var operation in new[]{"Add","Move","Reparent","Resize","Property","DataGrid","Logic"})
            Check(operation+"UndoRedo",()=>
            {
                var vm=NestedModel();vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(20,"Reparent")));
                var control=vm.Controls.Last();var id=control.Id;vm.SelectSingleControl(control);
                CanonicalHistoryBaseline(vm);
                HistoryEdit(vm,()=>
                {
                    switch(operation)
                    {
                        case "Add":vm.CreateControl("Button",0,0);break;
                        case "Move":control.StackOrder+=2;break;
                        case "Reparent":control.ParentId="host-b";control.GridRow=1;break;
                        case "Resize":control.Width+=20;break;
                        case "Property":control.Text="Cached text";break;
                        case "DataGrid":control.FilterMode=DesignControlModel.DataGridFilterModeEquals;break;
                        case "Logic":vm.Interactions.Add(new InteractionModel{SourceControlName=control.Name,TargetControlName=control.Name,ActionType=InteractionModel.ActionSetProperty});break;
                    }
                });
                var changed=vm.ActiveSession.CurrentSnapshot;var previous=vm.ActiveSession.UndoSnapshots.Peek();
                AssertHistoryCaptions(vm);var misses=vm.ActiveSession.HistoryDescriptions.MissCount;
                RefreshHistory(vm);RequireCapability(vm.ActiveSession.HistoryDescriptions.MissCount==misses,"Refresh recomputed unchanged captions.");
                vm.UndoCommand.Execute(null);AssertHistoryCaptions(vm);
                RequireCapability(vm.ActiveSession.CurrentSnapshot==previous&&vm.GetControl(id)!=null,$"Undo changed snapshot semantics or ControlId: equal={vm.ActiveSession.CurrentSnapshot==previous}, found={vm.GetControl(id)!=null}, undo={vm.ActiveSession.UndoSnapshots.Count}, redo={vm.ActiveSession.RedoSnapshots.Count}.");
                vm.RedoCommand.Execute(null);AssertHistoryCaptions(vm);
                RequireCapability(vm.ActiveSession.CurrentSnapshot==changed,"Redo did not restore the exact original snapshot.");
            });
        Check("RedoBranchPrunedAndCoalescing",()=>
        {
            var vm=NestedModel();var c=vm.CreateControl("Button",0,0);
            HistoryEdit(vm,()=>c.Text="Branch A");HistoryEdit(vm,()=>c.Text="Branch B");AssertHistoryCaptions(vm);
            vm.UndoCommand.Execute(null);HistoryEdit(vm,()=>vm.Controls.Last().Text="Replacement branch");AssertHistoryCaptions(vm);
            RequireCapability(vm.ActiveSession.RedoSnapshots.Count==0&&!vm.UndoRedoHistoryItems.Any(i=>i.Description=="Текст: Branch B."),"Redo branch caption leaked into new history.");
            vm.ActiveSession.LastHistoryMutationUtc=DateTime.UtcNow;vm.BeginUndoBatch();vm.Controls.Last().Text="Coalesced replacement";vm.CommitUndoBatch();AssertHistoryCaptions(vm);
            RequireCapability(vm.UndoRedoHistoryItems.Last().Description.Contains("Coalesced replacement"),"Coalesced snapshot reused an index-based caption.");
        });
        Check("ResetPrunesImmediately",()=>
        {
            var vm=NestedModel();vm.CreateControl("Button",0,0);AssertHistoryCaptions(vm);
            var s=vm.ActiveSession;s.ResetHistory(s.CurrentSnapshot,true);
            RequireCapability(s.HistoryDescriptions.Count==0&&s.UndoSnapshots.Count==0,"Reset retained captions.");
        });
        foreach(var action in new[]{"New","Open","Close"})Check(action+"ProjectSessionLifecycle",()=>
        {
            var vm=NestedModel();vm.CreateControl("Button",0,0);AssertHistoryCaptions(vm);var old=vm.ActiveSession;
            if(action=="New")vm.NewProjectEditorCommand!.Execute(null);
            else if(action=="Open")vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(3,"ToolboxCanvas")));
            else typeof(MainWindowViewModel).GetMethod("DisposeAllDocumentSessions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(vm,new object[]{"cache test close"});
            RequireCapability(old.IsDisposed&&old.HistoryDescriptions.Count==0,"Old session was not cleared/disposed.");
            if(action!="Close")RequireCapability(vm.ActiveSession.HistoryDescriptions.Count==0&&vm.ActiveSession.UndoSnapshots.Count==0,"Replacement project inherited captions/history.");
        });
        Check("MultiFormSwitchAndDelete",()=>
        {
            var vm=NestedModel();var aControl=vm.CreateControl("Button",0,0);HistoryEdit(vm,()=>aControl.Text="First form");
            var a=vm.ActiveDocumentId;AssertHistoryCaptions(vm);var first=vm.ActiveSession;
            vm.CreateNewForm("Grid");var bControl=vm.CreateControl("TextBox",0,0);HistoryEdit(vm,()=>bControl.Text="Second form");
            var b=vm.ActiveDocumentId;AssertHistoryCaptions(vm);var second=vm.ActiveSession;
            vm.SetActiveForm(a,"history cache test");AssertHistoryCaptions(vm);
            RequireCapability(ReferenceEquals(vm.ActiveSession,first)&&!vm.UndoRedoHistoryItems.Any(i=>i.Description.Contains("Second form")),"Form histories mixed.");
            vm.SetActiveForm(b,"history cache test");AssertHistoryCaptions(vm);
            RequireCapability(ReferenceEquals(vm.ActiveSession,second)&&!ReferenceEquals(first.HistoryDescriptions,second.HistoryDescriptions),"Cache is shared across forms.");
            vm.DeleteProjectExplorerItemCommand.Execute(new ProjectExplorerItemModel{Source=vm.ActiveFormDocument});
            RequireCapability(second.IsDisposed&&second.HistoryDescriptions.Count==0&&!vm.DocumentSessions.ContainsKey(b),"Deleted form retained its cache/session.");
        });
        Check("JsonRoundTripAndSelectionGridPlacement",()=>
        {
            using var f=new PointerFixture();var id=f.First.Id;f.Vm.SelectSingleControl(f.First);
            HistoryEdit(f.Vm,()=>{f.First.GridRow=1;f.First.GridColumn=1;});AssertHistoryCaptions(f.Vm);
            f.Vm.UndoCommand.Execute(null);f.Vm.RedoCommand.Execute(null);DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"cache redo visual");DropFrame(f.Window);
            RequireCapability(f.Vm.SelectedControl?.Id==id&&f.First.GridRow==1&&f.First.GridColumn==1,"Selection/Grid placement restoration changed.");
            var json=f.Vm.ExportWorkspaceJson();var opened=NestedModel();opened.LoadDocumentJson(json);AssertHistoryCaptions(opened);
            RequireCapability(opened.GetControl(id)?.GridRow==1&&!json.Contains("HistoryDescriptions"),"Runtime cache changed JSON serialization.");
        });
        Check("RepeatedUndoRedo",()=>
        {
            var vm=NestedModel();var c=vm.CreateControl("Button",0,0);
            for(var i=0;i<10;i++)HistoryEdit(vm,()=>c.Text="Step "+i);
            var expected=vm.ActiveSession.CurrentSnapshot;
            for(var i=0;i<20;i++){vm.UndoCommand.Execute(null);AssertHistoryCaptions(vm);vm.RedoCommand.Execute(null);AssertHistoryCaptions(vm);}
            RequireCapability(vm.ActiveSession.CurrentSnapshot==expected,"Repeated restore changed snapshot state.");
        });
        Check("RichDataGridBindingsLogicAndCustomProperties",()=>
        {
            var vm=NestedModel();var grid=vm.CreateControl("DataGrid",0,0);var source=ProductsSource();source.UseDemoData=true;vm.BindingSources.Add(source);grid.BindingSourceId=source.Id;
            grid.FilterMode=DesignControlModel.DataGridFilterModeEquals;grid.CustomProperties.Add(new DesignPropertyValueModel{Key="retained",ValueJson="\"value\""});
            vm.Interactions.Add(new InteractionModel{SourceControlName=grid.Name,TargetControlName=grid.Name,ActionType=InteractionModel.ActionSetProperty});
            CanonicalHistoryBaseline(vm);var id=grid.Id;var before=vm.ActiveSession.CurrentSnapshot;
            HistoryEdit(vm,()=>{grid.GridRow=1;grid.Width+=15;});var after=vm.ActiveSession.CurrentSnapshot;AssertHistoryCaptions(vm);
            vm.UndoCommand.Execute(null);RequireCapability(vm.ActiveSession.CurrentSnapshot==before,"Undo lost rich DataGrid document state.");
            vm.RedoCommand.Execute(null);AssertHistoryCaptions(vm);
            RequireCapability(vm.ActiveSession.CurrentSnapshot==after&&vm.GetControl(id)!.BindingSourceId==source.Id&&vm.Interactions.Count==1
                &&vm.BindingSources.Single().Fields.Count==source.Fields.Count&&vm.GetControl(id)!.CustomProperties.Single(p=>p.Key=="retained").ValueJson=="\"value\"","DataGrid/Logic/binding payload changed.");
        });
        Check("OrphanStringsCollectible",()=>
        {
            var (session,weak)=MakeOrphanCacheStrings();
            for(var i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
            RequireCapability(!weak.IsAlive&&session.HistoryDescriptions.Count==0,"Cache retained an unreachable snapshot.");session.Dispose();
        });
        Check("ClosedSessionCollectible",()=>
        {
            var weak=MakeClosedCacheSession();for(var i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
            RequireCapability(!weak.IsAlive,"Static references retained a closed session.");
        });
        Check("FormatterClosureNotRetained",()=>
        {
            var (session,weak)=MakeCaptionClosure();for(var i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
            RequireCapability(!weak.IsAlive&&session.HistoryDescriptions.Count==1,"Cache retained the formatter closure/owner.");session.Dispose();
        });
        Check("DiagnosticEventsDoNotContainSnapshots",()=>
        {
            var original=Environment.GetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS");var writer=new StringWriter();var listener=new TextWriterTraceListener(writer);
            try
            {
                Environment.SetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS","1");Trace.Listeners.Add(listener);
                using var s=new DesignerDocumentSession(null,new());var c=s.HistoryDescriptions;var pair=new[]{"secret-snapshot-one","secret-snapshot-two"};
                c.Prepare(pair,"v1");c.GetOrCreate(pair[0],pair[1],static(_,_) =>("private caption","private description"));c.GetOrCreate(pair[0],pair[1],static(_,_) =>("unused","unused"));
                c.ReportStatistics();c.Prepare(new[]{"replacement"},"v1");s.ResetHistory("new",true);s.Dispose();Trace.Flush();var log=writer.ToString();
                foreach(var code in new[]{"HIT","MISS","INVALIDATED","CLEARED","STATS"})RequireCapability(log.Contains("UNDO_HISTORY_CACHE_"+code),"Missing cache diagnostic: "+code);
                RequireCapability(!log.Contains("secret-snapshot")&&!log.Contains("private caption")&&log.Contains("SessionId")&&log.Contains("DocumentId"),"Diagnostics exposed snapshot/caption data.");
            }
            finally{Trace.Listeners.Remove(listener);listener.Dispose();Environment.SetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE_DIAGNOSTICS",original);}
        });
        Check("DisabledMatchesBaseline",()=>
        {
            var original=Environment.GetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE");
            try{Environment.SetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE","0");var vm=NestedModel();vm.CreateControl("Button",0,0);AssertHistoryCaptions(vm);RefreshHistory(vm);
                RequireCapability(!vm.ActiveSession.HistoryDescriptions.Enabled&&vm.ActiveSession.HistoryDescriptions.Count==0,"Diagnostic switch failed.");}
            finally{Environment.SetEnvironmentVariable("FORMDESIGNER_UNDO_HISTORY_CACHE",original);}
        });
        Console.WriteLine($"Undo history cache checks: {passed}/{passed+failed}");return failed==0?0:1;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DesignerDocumentSession,WeakReference) MakeOrphanCacheStrings()
    {
        var s=new DesignerDocumentSession(null,new());var a=new string('a',10000);var b=new string('b',10000);
        s.SetHistoryState(new[]{a},Array.Empty<string>(),b,"");s.HistoryDescriptions.Prepare(new[]{a,b},"v1");
        s.HistoryDescriptions.GetOrCreate(a,b,static(_,_) =>("title","description"));var weak=new WeakReference(a);
        s.ResetHistory("replacement",true);return(s,weak);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeClosedCacheSession(){var (s,_)=MakeOrphanCacheStrings();var weak=new WeakReference(s);s.Dispose();return weak;}
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DesignerDocumentSession,WeakReference) MakeCaptionClosure()
    {
        var s=new DesignerDocumentSession(null,new());var owner=new object();var weak=new WeakReference(owner);s.HistoryDescriptions.Prepare(new[]{"a","b"},"v1");
        s.HistoryDescriptions.GetOrCreate("a","b",(_,_)=>{GC.KeepAlive(owner);return("title","description");});return(s,weak);
    }
}
