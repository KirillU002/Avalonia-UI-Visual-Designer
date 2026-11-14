using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string Universal2Folder => Path.Combine(FindRepositoryRoot(), "artifacts", "universal-phase2");
    private static readonly string[] Phase2Types = { "RadioButton", "NumericUpDown", "Calendar", "DatePicker", "TimePicker", "Separator" };
    private static void SetUniversal2(DesignControlModel model, string key, string value)
    {
        var property = StandardControlProperties.Find(model.Type, key) ?? throw new Exception("Missing reviewed property " + model.Type + "." + key);
        RequireCapability(StandardControlProperties.TryWriteModel(model, property, value), "Rejected literal " + key + "=" + value);
    }
    private static MainWindowViewModel Universal2Scene(string layout = "Grid")
    {
        var vm = GridFirstVm();
        if (layout == "Canvas") { vm.NewFormLayoutChoice = MainWindowViewModel.NewFormCanvasChoice; vm.NewProjectEditorCommand!.Execute(null); }
        vm.DesignWidth = 1200; vm.DesignHeight = 1000;
        vm.SurfaceGridRowDefinitions = "*,*,*"; vm.SurfaceLayoutRows = 3;
        vm.SurfaceGridColumnDefinitions = "*,*"; vm.SurfaceLayoutColumns = 2;
        for (var i = 0; i < Phase2Types.Length; i++)
        {
            var c = vm.CreateControl(Phase2Types[i], (i % 2) * 500 + 15, (i / 2) * 320 + 10, null, layout == "Canvas");
            c.Name = "Universal2_" + c.Type; c.Text = "Выбор"; c.GridRow = i / 2; c.GridColumn = i % 2;
            c.Width = 360; c.Height = c.Type == "Calendar" ? 300 : c.Type == "Separator" ? 4 : 50;
            c.WidthMode = c.HeightMode = "Fixed"; c.Margin = "3";
            SetUniversal2(c, "MinWidth", "50"); SetUniversal2(c, "MaxWidth", "480");
            if (StandardControlProperties.Find(c.Type, "Padding") is not null) SetUniversal2(c, "Padding", "2,3,4,5");
            switch (c.Type)
            {
                case "RadioButton": SetUniversal2(c, "GroupName", "phase2"); SetUniversal2(c, "IsChecked", "True"); break;
                case "NumericUpDown": SetUniversal2(c, "Minimum", "0"); SetUniversal2(c, "Maximum", "100"); SetUniversal2(c, "Value", "12.5"); SetUniversal2(c, "Increment", "0.25"); SetUniversal2(c, "FormatString", "F2"); break;
                case "Calendar": SetUniversal2(c, "DisplayDate", "2026-11-01"); SetUniversal2(c, "SelectedDate", "2026-11-14"); SetUniversal2(c, "IsTodayHighlighted", "False"); break;
                case "DatePicker": SetUniversal2(c, "SelectedDate", "2026-11-14T00:00:00.0000000+00:00"); break;
                case "TimePicker": SetUniversal2(c, "SelectedTime", "14:30:00"); SetUniversal2(c, "ClockIdentifier", "24HourClock"); SetUniversal2(c, "MinuteIncrement", "5"); break;
            }
        }
        return vm;
    }
    private static Control Universal2Native(Control root, DesignControlModel model) => root.GetVisualDescendants().OfType<Control>()
        .Single(c => c.Name == model.Name && c.GetType() == StandardControlProperties.SupportedTypes[model.Type]);
    private static void RequireUniversal2Value(Control native, string type)
    {
        RequireCapability(native.IsVisible && native.GetVisualRoot() is not null && native.Bounds.Width > 0 && native.Bounds.Height > 0, "Missing actual native control " + type);
        RequireCapability(native.MinWidth == 50 && native.MaxWidth == 480, "Size constraints lost " + type);
        switch (native)
        {
            case RadioButton r: RequireCapability(r.IsChecked == true && r.GroupName == "phase2", "Radio grouping/state lost."); break;
            case NumericUpDown n: RequireCapability(n.Value == 12.5m && n.Increment == .25m && n.FormatString == "F2", "Decimal precision/format lost."); break;
            case Calendar c: RequireCapability(c.SelectedDate == new DateTime(2026,11,14) && c.DisplayDate.Month == 11 && !c.IsTodayHighlighted, "Calendar state lost."); break;
            case DatePicker d: RequireCapability(d.SelectedDate?.Date == new DateTime(2026,11,14), "DatePicker date lost."); break;
            case TimePicker t: RequireCapability(t.SelectedTime == new TimeSpan(14,30,0) && t.MinuteIncrement == 5 && t.ClockIdentifier == "24HourClock", "TimePicker time lost."); break;
        }
    }
    private static int RunUniversalControls2Checks()
    {
        Directory.CreateDirectory(Universal2Folder); var passed = 0; var failed = 0;
        void Check(string name, Action action)
        {
            var filter = Environment.GetEnvironmentVariable("UNIVERSAL2_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { action(); passed++; Console.WriteLine("PASS UNIVERSAL2_" + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL UNIVERSAL2_" + name + ": " + e); }
            finally { foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear(); }
        }
        foreach (var type in Phase2Types)
        {
            Check(type + "_VerticalSlice", () =>
            {
                var vm = Universal2Scene(); var model = vm.Controls.Single(c => c.Type == type); vm.SelectSingleControl(model);
                RequireCapability(vm.Registry.GetControls().Count(d => d.TypeKey == type) == 1, "Missing/duplicate Toolbox descriptor.");
                var owner = CreateSmokeWindow(vm); owner.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(150);
                RequireUniversal2Value(Universal2Native(owner, model), type);
                foreach (var mode in new[] { false, true })
                {
                    using var p = ShowDataGridRegressionPreview(vm, mode); var native = Universal2Native(p.Window, model); RequireUniversal2Value(native, type);
                    if (native is not Separator) RequireCapability(native.IsHitTestVisible, "Designer input mask leaked to runtime Preview.");
                }
                var json = vm.ExportWorkspaceJson(); var restored = GridFirstVm(); restored.LoadDocumentJson(json);
                var copy = restored.GetControl(model.Id)!;
                RequireCapability(copy.Type == model.Type && copy.ParentId == model.ParentId && copy.GridRow == model.GridRow
                    && model.CustomProperties.All(p => copy.CustomProperties.Any(q => q.Key == p.Key && q.ValueJson == p.ValueJson)), "JSON lost typed values or layout.");
                vm.GenerateXaml(); _ = System.Xml.Linq.XDocument.Parse(vm.GeneratedXaml);
                RequireCapability(vm.GeneratedXaml.Contains("<" + type) && !vm.GeneratedXaml.Contains("Canvas.Left="), "Export omitted native/Grid placement.");
                RequireCapability(vm.GeneratedCSharp.Contains("InitializeComponent();"), "Generated C# initialization missing.");
            });
            Check(type + "_RoutedToolboxAndSelection", () =>
            {
                using var f = new PointerFixture(); f.Vm.NewProjectEditorCommand!.Execute(null); f.Refresh();
                var data = GridToolboxData(f, type); var point = GridCellPoint(f.Vm, "", 0, 0);
                RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.Refresh(); f.AssertIdle();
                var model = f.Vm.Controls.Single(); var id = model.Id;
                RequireCapability(model.Type == type && model.ParentId == "" && model.GridRow == 0 && model.GridColumn == 0 && f.Vm.SelectedControl?.Id == id, "Drop identity/placement/selection lost.");
                GridEditorUndo(f.Vm); GridEditorUndo(f.Vm, true); f.Refresh(); model = f.Vm.Controls.Single();
                var wrapper = DropWrappers(f.Window)[id]; var click = wrapper.TranslatePoint(new Point(wrapper.Bounds.Width/2,wrapper.Bounds.Height/2), f.Window)!.Value;
                Console.WriteLine("HIT2 "+JsonSerializer.Serialize(new{type,Hit=(f.Window.InputHitTest(click) as Control)?.GetType().Name,Name=(f.Window.InputHitTest(click) as Control)?.Name,
                    Ancestors=(f.Window.InputHitTest(click) as Visual)?.GetVisualAncestors().OfType<Control>().Select(c=>c.GetType().Name+":"+c.Name).Take(8)}));
                f.Click(click); f.NoMarquee(); f.AssertIdle();
                Console.WriteLine("POINTER2 "+JsonSerializer.Serialize(new{type,click,Bounds=wrapper.Bounds.ToString(),Selected=f.Vm.SelectedControl?.Id,Expected=id,Events=f.Window.DesignerPointerDiagnostics.TakeLast(8)}));
                RequireCapability(f.Vm.SelectedControl?.Id == id && Universal2Native(f.Window, model).GetVisualRoot() == f.Window, "Native pointer lost Inspector selection.");
            });
            Check(type + "_SourcePatch", () => VerifyUniversal2Source(type));
            Check(type + "_OpaqueBindingAndTemplate", () =>
            {
                var key=type switch{"RadioButton"=>"IsChecked","Calendar" or "DatePicker"=>"SelectedDate","TimePicker"=>"SelectedTime","NumericUpDown"=>"Value",_=>"MinWidth"};
                var source=$"<Window xmlns=\"https://github.com/avaloniaui\" xmlns:q=\"urn:unknown\"><Grid><!-- preserve --><{type} Name=\"Bound2\" {key}=\"{{Binding Dangerous}}\" q:Other=\"keep\"><{type}.Styles><Style Selector=\"{type}\"><Setter Property=\"Opacity\" Value=\"0.8\"/></Style></{type}.Styles><{type}.Template><ControlTemplate><Border/></ControlTemplate></{type}.Template></{type}></Grid></Window>";
                var result=new AxamlImportService().Import(source);var model=result.Document.Controls.Single(c=>c.Name=="Bound2");
                var noop=new AxamlPatchWriter().CreatePatch(result.RoundTripDocument,result.Document,source);
                RequireCapability(noop.CanApply&&!noop.HasChanges&&noop.PatchedText==source,"Binding/template/styles no-op damaged source.");
                var p=StandardControlProperties.Find(type,key)!;var literal=key=="IsChecked"?"True":key=="SelectedDate"?(type=="DatePicker"?"2026-11-14T00:00:00.0000000+00:00":"2026-11-14"):key=="SelectedTime"?"14:30:00":"42";
                RequireCapability(StandardControlProperties.TryEncode(p,literal,out var json),"Fixture literal invalid.");
                var stored=model.CustomProperties.FirstOrDefault(v=>v.Key==key);
                if(stored is null)model.CustomProperties.Add(stored=new DesignPropertyValueFileModel{Key=key});stored.ValueJson=json;
                RequireCapability(!new AxamlPatchWriter().CreatePatch(result.RoundTripDocument,result.Document,source).CanApply,"Unsafe binding replacement permitted.");
            });
        }
        Check("TypedScalarEditorsDefaultsAndUndo", () =>
        {
            var vm = Universal2Scene(); var model = vm.Controls.Single(c => c.Type == "NumericUpDown"); vm.SelectSingleControl(model);
            var value = GetPropertyGridRow(vm, "Value"); RequireCapability(value.IsNumberEditor && value.DefaultValue == "(null)", "Nullable decimal editor/default missing.");
            vm.CommitPropertyGridTextEdit(value,"19.75"); GridEditorUndo(vm); GridEditorUndo(vm,true);
            RequireCapability(vm.GetControl(model.Id)!.CustomProperties.Any(p => p.Key=="Value" && p.ValueJson=="19.75"), "Decimal JSON/Undo/Redo lost precision.");
            vm.SelectSingleControl(vm.GetControl(model.Id)!); vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Value"),"(null)");
            RequireCapability(vm.GetControl(model.Id)!.CustomProperties.Any(p=>p.Key=="Value"&&p.ValueJson=="null"), "Nullable decimal null lost.");
            var radius=GetPropertyGridRow(vm,"CornerRadius"); RequireCapability(radius.Editor==PropertyGridEditorKind.CornerRadius,"Struct editor lost.");
            vm.CommitPropertyGridTextEdit(radius,"1,2,3,4");
            var numeric=vm.Controls.Single(c=>c.Id==model.Id); var old=numeric.CustomProperties.Single(p=>p.Key=="CornerRadius").ValueJson;
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"CornerRadius"),"NaN"); RequireCapability(numeric.CustomProperties.Single(p=>p.Key=="CornerRadius").ValueJson==old,"Invalid value changed model.");
            vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,"Maximum"),"79228162514264337593543950334");
            RequireCapability(GetPropertyGridRow(vm,"Maximum").IsModified && numeric.CustomProperties.Single(p=>p.Key=="Maximum").ValueJson=="79228162514264337593543950334",
                "Decimal comparison or serialization lost one unit near decimal.MaxValue.");
        });
        foreach(var mode in new[]{false,true}) Check("NativeInteractions_"+mode,()=>
        {
            var vm=Universal2Scene();using var p=ShowDataGridRegressionPreview(vm,mode);
            var numeric=(NumericUpDown)Universal2Native(p.Window,vm.Controls.Single(c=>c.Type=="NumericUpDown"));
            var up=numeric.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>b.Name?.Contains("Increase",StringComparison.OrdinalIgnoreCase)==true);
            RequireCapability(up is not null,"NumericUpDown native increase button missing: "+string.Join(",",numeric.GetVisualDescendants().OfType<Button>().Select(b=>b.Name)));
            up!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));NativeItemsUiWait(60);RequireCapability(numeric.Value==12.75m,"Native spinner did not change decimal value.");
            var calendar=(Calendar)Universal2Native(p.Window,vm.Controls.Single(c=>c.Type=="Calendar"));
            calendar.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right});NativeItemsUiWait(50);
            RequireCapability(calendar.SelectedDate==new DateTime(2026,11,15),"Native Calendar keyboard selection failed.");
            foreach(var picker in p.Window.GetVisualDescendants().OfType<Control>().Where(c=>c is DatePicker or TimePicker).ToArray())
            {
                var button=picker.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>b.Name=="PART_FlyoutButton");
                RequireCapability(button is not null,"Native picker button missing "+picker.GetType().Name+": "+string.Join(",",picker.GetVisualDescendants().OfType<Button>().Select(b=>b.Name))); button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));NativeItemsUiWait(80);
                var popups = new List<Popup>();
                for(var t=picker.GetType();t is not null;t=t.BaseType)
                    popups.AddRange(t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(f=>typeof(Popup).IsAssignableFrom(f.FieldType)).Select(f=>f.GetValue(picker)).OfType<Popup>());
                RequireCapability(popups.Any(popup=>popup.IsOpen),"Native picker popup did not open: "+picker.GetType().Name);
                foreach(var popup in popups)popup.IsOpen=false;
                picker.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});NativeItemsUiWait(30);
            }
        });
        foreach(var layout in new[]{"Canvas","Stack","Dock","Flex","Nested"})Check("Hierarchy_"+layout,()=>
        {
            var vm=Universal2Scene(layout=="Canvas"?"Canvas":"Grid");
            if(layout!="Canvas")
            {
                var parent=vm.CreateControl(layout=="Stack"||layout=="Nested"?"StackPanel":layout=="Flex"?"WrapPanel":"Group",0,0);
                if(layout=="Dock")parent.ChildLayoutMode="Dock";
                parent.Width=1100;parent.Height=950;parent.WidthMode=parent.HeightMode="Fixed";
                foreach(var c in vm.Controls.Where(c=>Phase2Types.Contains(c.Type))){c.ParentId=parent.Id;c.StackOrder=Array.IndexOf(Phase2Types,c.Type);}
                if(layout=="Nested"){var grid=vm.CreateControl("Grid",0,0);parent.ParentId=grid.Id;}
            }
            var w=CreateSmokeWindow(vm);w.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(180);
            foreach(var c in vm.Controls.Where(c=>Phase2Types.Contains(c.Type)))RequireUniversal2Value(Universal2Native(w,c),c.Type);
            var copy=GridFirstVm();copy.LoadDocumentJson(vm.ExportWorkspaceJson());RequireCapability(vm.Controls.Select(c=>(c.Id,c.ParentId,c.StackOrder)).SequenceEqual(copy.Controls.Select(c=>(c.Id,c.ParentId,c.StackOrder))),"Hierarchy/order JSON lost.");
        });
        Check("LegacyCanvasThemeMargins",()=>
        {
            var vm=Universal2Scene("Canvas");
            var owner=CreateSmokeWindow(vm);owner.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(150);
            var canvas=owner.GetVisualDescendants().OfType<FormDesigner.Views.DesignerSurface>().Single().Canvas;
            foreach(var model in vm.Controls)
            {
                var point=Universal2Native(owner,model).TranslatePoint(default,canvas)!.Value;
                RequireCapability(Math.Abs(point.X-model.X)<1.1&&Math.Abs(point.Y-model.Y)<1.1,"Designer theme margin shifted "+model.Type);
            }
            foreach(var mode in new[]{false,true})
            {
                using var preview=ShowDataGridRegressionPreview(vm,mode);
                foreach(var model in vm.Controls)
                {
                    var native=Universal2Native(preview.Window,model);
                    var parent=preview.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                    var point=native.TranslatePoint(default,parent)!.Value;
                    RequireCapability(Math.Abs(point.X-model.X)<1.1&&Math.Abs(point.Y-model.Y)<1.1,
                        $"Theme margin shifted {model.Type} in mode={mode}: {point}, expected {model.X},{model.Y}");
                }
            }
        });
        Check("MultiFormMetadataIsolation",()=>
        {
            var vm=Universal2Scene();var first=vm.ActiveDocumentId;var ids=vm.Controls.Select(c=>c.Id).ToArray();
            var second=vm.CreateNewForm("Absolute").Id;var numeric=vm.CreateControl("NumericUpDown",37,53,null,true);SetUniversal2(numeric,"Value","89.25");
            foreach(var form in new[]{first,second,first,second}){vm.SetActiveForm(form,"universal2");RequireCapability(vm.Controls.Count==(form==first?6:1),"Form contents mixed.");}
            var clone=GridFirstVm();clone.LoadDocumentJson(vm.ExportWorkspaceJson());clone.SetActiveForm(first,"reopen");RequireCapability(ids.SequenceEqual(clone.Controls.Select(c=>c.Id)),"Multi Form IDs lost.");
        });
        Check("SafeStringDomains",()=>
        {
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("TimePicker","ClockIdentifier")!,"broken-clock",out _),"Invalid clock identifier accepted.");
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("NumericUpDown","FormatString")!,"Q",out _),"Invalid decimal format accepted.");
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("DatePicker","YearFormat")!,"Q",out _),"Invalid date format accepted.");
            RequireCapability(!StandardControlProperties.TryEncode(StandardControlProperties.Find("RadioButton","GroupName")!,"{Binding Dangerous}",out _),"Markup was accepted as a scalar literal.");
        });
        Check("FavoritesSearchAndSelection",()=>
        {
            var vm=Universal2Scene();var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");vm.SelectSingleControl(model);
            vm.TogglePropertyGridFavoriteCommand.Execute(GetPropertyGridRow(vm,"Increment"));
            RequireCapability(GetPropertyGridRow(vm,"Increment").IsFavorite,"Metadata favorite lost.");
            vm.PropertyGridSearchText="Increment";
            RequireCapability(vm.PropertyGridCategories.SelectMany(c=>c.Rows).Any(r=>r.Key=="Increment")&&vm.SelectedControl?.Id==model.Id,"Search reset selection or hid the metadata row.");
            vm.PropertyGridSearchText="";
        });
        foreach(var mode in new[]{false,true})Check("CollectiblePreview_"+mode,()=>
        {
            var vm=Universal2Scene();var refs=Enumerable.Range(0,3).Select(_=>CloseMemoryPreview(vm,mode,false)).ToArray();VerifyMemoryReleased(refs);
        });
        Check("RemovedNativeControlReleased",()=>
        {
            var vm=Universal2Scene();var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(200);
            var reference=RemoveUniversal2Control(vm,window);NativeItemsUiWait(200);ForceFullGc();RequireCapability(!reference.IsAlive,"Removed native control retained by rendering/cache.");GC.KeepAlive(window);GC.KeepAlive(vm);
        });
        Check("CoverageAndExportFixtures",()=>
        {
            var vm=Universal2Scene(); MetadataCoverageReport.Write(vm.Registry,Path.Combine(FindRepositoryRoot(),"Docs"));
            foreach(var layout in new[]{"Grid","Canvas"}){var scene=Universal2Scene(layout);File.WriteAllText(Path.Combine(Universal2Folder,layout+".json"),scene.ExportWorkspaceJson());Pump(scene.ExportCurrentResultToProjectAsync(Path.Combine(Universal2Folder,"exports",layout)));}
        });
        Console.WriteLine($"Universal Phase2 checks: {passed}/{passed+failed}");return failed==0?0:1;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RemoveUniversal2Control(MainWindowViewModel vm, FormDesigner.Views.MainWindow window)
    {
        var model=vm.Controls.Single(c=>c.Type=="NumericUpDown");var native=Universal2Native(window,model);var reference=new WeakReference(native);
        vm.Controls.Remove(model);typeof(FormDesigner.Views.MainWindow).GetMethod("RenderDesigner",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);window.UpdateLayout();return reference;
    }
    private static void VerifyUniversal2Source(string type)
    {
        var (key,initial,next)=type switch
        {
            "RadioButton"=>("IsChecked","False","True"),"NumericUpDown"=>("Value","12.5","19.75"),
            "Calendar"=>("SelectedDate","2026-11-14","2026-11-15"),"DatePicker"=>("SelectedDate","2026-11-14T00:00:00.0000000+00:00","2026-11-15T00:00:00.0000000+00:00"),
            "TimePicker"=>("SelectedTime","14:30:00","15:45:00"),_=>("MinWidth","40","50")
        };
        var source=$"<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:q=\"urn:unknown\"><Window.Resources><!-- resources --><x:String x:Key=\"keep\">Keep</x:String></Window.Resources><Grid><!-- keep --><{type} Name=\"Source2\" {key}=\"{initial}\" q:Unknown=\"preserve\" Tag=\"{{Binding UserData}}\" /></Grid></Window>";
        var import=new AxamlImportService().Import(source,"phase2.axaml");var vm=GridFirstVm();vm.LoadAxamlImportedDocument(import,"phase2.axaml");var model=vm.Controls.Single(c=>c.Name=="Source2");
        var window=CreateSmokeWindow(vm);window.Show();vm.CloseStartScreenCommand.Execute(null);NativeItemsUiWait(150);
        RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==source,"No-op damaged source.");vm.SelectSingleControl(model);
        var row=GetPropertyGridRow(vm,key);RequireCapability(!row.IsReadOnly,"Literal editor unexpectedly read-only.");vm.CommitPropertyGridTextEdit(row,next);
        var metadata=StandardControlProperties.Find(type,key)!;StandardControlProperties.TryEncode(metadata,next,out var encodedNext);
        var normalizedNext=StandardControlProperties.Read(metadata,encodedNext);
        DropPumpUntil(()=>!(bool)typeof(FormDesigner.Views.MainWindow).GetField("_isDesignerRenderScheduled",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!,"phase2 source render");window.UpdateLayout();
        var native=window.GetVisualDescendants().OfType<Control>().Single(c=>c.Name=="Source2"&&c.GetType()==StandardControlProperties.SupportedTypes[type]);
        StandardControlProperties.TryParseValue(metadata,next,out var expectedNative);
        RequireCapability(native.GetVisualRoot()==window&&Equals(native.GetValue(metadata.Property!),expectedNative),"Source Inspector/native value mismatch.");
        var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&patch.PatchedText==source.Replace(key+"=\""+initial+"\"",key+"=\""+normalizedNext+"\""),"Non-minimal property patch: "+JsonSerializer.Serialize(patch));
        vm.MarkAxamlRoundTripSaved("phase2.axaml",patch.PatchedText);vm.SelectSingleControl(vm.Controls.Single(c=>c.Name=="Source2"));
        vm.CommitPropertyGridTextEdit(GetPropertyGridRow(vm,key),initial);
        StandardControlProperties.TryEncode(metadata,initial,out var encodedInitial);var normalizedInitial=StandardControlProperties.Read(metadata,encodedInitial);
        var repeated=vm.CreateActiveAxamlPatch();RequireCapability(repeated.CanApply&&repeated.PatchedText==source.Replace(key+"=\""+initial+"\"",key+"=\""+normalizedInitial+"\""),"Repeated patch lost syntax.");
        RequireCapability(!new AxamlPatchWriter().CreatePatch(import.RoundTripDocument,import.Document,source+"<!-- external edit -->").CanApply,"Checksum conflict was ignored.");
    }
    private static int RunUniversalControls2Profile()
    {
        Directory.CreateDirectory(Universal2Folder);
        var candidateAudit = new[] { typeof(RadioButton), typeof(Calendar), typeof(DatePicker), typeof(TimePicker), typeof(NumericUpDown), typeof(Separator) }
            .Select(t => new { Type = t.Name, Properties = FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.Get(t).Properties
                .Where(p => p.Property is not null && !p.ReadOnly && p.ClrProperty is not null)
                .Select(p => new { p.Key, Type = p.PropertyType.ToString(), p.DefaultText, p.Editor, p.Kind }) });
        File.WriteAllText(Path.Combine(Universal2Folder, "candidate-properties.json"), JsonSerializer.Serialize(candidateAudit, new JsonSerializerOptions { WriteIndented = true }));
        var newScene = Environment.GetEnvironmentVariable("UNIVERSAL2_PROFILE_SCENE") == "new";
        var vm = newScene ? Universal2Scene() : MetadataScene(); var window = CreateSmokeWindow(vm); window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(200);
        var inspector = window.FindControl<FormDesigner.Views.DesignerPropertyInspector>("DesignerPropertyInspector")!;
        var samples = new List<object>(); var times = new List<double>(); var allocated = new List<long>();
        var discovery = FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.BuildCount;
        var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var warmup = newScene ? 6 : 3;
        for (var i = -warmup; i < 30; i++)
        {
            var control = vm.Controls[(i + warmup) % vm.Controls.Count];
            using var profile = LayoutDropProfile.Begin(vm.ActiveDocumentId, "inspector-profile");
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
            vm.SelectSingleControl(control); var selection = clock.Elapsed.TotalMilliseconds;
            window.UpdateLayout(); clock.Stop(); bytes = GC.GetAllocatedBytesForCurrentThread() - bytes; profile.Dispose();
            var legacy = window.FindControl<ItemsControl>("LegacyPropertyGridCategoriesItemsControl")!;
            samples.Add(new { Iteration = i, Type = control.Type, ElapsedMs = clock.Elapsed.TotalMilliseconds, SelectionMs = selection,
                LayoutMs = clock.Elapsed.TotalMilliseconds - selection, AllocatedBytes = bytes, Profile = profile.Stages, Counters = profile.Counters,
                LegacyItems = legacy.Items.Count, LegacyVisuals = legacy.GetVisualDescendants().Count(), LegacyVisible = legacy.IsVisible,
                InspectorVisuals = inspector.GetVisualDescendants().Count(),
                GlyphVisuals = inspector.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("property-star") || b.Classes.Contains("property-action")).Sum(b => 1 + b.GetVisualDescendants().Count()),
                Categories = vm.PropertyGridCategories.Select(c => new { c.Key, c.IsExpanded, Count = c.Rows.Count }).ToArray(),
                WindowVisuals = window.GetVisualDescendants().Count(), Rows = vm.PropertyGridCategories.Sum(c => c.Rows.Count) });
            if (i >= 0) { times.Add(clock.Elapsed.TotalMilliseconds); allocated.Add(bytes); }
            NativeItemsUiWait(15);
        }
        var ordered = times.OrderBy(t => t).ToArray(); var allocations = allocated.OrderBy(t => t).ToArray();
        var result = new { MedianMs = (ordered[14] + ordered[15]) / 2, P95Ms = ordered[28], MedianAllocated = (allocations[14] + allocations[15]) / 2d,
            GcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - gc[g]), MetadataRebuilds = FormDesigner.DesignerSystem.Metadata.AvaloniaMetadataCatalog.BuildCount - discovery,
            Warmup = warmup, Samples = 30, Scene = newScene ? "Phase2 six controls; synchronous selection/Inspector/layout, no compositor timing" : "Unchanged Phase1 three controls; synchronous selection/Inspector/layout, no compositor timing", Observations = samples };
        var label = Environment.GetEnvironmentVariable("UNIVERSAL2_PROFILE_LABEL") ?? "current";
        if (label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new Exception("Invalid profile label.");
        File.WriteAllText(Path.Combine(Universal2Folder, "inspector-" + label + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var w in SmokeWindows) w.CloseForExternalHost(); SmokeWindows.Clear();
        Console.WriteLine($"Inspector {label}: median={result.MedianMs:F2}; P95={result.P95Ms:F2}; bytes={result.MedianAllocated}; builds={result.MetadataRebuilds}"); return 0;
    }
}
