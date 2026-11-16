using Avalonia.Controls;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Metadata;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    // Portable: copied unchanged into the archived baseline test harness.
    private static int RunUniversal5COperationsProfile()
    {
        var records=new List<object>();var vm=GridFirstVm();var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);
        var item=vm.CreateControl("Button",0,0);vm.SelectSingleControl(item);vm.EndPropertyGridTextEdit();vm.BeginUndoBatch();vm.CommitUndoBatch();window.UpdateLayout();
        try{for(var n=-5;n<30;n++){
            void Measure(string op,Action action){var bytes=GC.GetAllocatedBytesForCurrentThread();var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();var builds=AvaloniaMetadataCatalog.BuildCount;using var process=Process.GetCurrentProcess();var watch=Stopwatch.StartNew();action();watch.Stop();if(n>=0)records.Add(new{Operation=op,Iteration=n,Ms=watch.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),MetadataBuilds=AvaloniaMetadataCatalog.BuildCount-builds,PeakWorkingSet=process.PeakWorkingSet64});}
            Measure("ToolboxInitialize",()=>{var fresh=GridFirstVm();try{RequireCapability(fresh.ToolboxGroups.SelectMany(g=>g.Items).Any(),"Toolbox empty");}finally{Dispose5AProfileVm(fresh);}});
            Measure("MetadataDiscoveryCached",()=>{var types=AvaloniaMetadataCatalog.DiscoverControls();foreach(var type in types)_=AvaloniaMetadataCatalog.Get(type);RequireCapability(types.Count>100,"Discovery missing framework types");});
            Measure("DesignerProjection",()=>{typeof(FormDesigner.Views.MainWindow).GetMethod("RenderDesigner",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);window.UpdateLayout();});
            vm.SelectSingleControl(vm.Controls.Single());vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Text"),"Benchmark "+n);
            Measure("UndoRedo",()=>{vm.UndoEditorCommand!.Execute(null);vm.RedoEditorCommand!.Execute(null);Dispatcher.UIThread.RunJobs();window.UpdateLayout();RequireCapability(vm.Controls.Single().Text=="Benchmark "+n,"Undo/Redo changed authored text");});
        }}finally{window.CloseForExternalHost();SmokeWindows.Clear();Dispose5AProfileVm(vm);}
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL5C_OPERATIONS_OUTPUT")!,JsonSerializer.Serialize(records));Console.WriteLine("OPERATIONS5C samples="+records.Count);return 0;
    }
}
