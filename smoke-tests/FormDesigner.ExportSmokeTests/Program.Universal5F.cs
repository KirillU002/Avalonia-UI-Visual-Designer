using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;
internal static partial class Program
{
    private static readonly string[] New5F = { "SelectableTextBlock", "PathIcon", "Arc", "Sector" };
    private static readonly string[] Types5F = Phase5ETypes.Concat(New5F).ToArray();
    private static List<(string Key, string Value)> Values5F(string type)
    {
        var values = new List<(string, string)> { ("RenderTransform", "matrix(1,0,0,1,3,4)"), ("RenderTransformOrigin", "25%,75%"), ("Clip", "M 0,0 L 400,0 L 400,130 L 0,130 Z") };
        if (Phase5ETypes.Contains(type)) values.AddRange(Values5E(type));
        if (type == "SelectableTextBlock") values.AddRange(new[] { ("TextWrapping", "Wrap"), ("TextTrimming", "CharacterEllipsis"), ("LineHeight", "24"), ("SelectionBrush", "#FFABCDEF"), ("SelectionForegroundBrush", "#FF123456") });
        if (type == "PathIcon") values.Add(("Data", "M 0,0 L 100,0 L 50,80 Z"));
        if (type is "Arc" or "Sector") values.AddRange(new[] { ("StartAngle", "30"), ("SweepAngle", "210"), ("Fill", "#FFABCDEF"), ("Stroke", "#FF123456"), ("StrokeThickness", "3"), ("StrokeDashArray", "2 1"), ("Stretch", "Fill") });
        return values;
    }
    private static bool Equal5F(object? actual, object? expected) => (actual, expected) switch
    {
        (ITransform a, ITransform b) => a.Value == b.Value,
        (Geometry a, Geometry b) => a.Bounds == b.Bounds && new[] { new Point(20,20), new Point(350,110), new Point(450,150) }.All(p => a.FillContains(p) == b.FillContains(p)),
        (ISolidColorBrush a, ISolidColorBrush b) => a.Color == b.Color,
        (BoxShadows a, BoxShadows b) => a.ToString() == b.ToString(),
        (IEnumerable<double> a, IEnumerable<double> b) => a.SequenceEqual(b),
        _ => Equals(actual, expected)
    };
    private static void Assert5F(Control native, string type)
    {
        RequireCapability(native.GetType() == StandardControlProperties.PropertyTypeFor(type), "Wrong native type " + type);
        foreach (var (key, text) in Values5F(type))
        {
            var p = StandardControlProperties.Find(type, key)!;
            RequireCapability(StandardControlProperties.TryParseValue(p, text, out var expected), "Invalid fixture " + key);
            RequireCapability(Equal5F(native.GetValue(p.Property!), expected), "Wrong native property " + type + "." + key);
        }
        if (native is SelectableTextBlock selectable) RequireCapability(selectable.Text == "Phase 5F текст", "Text lost");
    }
    private static Control Native5F(Control root, DesignControlModel model, string? sourceType = null)
    {
        try { return Native5E(root,model,sourceType); }
        catch (Exception e) { throw new InvalidOperationException("Native tree missing: " + string.Join(" | ",root.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text)),e); }
    }
    private static FormDesigner.ViewModels.MainWindowViewModel Scene5F(string layout = "Grid", string? only = null)
    {
        var vm = GridFirstVm();
        if (layout == "Canvas") { vm.NewFormLayoutChoice = FormDesigner.ViewModels.MainWindowViewModel.NewFormCanvasChoice; vm.NewProjectEditorCommand!.Execute(null); }
        vm.DesignWidth=1200;vm.DesignHeight=1800;vm.SurfaceGridRowDefinitions="*,*,*,*,*,*";vm.SurfaceGridColumnDefinitions="*,*";
        var types=only is null?Types5F:new[]{only};
        for(var i=0;i<types.Length;i++)
        {
            var c=vm.CreateControl(types[i],20+i%2*560,20+i/2*200);c.Name="Proof5F_"+c.Type;c.GridRow=i/2;c.GridColumn=i%2;c.Width=450;c.Height=150;c.WidthMode=c.HeightMode="Fixed";c.Text="Phase 5F текст";
            vm.SelectSingleControl(c);
            foreach(var (key,value) in Values5F(c.Type))
            {
                var row=GetPropertyGridRow(vm,key);
                RequireCapability(row.UsesMetadata&&!row.IsReadOnly,"No typed editor "+c.Type+"."+key);
                vm.CommitPropertyGridTextEdit(row,value);
                RequireCapability(!row.HasValidationError,"Rejected literal "+key+": "+row.ValidationMessage);
                RequireCapability(vm.SelectedControl?.Id==c.Id,"Selection lost");
            }
            vm.EndPropertyGridTextEdit();
        }
        return vm;
    }
    private static int RunUniversal5FChecks()
    {
        var pass=0;var fail=0;
        void Check(string name,Action test)
        {
            var filter=Environment.GetEnvironmentVariable("UNIVERSAL5F_FILTER");if(filter is not null&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;
            try{test();pass++;Console.WriteLine("PASS UNIVERSAL5F_"+name);}catch(Exception e){fail++;Console.WriteLine("FAIL UNIVERSAL5F_"+name+": "+e);}
            finally{foreach(var w in SmokeWindows)w.CloseForExternalHost();SmokeWindows.Clear();}
        }
        Check("RegistrationAndToolbox",()=>
        {
            var vm=GridFirstVm();var descriptors=vm.Registry.GetControls().ToArray();
            foreach(var type in New5F)
            {
                RequireCapability(descriptors.Count(d=>d.TypeKey==type)==1,"Duplicate/missing descriptor "+type);
                RequireCapability(StandardControlProperties.PropertyTypeFor(type)!.IsPublic,"Nonpublic type");
                vm.ToolboxSearchText=type;var item=vm.ToolboxGroups.SelectMany(g=>g.Items).Single(i=>i.Type==type);
                RequireCapability(!string.IsNullOrEmpty(item.Icon)&&item.Icon!="AV"&&!string.IsNullOrEmpty(item.Description),"Missing search/icon/hint "+type);
            }
        });
        Check("SpaceDelimitedDashExport",()=>
        {
            var p=StandardControlProperties.Find("Arc","StrokeDashArray")!;
            RequireCapability(StandardControlProperties.AttributeText(p,"2 1")=="2,1","AXAML numeric collection converter mismatch");
        });
        Check("UnsupportedControlKeepsOpaqueFallback",()=>
        {
            var vm=GridFirstVm();var c=vm.CreateControl("DataGrid",0,0);vm.SelectSingleControl(c);
            foreach(var key in new[]{"Clip","RenderTransform","RenderTransformOrigin"})RequireCapability(GetPropertyGridRow(vm,key).IsReadOnly,"Unsupported complex property lost its fallback "+key);
        });
        Check("AdvancedEditorsRemainEditableWhenExpanded",()=>
        {
            var vm=Scene5F(only:"Button");var category=vm.PropertyGridCategories.Single(c=>c.Key=="Advanced");
            foreach(var key in new[]{"Clip","RenderTransform","RenderTransformOrigin"})RequireCapability(category.Rows.Any(r=>r.Key==key&&!r.IsReadOnly&&r.UsesMetadata),"Missing Advanced typed editor "+key);
            category.IsExpanded=true;var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(60);window.UpdateLayout();
            var row=GetPropertyGridRow(vm,"RenderTransform");vm.CommitPropertyGridTextEdit(row,"matrix(1,0,0,1,8,9)");vm.EndPropertyGridTextEdit();
            RequireCapability(!row.HasValidationError&&vm.SelectedControl?.Type=="Button","Expanded editor rejected edit");
        });
        Check("NullableComplexLiterals",()=>
        {
            var vm=Scene5F(only:"Button");vm.SelectSingleControl(vm.Controls.Single());
            foreach(var key in new[]{"Clip","RenderTransform"})vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),MetadataValueCodec.NullText);
            vm.EndPropertyGridTextEdit();var reopened=GridFirstVm();reopened.LoadDocumentJson(vm.ExportWorkspaceJson());
            foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(reopened,mode))
            {var native=Native5F(p.Window,reopened.Controls.Single());RequireCapability(native.Clip is null&&native.RenderTransform is null,"Null complex value lost");}
        });
        foreach(var type in Types5F)
        {
            foreach(var layout in new[]{"Grid","Canvas"}) Check(type+"_JsonPreviews_"+layout,()=>
            {
                var vm=Scene5F(layout,type);var file=Path.GetFullPath("artifacts/universal-phase5f/json/"+type+layout+".json");Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,vm.ExportWorkspaceJson());var reopened=GridFirstVm();reopened.LoadDocumentJson(File.ReadAllText(file));
                RequireCapability(JsonSerializer.Serialize(vm.Controls)==JsonSerializer.Serialize(reopened.Controls),"JSON changed");
                foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(reopened,mode))Assert5F(Native5F(p.Window,reopened.Controls.Single()),type);
            });
            Check(type+"_Designer",()=>
            {
                var vm=Scene5F(only:type);var c=vm.Controls.Single();var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(50);window.UpdateLayout();
                var native=Layout7Wrappers(window)[c.Id].GetVisualDescendants().OfType<Control>().First(v=>v.GetType()==StandardControlProperties.PropertyTypeFor(type));Assert5F(native,type);
            });
            Check(type+"_Imported",()=>
            {
                var attributes=string.Join(" ",Values5F(type).Select(p=>p.Key+"='"+System.Security.SecurityElement.Escape(p.Value)+"'"));
                var text=type=="SelectableTextBlock"?" Text='Phase 5F текст'":"";
                var vm=Universal4AVm("<a:"+type+" Name='Target' "+attributes+text+"/>");
                foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(vm,mode))Assert5F(Native5E(p.Window,vm.Controls.Single(),type),type);
            });
        }
        foreach(var type in New5F) Check(type+"_RoutedDropResizeUndo",()=>
        {
            using var f=new PointerFixture();f.Vm.NewProjectEditorCommand!.Execute(null);f.Refresh();var point=GridCellPoint(f.Vm,"",0,0);var data=GridToolboxData(f,type);
            RouteGridToolbox(f,data,DragDrop.DragOverEvent,point);RouteGridToolbox(f,data,DragDrop.DropEvent,point);f.Refresh();f.AssertIdle();var c=f.Vm.Controls.Single();
            RequireCapability(c.Type==type&&c.X==0&&c.Y==0&&f.Vm.SelectedControl?.Id==c.Id,"Drop placement/selection failed");
            c.Width=180;c.Height=90;c.WidthMode=c.HeightMode="Fixed";f.Vm.SelectSingleControl(c);f.Refresh();var start=f.ResizePoint();var width=c.Width;f.Down(start);f.Move(start+new Vector(20,15));f.Up(start+new Vector(20,15));f.Refresh();f.AssertIdle();RequireCapability(c.Width>width,"Resize failed");
            GridEditorUndo(f.Vm);GridEditorUndo(f.Vm,true);f.Refresh();RequireCapability(f.Vm.SelectedControl?.Id==c.Id,"Undo selection lost");
        });
        foreach(var sample in new[]{("Button","RenderTransform","matrix(1,0,0,1,0,0)","matrix(1,0,0,1,3,4)"),("Border","Clip","M0,0 L10,0 L0,10 Z","M0,0 L30,0 L0,30 Z"),("TextBox","RenderTransformOrigin","50%,50%","25%,75%"),("PathIcon","Data","M0,0 L10,0 L0,10 Z","M0,0 L30,0 L0,30 Z"),("Arc","SweepAngle","90","210"),("SelectableTextBlock","SelectionBrush","Red","Blue")}) Check(sample.Item1+"_PatchResetUndoRedo",()=>
        {
            var(type,key,before,after)=sample;var vm=Universal4AVm($"<a:{type} Name='Target' {key}='{before}' u:Keep='&amp;yes'/> <!-- untouched -->");var original=vm.ActiveAxamlSourceDocument!.OriginalText;
            vm.SelectSingleControl(vm.Controls.Single());vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),after);vm.EndPropertyGridTextEdit();var patch=vm.CreateActiveAxamlPatch();
            RequireCapability(patch.CanApply&&patch.Edits.Count==1&&patch.PatchedText.Contains("u:Keep='&amp;yes'"),"Nonminimal patch");
            vm.ResetPropertyGridProperty(GetPropertyGridRow(vm,key));var reset=vm.CreateActiveAxamlPatch().PatchedText;RequireCapability(!reset.Contains(key+"='"),"Reset failed");
            GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==patch.PatchedText,"Undo reset changed XML");GridEditorUndo(vm,true);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==reset,"Redo reset changed XML");GridEditorUndo(vm);GridEditorUndo(vm);RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Undo edit changed XML");
        });
        foreach(var expression in new[]{"{Binding NeverExecute}","{StaticResource Keep}","{DynamicResource Keep}"}) Check("Preserve_"+expression.Split(' ')[0],()=>
        {
            var vm=Universal4AVm($"<a:Button Name='Target' RenderTransform='{expression}' Clip='{expression}'/>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;vm.SelectSingleControl(vm.Controls.Single());
            RequireCapability(GetPropertyGridRow(vm,"RenderTransform").IsReadOnly&&GetPropertyGridRow(vm,"Clip").IsReadOnly,"Expression exposed as literal");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Expression changed");
        });
        foreach(var sample in new[]{("RenderTransform","RotateTransform Angle='20'"),("Clip","RectangleGeometry Rect='0,0,30,40'")}) Check("OpaquePropertyElement_"+sample.Item1,()=>
        {
            var vm=Universal4AVm($"<a:Button Name='Target'><a:Button.{sample.Item1}><a:{sample.Item2}/></a:Button.{sample.Item1}></a:Button>");var original=vm.ActiveAxamlSourceDocument!.OriginalText;
            vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Target"));RequireCapability(GetPropertyGridRow(vm,sample.Item1).IsReadOnly,"Complex object exposed as text");RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==original,"Opaque object changed");
        });
        Check("ValidationAndDefaults",()=>
        {
            foreach(var key in new[]{"RenderTransform","RenderTransformOrigin","Clip"})
            {
                var p=StandardControlProperties.Find("Button",key)!;RequireCapability(StandardControlProperties.TryEncode(p,p.DefaultText,out _),"Invalid default "+key+"="+p.DefaultText);
                foreach(var bad in new[]{"NaN,0","{Binding X}","<object/>",new string('1',17000)})RequireCapability(!StandardControlProperties.TryEncode(p,bad,out _),"Invalid input accepted "+key);
            }
            var matrix=StandardControlProperties.Find("Button","RenderTransform")!;RequireCapability(!StandardControlProperties.TryEncode(matrix,"matrix(1,0,0,1,Infinity,0)",out _),"Nonfinite matrix accepted");
            RequireCapability(!StandardControlProperties.TryEncode(matrix,"1,0,0,1,3,4",out _),"Legacy transform syntax cannot round-trip through AXAML");
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("Button","RenderTransformOrigin")!,"10%,20",out _),"Mixed unit origin accepted");
        });
        Check("MultiFormSelection",()=>{var vm=Scene5F(only:"PathIcon");var id=vm.ActiveDocumentId;vm.CreateNewForm();var c=vm.CreateControl("Sector",0,0);vm.SelectSingleControl(c);vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"SweepAngle"),"135");vm.EndPropertyGridTextEdit();vm.SetActiveForm(id,"5f");RequireCapability(vm.Controls.Single().Type=="PathIcon","Forms mixed");var reopen=GridFirstVm();reopen.LoadDocumentJson(vm.ExportWorkspaceJson());reopen.SetActiveForm(id,"5f");RequireCapability(reopen.Controls.Single().Type=="PathIcon","Reopen forms mixed");});
        Check("NativeSelectableInteraction",()=>{var vm=Scene5F(only:"SelectableTextBlock");using var p=ShowDataGridRegressionPreview(vm,true);var native=(SelectableTextBlock)Native5E(p.Window,vm.Controls.Single());native.SelectAll();RequireCapability(native.SelectedText==native.Text,"SelectAll failed");native.ClearSelection();RequireCapability(string.IsNullOrEmpty(native.SelectedText),"ClearSelection failed");});
        foreach(var mode in new[]{false,true})Check("Memory_"+mode,()=>{var vm=Scene5F();var refs=Enumerable.Range(0,10).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);GC.KeepAlive(vm);});
        if(fail==0&&Environment.GetEnvironmentVariable("UNIVERSAL5F_FILTER") is null)
        {
            Directory.CreateDirectory("artifacts/universal-phase5f");File.WriteAllText("artifacts/universal-phase5f/inspector-proof.json",JsonSerializer.Serialize(Types5F.SelectMany(type=>Values5F(type).Select(p=>new{Type=StandardControlProperties.PropertyTypeFor(type)!.FullName,p.Key,p.Value,Inspector=true,Json=true,Designer=true,AxamlPreview=true,LegacyPreview=true,ImportedPreview=true}))));
        }
        Console.WriteLine($"UNIVERSAL5F_CHECKS passed={pass}; failed={fail}");return fail==0?0:1;
    }
    private static int GenerateUniversal5FProjects()
    {
        var folder=Environment.GetEnvironmentVariable("UNIVERSAL5F_EXPORT_DIR")!;RequireCapability(!Directory.Exists(folder),"Fresh export required");
        foreach(var layout in new[]{"Grid","Canvas"}){var vm=Scene5F(layout);Pump(vm.ExportCurrentResultToProjectAsync(Path.Combine(folder,layout)));File.WriteAllText(Path.Combine(folder,layout+"-expected.json"),JsonSerializer.Serialize(vm.Controls.Select(c=>new{Name=c.Name,Type=StandardControlProperties.PropertyTypeFor(c.Type)!.FullName,Properties=Values5F(c.Type).ToDictionary(p=>p.Key,p=>p.Value)})));}
        return 0;
    }
    private static int Diagnose5FInspector()
    {
        var vm=Scene5F(only:"NumericUpDown");var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(60);window.UpdateLayout();
        foreach(var item in window.GetLogicalDescendants().OfType<ItemsControl>())
        {
            var values=item.Items.Cast<object>().ToArray();
            if(!values.Any(v=>v is FormDesigner.ViewModels.DescriptorPropertyEditorViewModel or FormDesigner.ViewModels.PropertyGridCategoryViewModel))continue;
            Console.WriteLine(JsonSerializer.Serialize(new{item.Name,item.IsVisible,item.IsEffectivelyVisible,Items=values.Length,ItemType=values[0].GetType().Name,VisualDescendants=item.GetVisualDescendants().Count(),LogicalDescendants=item.GetLogicalDescendants().Count()}));
        }
        window.CloseForExternalHost();SmokeWindows.Clear();Dispose5AProfileVm(vm);return 0;
    }
}
