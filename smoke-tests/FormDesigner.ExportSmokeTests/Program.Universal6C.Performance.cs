using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int ProfileItemTemplate(string directory)
    {
        Directory.CreateDirectory(directory);
        var samples = new List<object>();
        foreach (var size in new[] {10,50,100})
        {
            var form = GridFirstVm();
            try
            {
                var owner = form.CreateControl("ItemsControl",0,0);
                var local = new CollectionItemsContract();
                for (var i=0;i<size;i++) local.Items.Add(new() { Kind="ContentControl",Text="Строка "+i });
                owner.CustomProperties.Add(new() {Key=CollectionItemsContract.StorageKey("Items"),ValueJson=JsonSerializer.Serialize(local)});
                for (var iteration=-1;iteration<12;iteration++)
                {
                    void Measure(string operation,Action action)
                    {
                        var collections = Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
                        var bytes = GC.GetTotalAllocatedBytes(true);
                        var start = Stopwatch.GetTimestamp();
                        action();
                        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        var allocated = GC.GetTotalAllocatedBytes(true)-bytes;
                        if (iteration>=0) samples.Add(new {Operation=operation,Items=size,Iteration=iteration,Ms=ms,Bytes=allocated,
                            GC=Enumerable.Range(0,3).Select(n=>GC.CollectionCount(n)-collections[n]).ToArray()});
                    }
                    MainWindowViewModel? draft = null;
                    Measure("OpenDraft",()=>draft=form.OpenItemTemplate(owner));
                    try
                    {
                        DesignControlModel? stack = null;
                        Measure("AddLayout",()=>stack=draft!.CreateControl("StackPanel",0,0));
                        DesignControlModel? label=null;
                        Measure("AddNestedTextBlock",()=>label=draft!.CreateControl("TextBlock",0,0,stack!.Id));
                        draft!.SelectSingleControl(label!);
                        Commit6A(draft,PropertyValueKind.Binding,"{Binding .}");
                        Measure("SelectionAndInspector",()=>{draft.SelectSingleControl(stack);draft.SelectSingleControl(label); _=Row6A(draft);});
                        var rebuild=typeof(MainWindowViewModel).GetMethod("RebuildPropertyGrid",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
                        Measure("InspectorRows",()=>rebuild.Invoke(draft,new object[]{"TemplateProfile"}));
                        Measure("CommitTemplate",()=>form.SaveItemTemplate(form.ActiveDocumentId,owner,draft));
                    }
                    finally { draft?.CloseItemTemplate(); }
                    string json = "";
                    Measure("SaveJson",()=>json=form.ExportWorkspaceJson());
                    var reopened=GridFirstVm();
                    try
                    {
                        Measure("LoadJson",()=>reopened.LoadDocumentJson(json));
                        Measure("NativePreview",()=>
                        {
                            using var preview=ShowDataGridRegressionPreview(reopened,true);
                            var native=preview.Window.GetVisualDescendants().OfType<ItemsControl>().First(i=>i.Items.Count==size && i.ItemTemplate!=null);
                            RequireCapability(native.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Строка 0"),"Materialized row missing");
                        });
                        Measure("FolderExport",()=>Pump(reopened.ExportCurrentResultToProjectAsync(Path.Combine(directory,"exports",size+"-"+iteration))));
                    }
                    finally { Dispose5AProfileVm(reopened); }
                    form.SaveItemTemplate(form.ActiveDocumentId,owner,null);
                }
            }
            finally { Dispose5AProfileVm(form); }
        }
        File.WriteAllText(Path.Combine(directory,"samples.json"),JsonSerializer.Serialize(samples));
        return 0;
    }
}
