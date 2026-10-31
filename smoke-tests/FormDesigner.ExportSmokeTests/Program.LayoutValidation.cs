using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static readonly Size[] ResponsiveSizes = { new(800, 600), new(1200, 800), new(1600, 900), new(1920, 1080), new(320, 220) };
    private sealed record LayoutObservation(string Scene, string Mode, double ViewportWidth, double ViewportHeight,
        string Id, string Name, string ParentId, string Layout, double X, double Y, double Width, double Height);
    private static string LayoutPhase6Folder => Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase6");
    private static void ConfigureLayoutPhase6Export(MainWindowViewModel vm, string scene)
    {
        if (scene == "Mixed") { ConfigureResponsiveMixed(vm); return; }
        // Match real startup: register the available plugin before deserializing its controls.
        if (scene == "Eremex") LoadBuiltEremexPlugin(vm);
        var source = ResponsiveScene(scene); vm.LoadDocumentJson(JsonSerializer.Serialize(source.CreatePreviewDocumentSnapshot()));
        if (scene == "Eremex") vm.IncludePluginRuntimeReferences = true;
        if (scene == "Bound") vm.IncludeSampleData = true;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
    }
    private static void AssertLayoutPhase6Export(SmokeContext context)
    {
        var xml = System.Xml.Linq.XDocument.Parse(context.Xaml);
        RequireCapability(xml.Root!.Elements().Any(e => e.Name.LocalName == "Grid") && !context.Xaml.Contains("RootCanvas")
            && !context.Xaml.Contains("GuideOverlay") && !context.Xaml.Contains("NativeGridDragFrame"), "Native export root/overlays are incorrect.");
        if (context.Scenario.Name is "LayoutPhase6_Eremex" or "LayoutPhase6_Mixed")
            RequireContains(File.ReadAllText(Path.Combine(context.ProjectPath,"App.axaml")), "<theme:DeltaDesignTheme />", "Actual export must retain the plugin application style contribution.");
    }

    private static MainWindowViewModel ResponsiveScene(string scene)
    {
        if (scene == "Mixed") { var mixed=CreateViewModel("MixedResponsive"); ConfigureResponsiveMixed(mixed); return mixed; }
        var vm = NestedModel(); vm.DesignWidth = 800; vm.DesignHeight = 600;
        vm.SurfaceGridRowDefinitions = "Auto,*"; vm.SurfaceLayoutRows = 2;
        vm.SurfaceGridColumnDefinitions = scene is "A" or "Eremex" or "Constraints" or "Bound" ? "2*,*" : "*"; vm.SurfaceLayoutColumns = scene is "A" or "Eremex" or "Constraints" or "Bound" ? 2 : 1;
        DesignControlModel Leaf(string type, string name, string parent = "")
        {
            var c = vm.CreateControl(type, 0, 0, parent); c.Name = name; c.Text = name;
            c.FontFamily = "Segoe UI"; c.FontSize = 16; c.Padding = 8; return c;
        }
        if (scene is "A" or "Eremex" or "Constraints" or "Bound")
        {
            var title = Leaf("TextBlock", "Title"); title.Text = "Заголовок"; title.FontSize = 20; title.Margin = "12"; title.GridColumnSpan = vm.SurfaceLayoutColumns;
            var grid = Leaf("DataGrid", "Results"); grid.GridRow = 1; grid.Margin = "12,4,8,12";
            var side = NestedHost(vm, DesignerLayoutModes.Stack, 1); side.Name = "SidePanel"; side.GridRow = 1; side.Margin = "8,4,12,12"; side.Padding = 8; side.BorderThickness = 1; side.LayoutSpacing = 9;
            var save = Leaf("Button", "SaveButton", side.Id); save.Text = "Сохранить"; save.Margin = "2";
            var input = Leaf("TextBox", "NameInput", side.Id); input.StackOrder = 1; input.Text = ""; input.PlaceholderText = "Имя"; input.Margin = "2";
            if(scene=="Bound")
            {
                var source=ProductsSource(); source.UseDemoData=true; vm.BindingSources.Add(source); grid.BindingSourceId=source.Id;
                grid.AllowGrouping=false; grid.ShowGroupPanel=false; grid.ShowFilterRow=false; grid.ShowFooter=false; vm.IncludeSampleData=true;
            }
            if (scene == "Constraints")
            {
                LayoutPlacementProperties.SetLiteral(save, "MinWidth", 140d); LayoutPlacementProperties.SetLiteral(save, "MaxWidth", 180d);
                LayoutPlacementProperties.SetLiteral(input, "MinHeight", 50d); LayoutPlacementProperties.SetLiteral(input, "MaxHeight", 65d);
            }
            if (scene == "Eremex")
            {
                LoadBuiltEremexPlugin(vm); vm.IncludePluginRuntimeReferences = true; vm.Controls.Remove(grid); vm.Controls.Remove(input);
                var data = Leaf("Eremex.DataGridControl", "Results"); data.GridRow = 1; data.Margin = grid.Margin;
                var editor = Leaf("Eremex.TextEditor", "NameInput", side.Id); editor.StackOrder = 1; editor.Margin = "2";
            }
        }
        else if (scene == "B")
        {
            var dock = NestedHost(vm, DesignerLayoutModes.Dock); dock.Name = "HeaderDock";
            var menu = Leaf("Button", "MenuButton", dock.Id); menu.Text = "Меню"; menu.WidthMode = menu.HeightMode = "Fixed"; menu.Width = 100; menu.Height = 40;
            LayoutPlacementProperties.SetLiteral(menu, LayoutPlacementProperties.DockKey, "Left");
            var title = Leaf("TextBlock", "DockTitle", dock.Id); title.Text = "Заголовок"; title.FontSize = 20; title.Margin = "12,0"; title.StackOrder = 1;
            var wrap = NestedHost(vm, DesignerLayoutModes.Flex); wrap.Name = "Flow"; wrap.GridRow = 1; wrap.LayoutOrientation = "Horizontal"; wrap.Padding = 9; wrap.BorderThickness = 2;
            for (var i = 0; i < 9; i++) { var c = Leaf("Button", "Item" + i, wrap.Id); c.WidthMode = c.HeightMode = "Fixed"; c.Width = 150; c.Height = 44; c.Margin = "8"; c.StackOrder = i; }
        }
        else
        {
            var stack = NestedHost(vm, DesignerLayoutModes.Stack); stack.Name = "OuterStack"; stack.GridRow = 1; stack.Margin = "12"; stack.LayoutSpacing = 7;
            var border = Leaf("Border", "MixedBorder", stack.Id); border.ChildLayoutMode = DesignerLayoutModes.Absolute; border.HeightMode = "Fixed"; border.Height = 240; border.Padding = 13; border.BorderThickness = 2;
            var canvas = NestedHost(vm, DesignerLayoutModes.Absolute, 0, border.Id); canvas.Name = "InnerCanvas"; canvas.WidthMode = canvas.HeightMode = "Fixed"; canvas.Width = 500; canvas.Height = 200;
            var button = Leaf("Button", "FreeButton", canvas.Id); button.WidthMode = button.HeightMode = "Fixed"; button.Width = 120; button.Height = 40; button.X = 37; button.Y = 53;
            var input = Leaf("TextBox", "FreeInput", canvas.Id); input.WidthMode = input.HeightMode = "Fixed"; input.Width = 180; input.Height = 40; input.X = 190; input.Y = 53;
        }
        return vm;
    }

    private static void ConfigureResponsiveMixed(MainWindowViewModel vm)
    {
        LoadBuiltEremexPlugin(vm);
        vm.LoadDocumentJson(JsonSerializer.Serialize(ResponsiveScene("A").CreatePreviewDocumentSnapshot()));
        vm.IncludePluginRuntimeReferences=true; vm.DataGridExportMode=MainWindowViewModel.DataGridExportModeReal;
        var primary=vm.ActiveFormDocument!.Id; vm.CreateNewForm("Grid");
        var editor=vm.CreateControl("Eremex.TextEditor",0,0); editor.Name="SecondaryEditor";
        vm.CreateNewForm(); var legacy=vm.CreateControl("Button",37,53,null,true); legacy.Name="LegacyButton";
        vm.SetActiveForm(primary,"mixed-responsive-fixture");
    }

    private static IReadOnlyList<LayoutObservation> ObserveLayout(MainWindowViewModel vm, string scene, string mode, Control root, Func<DesignControlModel, Control> find)
    {
        var views = vm.Controls.ToDictionary(c=>c.Id,find);
        return vm.Controls.Select(model =>
        {
            var view = views[model.Id];
            var ancestor = view.GetVisualAncestors().OfType<Control>().FirstOrDefault(c=>views.Values.Contains(c));
            var parentId = ancestor is null ? "" : views.Single(p=>ReferenceEquals(p.Value,ancestor)).Key;
            RequireCapability(parentId == model.ParentId, $"{mode} {model.Name} visual hierarchy differs: expected {model.ParentId}, actual {parentId}.");
            var point = view.TranslatePoint(default, root) ?? throw new InvalidOperationException("Control is detached: " + model.Name);
            return new LayoutObservation(scene, mode, root.Bounds.Width, root.Bounds.Height, model.Id, model.Name, model.ParentId,
                vm.GetLayoutModeForParent(parentId), point.X, point.Y, view.Bounds.Width, view.Bounds.Height);
        }).ToArray();
    }
    private static IReadOnlyList<LayoutObservation> ObserveDesigner(MainWindow window, MainWindowViewModel vm, string scene)
    {
        var surface = window.GetVisualDescendants().OfType<DesignerSurface>().Single();
        var wrappers = (Dictionary<string, Border>)typeof(MainWindow).GetField("_wrapperByControlId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return ObserveLayout(vm, scene, "Designer", surface.Canvas, model => wrappers[model.Id]);
    }
    private static IReadOnlyList<LayoutObservation> ObservePreview(PreviewWindow window, MainWindowViewModel vm, string scene, bool axaml)
    {
        var host = window.FindControl<Border>("PreviewSurfaceBorder")!;
        return ObserveLayout(vm, scene, axaml ? "AXAML" : "Legacy", host, model => host.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == model.Name)
            ?? throw new InvalidOperationException($"Missing {model.Name} in {(axaml ? "AXAML" : "Legacy")}: " + string.Join(",", host.GetVisualDescendants().OfType<Control>().Where(c => c.Name is not null).Select(c => c.GetType().Name+":"+c.Name))));
    }
    private static void AssertLayoutMatches(IReadOnlyList<LayoutObservation> expected, IReadOnlyList<LayoutObservation> actual, double tolerance = 1.1)
    {
        var differences = new List<string>();
        foreach (var e in expected)
        {
            var a = actual.Single(c => c.Name == e.Name);
            var delta = new[] { Math.Abs(a.X-e.X), Math.Abs(a.Y-e.Y), Math.Abs(a.Width-e.Width), Math.Abs(a.Height-e.Height) }.Max();
            if (delta > tolerance || Math.Abs(a.ViewportWidth-e.ViewportWidth) > .1 || Math.Abs(a.ViewportHeight-e.ViewportHeight) > .1)
                differences.Add($"{e.Name}: {e.Mode}=({e.X},{e.Y},{e.Width},{e.Height}); {a.Mode}=({a.X},{a.Y},{a.Width},{a.Height}); viewport={a.ViewportWidth}x{a.ViewportHeight}; delta={delta}");
        }
        RequireCapability(differences.Count == 0, string.Join("; ", differences));
    }
    private static int RunLayoutValidationChecks()
    {
        Directory.CreateDirectory(LayoutPhase6Folder); var passed = 0; var failed = 0;
        var observations = new List<LayoutObservation>();
        void Check(string name, Action body)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER"); if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { body(); passed++; Console.WriteLine("PASS LAYOUT6_" + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL LAYOUT6_" + name + ": " + ex); }
        }
        Check("BasicNativeAxamlNamesAndLogic", () =>
        {
            var vm = NestedModel(); var button = vm.CreateControl("Button", 0, 0); button.Name = "ActionButton"; button.Text = "Действие";
            var text = vm.CreateControl("TextBlock", 0, 0); text.Name = "ActionTarget";
            vm.Interactions.Add(new InteractionModel { SourceControlName = button.Name, EventName = InteractionModel.EventButtonClick,
                ActionType = InteractionModel.ActionSetProperty, TargetControlName = text.Name, TargetProperty = "Text", TextTemplate = "Изменено" });
            using var preview = ShowDataGridRegressionPreview(vm, true);
            var actual = preview.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Действие");
            actual.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); NativeItemsUiWait(100);
            RequireCapability(preview.Window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Изменено"), "Basic native AXAML Preview lost control names/runtime Logic.");
        });
        RunLayoutPropertyChecks(Check);
        foreach (var scene in new[] { "A", "B", "C", "Eremex", "Constraints", "Mixed", "Bound" })
        {
            if (Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER") is { Length: > 0 } filter && !scene.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var vm = ResponsiveScene(scene); vm.GenerateXaml(); File.WriteAllText(Path.Combine(LayoutPhase6Folder, scene + ".axaml"), vm.GeneratedXaml);
            if (scene == "Eremex")
            {
                Console.WriteLine("EREMEX_CONTRIBUTIONS " + string.Join(";", vm.GenerateExportedAxamlPreviewSnapshot().RuntimePreviewContributions.Select(p => p.ProviderId + ":" + string.Join(",", p.Assemblies.Select(a => a.FullName)))));
                File.WriteAllLines(Path.Combine(LayoutPhase6Folder, "eremex-namespace-mappings.txt"), AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("Eremex", StringComparison.Ordinal))
                    .Select(a => a.FullName + ":" + string.Join(";", a.GetCustomAttributesData().Where(c => c.AttributeType.Name == "XmlnsDefinitionAttribute").Select(c => string.Join(",", c.ConstructorArguments.Select(v => v.Value))))));
            }
            var applicationStyleCount=Application.Current!.Styles.Count;
            var designer = CreateSmokeWindow(vm); designer.Width = 1600; designer.Height = 1000; designer.Show(); NativeItemsUiWait(120);
            try
            {
                using var legacy = ShowDataGridRegressionPreview(vm, false); using var axaml = ShowDataGridRegressionPreview(vm, true, window =>
                {
                    if (scene != "Eremex") return;
                    var snapshot = (ExportedAxamlPreviewModel)typeof(PreviewWindow).GetField("_exportedAxamlPreview", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                    Console.WriteLine("EREMEX_WINDOW_CONTRIBUTIONS " + snapshot.RuntimePreviewContributions.Count);
                });
                foreach (var size in ResponsiveSizes)
                    Check(scene + "_" + size.Width + "x" + size.Height, () =>
                    {
                        vm.DesignWidth = size.Width; vm.DesignHeight = size.Height;
                        legacy.Window.Width = axaml.Window.Width = size.Width; legacy.Window.Height = axaml.Window.Height = size.Height;
                        NativeItemsUiWait(160); designer.UpdateLayout(); legacy.Window.UpdateLayout(); axaml.Window.UpdateLayout();
                        var status = axaml.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!;
                        RequireCapability(status.Text == "AXAML Preview ready", "AXAML Preview fell back: " + ToolTip.GetTip(status));
                        var d = ObserveDesigner(designer, vm, scene); var l = ObservePreview(legacy.Window, vm, scene, false); var a = ObservePreview(axaml.Window, vm, scene, true);
                        observations.AddRange(d); observations.AddRange(l); observations.AddRange(a);
                        AssertLayoutMatches(a, l); AssertLayoutMatches(a, d);
                        if(scene=="Bound")
                            RequireCapability(legacy.Window.GetVisualDescendants().OfType<DataGrid>().Single().ItemsSource!.Cast<object>().Any()
                                && axaml.Window.GetVisualDescendants().OfType<DataGrid>().Single().ItemsSource!.Cast<object>().Any(),"Explicit manual BindingSource lost its local fixture rows.");
                    });
                Check(scene+"_ThemeScope",()=>
                {
                    RequireCapability(Application.Current!.Styles.Count==applicationStyleCount,"Native form styling mutated Application styles.");
                    if(scene!="Mixed") return;
                    var primary=vm.ActiveFormDocument!.Id; vm.SetActiveForm(vm.CurrentProject.Forms.Single(f=>f.Document.LayoutSemanticsVersion==1).Id,"canvas-theme-reset"); NativeItemsUiWait(80);
                    var owned=(System.Collections.ICollection)typeof(MainWindow).GetField("_nativeCanvasThemeStyles",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(designer)!;
                    RequireCapability(owned.Count==0,"Native scoped theme remained on legacy Canvas after switching forms.");
                    vm.SetActiveForm(primary,"native-theme-restore"); NativeItemsUiWait(80);
                });
            }
            finally { designer.CloseForExternalHost(); }
        }
        File.WriteAllText(Path.Combine(LayoutPhase6Folder, "visual-observations.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Layout validation checks: {passed}/{passed+failed}"); return failed == 0 ? 0 : 1;
    }

    private static int RunLayoutPerformanceChecks()
    {
        Directory.CreateDirectory(LayoutPhase6Folder); var measurements = new List<object>();
        foreach (var count in new[] { 100, 300, 500 })
        {
            var timer = Stopwatch.StartNew(); var vm = NestedModel(); vm.DesignWidth = 1200; vm.DesignHeight = 800;
            vm.SurfaceGridColumnDefinitions = "*"; vm.SurfaceLayoutColumns = 1;
            var stack = NestedHost(vm, DesignerLayoutModes.Flex); stack.LayoutOrientation = "Horizontal";
            for (var i = 1; i < count; i++) { var c = NestedLeaf(vm, stack, "Item " + i, i); c.Width = 75; c.Height = 28; }
            var createMs = timer.Elapsed.TotalMilliseconds; var json = vm.ExportWorkspaceJson();
            timer.Restart(); var loaded = CreateViewModel("Performance"); loaded.LoadDocumentJson(json); var openMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart(); var window = CreateSmokeWindow(vm); window.Show(); NativeItemsUiWait(120); var showMs = timer.Elapsed.TotalMilliseconds;
            try
            {
                var point = NestedPoint(vm, stack.Id, .5, .5); var samples = new List<double>();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 100; i++) { timer.Restart(); RequireCapability(vm.ResolveLayoutDropTarget(point).IsValid, "Performance target disappeared."); samples.Add(timer.Elapsed.TotalMilliseconds); }
                var hitAllocated = GC.GetAllocatedBytesForCurrentThread()-allocated;
                timer.Restart(); var factory = typeof(MainWindow).GetMethod("CreatePreviewControl", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var tree = vm.BuildNativeLayout(c => (Control)factory.Invoke(window, new object[] { c })!); tree.Measure(1200, 800); var measureMs = timer.Elapsed.TotalMilliseconds;
                var moving=vm.Controls.Last(); var firstChild=vm.NativeContainerMeasurements[stack.Id].Children.First().Bounds;
                var dropPoint=firstChild.TopLeft+new Vector(1,1); var target=vm.ResolveLayoutDropTarget(dropPoint,moving);
                timer.Restart(); var preview=vm.EvaluateGridDropPreview(target,moving,c=>(Control)factory.Invoke(window,new object[]{c})!); var dropPreviewMs=timer.Elapsed.TotalMilliseconds;
                timer.Restart(); RequireCapability(vm.CommitGridControlDrop(moving,preview,dropPoint),"Measured native Drop did not commit.");
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(vm.NativeGridMeasurementKey!=vm.GridDropLayoutKey && DateTime.UtcNow<deadline) NativeItemsUiWait(5);
                window.UpdateLayout(); var dropCommitUiMs=timer.Elapsed.TotalMilliseconds;
                RequireCapability(vm.NativeGridMeasurementKey==vm.GridDropLayoutKey && moving.StackOrder==0,"Drop did not reach the rendered snapshot.");
                var selected=vm.Controls[1]; timer.Restart(); vm.SelectSingleControl(selected); deadline=DateTime.UtcNow.AddSeconds(10);
                while(!vm.PropertyGridCategories.SelectMany(g=>g.Rows).Any(r=>r.Key=="Name" && r.ContextControlId==selected.Id) && DateTime.UtcNow<deadline) NativeItemsUiWait(5);
                window.UpdateLayout(); var selectionMs=timer.Elapsed.TotalMilliseconds;
                RequireCapability(vm.PropertyGridCategories.SelectMany(g=>g.Rows).Any(r=>r.Key=="Name" && r.ContextControlId==selected.Id),"Inspector did not follow measured selection.");
                timer.Restart(); var key = vm.ActiveFormDocument!.Id; vm.CreateNewForm(); vm.SetActiveForm(key, "perf"); NativeItemsUiWait(30); var switchMs = timer.Elapsed.TotalMilliseconds;
                var result = new { count, createMs, openMs, showMs, measureMs, dropPreviewMs, dropCommitUiMs, selectionMs, switchMs, hitMedianMs = samples.OrderBy(v=>v).ElementAt(50), hitAllocated, managedBytes = GC.GetTotalMemory(false) };
                measurements.Add(result); Console.WriteLine("PERFORMANCE " + JsonSerializer.Serialize(result));
            }
            finally { window.CloseForExternalHost(); }
        }
        File.WriteAllText(Path.Combine(LayoutPhase6Folder, "performance.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
}
