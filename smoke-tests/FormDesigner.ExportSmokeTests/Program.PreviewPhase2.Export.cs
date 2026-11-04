using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using FormDesigner.Controls;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel Preview2MixedModel(bool canvasRoot)
    {
        var vm=canvasRoot?CreateViewModel("GridInCanvas"):NativeGridModel("CanvasInGrid");
        vm.DesignWidth=1200;vm.DesignHeight=800;
        var host=vm.CreateControl("Group",100,50,bypassGridSnap:true);host.Name="MixedHost";
        host.ChildLayoutMode=canvasRoot?DesignerLayoutModes.Grid:DesignerLayoutModes.Absolute;
        host.Width=600;host.Height=400;host.Padding=host.BorderThickness=0;host.Margin="0";
        host.Columns=host.Rows=2;host.GridRowDefinitions="80,*";host.GridColumnDefinitions="120,*";
        if(!canvasRoot){host.WidthMode=host.HeightMode="Auto";host.VerticalAlignment="Stretch";}
        var button=vm.CreateControl("Button",100,150,host.Id,true);button.Name="NestedButton";button.Text="Nested";
        button.Width=160;button.Height=40;button.GridRow=button.GridColumn=1;
        return vm;
    }
    private static int RunPreviewPhase2ExportFixtures()
    {
        var observations=new List<ConsistencyRow>();var failures=0;
        foreach(var scene in new[]{"HeadersCanvas","HeadersGrid","CanvasInGrid","GridInCanvas","EremexEditor","EremexGrid"})
        {
            MainWindowViewModel vm;
            if(scene.StartsWith("Headers"))vm=Preview2GridModel(scene=="HeadersGrid");
            else if(scene.StartsWith("Eremex"))
            {
                vm=CreateViewModel(scene);
                if(scene=="EremexEditor")ConfigureEremexTextEditorVerticalSlice(vm);else ConfigureEremexDataGridControlVerticalSlice(vm);
            }
            else vm=Preview2MixedModel(scene=="GridInCanvas");
            try
            {
                var designer=CreateSmokeWindow(vm);designer.Show();NativeItemsUiWait(150);
                try
                {
                    using var legacy=ShowDataGridRegressionPreview(vm,false);using var axaml=ShowDataGridRegressionPreview(vm,true);
                    foreach(var size in ConsistencySizes)
                    {
                        vm.DesignWidth=size.Width;vm.DesignHeight=size.Height;
                        foreach(var p in new[]{legacy.Window,axaml.Window}){p.Width=size.Width;p.Height=size.Height;}
                        NativeItemsUiWait(200);designer.UpdateLayout();legacy.Window.UpdateLayout();axaml.Window.UpdateLayout();
                        var surface=designer.GetVisualDescendants().OfType<DesignerSurface>().Single();
                        var d=CaptureConsistency(vm,scene,"Designer",surface.Canvas,DropWrappers(designer).ToDictionary(p=>p.Key,p=>(Control)p.Value));
                        var left=legacy.Window.FindControl<Border>("PreviewSurfaceBorder")!;var right=axaml.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                        var lmap=(Dictionary<string,Control>)typeof(PreviewWindow).GetField("_builtPreviewControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(legacy.Window)!;
                        var snap=(ExportedAxamlPreviewModel)typeof(PreviewWindow).GetField("_exportedAxamlPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(axaml.Window)!;
                        var rmap=vm.Controls.ToDictionary(c=>c.Id,c=>right.GetVisualDescendants().OfType<Control>().Single(v=>v.Name==snap.StandardControlNames[c.Id]));
                        var l=CaptureConsistency(vm,scene,"Legacy",left,lmap);var r=CaptureConsistency(vm,scene,"AXAML",right,rmap);
                        observations.AddRange(d);observations.AddRange(l);observations.AddRange(r);
                        RequireCapability(l.All(o=>o.Attached&&o.NativePresent)&&r.All(o=>o.Attached&&o.NativePresent),"Native component missing in "+scene);
                    }
                }
                finally{designer.CloseForExternalHost();}
                var snapshot=vm.GenerateExportedAxamlPreviewSnapshot();
                var activeClass=XDocument.Parse(snapshot.ExportAxaml).Root!.Attribute(XName.Get("Class","http://schemas.microsoft.com/winfx/2006/xaml"))!.Value.Split('.').Last();
                File.WriteAllText(Path.Combine(Preview2Folder,scene+"-manifest.json"),JsonSerializer.Serialize(vm.Controls.Select(c=>new{c.Id,c.Name,c.Type,c.ParentId,c.ChildLayoutMode,WindowClass=activeClass}),new JsonSerializerOptions{WriteIndented=true}));
                var export=Path.Combine(Preview2Folder,"exports",scene);vm.ExportCurrentResultToProjectAsync(export).GetAwaiter().GetResult();
                Console.WriteLine("PASS PREVIEW2_EXPORT_FIXTURE_"+scene);
            }
            catch(Exception error){failures++;Console.WriteLine("FAIL PREVIEW2_EXPORT_FIXTURE_"+scene+": "+error);}
        }
        File.WriteAllText(Path.Combine(Preview2Folder,"geometry.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}
