using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal5BScaleProfile()
    {
        var results=new List<object>();
        foreach(var scene in new[]{"100Shapes","100Nested"}){
            var vm=GridFirstVm();vm.DesignWidth=1000;vm.DesignHeight=900;
            string parent="";
            if(scene=="100Nested"){
                var scroll=vm.CreateControl("ScrollViewer",0,0);scroll.Width=900;scroll.Height=800;scroll.WidthMode=scroll.HeightMode="Fixed";
                var stack=vm.CreateControl("StackPanel",0,0,scroll.Id);stack.Width=800;stack.WidthMode="Fixed";stack.HeightMode="Auto";parent=stack.Id;
            }else{vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);vm.DesignWidth=1000;vm.DesignHeight=900;}
            for(var i=0;i<100;i++){
                var c=vm.CreateControl(scene=="100Shapes"?new[]{"Line","Polygon","Polyline","Path"}[i%4]:"Button",i%10*90,i/10*70,parent,true);
                c.Width=70;c.Height=40;c.WidthMode=c.HeightMode="Fixed";c.StackOrder=i;
            }
            var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);window.UpdateLayout();
            try{
                for(var i=-5;i<30;i++){
                    void Measure(string op,Action action){var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var bytes=GC.GetAllocatedBytesForCurrentThread();var builds=AvaloniaMetadataCatalog.BuildCount;var timer=Stopwatch.StartNew();action();timer.Stop();
                        if(i>=0)results.Add(new{Scene=scene,Operation=op,Iteration=i,Ms=timer.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,GC=Enumerable.Range(0,3).Select(n=>GC.CollectionCount(n)-gc[n]).ToArray(),MetadataBuilds=AvaloniaMetadataCatalog.BuildCount-builds});}
                    FormDesigner.Models.DesignControlModel added=null!;
                    Measure("Add",()=>{added=vm.CreateControl(scene=="100Shapes"?"Line":"Button",0,0,parent,true);added.Width=70;added.Height=40;added.WidthMode=added.HeightMode="Fixed";window.UpdateLayout();});
                    vm.Controls.Remove(added);vm.SelectSingleControl(vm.Controls[0]);window.UpdateLayout();
                    Measure("Inspector",()=>{vm.SelectSingleControl(vm.Controls[1+Math.Max(0,i)%90]);window.UpdateLayout();});
                    Measure("SaveLoad",()=>{var copy=GridFirstVm();try{copy.LoadDocumentJson(vm.ExportWorkspaceJson());RequireCapability(copy.Controls.Count==vm.Controls.Count,"Scale JSON lost controls");}finally{Dispose5AProfileVm(copy);}});
                    Measure("Export",vm.GenerateXaml);
                    foreach(var mode in new[]{false,true})Measure(mode?"AXAMLPreview":"LegacyPreview",()=>{using var preview=ShowDataGridRegressionPreview(vm,mode);var count=preview.Window.GetVisualDescendants().OfType<Control>().Count(c=>vm.Controls.Any(m=>m.Name==c.Name));RequireCapability(count>=100,"Scale Preview lacks actual native controls");});
                }
            }finally{window.CloseForExternalHost();SmokeWindows.Clear();Dispose5AProfileVm(vm);}
            Console.WriteLine("SCALE5B "+scene+" samples=30 warmup=5");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL5B_OUTPUT")!,JsonSerializer.Serialize(results));return 0;
    }
}
