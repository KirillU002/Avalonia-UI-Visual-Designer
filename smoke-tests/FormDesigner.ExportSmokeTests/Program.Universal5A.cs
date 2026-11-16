using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static readonly string[] Phase5ATypes={"ScrollBar","GridSplitter","Rectangle","Ellipse","HyperlinkButton","RepeatButton"};
    private static int GenerateUniversal5AProjects()
    {
        var folder=Environment.GetEnvironmentVariable("UNIVERSAL5A_EXPORT_DIR")!;
        RequireCapability(!System.IO.Directory.Exists(folder),"Fresh export directory required.");
        foreach(var layout in new[]{"Grid","Canvas"})Pump(Phase5AScene(layout).ExportCurrentResultToProjectAsync(System.IO.Path.Combine(folder,layout)));
        Console.WriteLine("GENERATED5A "+folder);return 0;
    }
    private static MainWindowViewModel Phase5AScene(string layout="Grid")
    {
        var vm=GridFirstVm();
        if(layout=="Canvas"){vm.NewFormLayoutChoice=MainWindowViewModel.NewFormCanvasChoice;vm.NewProjectEditorCommand!.Execute(null);}
        vm.DesignWidth=1000;vm.DesignHeight=700;vm.SurfaceGridRowDefinitions="*,*,*";vm.SurfaceLayoutRows=3;vm.SurfaceGridColumnDefinitions="*,*";vm.SurfaceLayoutColumns=2;
        for(var i=0;i<Phase5ATypes.Length;i++)
        {
            var c=vm.CreateControl(Phase5ATypes[i],20+i%2*450,20+i/2*200,null,layout=="Canvas");
            c.Name="Phase5A_"+c.Type;c.GridRow=i/2;c.GridColumn=i%2;c.Width=c.Type=="GridSplitter"?8:220;c.Height=c.Type=="ScrollBar"?25:90;
            c.WidthMode=c.HeightMode="Fixed";c.Text="Phase 5A";
            foreach(var (key,value)in Phase5AValues(c.Type))SetUniversal2(c,key,value);
        }
        return vm;
    }
    private static (string Key,string Value)[] Phase5AValues(string type)=>type switch
    {
        "ScrollBar"=>new[]{("Minimum","0"),("Maximum","100"),("Value","25"),("ViewportSize","10"),("Orientation","Horizontal")},
        "GridSplitter"=>new[]{("ResizeDirection","Columns"),("ResizeBehavior","PreviousAndNext"),("KeyboardIncrement","8")},
        "Rectangle"=>new[]{("Fill","#123456"),("Stroke","#abcdef"),("StrokeThickness","3"),("RadiusX","12"),("RadiusY","7")},
        "Ellipse"=>new[]{("Fill","#123456"),("Stroke","#abcdef"),("StrokeThickness","3")},
        "HyperlinkButton"=>new[]{("NavigateUri","https://example.invalid/docs?a=1&b=2"),("IsVisited","True")},
        _=>new[]{("Delay","200"),("Interval","60")}
    };
    private static void Require5ANative(Control view,string type)
    {
        RequireCapability(view.GetType()==StandardControlProperties.SupportedTypes[type]&&view.GetVisualRoot()is not null&&view.Bounds.Width>0&&view.Bounds.Height>0,"Missing native control "+type);
        foreach(var (key,expected)in Phase5AValues(type))
        {
            var property=StandardControlProperties.Find(type,key)!;var actual=view.GetValue(property.Property!);
            RequireCapability(StandardControlProperties.TryParseValue(property,expected,out var value)&& (actual is ISolidColorBrush brush&&value is ISolidColorBrush color?brush.Color==color.Color:Equals(actual,value)),"Native value mismatch "+type+"."+key);
        }
    }
    private static int RunUniversal5AChecks()
    {
        var pass=0;var fail=0;
        void Check(string name,Action action)
        {
            var filter=Environment.GetEnvironmentVariable("UNIVERSAL5A_FILTER");if(!string.IsNullOrEmpty(filter)&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{action();pass++;Console.WriteLine("PASS UNIVERSAL5A_"+name);}catch(Exception e){fail++;Console.WriteLine("FAIL UNIVERSAL5A_"+name+": "+e);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}
        }
        foreach(var type in Phase5ATypes)
        {
            Check(type+"_RegistrationAndEditors",()=>{
                var vm=Phase5AScene();var c=vm.Controls.Single(c=>c.Type==type);vm.SelectSingleControl(c);
                RequireCapability(vm.Registry.GetControls().Count(d=>d.TypeKey==type)==1,"Duplicate/missing descriptor.");
                foreach(var (key,_)in Phase5AValues(type))RequireCapability(!GetPropertyGridRow(vm,key).IsReadOnly,"Missing safe property editor: "+key);
                if(StandardControlProperties.IsShape(type))RequireCapability(!vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.Key is "Background" or "Foreground" or "FontFamily" or "FontSize"),"Fictitious Shape property exposed.");
                vm.ToolboxSearchText=type;RequireCapability(vm.ToolboxGroups.SelectMany(g=>g.Items).Any(i=>i.Type==type&&i.Icon!="AV"),"Search/icon missing.");
                RequireCapability(vm.ToolboxGroups.Any(g=>g.Title==vm.Registry.GetControls().Single(d=>d.TypeKey==type).Category&&g.Items.Any(i=>i.Type==type)),"Incorrect Toolbox category.");
            });
            foreach(var layout in new[]{"Grid","Canvas"})Check(type+"_Vertical_"+layout,()=>{
                var vm=Phase5AScene(layout);var model=vm.Controls.Single(c=>c.Type==type);
                var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);window.UpdateLayout();
                Require5ANative(Universal2Native(window,model),type);
                foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))Require5ANative(Universal2Native(preview.Window,model),type);
                var copy=GridFirstVm();copy.LoadDocumentJson(vm.ExportWorkspaceJson());var restored=copy.GetControl(model.Id)!;
                if(type=="GridSplitter")RequireCapability(model.Width==8&&restored.Width==8&&Universal2Native(window,model).Width==8,"Thin size clamped by model/Designer/JSON.");
                RequireCapability(restored.Type==type&&restored.ParentId==model.ParentId&&restored.GridRow==model.GridRow&&restored.CustomProperties.All(p=>model.CustomProperties.Any(q=>q.Key==p.Key&&q.ValueJson==p.ValueJson)),"JSON restore lost state.");
                vm.GenerateXaml();RequireCapability(vm.GeneratedXaml.Contains("<"+type)&&!vm.GeneratedXaml.Contains("Placeholder"),"Exporter used a placeholder.");
                if(type is "Rectangle" or "Ellipse")RequireCapability(!System.Xml.Linq.XDocument.Parse(vm.GeneratedXaml).Descendants().Where(e=>e.Name.LocalName==type).SelectMany(e=>e.Attributes()).Any(a=>a.Name.LocalName is "Background" or "Foreground" or "FontSize"),"Invalid Shape AXAML attributes.");
            });
            Check(type+"_RoutedDropUndoSelection",()=>{
                using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();var data=GridToolboxData(f,type);var point=GridCellPoint(f.Vm,"",0,0);
                RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);f.Refresh();f.AssertIdle();var model=f.Vm.Controls.Single();var id=model.Id;
                RequireCapability(model.Type==type&&model.ParentId==""&&model.GridRow==0&&model.GridColumn==0&&f.Vm.SelectedControl?.Id==id,"Drop/selection identity lost.");
                GridEditorUndo(f.Vm);GridEditorUndo(f.Vm,true);f.Refresh();RequireCapability(f.Vm.Controls.Single().Id==id,"Undo/Redo changed ControlId.");
            });
            Check(type+"_SourceRoundTrip",()=>{
                var (key,value)=Phase5AValues(type).First();var initial=type switch{"ScrollBar"=>"1","GridSplitter"=>"Rows","Rectangle"or"Ellipse"=>"#654321","HyperlinkButton"=>"https://example.invalid/old",_=>"300"};
                var vm=Universal4AVm($"<a:Grid><!--keep--><a:{type} Name='Target' {key}='{initial}' u:Extra='keep'/></a:Grid>");var source=vm.ActiveAxamlSourceDocument!.OriginalText;
                vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target"));vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),value);
                var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("<!--keep-->")&&patch.PatchedText.Contains("u:Extra='keep'"),"Minimal patch lost source.");
                vm.UndoEditorCommand!.Execute(null);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"Undo lost exact source.");vm.RedoEditorCommand!.Execute(null);
                vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target"));vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));var reset=vm.CreateActiveAxamlPatch();RequireCapability(reset.CanApply&&!reset.PatchedText.Contains(key+"='"),"Reset failed.");
                vm.UndoEditorCommand!.Execute(null);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Undo Reset failed.");
            });
        }
        Check("UriAndNaNSafety",()=>{
            RequireCapability(!MetadataValueCodec.TryParse(typeof(Uri),"file:///c:/secret",out _)&&!MetadataValueCodec.TryParse(typeof(Uri),"javascript:alert(1)",out _)&&!MetadataValueCodec.TryParse(typeof(double),"NaN",out _),"Unsafe generic conversion enabled.");
            var p=StandardControlProperties.Find("ScrollBar","ViewportSize")!;RequireCapability(StandardControlProperties.TryParseValue(p,"NaN",out var v)&&v is double d&&double.IsNaN(d),"Native ScrollBar default unsupported.");
        });
        Check("InstalledPackageDiscovery",()=>{
            var types=AvaloniaMetadataCatalog.DiscoverControls();
            RequireCapability(types.Contains(typeof(ColorPicker))&&types.Contains(typeof(ItemsRepeater)),"Referenced standard packages omitted from coverage.");
            RequireCapability(AvaloniaMetadataCatalog.Get(typeof(Viewbox)).ChildProperty=="Child","Viewbox child semantics omitted from audit.");
        });
        Check("RepeatTimerValidation",()=>{
            var vm=Phase5AScene();var model=vm.Controls.Single(c=>c.Type=="RepeatButton");vm.SelectSingleControl(model);
            foreach(var key in new[]{"Delay","Interval"}){
                var before=model.CustomProperties.Single(p=>p.Key==key).ValueJson;
                var row=GetPropertyGridRow(vm,key);vm.CommitPropertyGridTextEdit(row,"-10");
                RequireCapability(row.HasValidationError&&model.CustomProperties.Single(p=>p.Key==key).ValueJson==before,"Negative RepeatButton timer accepted: "+key);
                RequireCapability(StandardControlProperties.TryEncode(StandardControlProperties.Find("RepeatButton",key)!,"0",out _),"Legal zero timer rejected.");
            }
        });
        Check("DesignerCompositeOpacity",()=>{
            var vm=Phase5AScene();var model=vm.Controls.Single(c=>c.Type=="Ellipse");model.Opacity=.6;
            var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);w.UpdateLayout();
            var native=Universal2Native(w,model);var opacity=native.GetVisualAncestors().OfType<Control>().Prepend(native).TakeWhile(c=>c!=w).Aggregate(1d,(value,c)=>value*c.Opacity);
            RequireCapability(Math.Abs(opacity-.6)<.001,"Effective Designer opacity="+opacity+" instead of .6");
        });
        foreach(var layout in new[]{"Grid","Canvas"})Check("Geometry_"+layout,()=>{
            var vm=Phase5AScene(layout);var owner=CreateSmokeWindow(vm);owner.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(60);owner.UpdateLayout();
            var canvas=owner.GetVisualDescendants().OfType<FormDesigner.Views.DesignerSurface>().Single().Canvas;
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode)){
                preview.Window.SystemDecorations=SystemDecorations.None;preview.Window.Width=1000;preview.Window.Height=700;NativeItemsUiWait(60);preview.Window.UpdateLayout();
                var root=preview.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                RequireCapability(Math.Abs(root.Bounds.Width-canvas.Bounds.Width)<=1&&Math.Abs(root.Bounds.Height-canvas.Bounds.Height)<=1,"Geometry comparison viewport differs.");
                foreach(var model in vm.Controls){var a=Universal2Native(owner,model);var b=Universal2Native(preview.Window,model);
                    var p=a.TranslatePoint(default,canvas)!.Value;var q=b.TranslatePoint(default,root)!.Value;
                    RequireCapability(Math.Abs(p.X-q.X)<=1&&Math.Abs(p.Y-q.Y)<=1&&Math.Abs(a.Bounds.Width-b.Bounds.Width)<=1&&Math.Abs(a.Bounds.Height-b.Bounds.Height)<=1,
                        $"Geometry {layout}/{mode}/{model.Type}: Designer={p}/{a.Bounds}; Preview={q}/{b.Bounds}");
                }
            }
        });
        foreach(var mode in new[]{false,true})Check("GridSplitterKeyboard_"+mode,()=>{
            var vm=Phase5AScene();using var preview=ShowDataGridRegressionPreview(vm,mode);var splitter=(GridSplitter)Universal2Native(preview.Window,vm.Controls.Single(c=>c.Type=="GridSplitter"));
            RequireCapability(splitter.GetVisualParent()is Grid,"Splitter lost Grid parent.");var grid=(Grid)splitter.GetVisualParent()!;
            grid.ColumnDefinitions=ColumnDefinitions.Parse("*,8,*");Grid.SetColumn(splitter,1);preview.Window.UpdateLayout();
            var before=grid.ColumnDefinitions[0].ActualWidth;var next=grid.ColumnDefinitions[2].ActualWidth;
            splitter.RaiseEvent(new Avalonia.Input.KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right});preview.Window.UpdateLayout();
            Console.WriteLine($"SPLITTER5A mode={mode}; first={before}->{grid.ColumnDefinitions[0].Width}; second={grid.ColumnDefinitions[2].Width}; native={splitter.Bounds}; logicalParent={splitter.Parent?.GetType().Name}; col={Grid.GetColumn(splitter)}; span={Grid.GetColumnSpan(splitter)}");
            RequireCapability(grid.ColumnDefinitions[0].ActualWidth>before&&grid.ColumnDefinitions[2].ActualWidth<next,"Native GridSplitter did not resize adjacent star tracks.");
        });
        foreach(var layout in new[]{"Stack","Dock","Flex","Nested"})Check("Hierarchy_"+layout,()=>{
            var vm=Phase5AScene();var parent=vm.CreateControl(layout is "Stack"or"Nested"?"StackPanel":layout=="Flex"?"WrapPanel":"Group",0,0);
            if(layout=="Dock")parent.ChildLayoutMode="Dock";parent.Width=950;parent.Height=650;parent.WidthMode=parent.HeightMode="Fixed";
            foreach(var c in vm.Controls.Where(c=>Phase5ATypes.Contains(c.Type))){c.ParentId=parent.Id;c.StackOrder=Array.IndexOf(Phase5ATypes,c.Type);}
            if(layout=="Nested"){var grid=vm.CreateControl("Grid",0,0);parent.ParentId=grid.Id;}
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))foreach(var model in vm.Controls.Where(c=>Phase5ATypes.Contains(c.Type)))Require5ANative(Universal2Native(preview.Window,model),model.Type);
            var copy=GridFirstVm();copy.LoadDocumentJson(vm.ExportWorkspaceJson());RequireCapability(vm.Controls.Select(c=>(c.Id,c.ParentId,c.StackOrder)).SequenceEqual(copy.Controls.Select(c=>(c.Id,c.ParentId,c.StackOrder))),"Hierarchy JSON changed.");
        });
        Check("SourceBindingResourcesAndReadonlyCollections",()=>{
            foreach(var type in Phase5ATypes){var key=Phase5AValues(type)[0].Key;var vm=Universal4AVm($"<a:{type} Name='Target' {key}='{{Binding Dangerous}}' Opacity='{{DynamicResource Alpha}}' u:Unknown='keep'/>");
                var source=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());RequireCapability(GetPropertyGridRow(vm,key).IsReadOnly,"Bound property editable.");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"Expression/source changed.");}
            var shapes=Phase5AScene();shapes.SelectSingleControl(shapes.Controls.Single(c=>c.Type=="Rectangle"));
            RequireCapability(!GetPropertyGridRow(shapes,"StrokeDashArray").IsReadOnly && !StandardControlProperties.TryEncode(StandardControlProperties.Find("Rectangle","StrokeDashArray")!,"{Binding Dashes}",out _),"Reviewed dash codec lost expression protection.");
        });
        Check("MultiFormAndPropertyUndo",()=>{
            var vm=Phase5AScene();var first=vm.ActiveFormDocument!.Id;var model=vm.Controls.Single(c=>c.Type=="Rectangle");vm.SelectSingleControl(model);
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"RadiusX"),"24");var id=model.Id;GridEditorUndo(vm);GridEditorUndo(vm,true);
            RequireCapability(vm.GetControl(id)!.CustomProperties.Any(p=>p.Key=="RadiusX"&&p.ValueJson=="24"),"Property Undo/Redo failed.");
            vm.CreateNewForm();var second=vm.ActiveFormDocument!.Id;var ellipse=vm.CreateControl("Ellipse",0,0);SetUniversal2(ellipse,"Fill","#fedcba");vm.SetActiveForm(first,"phase5a");
            RequireCapability(vm.Controls.Any(c=>c.Id==id)&&!vm.Controls.Any(c=>c.Id==ellipse.Id),"Form states mixed.");vm.SetActiveForm(second,"phase5a");RequireCapability(vm.Controls.Single().Id==ellipse.Id,"Second form lost.");
        });
        foreach(var mode in new[]{false,true})Check("TwentyPreviewCycles_"+mode,()=>{
            var vm=Phase5AScene();var refs=Enumerable.Range(0,20).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);GC.KeepAlive(vm);
        });
        Check("RemovedNativeControlsReleased",()=>{
            var vm=Phase5AScene();var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);w.UpdateLayout();
            var references=Remove5ANativeControls(vm,w);NativeItemsUiWait(200);ForceFullGc();
            RequireCapability(references.All(r=>!r.IsAlive),"Removed control retained by metadata/rendering cache.");GC.KeepAlive(w);GC.KeepAlive(vm);
        });
        Console.WriteLine($"UNIVERSAL5A_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] Remove5ANativeControls(MainWindowViewModel vm,FormDesigner.Views.MainWindow window)
    {
        var references=vm.Controls.Select(c=>new WeakReference(Universal2Native(window,c))).ToArray();vm.Controls.Clear();
        typeof(FormDesigner.Views.MainWindow).GetMethod("RenderDesigner",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,null);window.UpdateLayout();return references;
    }
    private static int RunUniversal5AAudit()
    {
        var folder=Environment.GetEnvironmentVariable("UNIVERSAL5A_OUTPUT_DIR")??System.IO.Path.Combine(FindRepositoryRoot(),"artifacts","universal-phase5a","audit");
        var vm=GridFirstVm();MetadataCoverageReport.Write(vm.Registry,folder,Environment.GetEnvironmentVariable("UNIVERSAL5A_RUNTIME_VERIFIED")=="1"?Phase5ATypes.ToHashSet():null);
        var types=AvaloniaMetadataCatalog.DiscoverControls();
        var rows=new List<string>{"Control,Property,NativeProperty,ReadOnly,Editor,ValueSource"};
        foreach(var name in Phase5ATypes){var scene=GridFirstVm();scene.SelectSingleControl(scene.CreateControl(name,0,0));
            foreach(var row in scene.PropertyGridCategories.SelectMany(c=>c.Rows))rows.Add(string.Join(",",new[]{name,row.Key,
                StandardControlProperties.HasNativeProperty(name,row.Key).ToString(),row.IsReadOnly.ToString(),row.Editor.ToString(),row.ValueSource.ToString()}.Select(v=>"\""+v.Replace("\"","\"\"")+"\"")));}
        System.IO.File.WriteAllLines(System.IO.Path.Combine(folder,"Phase5APropertyGrid.csv"),rows);
        var priorities=new[]{"Viewbox","UniformGrid","GridSplitter","ScrollViewer","ScrollBar","HyperlinkButton","CheckBox","Expander","GroupBox","HeaderedContentControl","Rectangle","Ellipse","Line","Path","Polygon","Polyline","Menu","MenuItem","TreeView","TreeViewItem","TabControl","TabItem","ItemsControl","RepeatButton"};
        var output=types.Select(type=>new{Type=type.FullName,Assembly=type.Assembly.GetName().Name,BaseType=type.BaseType?.FullName,
            type.IsAbstract,Registered=vm.Registry.GetControls().Any(d=>d.TypeKey==type.Name),Source=AxamlControlMetadata.Find(type.Name)is not null,
            Candidate=AvaloniaMetadataCatalog.Get(type).ToolboxCandidate,CandidateReason=AvaloniaMetadataCatalog.Get(type).CandidateReason,
            Properties=AvaloniaMetadataCatalog.Get(type).Properties.Select(p=>new{p.Key,p.Name,Owner=p.OwnerType.FullName,Type=p.PropertyType.FullName,
                p.Kind,p.DefaultText,p.ReadOnly,p.Nullable,p.Attached,p.Inherited,Editor=p.Editor.ToString()}).ToArray()}).ToArray();
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"discovery.json"),JsonSerializer.Serialize(new{Avalonia=typeof(Control).Assembly.GetName().Version?.ToString(),Types=output,
            MissingPriorities=priorities.Where(name=>!types.Any(t=>t.Name==name)).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        var keys=new[]{"Minimum","Maximum","Value","Orientation","ViewportSize","SmallChange","LargeChange","ResizeDirection","ResizeBehavior","ShowsPreview","DragIncrement","KeyboardIncrement","Fill","Stroke","StrokeThickness","StrokeDashArray","RadiusX","RadiusY","Stretch","IsVisited","NavigateUri","Delay","Interval","Content"};
        foreach(var name in priorities)
        {
            var type=types.FirstOrDefault(t=>t.Name==name);if(type is null){Console.WriteLine("AUDIT5A ABSENT "+name);continue;}
            Console.WriteLine($"AUDIT5A {name}; base={type.BaseType?.Name}; registered={vm.Registry.GetControls().Any(d=>d.TypeKey==name)}; properties="+
                string.Join("; ",AvaloniaMetadataCatalog.Get(type).Properties.Where(p=>keys.Contains(p.Key)).Select(p=>$"{p.Key}:{p.PropertyType.Name}/{p.Editor}/{p.DefaultText}")));
        }
        return 0;
    }
}
