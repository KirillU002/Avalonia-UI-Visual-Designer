using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Text.Json;
using System.Collections;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static readonly string[] Phase5BTypes={"Viewbox","UniformGrid","Expander","ScrollViewer","Line","Polygon","Polyline","Path"};
    private static (string Key,string Value)[] Phase5BValues(string type)=>type switch
    {
        "Viewbox"=>new[]{("Stretch","Uniform"),("StretchDirection","Both")},
        "UniformGrid"=>new[]{("Rows","2"),("Columns","2"),("FirstColumn","1")},
        "Expander"=>new[]{("Header","Раздел & текст"),("IsExpanded","True"),("ExpandDirection","Down")},
        "ScrollViewer"=>new[]{("HorizontalScrollBarVisibility","Auto"),("VerticalScrollBarVisibility","Visible")},
        "Line"=>new[]{("StartPoint","10,20"),("EndPoint","120,70"),("Fill","#123456"),("Stroke","#123456"),("StrokeThickness","3"),("StrokeDashArray","3,2")},
        "Polygon"=>new[]{("Points","0,0 140,0 70,80"),("Fill","#123456"),("Stroke","#abcdef"),("StrokeThickness","3"),("Stretch","Fill")},
        "Polyline"=>new[]{("Points","0,0 80,70 140,20"),("Fill","#123456"),("Stroke","#123456"),("StrokeThickness","3"),("StrokeDashArray","3,2"),("Stretch","Fill")},
        _=>new[]{("Data","M 0,0 C 10,30 40,60 100,80 Z"),("Fill","#123456"),("Stroke","#abcdef"),("StrokeThickness","3"),("Stretch","Uniform")}
    };
    private static MainWindowViewModel Phase5BScene(string layout="Grid",string? only=null)
    {
        var vm=GridFirstVm();if(layout=="Canvas"){vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);}
        vm.DesignWidth=1000;vm.DesignHeight=900;vm.SurfaceGridRowDefinitions="*,*,*,*";vm.SurfaceLayoutRows=4;vm.SurfaceGridColumnDefinitions="*,*";vm.SurfaceLayoutColumns=2;
        var types=only is null?Phase5BTypes:new[]{only};
        for(var i=0;i<types.Length;i++){
            var c=vm.CreateControl(types[i],20+i%2*450,20+i/2*210,null,layout=="Canvas");c.Name="Phase5B_"+c.Type;c.Width=300;c.Height=160;c.WidthMode=c.HeightMode="Fixed";c.GridRow=i/2;c.GridColumn=i%2;
            foreach(var (key,value)in Phase5BValues(c.Type))SetUniversal2(c,key,value);
            if(!StandardControlProperties.IsContainer(c.Type))continue;
            var child=vm.CreateControl(c.Type is "Viewbox" or "ScrollViewer"?"StackPanel":"TextBox",0,0,c.Id);child.Name=c.Type+"Child";child.Width=c.Type=="ScrollViewer"?500:150;child.Height=c.Type=="ScrollViewer"?300:60;child.WidthMode=child.HeightMode="Fixed";child.Text="Native nested";
            if(child.Type=="StackPanel"){var leaf=vm.CreateControl("Button",0,0,child.Id);leaf.Name=c.Type+"NestedButton";leaf.Text="Nested button";leaf.Width=120;leaf.Height=35;leaf.WidthMode=leaf.HeightMode="Fixed";}
            if(c.Type=="UniformGrid")for(var n=1;n<3;n++){var more=vm.CreateControl("TextBox",0,0,c.Id);more.Name="UniformChild"+n;more.Width=90;more.Height=40;more.WidthMode=more.HeightMode="Fixed";more.StackOrder=n;}
        }
        return vm;
    }
    private static Control Native5B(Control root,DesignControlModel model,string? sourceType=null)=>root.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==model.Name&&c.GetType()==StandardControlProperties.SupportedTypes[sourceType??model.Type]);
    private static void RequireNative5B(Control view,string type)
    {
        RequireCapability(view.GetType()==StandardControlProperties.SupportedTypes[type]&&view.GetVisualRoot()is not null&&view.Bounds.Width>0&&view.Bounds.Height>0,"Missing attached native "+type+" "+view.Bounds);
        foreach(var (key,text)in Phase5BValues(type)){
            var p=StandardControlProperties.Find(type,key)!;RequireCapability(StandardControlProperties.TryParseValue(p,text,out var expected),"Invalid test literal "+key);var actual=view.GetValue(p.Property!);
            var equal=actual is ISolidColorBrush a&&expected is ISolidColorBrush b?a.Color==b.Color:actual is Geometry g&&expected is Geometry e?g.Bounds==e.Bounds:
                actual is IEnumerable values&&expected is IEnumerable other&&actual is not string?values.Cast<object>().SequenceEqual(other.Cast<object>()):Equals(actual,expected);
            RequireCapability(equal,"Native mismatch "+type+"."+key+" actual="+MetadataValueCodec.Display(actual));
        }
        if(StandardControlProperties.IsContainer(type)){
            var children=view is Viewbox v?(v.Child is null?0:1):view is UniformGrid u?u.Children.Count:((ContentControl)view).Content is Control?1:0;
            RequireCapability(children==(type=="UniformGrid"?3:1),"Container hierarchy lost "+type);
        }
    }
    private static int RunUniversal5BChecks()
    {
        var pass=0;var fail=0;
        void Check(string name,Action action){var filter=Environment.GetEnvironmentVariable("UNIVERSAL5B_FILTER");if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{action();pass++;Console.WriteLine("PASS UNIVERSAL5B_"+name);}catch(Exception e){fail++;Console.WriteLine("FAIL UNIVERSAL5B_"+name+": "+e);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}}
        foreach(var type in Phase5BTypes){
            Check(type+"_RegistrationInspector",()=>{
                var vm=Phase5BScene(only:type);var c=vm.Controls.Single(c=>c.Type==type);vm.SelectSingleControl(c);
                RequireCapability(vm.Registry.GetControls().Count(d=>d.TypeKey==type)==1,"Duplicate/missing registry entry.");
                vm.ToolboxSearchText=type;RequireCapability(vm.ToolboxGroups.SelectMany(g=>g.Items).Any(i=>i.Type==type&&i.Icon!="AV"),"Missing searchable Toolbox icon.");
                foreach(var (key,_)in Phase5BValues(type))RequireCapability(!GetPropertyGridRow(vm,key).IsReadOnly,"Missing typed editor "+key);
                if(StandardControlProperties.IsContainer(type))RequireCapability(!vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.Key=="ChildLayoutMode"),"Native container exposes fake layout switch.");
            });
            foreach(var layout in new[]{"Grid","Canvas"})Check(type+"_Native_"+layout,()=>{
                var vm=Phase5BScene(layout,type);var c=vm.Controls.Single(c=>c.Type==type);var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(120);w.UpdateLayout();RequireNative5B(Native5B(w,c),type);
                foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))RequireNative5B(Native5B(preview.Window,c),type);
                var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());RequireCapability(vm.Controls.Select(c=>(c.Id,c.Type,c.ParentId,c.StackOrder)).SequenceEqual(clone.Controls.Select(c=>(c.Id,c.Type,c.ParentId,c.StackOrder))),"JSON hierarchy/identity lost.");
                vm.GenerateXaml();RequireCapability(vm.GeneratedXaml.Contains("<"+type+" "),"Native AXAML missing.");
                if(StandardControlProperties.IsContainer(type))RequireCapability(!System.Xml.Linq.XDocument.Parse(vm.GeneratedXaml).Descendants().Single(e=>e.Name.LocalName==type).Descendants().Any(e=>e.Name.LocalName=="Canvas"),"Hidden Canvas inserted.");
            });
            Check(type+"_RoutedDropUndo",()=>{
                using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();var data=GridToolboxData(f,type);var p=GridCellPoint(f.Vm,"",0,0);RouteGridToolbox(f,data,DragDrop.DragOverEvent,p);RouteGridToolbox(f,data,DragDrop.DropEvent,p);f.Refresh();f.AssertIdle();var c=f.Vm.Controls.Single();var id=c.Id;
                RequireCapability(c.Type==type&&c.ParentId==""&&c.X==0&&c.Y==0&&f.Vm.SelectedControl?.Id==id,"Drop/selection/placement lost.");GridEditorUndo(f.Vm);GridEditorUndo(f.Vm,true);f.Refresh();RequireCapability(f.Vm.Controls.Single().Id==id,"Undo identity lost.");
            });
            Check(type+"_SourcePatchResetUndo",()=>{
                var (key,value)=Phase5BValues(type)[0];var initial=type switch{"Viewbox"=>"Fill","UniformGrid"=>"1","Expander"=>"Before","ScrollViewer"=>"Hidden","Line"=>"1,2","Polygon"or"Polyline"=>"0,0 20,0 10,10",_=>"M0,0 L10,10 Z"};
                var vm=Universal4AVm($"<a:{type} Name='Target' {key}='{initial}' u:Keep='yes'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;var c=vm.Controls.Single();vm.SelectSingleControl(c);vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),value);
                var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("u:Keep='yes'"),"Source patch not minimal.");GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo source changed.");GridEditorUndo(vm,true);vm.SelectSingleControl(vm.Controls.Single());vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));RequireCapability(!vm.CreateActiveAxamlPatch().PatchedText.Contains(key+"='"),"Reset failed.");GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Undo Reset failed.");
            });
        }
        Check("GeometryValidation",()=>{
            foreach(var type in new[]{"Polygon","Polyline","Path","Line"}){var (key,value)=Phase5BValues(type)[0];var p=StandardControlProperties.Find(type,key)!;
                RequireCapability(StandardControlProperties.TryEncode(p,value,out var json)&&StandardControlProperties.TryParseValue(p,StandardControlProperties.Read(p,json),out var native),"Literal not reversible: "+type);
                if(type!="Line")RequireCapability(StandardControlProperties.Read(p,json)==value,"Shape text lost: "+type);
                foreach(var bad in new[]{"NaN,0","Infinity,0","{Binding Unsafe}","1e999,0",new string('1',17000)})RequireCapability(!StandardControlProperties.TryEncode(p,bad,out _),"Unsafe geometry accepted.");}
            var dash=StandardControlProperties.Find("Line","StrokeDashArray")!;RequireCapability(!StandardControlProperties.TryEncode(dash,"-1,2",out _)&&StandardControlProperties.TryEncode(dash,"",out _),"Dash validation/default failed.");
            foreach(var bad in new[]{"", "M", "M 0", "M0,0 L", "M0,0 Q1,2", "M0,0 A10,10 0 0 0"})
                RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("Path","Data")!,bad,out _),"Incomplete path accepted: "+bad);
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("Polygon","Points")!,"1,2,3",out _),"Odd point coordinates accepted.");
            var culture=System.Globalization.CultureInfo.CurrentCulture;
            try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
                var point=StandardControlProperties.Find("Line","StartPoint")!;
                RequireCapability(StandardControlProperties.TryEncode(point,"1.25,2.75",out var encoded)&&StandardControlProperties.TryParseValue(point,StandardControlProperties.Read(point,encoded),out var decoded)&&Equals(decoded,new Point(1.25,2.75)),"Point decimals lost invariant round-trip.");
            }finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
            foreach(var key in new[]{"Rows","Columns","FirstColumn"}){var property=StandardControlProperties.Find("UniformGrid",key)!;RequireCapability(!StandardControlProperties.TryEncode(property,"-1",out _)&&StandardControlProperties.TryEncode(property,"0",out _),"UniformGrid count validation failed.");}
        });
        Check("ShapeFillPipeline",()=>{
            foreach(var type in new[]{"Line","Polyline"}){
                var vm=Phase5BScene(only:type);var model=vm.Controls.Single(c=>c.Type==type);vm.SelectSingleControl(model);
                // Establish the programmatically seeded scene as the history baseline,
                // as in the existing typed-editor regression harness.
                vm.EndPropertyGridTextEdit();vm.BeginUndoBatch();vm.CommitUndoBatch();var history=vm.ActiveSession.UndoSnapshots.Count;
                Color ExportFill(){vm.GenerateXaml();var literal=System.Xml.Linq.XDocument.Parse(vm.GeneratedXaml).Descendants().Single(e=>e.Name.LocalName==type).Attribute("Fill")?.Value;
                    RequireCapability(literal is not null,"Fill missing from export: "+type);return Color.Parse(literal!);}
                vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Fill"),"#654321");
                RequireCapability(vm.ActiveSession.UndoSnapshots.Count==history+1,"Fill edit was not atomic.");
                RequireCapability(ExportFill()==Color.Parse("#654321"),"Edited Fill export differs.");
                GridEditorUndo(vm);RequireCapability(ExportFill()==Color.Parse("#123456"),"Fill Undo lost original color.");
                GridEditorUndo(vm,true);RequireCapability(ExportFill()==Color.Parse("#654321"),"Fill Redo lost edited color.");
                foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))
                    RequireCapability(((Avalonia.Controls.Shapes.Shape)Native5B(preview.Window,model)).Fill is ISolidColorBrush brush&&brush.Color==Color.Parse("#654321"),"Editable Fill did not reach native "+type);
            }
        });
        Check("SingleChildCapacityAtomic",()=>{
            var vm=Phase5BScene(only:"Viewbox");var before=JsonSerializer.Serialize(vm.Controls);try{vm.CreateControl("TextBox",0,0,vm.Controls.Single(c=>c.Type=="Viewbox").Id);throw new Exception("Second Child accepted");}catch(InvalidOperationException){}
            RequireCapability(before==JsonSerializer.Serialize(vm.Controls),"Failed capacity check mutated controls.");
            var extra=vm.CreateControl("Rectangle",0,0);vm.ReparentControl(extra,vm.Controls.Single(c=>c.Type=="Viewbox").Id,0,0);RequireCapability(extra.ParentId=="","Reparent bypassed Child capacity.");
        });
        Check("CanvasOccupiedContentDrop",()=>{
            foreach(var type in new[]{"Viewbox","Expander","ScrollViewer"}){
                var vm=Phase5BScene("Canvas",type);var host=vm.Controls.Single(c=>c.Type==type);var before=JsonSerializer.Serialize(vm.Controls);
                var result=vm.TryCreateControlFromToolboxDrop("Polygon",host.X+10,host.Y+10,host.Id,true,vm.ActiveDocumentId);
                RequireCapability(result is null&&JsonSerializer.Serialize(vm.Controls)==before,"Occupied Canvas Content drop changed project: "+type);
            }
        });
        foreach(var layout in new[]{"Stack","Wrap","Dock"})Check("NestedParent_"+layout,()=>{
            var vm=Phase5BScene();var host=vm.CreateControl(layout=="Dock"?"Group":layout+"Panel",0,0);
            if(layout=="Dock")host.ChildLayoutMode="Dock";
            host.Width=950;host.Height=850;host.WidthMode=host.HeightMode="Fixed";
            foreach(var c in vm.Controls.Where(c=>Phase5BTypes.Contains(c.Type))){c.ParentId=host.Id;c.StackOrder=Array.IndexOf(Phase5BTypes,c.Type);}
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))
                foreach(var c in vm.Controls.Where(c=>Phase5BTypes.Contains(c.Type)))RequireNative5B(Native5B(preview.Window,c),c.Type);
            var copy=GridFirstVm();copy.LoadDocumentJson(vm.ExportWorkspaceJson());
            RequireCapability(copy.Controls.Select(c=>(c.Id,c.ParentId,c.StackOrder)).SequenceEqual(vm.Controls.Select(c=>(c.Id,c.ParentId,c.StackOrder))),"Nested parent JSON changed.");
        });
        Check("ExplicitStandardNamespaces",()=>{
            var text="<Window xmlns='https://github.com/avaloniaui' xmlns:s='using:Avalonia.Controls.Shapes' xmlns:p='clr-namespace:Avalonia.Controls.Primitives;assembly=Avalonia.Controls'><Grid><p:UniformGrid Name='U' Rows='1' Columns='2'><s:Path Name='P' Data='M0,0 L10,20 Z'/><s:Line StartPoint='0,0' EndPoint='10,10'/></p:UniformGrid></Grid></Window>";
            var imported=new AxamlImportService().Import(text);RequireCapability(imported.Document.Controls.Count==4,"Trusted namespace type resolution failed.");var vm=GridFirstVm();vm.LoadAxamlImportedDocument(imported,"namespace.axaml");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==text,"Namespace no-op changed source.");
        });
        Check("ObjectGeometryOpaque",()=>{
            var source="<Window xmlns='https://github.com/avaloniaui'><Canvas><!--keep--><Path Name='P'><Path.Data><GeometryGroup><EllipseGeometry Center='10,10' RadiusX='5' RadiusY='5'/></GeometryGroup></Path.Data></Path></Canvas></Window>";
            var vm=GridFirstVm();vm.LoadAxamlImportedDocument(new AxamlImportService().Import(source),"complex.axaml");vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,"Data").IsReadOnly&&vm.CreateActiveAxamlPatch().PatchedText==source,"Object Geometry exposed/destructively rewritten.");
        });
        Check("ImportedExpanderLiteralContent",()=>{
            var vm=Universal4AVm("<a:Expander Name='Literal' Header='Header' Content='Original literal' IsExpanded='True'/>");var source=vm.ActiveAxamlSourceDocument!.OriginalText;
            using var preview=ShowDataGridRegressionPreview(vm,true);var native=preview.Window.GetVisualDescendants().OfType<Expander>().Single();RequireCapability(Equals(native.Content,"Original literal")&&Equals(native.Header,"Header"),"Existing source literal Content/Header lost.");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"Source literal content rewritten.");
        });
        Check("ImportedNativeAll",()=>{
            var authored=Phase5BScene();authored.GenerateXaml();var vm=GridFirstVm();
            vm.LoadAxamlImportedDocument(new AxamlImportService().Import(authored.GeneratedXaml),"phase5b-import.axaml");
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))
                foreach(var type in Phase5BTypes){var model=vm.Controls.Single(c=>c.Name=="Phase5B_"+type);RequireNative5B(Native5B(preview.Window,model,type),type);}
            RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==authored.GeneratedXaml,"Native source preview mutated AXAML.");
        });
        Check("EmptyViewboxScaffoldNotExported",()=>{
            var vm=GridFirstVm();vm.CreateControl("Viewbox",0,0);vm.GenerateXaml();var viewbox=System.Xml.Linq.XDocument.Parse(vm.GeneratedXaml).Descendants().Single(e=>e.Name.LocalName=="Viewbox");RequireCapability(!viewbox.Elements().Any()&&vm.Controls.Count==1,"Editor scaffold entered user model/export.");
        });
        Check("PointsUndoJsonMultiForm",()=>{
            var vm=Phase5BScene(only:"Polygon");var first=vm.ActiveDocumentId;var polygon=vm.Controls.Single();vm.SelectSingleControl(polygon);var previous=polygon.CustomProperties.Single(p=>p.Key=="Points").ValueJson;
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Points"),"0,0 60,0 90,20 30,80");var changed=polygon.CustomProperties.Single(p=>p.Key=="Points").ValueJson;GridEditorUndo(vm);RequireCapability(vm.Controls.Single().CustomProperties.Single(p=>p.Key=="Points").ValueJson==previous,"Points Undo lost value");GridEditorUndo(vm,true);
            vm.CreateNewForm();var second=vm.ActiveDocumentId;vm.CreateControl("Path",0,0);vm.SetActiveForm(first,"5b");RequireCapability(vm.Controls.Single().Id==polygon.Id,"Multi Form mixed controls");var copy=GridFirstVm();copy.LoadDocumentJson(vm.ExportWorkspaceJson());copy.SetActiveForm(first,"5b");RequireCapability(copy.Controls.Single().CustomProperties.Single(p=>p.Key=="Points").ValueJson==changed,"JSON points literal lost");copy.SetActiveForm(second,"5b");RequireCapability(copy.Controls.Single().Type=="Path","Second form lost");
        });
        Check("ViewboxScaledSelectionZoom",()=>{
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);var host=f.Vm.CreateControl("Viewbox",0,0);host.Width=300;host.Height=160;host.WidthMode=host.HeightMode="Fixed";SetUniversal2(host,"Stretch","Fill");
            var child=f.Vm.CreateControl("Line",0,0,host.Id);child.Width=100;child.Height=80;child.WidthMode=child.HeightMode="Fixed";f.Vm.EditorZoom=1.5;f.Refresh();var wrapper=DropWrappers(f.Window)[child.Id];var p=wrapper.TranslatePoint(new Point(30,30),f.Window)!.Value;f.Click(p);f.NoMarquee();f.AssertIdle();RequireCapability(f.Vm.SelectedControl?.Id==child.Id,"Scaled child pointer selection failed");
            var bounds=(Rect)typeof(FormDesigner.Views.MainWindow).GetMethod("GetAbsoluteBounds",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(f.Window,new object[]{child})!;
            RequireCapability(Math.Abs(bounds.Width-300)<1&&Math.Abs(bounds.Height-160)<1,"Scaled bounds use logical dimensions "+bounds);
        });
        foreach(var type in new[]{"Viewbox","UniformGrid","Expander","ScrollViewer"})Check(type+"_NestedRoutedDrop",()=>{
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);var host=f.Vm.CreateControl(type,0,0);host.Width=400;host.Height=300;host.WidthMode=host.HeightMode="Fixed";f.Refresh();
            RequireCapability(f.Vm.NativeContainerMeasurements.TryGetValue(host.Id,out var container)&&container.Bounds.Width>0&&container.Bounds.Height>0,"Empty native host cannot receive Drop.");var p=container!.Bounds.Center;
            var target=f.Vm.ResolveLayoutDropTarget(p);RequireCapability(target.IsValid&&target.ParentId==host.Id,"Native host not resolved: "+target.Reason);
            var data=GridToolboxData(f,"Polygon");RouteGridToolbox(f,data,DragDrop.DragOverEvent,p);RouteGridToolbox(f,data,DragDrop.DropEvent,p);f.Refresh();f.AssertIdle();var child=f.Vm.Controls.Single(c=>c.Type=="Polygon");RequireCapability(child.ParentId==host.Id&&child.X==0&&child.Y==0&&f.Vm.SelectedControl?.Id==child.Id,"Nested placement/selection lost.");
            if(type=="UniformGrid")RequireCapability(!f.Vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.Key is "GridRow" or "GridColumn"),"UniformGrid child exposes fake Grid placement.");
            GridEditorUndo(f.Vm);GridEditorUndo(f.Vm,true);f.Refresh();RequireCapability(f.Vm.GetControl(child.Id)?.ParentId==host.Id,"Nested drop Undo/Redo lost ParentId.");
        });
        foreach(var type in new[]{"Line","Polygon","Polyline","Path"})Check(type+"_TransparentPointer",()=>{
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);var c=f.Vm.CreateControl(type,0,0);c.Width=160;c.Height=90;c.WidthMode=c.HeightMode="Fixed";c.Opacity=0;f.Refresh();f.Vm.SelectSingleControl(null);f.Refresh();
            var wrapper=DropWrappers(f.Window)[c.Id];var point=wrapper.TranslatePoint(new Point(50,30),f.Window)!.Value;f.Click(point);f.NoMarquee();f.AssertIdle();RequireCapability(f.Vm.SelectedControl?.Id==c.Id,"Transparent shape cannot be selected.");
        });
        foreach(var type in new[]{"Line","Polygon","Polyline","Path"})Check(type+"_RoutedResize",()=>{
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);var c=f.Vm.CreateControl(type,0,0);c.Width=160;c.Height=90;c.WidthMode=c.HeightMode="Fixed";f.Vm.SelectSingleControl(c);f.Refresh();var p=f.ResizePoint();var size=new Size(c.Width,c.Height);f.Down(p);f.Move(p+new Vector(25,15));f.Up(p+new Vector(25,15));f.Refresh();f.AssertIdle();RequireCapability(c.Width>size.Width&&c.Height>size.Height,"Native routed resize failed: "+type);
        });
        foreach(var mode in new[]{false,true})Check("ExpanderNativeToggle_"+mode,()=>{
            var vm=Phase5BScene(only:"Expander");using var preview=ShowDataGridRegressionPreview(vm,mode);var c=vm.Controls.Single(c=>c.Type=="Expander");var native=(Expander)Native5B(preview.Window,c);native.IsExpanded=false;NativeItemsUiWait(80);preview.Window.UpdateLayout();RequireCapability(!native.IsExpanded&&native.Content is Control,"Collapse destroyed content.");native.IsExpanded=true;NativeItemsUiWait(120);preview.Window.UpdateLayout();RequireCapability(((Control)native.Content!).Bounds.Height>0,"Expand lost content.");
        });
        foreach(var layout in new[]{"Grid","Canvas"})Check("Geometry_"+layout,()=>{
            var vm=Phase5BScene(layout);var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(100);w.UpdateLayout();
            var canvas=w.GetVisualDescendants().OfType<FormDesigner.Views.DesignerSurface>().Single().Canvas;
            using var axaml=ShowDataGridRegressionPreview(vm,true);using var legacy=ShowDataGridRegressionPreview(vm,false);
            foreach(var scope in new[]{axaml,legacy}){scope.Window.SystemDecorations=SystemDecorations.None;scope.Window.Width=1000;scope.Window.Height=900;NativeItemsUiWait(80);scope.Window.UpdateLayout();}
            Rect Frame(Control c,Visual root)=>new Rect(c.Bounds.Size).TransformToAABB(c.TransformToVisual(root)!.Value);
            foreach(var model in vm.Controls.Where(c=>Phase5BTypes.Contains(c.Type)||c.Type is "Button" or "TextBox")){
                Control Native(Control root)=>Phase5BTypes.Contains(model.Type)?Native5B(root,model):ReferenceEquals(root,w)?DropWrappers(w)[model.Id].GetVisualDescendants().OfType<Control>().First(c=>c.GetType().Name==model.Type):root.GetVisualDescendants().OfType<Control>().Single(c=>c.Name==model.Name&&c.GetType().Name==model.Type);
                var actual=Frame(Native(w),canvas);var a=Frame(Native(axaml.Window),axaml.Window.FindControl<Border>("PreviewSurfaceBorder")!);var b=Frame(Native(legacy.Window),legacy.Window.FindControl<Border>("PreviewSurfaceBorder")!);
                bool Near(Rect x,Rect y)=>Math.Abs(x.X-y.X)<=2&&Math.Abs(x.Y-y.Y)<=2&&Math.Abs(x.Width-y.Width)<=2&&Math.Abs(x.Height-y.Height)<=2;
                RequireCapability(Near(actual,a)&&Near(a,b),$"Geometry {layout}/{model.Name}: Designer={actual}, AXAML={a}, Legacy={b}");
            }
        });
        Check("BindingResourcePreservation",()=>{
            foreach(var type in Phase5BTypes){var key=Phase5BValues(type)[0].Key;var vm=Universal4AVm($"<a:{type} Name='Bound' {key}='{{Binding Safe}}' Opacity='{{DynamicResource Alpha}}' u:Keep='yes'/>");var source=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,key).IsReadOnly&&vm.CreateActiveAxamlPatch().PatchedText==source,"Binding/resource overwritten.");}
        });
        foreach(var mode in new[]{false,true})Check("MemoryTwentyCycles_"+mode,()=>{var vm=Phase5BScene();var refs=Enumerable.Range(0,20).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);GC.KeepAlive(vm);});
        Console.WriteLine($"UNIVERSAL5B_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    private static int GenerateUniversal5BProjects(){var folder=Environment.GetEnvironmentVariable("UNIVERSAL5B_EXPORT_DIR")!;RequireCapability(!Directory.Exists(folder),"Fresh output required.");foreach(var layout in new[]{"Grid","Canvas"})Pump(Phase5BScene(layout).ExportCurrentResultToProjectAsync(System.IO.Path.Combine(folder,layout)));Console.WriteLine("GENERATED5B "+folder);return 0;}
    private static int RunUniversal5BAudit()
    {
        var vm=GridFirstVm();var folder=Environment.GetEnvironmentVariable("UNIVERSAL5B_OUTPUT_DIR")!;
        MetadataCoverageReport.Write(vm.Registry,folder,Environment.GetEnvironmentVariable("UNIVERSAL5B_RUNTIME_VERIFIED")=="1"?Phase5ATypes.Concat(Phase5BTypes).ToHashSet():null);
        var rows=new List<string>{"Control,Property,ReadOnly,Editor,ValueSource"};
        foreach(var type in Phase5BTypes){var scene=GridFirstVm();try{scene.SelectSingleControl(scene.CreateControl(type,0,0));
            foreach(var row in scene.PropertyGridCategories.SelectMany(c=>c.Rows))rows.Add(string.Join(",",new[]{type,row.Key,row.IsReadOnly.ToString(),row.Editor.ToString(),row.ValueSource.ToString()}.Select(v=>"\""+v.Replace("\"","\"\"")+"\"")));}
            finally{Dispose5AProfileVm(scene);}}
        File.WriteAllLines(System.IO.Path.Combine(folder,"Phase5BPropertyGrid.csv"),rows);Dispose5AProfileVm(vm);return 0;
    }
}
