using Avalonia;
using Avalonia.Controls;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.Services;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string GridFirstFolder => Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase7b");
    private static MainWindowViewModel GridFirstVm()
    {
        var registry=new DesignerRegistry();BuiltInControlRegistrar.Register(registry);
        registry.RegisterBindingProvider(new ReflectionBindingMetadataProvider());
        return new MainWindowViewModel(registry,new TestDesignerHostServices(true))
        {
            ExportProjectNamespace="SmokeGenerated.GridFirst",ExportTarget=MainWindowViewModel.ExportTargetMainWindow,
            LayoutExportMode=MainWindowViewModel.LayoutExportModeCanvas,DataGridExportMode=MainWindowViewModel.DataGridExportModeReal,
            IncludeSampleData=false,IncludePluginRuntimeReferences=false
        };
    }
    private static string GridFirstLegacyJson() => JsonSerializer.Serialize(new DesignerDocumentFileModel
    {
        FormTitle="LegacyCanvas",Controls=Enumerable.Range(0,20).Select(i=>new DesignerControlFileModel
        {Id="legacy-"+i,Name="LegacyButton"+i,Type="Button",X=20+(i%5)*180,Y=20+(i/5)*70,Width=150,Height=40,Text="Legacy "+i}).ToList()
    });
    private static void GridFirstVisible(MainWindow window,MainWindowViewModel vm)
    {
        var limit=DateTime.UtcNow.AddSeconds(5);
        while((vm.CanvasRenderedDocumentId!=vm.ActiveDocumentId||Layout7Wrappers(window).Count!=vm.Controls.Count)&&DateTime.UtcNow<limit)NativeItemsUiWait(1);
        RequireCapability(vm.CanvasRenderedDocumentId==vm.ActiveDocumentId&&Layout7Wrappers(window).Count==vm.Controls.Count,"Designer did not show the current form.");
        WaitLayout7Frame(window);
    }
    private static int RunGridFirstBenchmark(string phase)
    {
        Directory.CreateDirectory(GridFirstFolder);var samples=new List<object>();
        for(var iteration=-5;iteration<30;iteration++)
        {
            var vm=GridFirstVm();var window=CreateSmokeWindow(vm);window.Width=1600;window.Height=1000;
            window.ShowInTaskbar=false;window.ShowActivated=false;window.Position=new PixelPoint(-3500,-3500);window.Show();vm.CloseStartScreenCommand.Execute(null);
            try
            {
                NativeItemsUiWait(120);GridFirstVisible(window,vm);
                void Measure(string operation,Action action)
                {
                    var allocated=GC.GetTotalAllocatedBytes(false);var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var watch=Stopwatch.StartNew();
                    using var profile=LayoutDropProfile.Begin(vm.ActiveDocumentId,operation);
                    action();var modelMs=watch.Elapsed.TotalMilliseconds;GridFirstVisible(window,vm);var visibleMs=watch.Elapsed.TotalMilliseconds;
                    profile.Dispose();
                    if(iteration>=0)samples.Add(new{phase,iteration,operation,modelMs,visibleMs,allocatedBytes=GC.GetTotalAllocatedBytes(false)-allocated,
                        gc=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),counters=profile.Counters,
                        applyDocumentCalls=profile.Stages.Where(s=>s.Stage=="ApplyDocument").Sum(s=>s.Calls),root=vm.SurfaceLayoutMode});
                }
                Measure("NewProject",()=>vm.NewProjectEditorCommand!.Execute(null));var first=vm.ActiveDocumentId;
                Measure("AddForm",()=>vm.CreateNewForm());
                Measure("FirstToolboxDrop",()=>
                {
                    var point=new Point(30,30);var target=vm.UsesNativeLayout?vm.ResolveLayoutDropTarget(point):null;
                    if(target is not null)RequireCapability(vm.CommitGridToolboxDrop("Button",target,point,vm.ActiveDocumentId) is not null,"First Grid Drop rejected.");
                    else RequireCapability(vm.TryCreateControlFromToolboxDrop("Button",point.X,point.Y,null,false,vm.ActiveDocumentId) is not null,"First Canvas Drop rejected.");
                });
                Measure("SwitchForm",()=>vm.SetActiveForm(first,"grid-first-benchmark"));
                Measure("OpenLegacy20",()=>vm.LoadDocumentJson(GridFirstLegacyJson()));
            }
            finally{window.CloseForExternalHost();SmokeWindows.Remove(window);}
        }
        File.WriteAllText(Path.Combine(GridFirstFolder,"performance-"+phase+".json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PERFORMANCE "+phase+": 5 warmup + 30 iterations; 150 samples; real Window/layout/compositor; timings are measurements, not CI thresholds.");
        return 0;
    }
}
