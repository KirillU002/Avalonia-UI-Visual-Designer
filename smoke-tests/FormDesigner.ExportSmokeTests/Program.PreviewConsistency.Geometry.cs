using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private sealed record ConsistencyRow(string Scene,string Mode,double ViewportWidth,double ViewportHeight,string Id,string Name,string ParentId,
        string Type,string NativeType,bool NativePresent,bool Attached,bool Visible,double X,double Y,double Width,double Height);
    private static readonly Size[] ConsistencySizes={new(800,600),new(1200,800),new(1600,900)};

    private static MainWindowViewModel ConsistencyNestedScene()
    {
        var vm=NativeGridModel();vm.Controls.Clear();vm.BindingSources.Clear();vm.DesignWidth=1200;vm.DesignHeight=800;
        vm.SurfaceGridRowDefinitions="Auto,*";vm.SurfaceLayoutRows=2;vm.SurfaceGridColumnDefinitions="*";vm.SurfaceLayoutColumns=1;
        DesignControlModel Add(string type,string name,string parent="")
        {
            var c=vm.CreateControl(type,0,0,parent);c.Name=name;c.Text=name;c.WidthMode=c.HeightMode="Auto";
            c.Margin="4";c.Padding=4;c.BorderThickness=1;c.FontFamily="Segoe UI";return c;
        }
        var dock=Add("Group","HeaderDock");dock.ChildLayoutMode=DesignerLayoutModes.Dock;dock.Margin="8";
        var stack=Add("StackPanel","SearchStack",dock.Id);stack.LayoutOrientation="Horizontal";stack.LayoutSpacing=7;
        LayoutPlacementProperties.SetLiteral(stack,"DockPanel.Dock","Left");
        var b=Add("Button","SearchButton",stack.Id);b.WidthMode=b.HeightMode="Fixed";b.Width=100;b.Height=40;
        var input=Add("TextBox","SearchInput",stack.Id);input.WidthMode=input.HeightMode="Fixed";input.Width=180;input.Height=40;input.StackOrder=1;
        var wrap=Add("WrapPanel","HeaderWrap",dock.Id);wrap.StackOrder=1;wrap.LayoutOrientation="Horizontal";
        for(var i=0;i<4;i++){var c=Add(i==3?"ListBox":"DropDownButton","WrapItem"+i,wrap.Id);c.WidthMode=c.HeightMode="Fixed";c.Width=160;c.Height=80;c.StackOrder=i;}
        var border=Add("Border","BodyBorder");border.GridRow=1;border.Margin="12";border.Padding=8;border.ChildLayoutMode=DesignerLayoutModes.Grid;
        border.GridRowDefinitions="*,Auto";border.Rows=2;border.GridColumnDefinitions="2*,*";border.Columns=2;
        var grid=Add("DataGrid","Results",border.Id);grid.GridColumnSpan=2;grid.ShowFilterRow=grid.ShowFooter=grid.ShowGroupPanel=false;
        var label=Add("TextBlock","FooterLabel",border.Id);label.GridRow=1;
        var split=Add("SplitButton","FooterAction",border.Id);split.GridRow=1;split.GridColumn=1;split.WidthMode=split.HeightMode="Fixed";split.Width=180;split.Height=40;
        vm.DataGridExportMode=MainWindowViewModel.DataGridExportModeReal;vm.IncludeSampleData=false;
        return vm;
    }

    private static Type? ExpectedConsistencyType(DesignControlModel model)
    {
        if(model.Type.StartsWith("Eremex.",StringComparison.Ordinal))
        {
            var fullName=model.Type=="Eremex.TextEditor"?"Eremex.AvaloniaUI.Controls.Editors.TextEditor":"Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl";
            return AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(fullName,false)).FirstOrDefault(t=>t!=null);
        }
        var name=model.Type=="Group"?model.ChildLayoutMode switch{"Stack"=>"StackPanel","Grid"=>"Grid","Flex"=>"WrapPanel","Dock"=>"DockPanel",_=>"Canvas"}:model.Type;
        return name=="DataGrid"?typeof(DataGrid):typeof(Control).Assembly.GetType("Avalonia.Controls."+name);
    }
    private static Control PlacementView(DesignControlModel model,Control view)
        =>model.Type=="Image"&&view is Image&&view.GetVisualParent() is Border border?border:view;

    private static IReadOnlyList<ConsistencyRow> CaptureConsistency(MainWindowViewModel vm,string scene,string mode,Control root,Dictionary<string,Control> raw)
    {
        var views=vm.Controls.ToDictionary(c=>c.Id,c=>PlacementView(c,raw[c.Id]));
        return vm.Controls.Select(model=>
        {
            var view=views[model.Id];var expected=ExpectedConsistencyType(model);
            var native=view.GetVisualDescendants().OfType<Control>().Prepend(view).FirstOrDefault(c=>expected?.IsInstanceOfType(c)==true);
            var parent=view.GetVisualAncestors().OfType<Control>().FirstOrDefault(c=>views.Values.Contains(c));
            var parentId=parent is null?"":views.Single(p=>ReferenceEquals(p.Value,parent)).Key;
            var point=view.TranslatePoint(default,root)??throw new Exception("Detached control "+model.Name);
            return new ConsistencyRow(scene,mode,root.Bounds.Width,root.Bounds.Height,model.Id,model.Name,parentId,model.Type,
                native?.GetType().FullName??view.GetType().FullName!,native!=null,view.GetVisualRoot()!=null,
                view.IsVisible&&view.GetVisualAncestors().OfType<Control>().All(c=>c.IsVisible),point.X,point.Y,view.Bounds.Width,view.Bounds.Height);
        }).ToArray();
    }

    private static void RunConsistencyGeometry(Action<string,Action> check)
    {
        var records=new List<ConsistencyRow>();
        foreach(var scene in new[]{"RegistryNative","NestedNative","RegistryCanvas"})
        {
            var vm=scene=="NestedNative"?ConsistencyNestedScene():AllRegistryScene(scene=="RegistryNative");
            var window=CreateSmokeWindow(vm);window.Width=1600;window.Height=1000;window.Show();NativeItemsUiWait(150);
            try
            {
                using var legacy=ShowDataGridRegressionPreview(vm,false);using var axaml=ShowDataGridRegressionPreview(vm,true);
                foreach(var size in ConsistencySizes)check(scene+"_Geometry_"+size,()=>
                {
                    vm.DesignWidth=size.Width;vm.DesignHeight=size.Height;
                    foreach(var p in new[]{legacy.Window,axaml.Window}){p.Width=size.Width;p.Height=size.Height;}
                    NativeItemsUiWait(200);window.UpdateLayout();legacy.Window.UpdateLayout();axaml.Window.UpdateLayout();
                    var surface=window.GetVisualDescendants().OfType<DesignerSurface>().Single();
                    var designer=CaptureConsistency(vm,scene,"Designer",surface.Canvas,DropWrappers(window).ToDictionary(p=>p.Key,p=>(Control)p.Value));
                    var lhost=legacy.Window.FindControl<Border>("PreviewSurfaceBorder")!;var ahost=axaml.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                    var lmap=(Dictionary<string,Control>)typeof(PreviewWindow).GetField("_builtPreviewControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(legacy.Window)!;
                    var snapshot=(ExportedAxamlPreviewModel)typeof(PreviewWindow).GetField("_exportedAxamlPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(axaml.Window)!;
                    var amap=vm.Controls.ToDictionary(c=>c.Id,c=>ahost.GetVisualDescendants().OfType<Control>().Single(v=>v.Name==snapshot.StandardControlNames[c.Id]));
                    var l=CaptureConsistency(vm,scene,"Legacy",lhost,lmap);var a=CaptureConsistency(vm,scene,"AXAML",ahost,amap);
                    records.AddRange(designer);records.AddRange(l);records.AddRange(a);
                    RequireCapability(l.All(r=>r.Attached&&r.NativePresent)&&a.All(r=>r.Attached&&r.NativePresent),"A registry component is missing a native instance.");
                    foreach(var model in vm.Controls)
                    {
                        var left=l.Single(r=>r.Id==model.Id);var right=a.Single(r=>r.Id==model.Id);var d=designer.Single(r=>r.Id==model.Id);
                        RequireCapability(left.ParentId==model.ParentId&&right.ParentId==model.ParentId,"Native hierarchy lost ParentId for "+model.Name);
                        var delta=new[]{Math.Abs(left.X-right.X),Math.Abs(left.Y-right.Y),Math.Abs(left.Width-right.Width),Math.Abs(left.Height-right.Height),
                            Math.Abs(d.X-right.X),Math.Abs(d.Y-right.Y),Math.Abs(d.Width-right.Width),Math.Abs(d.Height-right.Height)}.Max();
                        RequireCapability(delta<=1.1,$"{model.Name} geometry differs by {delta:F2} DIP: Designer {d}; Legacy {left}; AXAML {right}");
                    }
                });
                var previewSnapshot=vm.GenerateExportedAxamlPreviewSnapshot();
                var activeClass=System.Xml.Linq.XDocument.Parse(previewSnapshot.ExportAxaml).Root!
                    .Attribute(System.Xml.Linq.XName.Get("Class","http://schemas.microsoft.com/winfx/2006/xaml"))!.Value.Split('.').Last();
                File.WriteAllText(Path.Combine(ConsistencyFolder,scene+"-manifest.json"),JsonSerializer.Serialize(vm.Controls.Select(c=>new{c.Id,c.Name,c.Type,c.ParentId,c.ChildLayoutMode,WindowClass=activeClass}),new JsonSerializerOptions{WriteIndented=true}));
                vm.DesignWidth=1200;vm.DesignHeight=800;
                var export=Path.Combine(ConsistencyFolder,"exports",scene);vm.ExportCurrentResultToProjectAsync(export).GetAwaiter().GetResult();
            }
            finally{window.CloseForExternalHost();}
        }
        File.WriteAllText(Path.Combine(ConsistencyFolder,"geometry.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
    }
}
