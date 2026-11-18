using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.Views;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int ProfileStyleExport(string directory)
    {
        Directory.CreateDirectory(directory);
        var samples = new List<object>();
        var vm = StyleScene("Grid");
        try
        {
            for (var i = -5; i < 30; i++)
            {
                foreach (var format in new[] { "Folder", "Zip" })
                {
                    // Invalidate the generated result just as a committed material edit would.
                    var sheet = vm.AppearanceDraft();
                    sheet.Styles[1].Setters[0] = new("CornerRadius", ((i + 6) * 2 + (format == "Zip" ? 1 : 0)).ToString());
                    vm.SaveAppearance(vm.ActiveDocumentId, sheet);
                    var target = Path.Combine(directory, format == "Zip" ? "project.zip" : "project");
                    var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                    var allocated = GC.GetTotalAllocatedBytes(true);
                    var start = Stopwatch.GetTimestamp();
                    if (format == "Folder") Pump(vm.ExportCurrentResultToProjectAsync(target));
                    else Pump(vm.ExportCurrentResultAsZipAsync(target));
                    var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
                    using var process = Process.GetCurrentProcess();
                    if (i >= 0) samples.Add(new { Operation = "Export" + format, Iteration = i, Ms = ms, Bytes = bytes,
                        GC = Enumerable.Range(0, 3).Select(n => GC.CollectionCount(n) - gc[n]).ToArray(), Peak = process.PeakWorkingSet64 });
                    if (format == "Folder") RequireCapability(Directory.EnumerateFiles(target, "*.csproj", SearchOption.AllDirectories).Any(), "No generated project");
                    else { using var zip = System.IO.Compression.ZipFile.OpenRead(target);
                        RequireCapability(zip.Entries.Any(e => e.FullName.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)), "No AXAML in ZIP"); }
                }
            }
        }
        finally { Dispose5AProfileVm(vm); }
        File.WriteAllText(Path.Combine(directory, "export-profile.json"), JsonSerializer.Serialize(samples));
        return 0;
    }

    private static int ProfileStyleEditor(string path)
    {
        var samples = new List<object>();
        var source = File.ReadAllText(Path.Combine("Views","MainWindow.axaml"));
        var vm = StyleScene("Grid");
        var original = vm.AppearanceDraft();
        var window = CreateSmokeWindow(vm); window.Show(); window.UpdateLayout();
        var selections = new Dictionary<string,DesignControlModel[]>();
        foreach (var type in new[]{"Button","MaskedTextBox"})
            selections[type] = new[]{vm.CreateControl(type,0,0),vm.CreateControl(type,0,0)};
        try
        {
            for (var i=-5;i<30;i++)
            {
                void Measure(string operation,Action action)
                {
                    using var process=Process.GetCurrentProcess();
                    var gc=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                    var bytes=GC.GetAllocatedBytesForCurrentThread();
                    var start=Stopwatch.GetTimestamp();
                    action();
                    var ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    if(i>=0) samples.Add(new { Operation=operation, Iteration=i, Ms=ms, Bytes=allocated,
                        GC=Enumerable.Range(0,3).Select(n=>GC.CollectionCount(n)-gc[n]).ToArray(), Peak=process.PeakWorkingSet64 });
                }
                foreach (var (type, pair) in selections)
                {
                    vm.ClearSelection(); window.UpdateLayout();
                    Measure("FirstSelection."+type,()=>{vm.SelectSingleControl(pair[0]);window.UpdateLayout();});
                    Measure("RepeatedTypeSelection."+type,()=>{vm.SelectSingleControl(pair[1]);window.UpdateLayout();});
                }
                FormStyleEditorWindow? editor=null;
                Measure("OpenStyles",()=>{editor=new(vm);editor.Show(window);editor.UpdateLayout();});
                var openEditor = editor ?? throw new InvalidOperationException("Style editor did not open");
                var tabs=openEditor.GetVisualDescendants().OfType<TabControl>().Single();
                Measure("OpenResourcesTab",()=>{tabs.SelectedIndex=1;openEditor.UpdateLayout();});
                tabs.SelectedIndex=0;openEditor.UpdateLayout();
                var list=openEditor.GetVisualDescendants().OfType<ListBox>().First();
                list.SelectedIndex=0;
                Measure("SwitchStyle",()=>{list.SelectedIndex=1;openEditor.UpdateLayout();});
                openEditor.Close();
                foreach(var count in new[]{10,50,100})
                    Measure("Commit"+count+"Setters",()=>{
                        var draft=original.Copy();
                        for(var j=0;j<count;j++) draft.Styles.Add(new("Button","Button.c"+j,new(){new("Opacity","0.7")}));
                        vm.SaveAppearance(vm.ActiveDocumentId,draft);
                    });
                Measure("Commit100Resources",()=>{
                    var draft=original.Copy();
                    for(var j=0;j<100;j++) draft.Resources.Add(new("r"+j,"Double",j.ToString()));
                    vm.SaveAppearance(vm.ActiveDocumentId,draft);
                });
                vm.SaveAppearance(vm.ActiveDocumentId,original);
                Measure("PreviewRefresh",()=>{using var preview=ShowDataGridRegressionPreview(vm,true);
                    RequireCapability(preview.Window.GetVisualDescendants().OfType<TextBox>().Any(c=>c.Name=="NameInput" && c.FontSize==23),"Preview style missing");});
                Measure("LargeImport",()=>new AxamlImportService().Import(source,"MainWindow.axaml"));
                Measure("GenerateAxaml",vm.GenerateXaml);
            }
        }
        finally {window.CloseForExternalHost();SmokeWindows.Clear();Dispose5AProfileVm(vm);}
        File.WriteAllText(path,JsonSerializer.Serialize(samples));return 0;
    }
}
