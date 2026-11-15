using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Services;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunUniversal4BAckProfile()
    {
        var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(Universal4ASource(100)),"ack.axaml");
        var samples=new List<object>();
        for(var iteration=-5;iteration<100;iteration++)
        {
            var target=vm.Controls.Single(c=>c.Name=="N"+Math.Max(0,iteration));var id=target.Id;
            var bytes=GC.GetAllocatedBytesForCurrentThread();var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            var timer=Stopwatch.StartNew();vm.SelectSingleControl(target);
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Value"),(15m+iteration/1000m).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.HasChanges,"Sequential ACK needs a real patch.");
            vm.MarkAxamlRoundTripSaved("ack.axaml",patch.PatchedText);timer.Stop();
            if(iteration>=0)samples.Add(new{Operation="EditPatchAck",Iteration=iteration,Ms=timer.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,
                GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),PeakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64});
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges&&vm.Controls.Single(c=>c.Name==target.Name).Id==id,"ACK/source map identity lost.");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL4B_OUTPUT")!,JsonSerializer.Serialize(samples));Console.WriteLine("ACK_PROFILE4B samples="+samples.Count);return 0;
    }
    private static int RunUniversal4BLargeStages()
    {
        var text=File.ReadAllText(Path.Combine(FindRepositoryRoot(),"Views","MainWindow.axaml"));
        var samples=new List<object>();
        for(var iteration=-5;iteration<30;iteration++)
        {
            var source=new AxamlImportService().Import(text);var vm=GridFirstVm();
            void Measure(string operation,Action action)
            {
                using var profile=LayoutDropProfile.Begin("4b",operation);
                var bytes=GC.GetAllocatedBytesForCurrentThread();var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                var timer=Stopwatch.StartNew();action();timer.Stop();
                if(iteration>=0)samples.Add(new{Operation=operation,Iteration=iteration,Ms=timer.Elapsed.TotalMilliseconds,
                    Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),
                    Stages=profile.Stages.ToArray(),ManagedBytes=GC.GetTotalMemory(false),PeakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64});
            }
            Measure("Large.ApplyDocumentInspector",()=>vm.LoadAxamlImportedDocument(source,"MainWindow.axaml"));
            Measure("Large.NativeMeasureArrange",()=>RequireCapability(AxamlLayoutProjection.Arrange(source.RoundTripDocument,vm.Controls,1200,800).Count==vm.Controls.Count,"Native projection incomplete."));
            Measure("Large.SourceLookup100",()=>{
                foreach(var item in vm.Controls.Take(100))RequireCapability(source.RoundTripDocument.SourceMap.TryGet(item.Id,out var reference)&&reference.Element.ElementSpan.End<=text.Length,"Source lookup failed.");
            });
            RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Large projection mutated source.");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL4B_OUTPUT")!,JsonSerializer.Serialize(samples));
        Console.WriteLine("LARGE_STAGES4B samples="+samples.Count);return 0;
    }
    // Identical code is used against the archived Phase 4A baseline and current assembly.
    // 100 different controls/properties, not 100 repeated reads of the same selected row.
    private static int RunUniversal4BInteractions()
    {
        var records = new List<object>();
        var source = Universal4ASource(100);
        var vm = GridFirstVm(); vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source), "interactions.axaml");
        var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); window.UpdateLayout();
        try
        {
            for (var iteration = -5; iteration < 100; iteration++)
            {
                void Measure(string operation, Action action)
                {
                    using var stages = LayoutDropProfile.Begin("4b", operation);
                    var bytes = GC.GetAllocatedBytesForCurrentThread(); var gc = Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                    var timer = Stopwatch.StartNew(); action(); window.UpdateLayout(); timer.Stop();
                    if (iteration >= 0) records.Add(new { Operation=operation, Iteration=iteration, Ms=timer.Elapsed.TotalMilliseconds,
                        Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes, GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),
                        Stages=stages.Stages.ToArray(), ManagedBytes=GC.GetTotalMemory(false), PeakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64 });
                }
                Measure("Select",()=>vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="N"+Math.Max(0,iteration))));
                Measure("Edit",()=>vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Value"),"15.5"));
                Measure("Patch",()=>RequireCapability(vm.CreateActiveAxamlPatch().HasChanges,"No real patch."));
                Measure("Undo",()=>vm.UndoEditorCommand!.Execute(null));
                RequireCapability(!vm.CreateActiveAxamlPatch().HasChanges,"Undo lost source.");
                Measure("Redo",()=>vm.RedoEditorCommand!.Execute(null));
                vm.UndoEditorCommand!.Execute(null); window.UpdateLayout();
            }
        }
        finally { window.CloseForExternalHost(); SmokeWindows.Clear(); }
        // Initialization includes ApplyDocument, surface projection, Inspector and native layout.
        // Preview is measured separately in its real window (includes existing loading overlay).
        for(var iteration=-5;iteration<30;iteration++)
        {
            var imported=new AxamlImportService().Import(source);
            var current=GridFirstVm();
            using var profile=LayoutDropProfile.Begin("4b","Initialization");
            var bytes=GC.GetAllocatedBytesForCurrentThread();var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            var timer=Stopwatch.StartNew();current.LoadAxamlImportedDocument(imported,"init.axaml");
            var host=CreateSmokeWindow(current);host.Show();current.CloseStartScreenCommand.Execute(null);host.UpdateLayout();timer.Stop();
            if(iteration>=0)records.Add(new{Operation="Initialize",Iteration=iteration,Ms=timer.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,
                GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),Stages=profile.Stages.ToArray(),ManagedBytes=GC.GetTotalMemory(false),PeakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64});
            host.CloseForExternalHost();SmokeWindows.Clear();profile.Dispose();
            using var previewProfile=LayoutDropProfile.Begin("4b","Preview");
            bytes=GC.GetAllocatedBytesForCurrentThread();gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();timer.Restart();
            using(var preview=ShowDataGridRegressionPreview(current,true))
            {
                timer.Stop();
                if(iteration>=0)records.Add(new{Operation="Preview",Iteration=iteration,Ms=timer.Elapsed.TotalMilliseconds,Bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,
                    GC=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),Stages=previewProfile.Stages.ToArray(),ManagedBytes=GC.GetTotalMemory(false),PeakWorkingSet=Process.GetCurrentProcess().PeakWorkingSet64});
            }
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("UNIVERSAL4B_OUTPUT")!,JsonSerializer.Serialize(records));
        Console.WriteLine("INTERACTIONS4B samples="+records.Count);return 0;
    }
}
