using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.PluginContracts;
using FormDesigner.Services;
using Avalonia.Interactivity;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static string Preview2Folder => Environment.GetEnvironmentVariable("FORMDESIGNER_PREVIEW2_ARTIFACTS")
        ?? Path.Combine(FindRepositoryRoot(), "artifacts", "preview-phase2");
    private static int RunPreviewPhase2MissingTheme(bool missingCore = false)
    {
        try
        {
            var source=Path.Combine(FindRepositoryRoot(),"bin","Release","net8.0","Plugins","EremexDesignerPlugin");
            var folder=Path.Combine(Preview2Folder,missingCore?"missing-core-plugin":"missing-theme-plugin");Directory.CreateDirectory(folder);
            foreach(var file in Directory.GetFiles(source))
                if(Path.GetFileName(file)!=(missingCore?"Eremex.Avalonia.Controls.dll":"Eremex.Avalonia.Themes.DeltaDesign.dll"))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)),true);
            var vm=CreateViewModel("MissingTheme");var reports=new PluginLoader(new TraceDesignerLogger()).LoadFromFolder(folder,vm.Registry,true);
            var absent=missingCore?"Eremex.Avalonia.Controls.dll":"Eremex.Avalonia.Themes.DeltaDesign.dll";
            RequireCapability(reports.Any(r=>r.Errors.Any(e=>e.Contains(absent))),"The absent required dependency was not identified by plugin discovery.");
            var standard=vm.CreateControl("Button",20,20);standard.Text="Standard survives";
            vm.Controls.Add(new DesignControlModel{Type="Eremex.TextEditor",Name="UnavailableVendorEditor",X=20,Y=100,Width=300,Height=40});
            foreach(var mode in new[]{false,true})using(var preview=ShowDataGridRegressionPreview(vm,mode))
            {
                var map=(Dictionary<string,Control>)typeof(PreviewWindow).GetField("_builtPreviewControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview.Window)!;
                RequireCapability(map.TryGetValue(standard.Id,out var actual)&&actual.GetVisualRoot()==preview.Window,"Missing Eremex theme removed the standard control.");
                RequireCapability(preview.Window.BuildIssues.Count>0,"Missing theme was silently ignored.");
            }
            Console.WriteLine("PASS PREVIEW2_Missing"+(missingCore?"Runtime":"Theme")+": standard controls remain; missing dependency is diagnosed in both modes.");return 0;
        }
        catch(Exception error){Console.WriteLine("FAIL PREVIEW2_Missing"+(missingCore?"Runtime":"Theme")+": "+error);return 1;}
    }
    private static int RunPreviewPhase2PluginCold()
    {
        try
        {
            string[] NativeAssemblies() => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name!)
                .Where(n=>n.StartsWith("Eremex.",StringComparison.OrdinalIgnoreCase)).OrderBy(n=>n).ToArray();
            RequireCapability(NativeAssemblies().Length==0,"Plugin isolation requires a cold process.");
            var vm=CreateViewModel("OptionalEremex"); var loader=new PluginLoader(new TraceDesignerLogger());
            var path=Path.Combine(FindRepositoryRoot(),"bin","Release","net8.0","Plugins","EremexDesignerPlugin");
            var reports=loader.LoadFromFolder(path,vm.Registry,true);
            Console.WriteLine("PLUGIN_REPORTS "+JsonSerializer.Serialize(reports));
            RequireCapability(reports.Any(r=>r.PluginId=="Eremex.DesignerPlugin"&&r.RegisteredControls.Count==2&&r.Errors.Count==0),"Optional plugin metadata did not register.");
            RequireCapability(NativeAssemblies().Length==0,"Discovery loaded Eremex runtime assemblies before any control was used: "+string.Join(",",NativeAssemblies()));
            vm.RefreshRegistryBackedCollections(); vm.CreateControl("Button",10,10);
            var designer=CreateSmokeWindow(vm);designer.Show();NativeItemsUiWait(150);
            try{RequireCapability(NativeAssemblies().Length==0,"Opening the standard Designer activated an unused vendor runtime.");}
            finally{designer.CloseForExternalHost();}
            foreach(var mode in new[]{false,true})using(ShowDataGridRegressionPreview(vm,mode)){}
            vm.GenerateXaml();RequireCapability(!vm.RequiredPackages.Any(p=>p.Id.StartsWith("Eremex.",StringComparison.OrdinalIgnoreCase)),"Standard export acquired Eremex dependencies.");
            RequireCapability(NativeAssemblies().Length==0,"Standard Designer/Preview/export loaded vendor runtime.");
            var contexts=(System.Collections.ICollection)typeof(PluginLoader).GetField("RetainedContexts",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var before=contexts.Count;
            for(var i=0;i<3;i++){((DesignerRegistry)vm.Registry).ClearPluginRegistrations();loader.LoadFromFolder(path,vm.Registry,true);}
            RequireCapability(contexts.Count==before,"Unchanged reload accumulated plugin load contexts.");
            var editor=vm.CreateControl("Eremex.TextEditor",0,80);editor.Name="OptionalEditor";
            RequireCapability(NativeAssemblies().Length>0,"Native plugin did not load on first explicit use.");
            foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(vm,mode))
                RequireCapability(p.Window.GetVisualDescendants().OfType<Control>().Any(c=>c.GetType().FullName=="Eremex.AvaloniaUI.Controls.Editors.TextEditor"),"Used Eremex editor was not created.");
            RequireSingleCoreAvaloniaAssemblyIdentity();
            Console.WriteLine("PASS PREVIEW2_PluginCold: no native assemblies for standard workflow; first-use activation; both previews; stable reload contexts="+before);
            return 0;
        }
        catch(Exception error){Console.WriteLine("FAIL PREVIEW2_PluginCold: "+error);return 1;}
    }
    private sealed record HeaderAppearance(string Background, string Foreground, string BorderBrush, string BorderThickness,
        string Padding, string FontFamily, double FontSize, string FontWeight, double MinHeight, double Height, string HeaderTemplate);
    private static HeaderAppearance ReadHeader(DataGridColumnHeader header) => new(header.Background?.ToString() ?? "",
        header.Foreground?.ToString() ?? "", header.BorderBrush?.ToString() ?? "", header.BorderThickness.ToString(),
        header.Padding.ToString(), header.FontFamily.ToString(), header.FontSize, header.FontWeight.ToString(), header.MinHeight,
        header.Bounds.Height, header.ContentTemplate?.GetType().Name ?? "none");
    private static void RequireMainWindowPreview(bool? selected, string? imported = null, bool exportFault = false)
    {
        var vm=CreateViewModel("MainWindowPreviewMode");
        if(exportFault)vm.Registry.RegisterControl(new FailingExportDescriptor());
        var control=vm.CreateControl(exportFault?"AuditExportFailure":"Button",20,20);control.Name="ModeProbeButton";
        var owner=CreateSmokeWindow(vm);owner.Show();NativeItemsUiWait(100);
        try
        {
            var settings=new AppSettingsModel();if(selected.HasValue)settings.Preview.UseExportedAxamlPreview=selected.Value;
            vm.ApplyAppSettings(settings);
            if(exportFault)vm.IncludePluginRuntimeReferences=true;
            if(imported!=null)vm.LoadAxamlImportedDocument(new FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportService().Import(imported),"mode-import.axaml");
            var expected=selected??(imported==null&&PreviewModePolicy.DefaultForJson);
            var task=(Task)typeof(MainWindow).GetMethod("LaunchPreviewAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(owner,null)!;
            while(!task.IsCompleted)NativeItemsUiWait(10);task.GetAwaiter().GetResult();NativeItemsUiWait(400);
            var preview=(PreviewWindow?)typeof(MainWindow).GetField("_launchPreviewWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);
            RequireCapability(preview!=null,"MainWindow did not open Preview.");
            try
            {
                RequireCapability(preview!.LifecycleDiagnostics.First().Mode==(expected?"AXAML":"Legacy"),"MainWindow selected the wrong mode.");
                if(exportFault)
                    RequireCapability(preview.ModeDiagnostics.Any(d=>d.Event=="LEGACY_PREVIEW_FALLBACK_OFFERED")&&!preview.ModeDiagnostics.Any(d=>d.PreviewMode=="Legacy"),"Generation failure was not offered as a recoverable AXAML error.");
                else if(expected)RequireCapability(preview.ModeDiagnostics.Any(d=>d.Event=="AXAML_PREVIEW_SUCCESS"),"MainWindow AXAML Preview failed.");
                if(imported!=null)RequireCapability(vm.CreateActiveAxamlPatch().PatchedText==imported,"Launching imported Preview changed its source.");
            }
            finally{preview?.Close();}
        }
        finally{owner.CloseForExternalHost();}
    }
    private sealed class FailingExportDescriptor:IControlDescriptor
    {
        public string TypeKey=>"AuditExportFailure";public string Title=>TypeKey;public string Description=>"Test only";public string Category=>"Tests";
        public bool IsContainer=>false;public bool CanHostChildren=>false;public string ChildLayoutMode=>"Absolute";
        public IReadOnlyList<DesignPropertyDescriptor> Properties=>Array.Empty<DesignPropertyDescriptor>();
        public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context)=>new(){TypeKey=TypeKey,DescriptorId=TypeKey};
        public Control BuildPreview(IDesignControlNode control,IPreviewContext context)=>new Button{Content="Legacy recovery",Width=160,Height=40};
        public void AppendXaml(IXamlWriter writer,IDesignControlNode control,int indentLevel,IXamlExportContext context)=>throw new InvalidOperationException("Controlled export failure");
    }
    private static MainWindowViewModel Preview2GridModel(bool native)
    {
        var vm = native ? NativeGridModel("Preview2Headers") : CreateViewModel("Preview2Headers");
        var source = ProductsSource(); source.UseDemoData = true; vm.BindingSources.Add(source);
        var model = vm.CreateControl("DataGrid", 10, 10); model.Name = "StyledGrid"; model.BindingSourceId = source.Id;
        model.WidthMode = model.HeightMode = "Fixed"; model.Width = 700; model.Height = 300;
        model.DataGridHeaderBackground = "#213457"; model.DataGridHeaderForeground = "#F7ECD2";
        model.DataGridGridLineBrush = "#785634"; model.DataGridHeaderFontSize = 19; model.DataGridHeaderFontWeight = "Bold";
        model.DataGridHeaderHeight = 52; model.DataGridCellPadding = 16; model.FontFamily = "Segoe UI";
        model.DataGridRowFontSize = 16; model.DataGridRowHeight = 44;
        model.ShowFilterRow = model.ShowGroupPanel = model.ShowFooter = false;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal; vm.IncludeSampleData = true;
        return vm;
    }
    private static int RunPreviewPhase2Checks(bool validateDefault = true)
    {
        Directory.CreateDirectory(Preview2Folder); var passed = 0; var failed = 0;
        void Check(string name, Action action)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { action(); passed++; Console.WriteLine("PASS PREVIEW2_" + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL PREVIEW2_" + name + ": " + error); }
        }
        foreach (var native in new[] { false, true }) Check("HeaderProperties_" + native, () =>
        {
            var vm = Preview2GridModel(native);
            using var legacy = ShowDataGridRegressionPreview(vm, false); using var axaml = ShowDataGridRegressionPreview(vm, true);
            var leftGrid = legacy.Window.GetVisualDescendants().OfType<DataGrid>().Single();
            var rightGrid = axaml.Window.GetVisualDescendants().OfType<DataGrid>().Single();
            var caption = vm.BindingSources.Single().Fields.First().Header;
            var left = leftGrid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content?.ToString() == caption).Select(ReadHeader).Single();
            var right = rightGrid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content?.ToString() == caption).Select(ReadHeader).Single();
            Console.WriteLine("HEADERS " + JsonSerializer.Serialize(new { native, Legacy = left, Axaml = right }));
            RequireCapability(left == right, "Actual native header properties differ.");
            RequireCapability(left.FontSize == 19 && left.Background == "#ff213457" && left.FontWeight == "Bold", "The comparison did not apply authored values.");
            var designer=CreateSmokeWindow(vm);designer.Show();NativeItemsUiWait(200);designer.UpdateLayout();
            try
            {
                var shape=designer.GetVisualDescendants().OfType<Border>().Single(b=>b.Tag is BindingFieldModel f&&f.Path==vm.BindingSources.Single().Fields.First().Path);
                var text=shape.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text==caption);
                var rendered=new HeaderAppearance(shape.Background?.ToString()??"",text.Foreground?.ToString()??"",shape.BorderBrush?.ToString()??"",shape.BorderThickness.ToString(),
                    shape.Padding.ToString(),text.FontFamily.ToString(),text.FontSize,text.FontWeight.ToString(),shape.MinHeight,shape.Bounds.Height,"none");
                Console.WriteLine("DESIGNER_HEADER "+JsonSerializer.Serialize(rendered));
                RequireCapability(rendered==right,"Designer header common appearance differs from native Preview.");
            }
            finally{designer.CloseForExternalHost();}
        });
        foreach (var axaml in new[] { false, true }) Check("CanvasResize_" + axaml, () =>
        {
            var vm = CreateViewModel("Preview2Canvas"); vm.DesignWidth = 1200; vm.DesignHeight = 800;
            var button = vm.CreateControl("Button", 100, 150, bypassGridSnap: true); button.Name = "FixedButton";
            button.Width = 160; button.Height = 40;
            using var preview = ShowDataGridRegressionPreview(vm, axaml);
            foreach (var size in new[] { new Size(800, 600), new Size(1200, 800), new Size(1600, 900), new Size(953, 647) })
            {
                preview.Window.Width = size.Width; preview.Window.Height = size.Height; NativeItemsUiWait(180); preview.Window.UpdateLayout();
                var host = preview.Window.FindControl<Border>("PreviewSurfaceBorder")!;
                var root = axaml ? host.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "RootCanvas") : preview.Window.FindControl<Canvas>("PreviewCanvas")!;
                var offset = root.TranslatePoint(default, host)!.Value;
                Console.WriteLine($"CANVAS mode={axaml}; client={preview.Window.ClientSize}; root={root.Bounds}; offset={offset}");
                RequireCapability(Math.Abs(offset.X) < 1.1 && Math.Abs(offset.Y) < 1.1, "Root Canvas moved relative to the client area.");
                if (axaml)
                {
                    var actual = root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FixedButton");
                    var point = actual.TranslatePoint(default, root)!.Value;
                    RequireCapability(Math.Abs(point.X - 100) < 1.1 && Math.Abs(point.Y - 150) < 1.1, "Canvas coordinates changed on resize.");
                }
            }
        });
        Check("GridInsideLegacyCanvasGeometry",()=>
        {
            var vm=Preview2MixedModel(true);var designer=CreateSmokeWindow(vm);designer.Show();NativeItemsUiWait(150);
            try
            {
                using var legacy=ShowDataGridRegressionPreview(vm,false);using var axaml=ShowDataGridRegressionPreview(vm,true);
                var rows=new List<ConsistencyRow>();
                var surface=designer.GetVisualDescendants().OfType<FormDesigner.Views.DesignerSurface>().Single();
                rows.AddRange(CaptureConsistency(vm,"GridInCanvas","Designer",surface.Canvas,DropWrappers(designer).ToDictionary(p=>p.Key,p=>(Control)p.Value)));
                foreach(var p in new[]{legacy.Window,axaml.Window})
                {
                    var host=p.FindControl<Border>("PreviewSurfaceBorder")!;
                    var built=(Dictionary<string,Control>)typeof(PreviewWindow).GetField("_builtPreviewControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
                    rows.AddRange(CaptureConsistency(vm,"GridInCanvas",p==legacy.Window?"Legacy":"AXAML",host,built));
                }
                foreach(var expected in rows.Where(r=>r.Mode=="AXAML"))foreach(var actual in rows.Where(r=>r.Id==expected.Id&&r.Mode!="AXAML"))
                    RequireCapability(new[]{Math.Abs(actual.X-expected.X),Math.Abs(actual.Y-expected.Y),Math.Abs(actual.Width-expected.Width),Math.Abs(actual.Height-expected.Height)}.Max()<=1.1,
                        "Legacy Canvas managed geometry differs: "+JsonSerializer.Serialize(new{actual,expected}));
            }
            finally{designer.CloseForExternalHost();}
        });
        Check("CanvasOverflowGeometryPreserved",()=>
        {
            var vm=CreateViewModel("CanvasOverflow");vm.DesignWidth=900;vm.DesignHeight=620;
            var control=vm.CreateControl("Button",790,490,bypassGridSnap:true);
            control.Name="OverflowButton";control.X=790;control.Y=490;control.Width=420;control.Height=180;
            var snapshot=vm.GenerateExportedAxamlPreviewSnapshot();
            var element=System.Xml.Linq.XDocument.Parse(snapshot.ExportAxaml).Descendants().Single(e=>e.Name.LocalName=="Button");
            RequireCapability(element.Attribute("Width")?.Value=="420"&&element.Attribute("Height")?.Value=="180"&&
                element.Attribute("Canvas.Left")?.Value=="790"&&element.Attribute("Canvas.Top")?.Value=="490","Export-only snapshot clamped authored Canvas geometry.");
            RequireCapability(control.Width==420&&control.Height==180&&control.X==790&&control.Y==490,"Snapshot mutated the original Canvas document.");
        });
        foreach(var chosen in new[]{false,true})Check("ExistingModePreserved_"+chosen,()=>
        {
            var settings=JsonSerializer.Deserialize<AppSettingsModel>("{\"Preview\":{\"UseExportedAxamlPreview\":"+chosen.ToString().ToLowerInvariant()+"}}")!;
            RequireCapability(settings.Preview.HasExplicitPreviewMode,"Stored boolean lost its presence.");
            var vm=CreateViewModel("StoredMode");vm.ApplyAppSettings(settings);
            RequireCapability(vm.ResolvePreviewMode().PreviewMode==(chosen?"AXAML":"Legacy"),"Explicit mode was replaced.");
            var folder=Path.Combine(Preview2Folder,"settings-"+chosen);var service=new AppSettingsService(folder);
            settings.Preview=vm.CapturePreviewSettings();service.SaveAsync(settings).GetAwaiter().GetResult();
            var restored=service.Load();RequireCapability(restored.Preview.HasExplicitPreviewMode&&restored.Preview.UseExportedAxamlPreview==chosen,"Settings save/open changed explicit mode.");
        });
        foreach(var mode in new[]{false,true})Check("EmptyStyledDataGrid_"+mode,()=>
        {
            var vm=Preview2GridModel(true);vm.IncludeSampleData=false;var source=vm.BindingSources.Single();
            source.UseDemoData=false;source.PreviewRowMode=BindingSourceModel.PreviewRowModeSchemaOnly;
            using var p=ShowDataGridRegressionPreview(vm,mode);var grid=p.Window.GetVisualDescendants().OfType<DataGrid>().Single();
            RequireCapability(grid.ItemsSource is null||!grid.ItemsSource.Cast<object>().Any(),"Empty source acquired synthetic rows.");
            RequireCapability(grid.Columns.Count==source.Fields.Count,"Empty source lost its columns.");
            RequireCapability(grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Any(h=>h.FontSize==19),"Empty DataGrid lost its header style.");
        });
        foreach(var mode in new[]{false,true})Check("MultipleGridStyleScopes_"+mode,()=>
        {
            var vm=Preview2GridModel(false);var second=vm.CreateControl("DataGrid",10,340);second.Name="SecondStyledGrid";
            second.BindingSourceId=vm.BindingSources.Single().Id;second.Width=700;second.Height=200;second.DataGridHeaderBackground="#654321";second.DataGridHeaderFontSize=17;
            second.ShowFilterRow=second.ShowGroupPanel=second.ShowFooter=false;
            var globalStyles=Application.Current!.Styles.Count;
            using var p=ShowDataGridRegressionPreview(vm,mode);var grids=p.Window.GetVisualDescendants().OfType<DataGrid>().ToArray();
            RequireCapability(grids.Length==2,"A styled sibling DataGrid was lost.");
            RequireCapability(grids.Any(g=>g.GetVisualDescendants().OfType<DataGridColumnHeader>().Any(h=>h.FontSize==19&&h.Background?.ToString()=="#ff213457")),"First header style changed.");
            RequireCapability(grids.Any(g=>g.GetVisualDescendants().OfType<DataGridColumnHeader>().Any(h=>h.FontSize==17&&h.Background?.ToString()=="#ff654321")),"Second header style was not local.");
            RequireCapability(Application.Current.Styles.Count==globalStyles,"DataGrid styling changed global resources.");
        });
        foreach(var native in new[]{false,true})Check("CanvasButtonLogic_"+native,()=>
        {
            var vm=native?NativeGridModel("ButtonLogic"):CreateViewModel("CanvasButtonLogic");
            var button=vm.CreateControl("Button",20,20);button.Name="LogicButton";button.Text="Go";
            var target=vm.CreateControl("TextBlock",20,100);target.Name="LogicTarget";target.Text="Before";
            vm.Interactions.Add(new(){SourceControlName=button.Name,EventName=InteractionModel.EventButtonClick,ActionType=InteractionModel.ActionSetProperty,
                TargetControlName=target.Name,TargetProperty=InteractionModel.TargetPropertyText,TextTemplate="Updated by Logic"});
            using var p=ShowDataGridRegressionPreview(vm,true);
            p.Window.GetVisualDescendants().OfType<Button>().Single(b=>b.Name==button.Name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));NativeItemsUiWait(100);
            RequireCapability(p.Window.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="Updated by Logic"),"AXAML basic Button.Click did not reach the runtime Logic context.");
        });
        Check("MissingModeRemainsMissingOnUnrelatedApply",()=>
        {
            var vm=CreateViewModel("MissingMode");vm.ApplyAppSettings(JsonSerializer.Deserialize<AppSettingsModel>("{\"Preview\":{}}")!);
            var editor=new SettingsWindowViewModel(vm,"isolated-unused.json");editor.ShowPreviewRuntimeBadge=!editor.ShowPreviewRuntimeBadge;editor.ApplyToSource();
            RequireCapability(!vm.HasExplicitPreviewMode&&!vm.CapturePreviewSettings().HasExplicitPreviewMode,"An unrelated settings apply invented a user mode choice.");
            RequireCapability(!JsonSerializer.Serialize(vm.CapturePreviewSettings()).Contains("UseExportedAxamlPreview"),"Missing mode was serialized as an explicit choice.");
        });
        Check("LegacyRemainsSelectable",()=>
        {
            var vm=CreateViewModel("LegacyChoice");vm.ApplyAppSettings(new AppSettingsModel{Preview=new(){UseExportedAxamlPreview=true}});
            var editor=new SettingsWindowViewModel(vm,"isolated-unused.json");editor.UseExportedAxamlPreview=false;editor.ApplyToSource();
            RequireCapability(vm.HasExplicitPreviewMode&&vm.ResolvePreviewMode().PreviewMode=="Legacy","Legacy cannot be selected.");
            using var p=ShowDataGridRegressionPreview(vm,false);
        });
        Check("ImportedSessionKeepsLegacyWhenModeMissing",()=>
        {
            const string text="<Window xmlns=\"https://github.com/avaloniaui\"><Grid><!-- original --><ComboBox Name=\"ImportedCombo\"/><Button Name=\"ImportedButton\" Content=\"Hello\"/></Grid></Window>";
            var vm=CreateViewModel("ImportedMode");vm.ApplyAppSettings(new AppSettingsModel());
            vm.LoadAxamlImportedDocument(new FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlImportService().Import(text),"preview2.axaml");
            var decision=vm.ResolvePreviewMode();RequireCapability(decision.SourceKind=="ImportedAxaml"&&decision.PreviewMode=="Legacy"&&decision.Reason=="ImportedWorkflowLegacy","JSON default leaked into import.");
            var patch=vm.CreateActiveAxamlPatch();RequireCapability(patch.CanApply&&!patch.HasChanges&&patch.PatchedText==text,"Resolving Preview changed source.");
            vm.UseExportedAxamlPreview=true;RequireCapability(vm.ResolvePreviewMode().PreviewMode=="AXAML","Explicit experimental mode was ignored.");
        });
        foreach(var mode in new[]{false,true})Check("MainWindowExplicitMode_"+mode,()=>RequireMainWindowPreview(mode));
        Check("MainWindowGenerationFailureOffersLegacy",()=>RequireMainWindowPreview(true,exportFault:true));
        Check("MainWindowImportedDefaultUnchanged",()=>RequireMainWindowPreview(null,"<Window xmlns=\"https://github.com/avaloniaui\"><Grid><Button Name=\"ImportedModeButton\" Content=\"Source\"/></Grid></Window>"));
        Check("MissingEremexProjectPreserved",()=>
        {
            var vm=CreateViewModel("MissingEremex");
            RequireCapability(!vm.Registry.TryGetControl("Eremex.TextEditor",out _),"Fixture unexpectedly has an Eremex descriptor.");
            var standard=vm.CreateControl("Button",20,20);standard.Name="StandardWithoutEremex";
            var missing=new DesignControlModel{Type="Eremex.TextEditor",Name="SavedExperimentalEditor",X=20,Y=100,Width=300,Height=40};
            SetCustomProperty(missing,"EditorValue","Keep authored value");vm.Controls.Add(missing);
            var expected=JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot().Controls.Single(c=>c.Id==missing.Id).CustomProperties);
            vm.LoadDocumentJson(JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()));
            var restored=vm.Controls.Single(c=>c.Id==missing.Id);
            RequireCapability(restored.Type==missing.Type&&restored.Name==missing.Name,"Missing optional plugin changed control identity.");
            var designer=CreateSmokeWindow(vm);designer.Show();NativeItemsUiWait(100);
            try
            {
                foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(vm,mode))
                {
                    var built=(Dictionary<string,Control>)typeof(PreviewWindow).GetField("_builtPreviewControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p.Window)!;
                    RequireCapability(built.TryGetValue(standard.Id,out var view)&&view.GetVisualRoot()==p.Window&&
                        view.GetVisualDescendants().OfType<Button>().Prepend(view).OfType<Button>().Any(),"Missing Eremex removed the standard native control.");
                    RequireCapability(p.Window.BuildIssues.Any(),"Missing optional plugin had no diagnosis.");
                }
            }
            finally{designer.CloseForExternalHost();}
            RequireCapability(JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot().Controls.Single(c=>c.Id==missing.Id).CustomProperties)==expected,"Missing Eremex destroyed authored properties during Save/Load/Preview.");
            RequireCapability(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Eremex.")),"Missing-plugin workflow loaded native Eremex.");
        });
        Check("BrokenOptionalRuntimeDoesNotAbortDiscovery",()=>
        {
            var folder=Path.Combine(Preview2Folder,"broken-runtime-plugin");Directory.CreateDirectory(folder);
            var plugin=Path.Combine(FindRepositoryRoot(),"bin","Release","net8.0","Plugins","MinimalDesignerPlugin");
            foreach(var file in Directory.GetFiles(plugin))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)),true);
            File.WriteAllText(Path.Combine(folder,"plugin.runtime.json"),"{\"runtimeAssemblies\":[\"Missing.Audit.Native.dll\"]}");
            var vm=CreateViewModel("BrokenOptionalRuntime");var reports=new PluginLoader(new TraceDesignerLogger()).LoadFromFolder(folder,vm.Registry,true);
            RequireCapability(reports.Any(r=>r.Errors.Count>0),"Bad runtime manifest must produce a report.");
            var standard=vm.CreateControl("Button",20,20);
            foreach(var mode in new[]{false,true})using(var p=ShowDataGridRegressionPreview(vm,mode))
                RequireCapability(p.Window.GetVisualDescendants().OfType<Button>().Any(),"Broken optional plugin stopped standard Preview.");
        });
        foreach(var generationFailure in new[]{false,true})Check("ExplicitFallbackOffer_"+generationFailure,()=>
        {
            var vm=CreateViewModel("FallbackOffer");vm.CreateControl("Button",20,20).Name="RecoveryButton";vm.UseExportedAxamlPreview=true;
            var model=new ExportedAxamlPreviewModel{ProjectId=vm.CurrentProject.Id,ActiveFormId=vm.ActiveDocumentId,
                Axaml=generationFailure?"":"<UserControl xmlns=\"https://github.com/avaloniaui\"><UnavailableControl/></UserControl>",
                GenerationFailure=generationFailure?"Test generation failure":"",Document=vm.CreatePreviewDocumentSnapshot(),FallbackToLegacyPreviewOnError=true,ShowGeneratedAxamlOnError=false};
            var p=new PreviewWindow(model,vm.Registry);p.Position=new PixelPoint(-3000,-3000);p.Show();NativeItemsUiWait(400);
            try
            {
                RequireCapability(p.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text=="AXAML Preview failed","Failure silently changed mode.");
                var built=(Dictionary<string,Control>)typeof(PreviewWindow).GetField("_builtPreviewControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(p)!;
                RequireCapability(!built.ContainsKey(vm.Controls.Single().Id),"Legacy opened without consent.");
                var offer=p.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="OpenLegacyPreviewFallback");RequireCapability(offer.IsVisible,"Legacy was not offered.");
                var codes=p.ModeDiagnostics.Select(d=>d.Event).ToArray();RequireCapability(new[]{"AXAML_PREVIEW_START","AXAML_PREVIEW_FAILED","LEGACY_PREVIEW_FALLBACK_OFFERED"}.All(codes.Contains),"Mode diagnostics missing.");
                offer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));NativeItemsUiWait(150);
                RequireCapability(built.TryGetValue(vm.Controls.Single().Id,out var recovered)&&recovered.GetVisualRoot()==p&&recovered.GetVisualDescendants().OfType<Button>().Any(b=>b.Bounds.Width>0),"Requested Legacy did not render the actual control.");
                RequireCapability(vm.UseExportedAxamlPreview,"Temporary fallback changed the saved mode.");
            }
            finally{p.Close();}
        });
        if(validateDefault)
        {
            Check("NewSettingsAxamlDefault",()=>RequireCapability(new PreviewSettingsModel().UseExportedAxamlPreview&&!new PreviewSettingsModel().HasExplicitPreviewMode,"New settings must default to AXAML without inventing an explicit choice."));
            Check("MissingModeAxamlDefault",()=>
            {
                var vm=CreateViewModel("NewDefault");vm.ApplyAppSettings(JsonSerializer.Deserialize<AppSettingsModel>("{\"Preview\":{}}")!);
                RequireCapability(vm.ResolvePreviewMode().PreviewMode=="AXAML"&&!vm.HasExplicitPreviewMode,"Missing mode must resolve to AXAML.");
            });
            Check("MainWindowNewDefault",()=>RequireMainWindowPreview(null));
        }
        Console.WriteLine($"Preview Phase 2 checks: {passed}/{passed + failed}"); return failed == 0 && passed > 0 ? 0 : 1;
    }
}
