using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunLayout72Checks()
    {
        var passed=0;var failed=0;
        void Check(string name,Action run)
        {
            var filter=Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter))return;
            try{run();passed++;Console.WriteLine("PASS LAYOUT72_"+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL LAYOUT72_"+name+": "+ex);}
        }
        Check("OneActiveInspectorEditor",()=>
        {
            using var f=new PointerFixture();f.Vm.SelectSingleControl(f.First);f.Refresh();DropFrame(f.Window);
            var inspector=f.Window.GetVisualDescendants().OfType<DesignerPropertyInspector>().Single();
            foreach(var border in inspector.GetVisualDescendants().OfType<Border>().Where(b=>b.Classes.Contains("property-row")))
            {
                var row=(PropertyGridRowViewModel)border.DataContext!;
                RequireCapability(border.GetVisualDescendants().OfType<PropertyGridTextEditor>().Count()==(row.IsTextEditor||row.IsNumberEditor?1:0),"Hidden text editors exist for "+row.Key);
                RequireCapability(border.GetVisualDescendants().OfType<ComboBox>().Count()==(row.IsEnumEditor||row.IsBindingSourceEditor?1:0),"Hidden ComboBoxes exist for "+row.Key);
            }
            RequireCapability(inspector.GetVisualDescendants().OfType<PropertyGridTextEditor>().Any(),"Inspector has no active editor.");
        });
        Check("InspectorTextRetargetAndEnumBinding",()=>
        {
            using var f=new PointerFixture();var second=f.Vm.Controls.First(c=>c.Id!=f.First.Id);f.Vm.SelectSingleControl(f.First);f.Refresh();DropFrame(f.Window);
            PropertyGridTextEditor Editor(string key)=>f.Window.GetVisualDescendants().OfType<DesignerPropertyInspector>().Single().GetVisualDescendants().OfType<PropertyGridTextEditor>().Single(e=>e.Row?.Key==key);
            Editor("Name").Text="FirstRenamed";Editor("Name").RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});NativeItemsUiWait(30);
            RequireCapability(f.First.Name=="FirstRenamed","Lazy editor did not commit to the selected control.");
            f.Vm.SelectSingleControl(second);f.Refresh();DropFrame(f.Window);Editor("Name").Text="SecondRenamed";
            Editor("Name").RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});NativeItemsUiWait(30);
            RequireCapability(second.Name=="SecondRenamed"&&f.First.Name=="FirstRenamed","Inspector retained stale context.");
            var row=f.Vm.PropertyGridCategories.SelectMany(c=>c.Rows).First(r=>r.Key=="VerticalAlignment");
            var combo=f.Window.GetVisualDescendants().OfType<DesignerPropertyInspector>().Single().GetVisualDescendants().OfType<ComboBox>().Single(c=>ReferenceEquals(c.DataContext,row));
            combo.SelectedItem=row.Options.Single(o=>o.Value=="Center");NativeItemsUiWait(30);
            RequireCapability(second.VerticalAlignment=="Center","Enum binding was not preserved.");
        });
        Check("ReparentReuseInspectorAndUndo",()=>
        {
            using var f=NestedPointerFixture();var a=NestedHost(f.Vm,DesignerLayoutModes.Stack);var b=NestedHost(f.Vm,DesignerLayoutModes.Stack,1);var child=NestedLeaf(f.Vm,a,"Move me");f.Refresh();
            foreach(var host in new[]{a,b}){host.WidthMode=host.HeightMode="Fixed";host.Width=200;host.Height=400;}f.Refresh();
            var before=DropWrappers(f.Window).ToDictionary(p=>p.Key,p=>p.Value);using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,child.Id);
            var target=NestedPoint(f.Vm,b.Id);f.Down(f.ControlPoint(child));var end=f.CanvasPoint(target.X,target.Y);f.Move(end);f.Up(end);DropFrame(f.Window);f.AssertIdle();AssertLayout7Surface(f);
            Console.WriteLine("REPARENT_PROFILE "+JsonSerializer.Serialize(profile.Counters));
            File.WriteAllText(Path.Combine(Layout72Folder,"reparent-notes.json"),JsonSerializer.Serialize(profile.Notes,new JsonSerializerOptions{WriteIndented=true}));
            RequireCapability(child.ParentId==b.Id&&ReferenceEquals(DropWrappers(f.Window)[child.Id],before[child.Id]),"Reparent rebuilt the leaf wrapper.");
            RequireCapability(profile.Stages.Where(s=>s.Stage=="RefreshPropertyInspector").Sum(s=>s.Calls)==1,"Reparent rebuilt Inspector more than once.");
            RequireCapability(profile.Counters.GetValueOrDefault("DesignerWrappersCreated")==0,"Reparent rebuilt unrelated controls.");profile.Dispose();
            GridEditorUndo(f.Vm);RequireCapability(f.Vm.GetControl(child.Id)!.ParentId==a.Id,"Undo lost the original parent.");GridEditorUndo(f.Vm,true);RequireCapability(f.Vm.GetControl(child.Id)!.ParentId==b.Id,"Redo lost the target parent.");
        });
        Check("DockDropOnlyAffectedLeaves",()=>
        {
            using var f=new PointerFixture();f.Vm.LoadDocumentJson(JsonSerializer.Serialize(DropSeed(50,"ToolboxDockPanel")));f.Refresh();
            var host=DropWrappers(f.Window)["host-a"];using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,"toolbox");
            var data=GridToolboxData(f);var point=f.Vm.NativeContainerMeasurements["host-a"].Bounds.TopLeft+new Avalonia.Vector(4,4);
            RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);AssertLayout7Surface(f);
            RequireCapability(ReferenceEquals(host,DropWrappers(f.Window)["host-a"])&&profile.Counters.GetValueOrDefault("DesignerWrappersCreated")<5,"DockPanel rebuilt its entire visual tree.");
        });
        foreach(var type in new[]{"DataGrid","Eremex.DataGridControl","Eremex.TextEditor"})
            Check("HeavyReuse_"+type,()=>
            {
                using var f=NestedPointerFixture();f.Vm.DataGridExportMode=MainWindowViewModel.DataGridExportModeReal;
                if(type.StartsWith("Eremex."))LoadBuiltEremexPlugin(f.Vm);
                var source=ProductsSource();source.UseDemoData=true;f.Vm.BindingSources.Add(source);
                var heavy=f.Vm.CreateControl(type,0,0);heavy.WidthMode=heavy.HeightMode="Fixed";heavy.Width=300;heavy.Height=300;
                if(type!="Eremex.TextEditor")heavy.BindingSourceId=source.Id;
                var plain=f.Vm.CreateControl("Button",0,0);f.Refresh();DropFrame(f.Window);
                var wrapper=DropWrappers(f.Window)[heavy.Id];
                Console.WriteLine("HEAVY_TYPES "+type+" "+string.Join(",",wrapper.GetVisualDescendants().Select(c=>c.GetType().Name).Distinct()));
                var actual=((Canvas)((Canvas)wrapper.Child!).Children.OfType<Canvas>().First()).Children.OfType<Control>().First();
                var items=actual.GetType().GetProperty("ItemsSource")?.GetValue(actual);
                var data=GridToolboxData(f);using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,"toolbox");
                var point=new Avalonia.Point(620,370);RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);
                Console.WriteLine($"HEAVY_PROFILE {type} wrappersCreated={profile.Counters.GetValueOrDefault("DesignerWrappersCreated")}; controls={f.Vm.Controls.Count}");
                RequireCapability(ReferenceEquals(wrapper,DropWrappers(f.Window)[heavy.Id]),"Unrelated Button Drop rebuilt the heavy wrapper.");
                RequireCapability(DropWrappers(f.Window)[heavy.Id].GetVisualDescendants().Any(v=>ReferenceEquals(v,actual)),"Heavy runtime control was replaced.");
                if(type!="Eremex.TextEditor")
                {
                    RequireCapability(heavy.BindingSourceId==source.Id,"BindingSource was lost.");
                    if(actual.GetType().GetProperty("ItemsSource") is { } property)RequireCapability(items is not null&&ReferenceEquals(items,property.GetValue(actual)),"ItemsSource was replaced or lost.");
                    else
                    {
                        RequireCapability(source.Fields.All(field=>wrapper.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==field.Header)),"Designer table lost column metadata.");
                        // Standard Designer uses its existing table skeleton. Actual
                        // native DataGrid rows belong to the two runtime Previews.
                        foreach(var axaml in new[]{false,true})
                        {
                            using var preview=ShowDataGridRegressionPreview(f.Vm,axaml);
                            var runtime=preview.Window.GetVisualDescendants().OfType<DataGrid>().Single();
                            RequireCapability(runtime.ItemsSource is System.Collections.IEnumerable rows&&rows.Cast<object>().Any(),"Runtime Preview lost explicit manual rows.");
                        }
                    }
                }
            });
        Check("ProfileEventsAndAffectedControls",()=>
        {
            using var f=NestedPointerFixture();f.Refresh();var events=new List<string>();
            using(var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,"toolbox",(code,_)=>events.Add(code)))
            {var data=GridToolboxData(f);var point=new Avalonia.Point(50,50);RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);using(LayoutDropProfile.Stage("FirstCompositorCommit"))DropFrame(f.Window);}
            foreach(var code in new[]{"TOOLBOX_DROP_PROFILE_START","TOOLBOX_DROP_PROFILE_STAGE","TOOLBOX_DROP_PROFILE_END","PROPERTY_INSPECTOR_REFRESH_PROFILE","LAYOUT_FIRST_RENDER_COMPLETED","LAYOUT_VISUAL_WRAPPERS_CREATED"})RequireCapability(events.Contains(code),"Missing profile event "+code);
        });
        Check("HeavyPropertyChangeInvalidatesContent",()=>
        {
            using var f=NestedPointerFixture();LoadBuiltEremexPlugin(f.Vm);var editor=f.Vm.CreateControl("Eremex.TextEditor",0,0);editor.WidthMode=editor.HeightMode="Fixed";editor.Width=220;editor.Height=40;
            f.Vm.CreateControl("Button",0,0);f.Refresh();var old=DropWrappers(f.Window)[editor.Id];
            SetCustomProperty(editor,"EditorValue","Updated content");
            var data=GridToolboxData(f);var point=new Avalonia.Point(620,370);RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);
            var current=DropWrappers(f.Window)[editor.Id];var actual=current.GetVisualDescendants().OfType<Control>().Single(c=>c.GetType().Name=="TextEditor");
            RequireCapability(!ReferenceEquals(old,current)&&actual.GetType().GetProperty("EditorValue")!.GetValue(actual)?.ToString()=="Updated content","Reused a stale plugin value.");
        });
        Check("PreviewCacheInvalidatesOnlyDataWrapper",()=>
        {
            using var f=NestedPointerFixture();var source=ProductsSource();source.UseDemoData=true;f.Vm.BindingSources.Add(source);
            var grid=f.Vm.CreateControl("DataGrid",0,0);grid.BindingSourceId=source.Id;grid.WidthMode=grid.HeightMode="Fixed";grid.Width=300;grid.Height=300;
            var button=f.Vm.CreateControl("Button",0,0);f.Refresh();var before=DropWrappers(f.Window).ToDictionary(p=>p.Key,p=>p.Value);
            typeof(MainWindow).GetMethod("ResetInteractiveRuntimePreviewState",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(f.Window,null);
            var data=GridToolboxData(f);var point=new Avalonia.Point(620,370);RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);
            RequireCapability(!ReferenceEquals(before[grid.Id],DropWrappers(f.Window)[grid.Id])&&ReferenceEquals(before[button.Id],DropWrappers(f.Window)[button.Id]),"Data invalidation rebuilt unrelated controls or retained stale data.");
        });
        Check("RepeatedDropUndoRedoNoAccumulation",()=>
        {
            using var f=NestedPointerFixture();var host=NestedHost(f.Vm,DesignerLayoutModes.Stack);f.Refresh();var count=f.Vm.Controls.Count;
            for(var i=0;i<10;i++)
            {
                var data=GridToolboxData(f);var point=NestedPoint(f.Vm,host.Id);RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);f.AssertIdle();
                var id=f.Vm.SelectedControl!.Id;RequireCapability(f.Vm.Controls.Count==count+1&&DropWrappers(f.Window).Count==count+1&&f.Surface.GuideOverlay.Children.Count==0,"Drop duplicated wrappers/overlays.");
                GridEditorUndo(f.Vm);DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"undo visual");RequireCapability(f.Vm.GetControl(id) is null,"Undo retained the added control.");
                GridEditorUndo(f.Vm,true);DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"redo visual");RequireCapability(f.Vm.GetControl(id)?.ParentId==host.Id,"Redo changed identity/parent.");
                GridEditorUndo(f.Vm);DropPumpUntil(()=>DropSnapshotReady(f.Window,f.Vm),"final undo visual");
            }
        });
        Check("CanvasAppendReusesOldWrappers",()=>
        {
            using var f=new PointerFixture();f.Vm.SelectSingleControl(f.First);f.Refresh();var before=DropWrappers(f.Window).ToDictionary(p=>p.Key,p=>p.Value);
            using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,"toolbox");var data=GridToolboxData(f);var point=new Avalonia.Point(200,300);
            RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);f.AssertIdle();
            RequireCapability(before.All(p=>ReferenceEquals(p.Value,DropWrappers(f.Window)[p.Key]))&&profile.Counters.GetValueOrDefault("DesignerWrappersCreated")==1,"Canvas recreated existing wrappers.");
            var added=f.Vm.SelectedControl!;RequireCapability(added.ParentId.Length==0&&added.X==200&&added.Y==300,"Canvas coordinates changed.");
            RequireCapability(before[f.First.Id].BorderThickness.Left==1,"Old Canvas selection frame remained active.");
        });
        Check("PendingAuthoredChangeBeforeDropIsRendered",()=>
        {
            using var f=NestedPointerFixture();var button=f.Vm.CreateControl("Button",0,0);button.Text="Old text";f.Refresh();button.Text="Authored before Drop";
            var data=GridToolboxData(f);var point=new Avalonia.Point(620,370);RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);DropFrame(f.Window);
            RequireCapability(DropWrappers(f.Window)[button.Id].GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()==button.Text),"Pending authored text was hidden by Drop reuse.");
        });
        Console.WriteLine($"Layout Phase 7A.2 checks: {passed}/{passed+failed}");return failed==0&&passed>0?0:1;
    }
}
