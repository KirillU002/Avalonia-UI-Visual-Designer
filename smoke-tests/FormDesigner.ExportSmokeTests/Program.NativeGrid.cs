using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel NativeGridModel(string name = "NativeGrid")
    {
        var vm = CreateViewModel(name);
        vm.CreateNewForm(DesignerLayoutModes.Grid);
        vm.DesignWidth = 600; vm.DesignHeight = 400;
        return vm;
    }

    private static MainWindowViewModel NativeGridControls()
    {
        var vm = NativeGridModel();
        vm.SurfaceGridRowDefinitions = "Auto,*"; vm.SurfaceLayoutRows = 2;
        var title = vm.CreateControl(DesignerControlTypes.TextBlock, 250, 180);
        title.Name = "NativeTitle"; title.Text = "Native Grid header";
        var grid = vm.CreateControl(DesignerControlTypes.DataGrid, 400, 300);
        grid.Name = "NativeData"; grid.GridRow = 1; grid.Margin = "10,4,20,12";
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
        return vm;
    }

    private static int RunNativeGridChecks()
    {
        var failures = 0; var total = 0;
        void Check(string name, Action test)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            total++;
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        Check("NativeGrid_CanvasCreation", () => { var vm = CreateViewModel("Canvas"); vm.CreateNewForm(DesignerLayoutModes.Absolute); RequireCapability(!vm.UsesNativeLayout && vm.SurfaceLayoutMode == "Absolute" && vm.NewFormLayoutChoice == MainWindowViewModel.NewFormGridChoice, "Explicit Canvas or one-shot creation choice changed."); });
        Check("NativeGrid_GridCreation", () => { var vm = NativeGridModel(); RequireCapability(vm.UsesNativeLayout && vm.SurfaceLayoutMode == "Grid" && vm.SurfaceLayoutRows == 1 && vm.SurfaceGridColumnDefinitions == "*", "Grid factory did not configure a native root."); });
        foreach (var type in new[] { DesignerControlTypes.Button, DesignerControlTypes.TextBox, DesignerControlTypes.TextBlock })
            Check("NativeGrid_Add_" + type, () =>
            {
                var vm = NativeGridModel(); var c = vm.TryCreateControlFromToolboxDrop(type, 420, 230, null, false, vm.ActiveFormDocument!.Id)!;
                RequireCapability(c.ParentId == "" && c.GridRow == 0 && c.GridColumn == 0 && c.X == 0 && c.Y == 0 && c.WidthMode == "Auto", "Grid insertion used fake Canvas coordinates.");
                RequireCapability(vm.StatusText.Contains("0, столбец 0"), "Deterministic insertion must be explained.");
            });
        Check("NativeGrid_JsonRoundTrip", () =>
        {
            var vm = NativeGridControls(); var c = vm.Controls.Last(); c.GridColumnSpan = 2; vm.SurfaceLayoutColumns = 2; vm.SurfaceGridColumnDefinitions = "100,*";
            var restored = CreateViewModel("NativeReload"); restored.LoadDocumentJson(vm.ExportWorkspaceJson());
            RequireCapability(restored.UsesNativeLayout && restored.SurfaceGridRowDefinitions == "Auto,*" && restored.Controls.Last().GridColumnSpan == 2 && restored.Controls.Last().WidthMode == "Auto", "Workspace lost native structure or sizing.");
            var single = CreateViewModel("NativeSingle"); single.LoadDocumentJson(JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()));
            RequireCapability(single.UsesNativeLayout && single.Controls.Last().ParentId == "" && single.SurfaceGridColumnDefinitions == "100,*", "Single-document JSON lost root layout.");
        });
        Check("NativeGrid_LegacyJson", () =>
        {
            var vm = CreateViewModel("Legacy"); vm.LoadDocumentJson("{\"DesignWidth\":600,\"DesignHeight\":400,\"Controls\":[{\"Id\":\"old\",\"Type\":\"Button\",\"Name\":\"OldButton\",\"X\":37,\"Y\":53,\"Width\":140,\"Height\":40}]}");
            vm.GenerateXaml(); RequireCapability(!vm.UsesNativeLayout && vm.Controls.Single().X == 37 && vm.GeneratedXaml.Contains("RootCanvas") && vm.GeneratedXaml.Contains("Canvas.Left=\"37\""), "Old JSON was migrated.");
        });
        Check("NativeGrid_Inspector", () =>
        {
            var vm = NativeGridModel(); var c = vm.CreateControl("Button", 0, 0); vm.SelectSingleControl(c);
            var rows = vm.PropertyGridCategories.SelectMany(g => g.Rows).ToArray();
            foreach (var key in new[] { "GridRow", "GridColumn", "GridRowSpan", "GridColumnSpan", "Margin", "HorizontalAlignment", "VerticalAlignment", "Width", "Height", "WidthMode", "HeightMode" })
                RequireCapability(rows.Any(r => r.Key == key && !r.IsReadOnly), "Native Inspector missing property " + key);
            rows.Single(r => r.Key == "GridRow").Value = "1";
            rows.Single(r => r.Key == "GridRow").CommitValue();
            RequireCapability(c.GridRow == 1, "Inspector edit did not reach placement.");
        });
        Check("NativeGrid_DefinitionsUndo", () =>
        {
            var vm = NativeGridModel(); vm.AddGridRowDefinitionCommand.Execute(null);
            RequireCapability(vm.SurfaceLayoutRows == 2, "Existing definition editor did not add a row.");
            NativeItemsUiWait(150); // Refresh the same deferred EditorCommand state that the UI consumes.
            vm.UndoEditorCommand!.Execute(null); RequireCapability(vm.SurfaceLayoutRows == 1 && vm.UsesNativeLayout, $"Undo lost root or definitions: rows={vm.SurfaceLayoutRows}; semantics={vm.LayoutSemanticsVersion}; root={vm.SurfaceLayoutMode}; undoEnabled={vm.UndoEditorCommand.CanExecute(null)}.");
            NativeItemsUiWait(150); vm.RedoEditorCommand!.Execute(null); RequireCapability(vm.SurfaceLayoutRows == 2, "Redo failed.");
        });
        Check("NativeGrid_NativeMeasureMarginAlignmentSpans", () =>
        {
            var vm = NativeGridModel(); vm.SurfaceGridRowDefinitions = "80,*"; vm.SurfaceLayoutRows = 2;
            vm.SurfaceGridColumnDefinitions = "100,2*,*"; vm.SurfaceLayoutColumns = 3;
            var c = vm.CreateControl("Button", 0, 0); c.GridRow = 1; c.GridColumn = 1; c.GridColumnSpan = 2;
            c.VerticalAlignment = "Stretch"; c.Margin = "10,5,20,15";
            var bounds = vm.BuildNativeLayout(m => new Button { Content = m.Text }).Measure(600, 400)[c.Id];
            Near(bounds.X, 110, "Native column/margin"); Near(bounds.Y, 85, "Native row/margin"); Near(bounds.Width, 470, "Native span/width"); Near(bounds.Height, 300, "Native stretch height");
            c.HorizontalAlignment = "Right"; c.Width = 120; c.WidthMode = "Fixed";
            bounds = vm.BuildNativeLayout(m => new Button()).Measure(600, 400)[c.Id]; Near(bounds.X, 460, "Right alignment"); Near(bounds.Width, 120, "Fixed width");
        });
        Check("NativeGrid_DesignerMeasureResizeSelection", () =>
        {
            var vm = NativeGridControls(); var window = CreateSmokeWindow(vm);
            try
            {
                window.Position = new PixelPoint(-3000, -3000); window.ShowInTaskbar = false; window.ShowActivated = false; window.Show(); vm.CloseStartScreenCommand.Execute(null); NativeItemsUiWait(350);
                typeof(MainWindow).GetMethod("RenderDesigner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); window.UpdateLayout();
                var before = vm.NativeLayoutBounds[vm.Controls.Last().Id];
                vm.SelectSingleControl(vm.Controls.Last()); NativeItemsUiWait(100);
                var selected = vm.NativeLayoutBounds[vm.Controls.Last().Id]; Near(selected.Width, before.Width, "Selection changed native geometry");
                vm.DesignWidth += 200; vm.DesignHeight += 100; NativeItemsUiWait(180);
                var after = vm.NativeLayoutBounds[vm.Controls.Last().Id]; RequireCapability(after.Width > before.Width + 190 && after.Height > before.Height + 90, "Form resize did not remeasure Grid.");
            }
            finally { window.CloseForExternalHost(); }
        });
        foreach (var axaml in new[] { false, true })
        {
            Check("NativeGrid_" + (axaml ? "Axaml" : "Legacy") + "_PreviewResize", () =>
            {
                var vm = NativeGridControls(); using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var window = preview.Window;
                if (axaml) RequireCapability(window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text == "AXAML Preview ready", "AXAML loader fell back.");
                var root = window.FindControl<Border>("PreviewSurfaceBorder")!.Child;
                if (root is UserControl userControl) root = userControl.Content as Control;
                RequireCapability(root is Grid, "Runtime root must be a real Grid, got " + root?.GetType().Name);
                var grid = window.GetLogicalDescendants().OfType<DataGrid>().Single(); var before = grid.Bounds.Size;
                window.Width += 240; window.Height += 120; window.UpdateLayout(); NativeItemsUiWait(200);
                grid = window.GetLogicalDescendants().OfType<DataGrid>().Single();
                RequireCapability(grid.Bounds.Width > before.Width + 220 && grid.Bounds.Height > before.Height + 100, $"Runtime DataGrid did not grow: {before} -> {grid.Bounds.Size}");
            });
        }
        Check("NativeGrid_ExportResponsiveDoesNotConvert", () =>
        {
            var vm = NativeGridControls(); vm.LayoutExportMode = MainWindowViewModel.LayoutExportModeResponsive; vm.GenerateXaml();
            var xml = XDocument.Parse(vm.GeneratedXaml); var root = xml.Root!.Elements().Last();
            RequireCapability(root.Name.LocalName == "Grid" && root.Attribute("Width") is null && root.Attribute("Height") is null && !vm.GeneratedXaml.Contains("RootCanvas") && !vm.GeneratedXaml.Contains("Canvas.Left"), "Native Grid was converted or fixed at export.");
        });
        Check("NativeGrid_MultiForm", () =>
        {
            var vm = CreateViewModel("MixedLayouts"); var canvas = vm.ActiveFormDocument!.Id;
            vm.CreateControl("Button", 30, 40); var grid = vm.CreateNewForm("Grid").Id; vm.CreateControl("TextBox", 300, 200);
            vm.SetActiveForm(canvas, "verify canvas"); RequireCapability(!vm.UsesNativeLayout && vm.Controls.Single().X == 30, "Canvas session mutated.");
            vm.SetActiveForm(grid, "verify Grid"); RequireCapability(vm.UsesNativeLayout && vm.Controls.Single().X == 0, "Grid session lost its contract.");
            var restored = CreateViewModel("MixedReload"); restored.LoadDocumentJson(vm.ExportWorkspaceJson()); restored.SetActiveForm(canvas, "reload canvas"); RequireCapability(!restored.UsesNativeLayout, "Mixed JSON provider/state leak.");
        });
        Check("NativeGrid_NestedNativeGrid", () =>
        {
            var vm = NativeGridModel(); var parent = vm.CreateControl("Grid", 0, 0);
            parent.GridRowDefinitions = "60,*"; parent.Rows = 2; parent.GridColumnDefinitions = "100,*"; parent.Columns = 2;
            var c = vm.CreateControl("TextBox", 0, 0, parent.Id); c.GridRow = 1; c.GridColumn = 1; c.VerticalAlignment = "Stretch";
            var frames = vm.BuildNativeLayout(m => new TextBox()).Measure(600, 400);
            RequireCapability(frames[c.Id].X > 100 && frames[c.Id].Y > 60 && frames[c.Id].Width > 450, "Nested native placement lost container padding or Grid definitions.");
            vm.GenerateXaml(); RequireCapability(vm.GeneratedXaml.Contains("RowDefinitions=\"60,*\"") && !vm.GeneratedXaml.Contains("Canvas.Left"), "Nested export lost layout.");
        });
        foreach (var axaml in new[] { false, true })
            Check("NativeGrid_" + (axaml ? "Axaml" : "Legacy") + "_DllBindingRows", () =>
            {
                var vm = NativeGridControls(); ConfigureDllTablePreviewRealRows(vm); vm.Controls.Last().BindingSourceId = vm.BindingSources.Single().Id;
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                var grid = preview.Window.GetLogicalDescendants().OfType<DataGrid>().Single();
                RequireCapability(grid.ItemsSource!.Cast<object>().Count() == 12 && grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Real order 001") && grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Real order 002"), "Native layout lost real DLL rows or cell values.");
            });
        Check("NativeGrid_EremexPlacement", () =>
        {
            var vm = NativeGridModel(); ConfigureEremexTextEditorVerticalSlice(vm); vm.IncludePluginRuntimeReferences = true;
            var editor = vm.Controls.Single(); editor.GridRow = 1; editor.WidthMode = "Auto"; vm.SurfaceGridRowDefinitions = "60,*"; vm.SurfaceLayoutRows = 2;
            vm.GenerateXaml(); var xml = XDocument.Parse(vm.GeneratedXaml); var element = xml.Descendants().Single(e => e.Name.LocalName == "TextEditor");
            RequireCapability(element.Attribute("Grid.Row")?.Value == "1" && element.Attribute("Canvas.Left") is null && element.Attribute("Width") is null, "Eremex bypassed native placement payload.");
        });
        Check("NativeGrid_CreateViaExistingUi", () =>
        {
            using var f = new PointerFixture();
            var choice = f.Window.FindControl<ComboBox>("NewFormLayoutSelector")!;
            choice.SelectedItem = MainWindowViewModel.NewFormGridChoice;
            f.Vm.NewFormEditorCommand!.Execute(null); f.Refresh();
            RequireCapability(f.Vm.UsesNativeLayout && f.Vm.SurfaceLayoutMode == "Grid", "New Form UI ignored the selected layout.");
        });
        Check("NativeGrid_FractionalDefinitionEditor", () =>
        {
            var vm = NativeGridModel(); vm.SurfaceGridRowDefinitions = "0,0.5*,*"; vm.SurfaceLayoutRows = 3;
            RequireCapability(vm.LayoutGridRows[1].Value == 0.5 && vm.LayoutGridRows[0].Value == 0, "Definition editor clamped native GridLength values.");
            vm.LayoutGridRows[1].Value = 0.75;
            RequireCapability(vm.SurfaceGridRowDefinitions == "0,0.75*,*", "Definition editor changed a different track.");
            RequireCapability(vm.ActiveGridDefinitionsPreview.Contains("0,0.75*,*"), "Grid toolbar preview changed native track values.");
            vm.LayoutGridRows[1].Value = 0.0005;
            RequireCapability(vm.SurfaceGridRowDefinitions == "0,0.0005*,*", "Native definition serialization rounded a valid GridLength to zero.");
            vm.LayoutGridRows[1].Value = 0.9995;
            RequireCapability(vm.SurfaceGridRowDefinitions == "0,0.9995*,*", "Native definition serialization rounded a valid star weight to one.");
        });
        Check("NativeGrid_InvalidMarginKeepsDocument", () =>
        {
            var vm = NativeGridModel(); var c = vm.CreateControl("Button", 0, 0); vm.SelectSingleControl(c);
            var row = vm.PropertyGridCategories.SelectMany(g => g.Rows).Single(r => r.Key == "Margin"); row.Value = "invalid"; row.CommitValue();
            RequireCapability(c.Margin == "0" && vm.StatusText.Contains("Margin"), "Invalid margin corrupted native layout.");
        });
        Check("NativeGrid_RowSpan", () =>
        {
            var vm = NativeGridModel(); vm.SurfaceGridRowDefinitions = "80,90,*"; vm.SurfaceLayoutRows = 3;
            var c = vm.CreateControl("Button", 0, 0); c.GridRowSpan = 2; c.VerticalAlignment = "Stretch";
            var bounds = vm.BuildNativeLayout(m => new Button()).Measure(600, 400)[c.Id]; Near(bounds.Height, 170, "Native RowSpan");
            vm.GenerateXaml(); RequireCapability(vm.GeneratedXaml.Contains("Grid.RowSpan=\"2\""), "RowSpan export missing.");
        });
        Check("NativeGrid_FileSaveCloseReopen", () =>
        {
            var vm = NativeGridControls(); var path = Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase2", "saved-grid.formdesigner.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, vm.ExportWorkspaceJson()); var reopened = CreateViewModel("ReopenGrid"); reopened.LoadDocumentJson(File.ReadAllText(path), path);
            RequireCapability(reopened.UsesNativeLayout && reopened.Controls.Last().GridRow == 1 && reopened.Controls.Last().WidthMode == "Auto", "File reload lost Grid root/placement/Auto.");
        });
        Check("NativeGrid_UnsupportedSemanticsRejected", () =>
        {
            var vm = CreateViewModel("RejectUnknown");
            try { vm.LoadDocumentJson("{\"LayoutSemanticsVersion\":99}"); throw new Exception("Unknown semantics accepted."); }
            catch (InvalidOperationException ex) { RequireCapability(ex.Message.Contains("layout"), "Unknown semantics error is not useful."); }
        });
        foreach (var axaml in new[] { false, true })
            Check("NativeGrid_" + (axaml ? "Axaml" : "Legacy") + "_EremexResize", () =>
            {
                var vm = NativeGridModel(); LoadBuiltEremexPlugin(vm); vm.IncludePluginRuntimeReferences = true;
                var model = vm.CreateControl("Eremex.DataGridControl", 0, 0); model.Name = "NativeEremex";
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                Control Find() => preview.Window.GetLogicalDescendants().OfType<Control>().Single(c => c.GetType().FullName == "Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl");
                var grid = Find(); var before = grid.Bounds.Size; preview.Window.Width += 200; preview.Window.Height += 100; preview.Window.UpdateLayout(); NativeItemsUiWait(180);
                RequireCapability(Find().Bounds.Width > before.Width + 180 && Find().Bounds.Height > before.Height + 80, "Eremex did not follow native Grid resize.");
            });
        Check("NativeGrid_PointerSelectionResizeMarquee", () =>
        {
            using var f = new PointerFixture(); f.Vm.CreateNewForm("Grid"); f.Vm.DesignWidth = 900; f.Vm.DesignHeight = 600;
            var c = f.Vm.CreateControl("Button", 300, 200); f.Refresh(); var point = f.ControlPoint(c);
            f.Click(point); f.AssertIdle(); RequireCapability(f.Vm.SelectedControl?.Id == c.Id, "Grid selection did not reach the session.");
            f.Refresh(); var grip = f.ResizePoint(); f.Down(grip); f.Move(grip + new Vector(-100, 20)); f.Up(grip + new Vector(-100, 20)); f.AssertIdle();
            RequireCapability(c.X == 0 && c.Y == 0 && c.WidthMode == "Fixed", "Resize changed placement or did not set Fixed sizing.");
            f.Refresh(); var empty = f.CanvasPoint(30, 300); f.AssertEmpty(empty); f.Down(empty); f.Move(empty + new Vector(80, 40)); RequireCapability(f.MarqueeCount == 1, "Empty Grid area lost Marquee."); f.Escape(); f.AssertIdle();
        });
        Console.WriteLine($"Native Grid checks: {total - failures}/{total}");
        return failures == 0 && total > 0 ? 0 : 1;
    }

    private static void ConfigureNativeGridExport(MainWindowViewModel vm)
    {
        var source = NativeGridControls();
        source.SurfaceLayoutRows = 3; source.SurfaceGridRowDefinitions = "Auto,Auto,*";
        source.SurfaceLayoutColumns = 2; source.SurfaceGridColumnDefinitions = "*,Auto";
        source.Controls.First().GridColumnSpan = 2;
        source.Controls.Last().GridRow = 2; source.Controls.Last().GridColumnSpan = 2;
        var input = source.CreateControl("TextBox", 0, 0); input.Name = "NativeInput"; input.GridRow = 1;
        var button = source.CreateControl("Button", 0, 0); button.Name = "NativeButton"; button.Text = "Apply"; button.GridRow = 1; button.GridColumn = 1;
        vm.LoadDocumentJson(JsonSerializer.Serialize(source.CreatePreviewDocumentSnapshot()));
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
    }
    private static void AssertNativeGridExport(SmokeContext context)
    {
        RequireContains(context.Xaml, "<Grid x:Name=\"RootLayout\"", "Native Grid export must keep its root.");
        RequireNotContains(context.Xaml, "RootCanvas", "Native export added a hidden Canvas root.");
        RequireNotContains(context.Xaml, "Canvas.Left", "Grid children exported Canvas coordinates.");
        RequireGeneratedFile(context, "MainWindow.axaml.cs");
        RequireCapability(context.GeneratedFiles.Single(f => f.Path == "MainWindow.axaml").Content.Contains("<Grid x:Name=\"RootLayout\""), "The actual generated startup file must contain Grid, not only the active XAML text.");
    }
    private static void ConfigureNativeGridMixedExport(MainWindowViewModel vm)
    {
        vm.CreateControl("Button", 30, 40); vm.CreateNewForm("Grid");
        vm.SurfaceGridRowDefinitions = "Auto,*"; vm.SurfaceLayoutRows = 2;
        vm.CreateControl("TextBlock", 0, 0).Text = "Secondary native Grid";
        var grid = vm.CreateControl("DataGrid", 0, 0); grid.GridRow = 1;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
    }
    private static void AssertNativeGridMixedExport(SmokeContext context)
    {
        var layouts = context.GeneratedFiles.Where(f => f.Path.EndsWith(".axaml") && (f.Content.Contains("RootLayout") || f.Content.Contains("RootCanvas"))).ToArray();
        RequireCapability(layouts.Any(f => f.Content.Contains("RootCanvas")) && layouts.Any(f => f.Content.Contains("<Grid x:Name=\"RootLayout\"")), "Multi-form export lost an independent layout root.");
    }
    private static void ConfigureNativeGridEremexExport(MainWindowViewModel vm)
    {
        vm.CreateNewForm("Grid"); ConfigureEremexDataGridControlVerticalSlice(vm);
        vm.SurfaceLayoutRows = 3; vm.SurfaceGridRowDefinitions = "*,*,*";
        for (var i = 0; i < vm.Controls.Count; i++) { vm.Controls[i].GridRow = i; vm.Controls[i].WidthMode = vm.Controls[i].HeightMode = "Auto"; }
        vm.IncludePluginRuntimeReferences = true;
    }
    private static void AssertNativeGridEremexExport(SmokeContext context)
    {
        var xml = XDocument.Parse(context.Xaml);
        var grids = xml.Descendants().Where(e => e.Name.LocalName == "DataGridControl").ToArray();
        RequireCapability(grids.Length == 3 && grids.All(g => g.Attribute("Canvas.Left") is null && g.Attribute("Width") is null) && grids[2].Attribute("Grid.Row")?.Value == "2", "Eremex native DataGrid export has invalid placement.");
    }
}
