using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static Dictionary<string,Border> Layout7Wrappers(MainWindow window) => (Dictionary<string,Border>)typeof(MainWindow).GetField("_wrapperByControlId",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
    private static void AssertLayout7Surface(PointerFixture fixture)
    {
        foreach(var pair in Layout7Wrappers(fixture.Window))
        {
            var model=fixture.Vm.Controls.Single(c=>c.Id==pair.Key);var expected=fixture.Vm.GetAbsolutePosition(model);
            var actual=pair.Value.TranslatePoint(default,fixture.Surface.Canvas)!.Value;var size=fixture.Vm.NativeLayoutBounds[pair.Key].Size;
            RequireCapability(Math.Abs(actual.X-expected.X)<=1.1&&Math.Abs(actual.Y-expected.Y)<=1.1
                &&Math.Abs(pair.Value.Bounds.Width-size.Width)<=1.1&&Math.Abs(pair.Value.Bounds.Height-size.Height)<=1.1,"Reused wrapper geometry differs from native layout: "+model.Name);
        }
    }
    private static void WaitLayout7Frame(MainWindow window)
    {
        window.UpdateLayout();
        var commit=Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
        var limit=DateTime.UtcNow.AddSeconds(5);
        while(!commit.IsCompleted && DateTime.UtcNow<limit) NativeItemsUiWait(1);
        RequireCapability(commit.IsCompletedSuccessfully,"Native frame did not commit.");
    }
    private static int RunLayout7Checks()
    {
        Directory.CreateDirectory(Layout7Folder); var passed=0; var failed=0;
        void Check(string name,Action run)
        {
            var filter=Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER"); if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase)) return;
            try{run();passed++;Console.WriteLine("PASS LAYOUT7_"+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL LAYOUT7_"+name+": "+ex);}
        }
        Check("ProfilerFailedStartReleasesAmbientScope",()=>
        {
            try{using var bad=LayoutDropProfile.Begin("test","control",(_,_)=>throw new IOException("Test writer"));throw new Exception("Expected writer failure.");}
            catch(IOException){}
            using var next=LayoutDropProfile.Begin("test","next");
        });
        Check("RoutedDropReuseAndAtomicUndo",()=>
        {
            using var f=NestedPointerFixture(); var host=NestedHost(f.Vm,DesignerLayoutModes.Flex); host.LayoutOrientation="Horizontal";
            var a=NestedLeaf(f.Vm,host,"First"); var b=NestedLeaf(f.Vm,host,"Second",1); var c=NestedLeaf(f.Vm,host,"Third",2); f.Refresh();
            var before=Layout7Wrappers(f.Window).ToDictionary(p=>p.Key,p=>p.Value);
            f.Down(f.ControlPoint(c)); var rect=f.Vm.NativeContainerMeasurements[host.Id].Children.First().Bounds; var end=f.CanvasPoint(rect.X+1,rect.Y+1); f.Move(end);
            using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,c.Id);
            f.Up(end); WaitLayout7Frame(f.Window); f.AssertIdle();
            AssertLayout7Surface(f);
            RequireCapability(c.StackOrder==0&&f.Vm.SelectedControl?.Id==c.Id,"Reorder/selection failed.");
            RequireCapability(Layout7Wrappers(f.Window).All(p=>ReferenceEquals(p.Value,before[p.Key])),"A placement-only Drop recreated stable wrappers.");
            RequireCapability(profile.Counters.GetValueOrDefault("DesignerWrappersCreated")==0,"Unexpected wrapper creation.");
            RequireCapability(profile.Stages.Where(s=>s.Stage=="RebuildVisualTree").Sum(s=>s.Calls)==1,"Drop triggered repeated render.");
            RequireCapability(f.Vm.PropertyGridCategories.SelectMany(g=>g.Rows).Single(r=>r.Key=="StackOrder").Value=="0","Inspector placement row is stale.");
            profile.Dispose(); File.WriteAllText(Path.Combine(Layout7Folder,"routed-drop-profile.json"),JsonSerializer.Serialize(new{profile.Stages,profile.Counters,profile.ElapsedMs,profile.AllocatedBytes}));
            GridEditorUndo(f.Vm); RequireCapability(NestedOrder(f.Vm,host.Id).SequenceEqual(new[]{a.Id,b.Id,c.Id}),"Undo did not restore original ordering atomically.");
            GridEditorUndo(f.Vm,true); RequireCapability(NestedOrder(f.Vm,host.Id).SequenceEqual(new[]{c.Id,a.Id,b.Id}),"Redo lost identity/order.");
        });
        Check("ReuseCancelledByAuthoredEdit",()=>
        {
            using var f=NestedPointerFixture(); var host=NestedHost(f.Vm,DesignerLayoutModes.Stack); var a=NestedLeaf(f.Vm,host,"A"); var b=NestedLeaf(f.Vm,host,"B",1); f.Refresh();
            var old=Layout7Wrappers(f.Window)[a.Id]; var bounds=f.Vm.NativeContainerMeasurements[host.Id].Children.First().Bounds;
            NestedMove(f.Vm,b,bounds.TopLeft+new Vector(1,1)); a.Text="Changed before pending render";
            NativeItemsUiWait(100); WaitLayout7Frame(f.Window);
            RequireCapability(!ReferenceEquals(Layout7Wrappers(f.Window)[a.Id],old),"A later authored edit reused stale content.");
            RequireCapability(Layout7Wrappers(f.Window)[a.Id].GetVisualDescendants().OfType<Button>().Any(v=>v.Content?.ToString()==a.Text),"New text was not painted.");
        });
        Check("ToolboxInspectorSingleRefresh",()=>
        {
            using var f=NestedPointerFixture(); var host=NestedHost(f.Vm,DesignerLayoutModes.Stack); f.Refresh();
            var before=Layout7Wrappers(f.Window)[host.Id];
            using var profile=LayoutDropProfile.Begin(f.Vm.ActiveDocumentId,"toolbox");
            var point=NestedPoint(f.Vm,host.Id); var data=GridToolboxData(f);
            RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point); WaitLayout7Frame(f.Window);f.AssertIdle();
            var child=f.Vm.SelectedControl!;
            AssertLayout7Surface(f);
            RequireCapability(profile.Stages.Where(s=>s.Stage=="RefreshPropertyInspector").Sum(s=>s.Calls)==1,"Toolbox Drop rebuilt an intermediate empty or duplicate Inspector.");
            RequireCapability(f.Vm.SelectedControl?.Id==child.Id&&f.Vm.PropertyGridCategories.SelectMany(g=>g.Rows).Any(r=>r.ContextControlId==child.Id),"Toolbox selection/Inspector was lost.");
            RequireCapability(ReferenceEquals(before,Layout7Wrappers(f.Window)[host.Id])&&profile.Counters.GetValueOrDefault("DesignerWrappersCreated")==1,"Toolbox Drop rebuilt stable sibling/container wrappers.");
        });
        Check("DeferredContextMenu",()=>
        {
            using var f=new PointerFixture(); var wrapper=Layout7Wrappers(f.Window)[f.First.Id];
            RequireCapability(wrapper.ContextMenu is null,"Context menu was eagerly allocated.");
            var point=f.ControlPoint(f.First);
            f.Send(RawPointerEventType.RightButtonDown,point,RawInputModifiers.RightMouseButton);
            f.Send(RawPointerEventType.RightButtonUp,point); NativeItemsUiWait(80);
            var menu=wrapper.ContextMenu ?? Layout7Wrappers(f.Window)[f.First.Id].ContextMenu ?? throw new Exception("Right click did not create a context menu.");
            RequireCapability(menu.IsOpen,"Right click created an unopened menu.");
            RequireCapability(menu.Items.OfType<MenuItem>().Any(i=>i.Header?.ToString()?.Contains("Удалить")==true),"Deferred menu lost commands.");
            var current=Layout7Wrappers(f.Window)[f.First.Id]; if(ReferenceEquals(current.ContextMenu,menu))wrapper=current;
            var items=menu.Items.ToArray(); menu.Close(); menu.Open(wrapper); NativeItemsUiWait(30);
            RequireCapability(menu.Items.SequenceEqual(items),"Menu items/subscriptions accumulated on reopen."); menu.Close();
        });
        foreach(var cancel in new[]{"CaptureLost","Escape","FormSwitch","TargetRemoved"})
            Check("PendingMenuCancelled_"+cancel,()=>
            {
                using var f=new PointerFixture(); var selected=f.First; var point=f.ControlPoint(selected);
                f.Send(RawPointerEventType.RightButtonDown,point,RawInputModifiers.RightMouseButton);
                if(cancel=="CaptureLost")f.Pointer.Capture(null);
                if(cancel=="Escape")f.Window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});
                if(cancel=="FormSwitch")f.Vm.CreateNewForm();
                if(cancel=="TargetRemoved")f.Vm.Controls.Remove(selected);
                f.Send(RawPointerEventType.RightButtonUp,point);NativeItemsUiWait(50);
                RequireCapability(f.Pointer.Captured is null&&((string)typeof(MainWindow).GetField("_pendingContextMenuControlId",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Window)!).Length==0,"Cancelled context menu retained capture/state.");
            });
        foreach(var zoom in new[]{.5,.75,1,1.25,1.5,2})
            Check("BadgesAndPointerZoom_"+zoom,()=>
            {
                using var f=NestedPointerFixture(); f.Vm.SurfaceGridColumnDefinitions="*,*,*";
                var a=NestedHost(f.Vm,DesignerLayoutModes.Stack); var b=NestedHost(f.Vm,DesignerLayoutModes.Stack,1); var child=NestedLeaf(f.Vm,a,"Tiny"); child.Width=24; child.Height=18;
                typeof(MainWindow).GetMethod("SetSurfaceZoom",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(f.Window,new object?[]{zoom,null}); f.Refresh();
                var scroll=f.Surface.GetVisualDescendants().OfType<ScrollViewer>().Single(s=>s.Name=="DesignerViewportScrollViewer"); scroll.Offset=default; WaitLayout7Frame(f.Window);
                RequireCapability(Math.Abs((double)typeof(MainWindow).GetField("_surfaceZoom",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Window)!-zoom)<.001,"Zoom was rounded.");
                var badges=f.Surface.SelectionOverlay.GetVisualDescendants().OfType<Border>().Where(c=>c.Name=="DesignerControlNameBadge").ToArray();
                RequireCapability(badges.Length==1&&!badges[0].IsHitTestVisible,"Primary badge policy failed.");
                var wrapper=Layout7Wrappers(f.Window)[child.Id]; var origin=wrapper.TranslatePoint(default,f.Surface.Canvas)!.Value;
                var badgeRect=new Rect(badges[0].TranslatePoint(default,f.Surface.Canvas)!.Value,badges[0].Bounds.Size);
                RequireCapability(Math.Abs(badgeRect.X-Math.Clamp(origin.X,0,Math.Max(0,f.Vm.PreviewFormWidth-badgeRect.Width)))<1.1,"Badge is not anchored to the selected component.");
                var grip=new Rect(origin.X+Math.Max(0,wrapper.Width-9),origin.Y+Math.Max(0,wrapper.Height-9),18,18);
                RequireCapability(!badgeRect.Intersects(grip),"Badge overlaps the resize grip.");
                RequireCapability(ToolTip.GetTip(wrapper)?.ToString()?.Contains(child.Name)==true,"Hover identity is unavailable.");
                // The tiny control has no safe 35 DIP click point; restore its authored size for input.
                child.Width=80; child.Height=32; f.Refresh(); scroll.Offset=default; WaitLayout7Frame(f.Window);
                NestedPointerMove(f,child,NestedPoint(f.Vm,b.Id)); RequireCapability(child.ParentId==b.Id,"Zoom changed nested target.");
                if(zoom==1.25||zoom==2)
                {
                    var content=(Control)f.Window.Content!; using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width,(int)content.Bounds.Height)); image.Render(content); image.Save(Path.Combine(Layout7Folder,"badges-"+(int)(zoom*100)+".png"));
                }
            });
        Console.WriteLine($"Layout Phase 7A checks: {passed}/{passed+failed}"); return failed==0&&passed>0?0:1;
    }

    private static void ConfigureLayout7MultiForm(MainWindowViewModel vm)
    {
        vm.IncludeSampleData=true; vm.DataGridExportMode=MainWindowViewModel.DataGridExportModeReal;
        var home=vm.ActiveFormDocument!.Id; vm.FormTitle="7A Canvas";
        var navigation=new List<DesignControlModel>();
        foreach(var name in new[]{"OpenGrid","OpenNested","OpenData"})
        {var button=vm.CreateControl("Button",30,30+navigation.Count*60);button.Name=name;button.Text=name;navigation.Add(button);}
        AddRows(vm,"HomeRows",null);
        var targets=new List<string>();
        vm.CreateNewForm("Grid"); targets.Add(vm.ActiveFormDocument!.Id); vm.FormTitle="7A Grid";
        var input=vm.CreateControl("TextBox",0,0);input.Name="GridInput";
        var apply=vm.CreateControl("Button",0,0);apply.Name="GridApply";apply.Text="Apply";apply.Margin="0,60,0,0";
        vm.Interactions.Add(new(){SourceControlName=apply.Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionSetProperty,TargetControlName=input.Name,TargetProperty="Text",TextTemplate="Grid accepted"});
        AddRows(vm,"GridRows",null); Back(vm,home);
        vm.CreateNewForm("Grid");targets.Add(vm.ActiveFormDocument!.Id); vm.FormTitle="7A Nested";
        var stack=NestedHost(vm,DesignerLayoutModes.Stack);stack.Name="NestedStack";
        var wrap=NestedHost(vm,DesignerLayoutModes.Flex,parent:stack.Id);wrap.Name="NestedWrap";
        foreach(var type in new[]{"DropDownButton","SplitButton"}) {var c=vm.CreateControl(type,0,0,wrap.Id);c.Name=type;c.Text=type;StandardItemsProperties.Write(c,StandardItemsProperties.MenuKey,new[]{new StandardMenuItem{Text="Menu item"}});}
        AddRows(vm,"NestedRows",stack.Id);
        var staticList=vm.CreateControl("ListBox",0,0,stack.Id);staticList.Name="NestedStaticList";
        StandardItemsProperties.Write(staticList,"Items",new[]{"First","Second","Third"});
        Back(vm,home);
        vm.CreateNewForm("Grid");targets.Add(vm.ActiveFormDocument!.Id); vm.FormTitle="7A Data";
        vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;
        var source=ProductsSource();source.UseDemoData=true;vm.BindingSources.Add(source);
        var grid=vm.CreateControl("DataGrid",0,0);grid.Name="BoundGrid";grid.BindingSourceId=source.Id;grid.GridRow=1;
        Back(vm,home);
        vm.SetActiveForm(home,"layout7-export-home");
        for(var i=0;i<targets.Count;i++)vm.Interactions.Add(new(){SourceControlName=navigation[i].Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionOpenForm,TargetFormId=targets[i],OpenMode=InteractionModel.OpenModeShow});
        // Explicit local Manual data is permitted for DataGrid. Bound ListBox deliberately
        // exports only real provider rows; cover its static items separately above.
        vm.IncludeSampleData=true;vm.DataGridExportMode=MainWindowViewModel.DataGridExportModeReal;
        void AddRows(MainWindowViewModel model,string name,string? parent)
        {
            var source=ProductsSource();source.UseDemoData=true; model.BindingSources.Add(source);
            var list=model.CreateControl("DataGrid",300,30,parent);list.Name=name;list.BindingSourceId=source.Id;list.HeightMode="Fixed";list.Height=90;list.Margin="0,120,0,0";
        }
        void Back(MainWindowViewModel model,string target)
        {
            var b=model.CreateControl("Button",0,0);b.Name="BackHome";b.Text="Home";b.HorizontalAlignment="Right";
            model.Interactions.Add(new(){SourceControlName=b.Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionOpenForm,TargetFormId=target,OpenMode=InteractionModel.OpenModeShow});
        }
    }
    private static void AssertLayout7MultiForm(SmokeContext context)
    {
        var vm=context.ViewModel; RequireCapability(vm.CurrentProject.Forms.Count==4,"Expected four forms.");
        var zip=Path.Combine(context.ProjectPath,"MultiForm.zip"); var task=vm.ExportCurrentResultAsZipAsync(zip);
        var limit=DateTime.UtcNow.AddSeconds(30);
        while(!task.IsCompleted&&DateTime.UtcNow<limit)NativeItemsUiWait(1);
        RequireCapability(task.IsCompleted,"ZIP export did not finish.");task.GetAwaiter().GetResult();
        using var archive=ZipFile.OpenRead(zip); RequireCapability(archive.Entries.Count(e=>e.FullName.EndsWith(".axaml",StringComparison.OrdinalIgnoreCase))==5,"ZIP lost form AXAML or App.");
        File.WriteAllText(Path.Combine(context.ProjectPath,"workspace.fixture.json"),vm.ExportWorkspaceJson());
    }
    private static int RunLayout7Desktop()
    {
        Directory.CreateDirectory(Layout7Folder);var vm=NestedModel();vm.DesignWidth=900;vm.DesignHeight=600;
        vm.SurfaceGridColumnDefinitions="*,*,*";vm.SurfaceGridRowDefinitions="*,*";vm.SurfaceLayoutRows=2;vm.SurfaceLayoutColumns=3;
        foreach(var (mode,column,row) in new[]{(DesignerLayoutModes.Stack,0,0),(DesignerLayoutModes.Dock,1,0),(DesignerLayoutModes.Flex,2,0),(DesignerLayoutModes.Absolute,0,1),(DesignerLayoutModes.Grid,1,1)})
        {
            var host=NestedHost(vm,mode,column);host.GridRow=row;host.Name=mode+"Target";
            if(mode==DesignerLayoutModes.Dock) LayoutPlacementProperties.SetLiteral(host,LayoutPlacementProperties.LastChildFillKey,false);
            if(mode==DesignerLayoutModes.Grid){host.Rows=2;host.GridRowDefinitions="Auto,*";}
            var label=vm.CreateControl("TextBlock",10,10,host.Id,true);label.Text=mode+" target";label.FontSize=18;
            if(mode==DesignerLayoutModes.Dock)LayoutPlacementProperties.SetLiteral(label,LayoutPlacementProperties.DockKey,"Top");
        }
        var window=CreateSmokeWindow(vm);window.Width=1600;window.Height=1000;window.Title="Layout 7A Desktop Drag Test";
        window.Show();vm.CloseStartScreenCommand.Execute(null);
        void Snapshot(object? sender,EventArgs e)
        {File.WriteAllText(Path.Combine(Layout7Folder,"desktop-state.json"),vm.ExportWorkspaceJson());}
        vm.DesignerChanged+=Snapshot;Snapshot(null,EventArgs.Empty);
        using var stop=new CancellationTokenSource(TimeSpan.FromMinutes(15));window.Closed+=(_,_)=>stop.Cancel();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
        timer.Tick+=(_,_)=>{Snapshot(null,EventArgs.Empty);File.WriteAllText(Path.Combine(Layout7Folder,"desktop-grid-diagnostics.json"),JsonSerializer.Serialize(window.GridDragDiagnostics));File.WriteAllText(Path.Combine(Layout7Folder,"desktop-interaction.json"),JsonSerializer.Serialize(new{Mode=window.CurrentDesignerInteractionMode.ToString(),Controls=vm.Controls.Count,Pointer=window.DesignerPointerDiagnostics.TakeLast(12)}));if(File.Exists(Path.Combine(Layout7Folder,"desktop-stop.txt")))window.CloseForExternalHost();};
        timer.Start();try{Dispatcher.UIThread.MainLoop(stop.Token);}finally{timer.Stop();vm.DesignerChanged-=Snapshot;window.CloseForExternalHost();}
        File.WriteAllText(Path.Combine(Layout7Folder,"desktop-grid-diagnostics.json"),JsonSerializer.Serialize(window.GridDragDiagnostics));
        return 0;
    }
}
