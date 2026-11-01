using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string Layout7Folder => Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase7a");
    private static int RunLayoutDropPerformanceChecks()
    {
        Directory.CreateDirectory(Layout7Folder);
        var results = new List<object>();
        var counts=(Environment.GetEnvironmentVariable("LAYOUT_PROFILE_COUNTS")??"50,100,300,500").Split(',').Select(int.Parse);
        foreach (var count in counts)
        {
            var vm = NestedModel(); vm.DesignWidth = 1200; vm.DesignHeight = 800;
            vm.SurfaceGridColumnDefinitions = "*"; vm.SurfaceLayoutColumns = 1;
            var host = NestedHost(vm, DesignerLayoutModes.Flex); host.LayoutOrientation = "Horizontal";
            for (var i = 1; i < count; i++) { var c = NestedLeaf(vm, host, "Item " + i, i); c.Width = 75; c.Height = 28; }
            var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(150); window.UpdateLayout();
            try
            {
                var factory = typeof(MainWindow).GetMethod("CreatePreviewControl", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var operation=Environment.GetEnvironmentVariable("LAYOUT_PROFILE_OPERATION")=="toolbox"?"Toolbox":"Move";
                var moving = operation=="Toolbox"?vm.CreateGridDropPrototype("Button"):vm.Controls.Last(); var bounds = vm.NativeContainerMeasurements[host.Id].Children.First().Bounds;
                var point = bounds.TopLeft + new Vector(1,1);
                var target = vm.ResolveLayoutDropTarget(point,moving);
                target = vm.EvaluateGridDropPreview(target,moving,c=>(Control)factory.Invoke(window,new object[]{c})!);
                using var profile = LayoutDropProfile.Begin(vm.ActiveDocumentId,moving.Id,(code,detail)=>Console.WriteLine(code+" "+detail));
                var clock = Stopwatch.StartNew();
                if(operation=="Toolbox")moving=vm.CommitGridToolboxDrop("Button",target,point,vm.ActiveDocumentId)??throw new Exception("Profiled Toolbox Drop rejected: "+vm.StatusText);
                else RequireCapability(vm.CommitGridControlDrop(moving,target,point),"Profiled Drop rejected: "+vm.StatusText);
                var modelMs=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("WaitVisualSnapshot"))
                {
                    var limit=DateTime.UtcNow.AddSeconds(10);
                    while(vm.NativeGridMeasurementKey!=vm.GridDropLayoutKey && DateTime.UtcNow<limit) NativeItemsUiWait(1);
                    RequireCapability(vm.NativeGridMeasurementKey==vm.GridDropLayoutKey,"Drop visual snapshot did not update.");
                }
                var visualMs=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("UILayout")) window.UpdateLayout();
                var layoutMs=clock.Elapsed.TotalMilliseconds;
                using(LayoutDropProfile.Stage("FirstRenderAfterDrop"))
                {
                    var commit=Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(window)!.Compositor.RequestCommitAsync();
                    var limit=DateTime.UtcNow.AddSeconds(5);
                    while(!commit.IsCompleted && DateTime.UtcNow<limit) NativeItemsUiWait(1);
                    RequireCapability(commit.IsCompletedSuccessfully,"First composition commit did not complete.");
                }
                var firstRenderMs=clock.Elapsed.TotalMilliseconds;
                var ready=false;
                using(LayoutDropProfile.Stage("NextInputReady"))
                {
                    Dispatcher.UIThread.Post(()=>ready=true,DispatcherPriority.Input);
                    var limit=DateTime.UtcNow.AddSeconds(5);
                    while(!ready && DateTime.UtcNow<limit) NativeItemsUiWait(1);
                    RequireCapability(ready,"Next input callback did not complete.");
                }
                var readyMs=clock.Elapsed.TotalMilliseconds;
                RequireCapability(moving.StackOrder==0 && vm.SelectedControl?.Id==moving.Id,"Drop lost order/selection.");
                RequireCapability(vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.Key=="Name"&&r.ContextControlId==moving.Id),"Inspector target is stale.");
                profile.Dispose();
                var result=new {count,operation,modelMs,visualMs,layoutMs,firstRenderMs,readyMs,profile.AllocatedBytes,Stages=profile.Stages,Counters=profile.Counters};
                results.Add(result); Console.WriteLine("DROP_PERFORMANCE "+JsonSerializer.Serialize(result));
            }
            finally {window.CloseForExternalHost();}
        }
        var label=Environment.GetEnvironmentVariable("LAYOUT_PROFILE_LABEL")??"current";
        File.WriteAllText(Path.Combine(Layout7Folder,"drop-"+label+".json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
