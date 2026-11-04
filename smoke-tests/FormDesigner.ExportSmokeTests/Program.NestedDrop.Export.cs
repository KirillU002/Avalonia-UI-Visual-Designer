using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel NestedRuntimeScene()
    {
        var vm = NestedModel(); vm.SurfaceGridColumnDefinitions = "*"; vm.SurfaceLayoutColumns = 1;
        vm.SurfaceGridRowDefinitions = "Auto,*,Auto"; vm.SurfaceLayoutRows = 3;
        var toolbar = NestedHost(vm, DesignerLayoutModes.Stack); toolbar.Name = "HeaderStack"; toolbar.LayoutOrientation = "Horizontal"; toolbar.LayoutSpacing = 8;
        var input = vm.CreateControl("TextBox", 0, 0, toolbar.Id); input.Name = "SearchInput"; input.WidthMode = "Fixed"; input.Width = 220; input.StackOrder = 0;
        var button = vm.CreateControl("Button", 0, 0); button.Name = "ApplyButton"; button.Text = "Поиск";
        MeasureNested(vm); NestedMove(vm, button, NestedPoint(vm, toolbar.Id, .85, .5));
        vm.Interactions.Add(new InteractionModel { SourceControlName = button.Name, EventName = InteractionModel.EventButtonClick,
            ActionType = InteractionModel.ActionSetProperty, TargetControlName = input.Name, TargetProperty = "Text", TextTemplate = "nested runtime action" });
        var dock = NestedHost(vm, DesignerLayoutModes.Dock); dock.Name = "DockBody"; dock.GridRow = 1;
        var commands = NestedHost(vm, DesignerLayoutModes.Stack, 0, dock.Id); commands.Name = "DockCommands";
        commands.WidthMode = "Fixed"; commands.Width = 150; commands.LayoutSpacing = 6;
        LayoutPlacementProperties.SetLiteral(commands, LayoutPlacementProperties.DockKey, "Left");
        NestedLeaf(vm, commands, "Команды"); NestedLeaf(vm, commands, "Настройки", 1);
        var grid = vm.CreateControl("DataGrid", 0, 0, dock.Id); grid.Name = "NestedDataGrid"; grid.StackOrder = 1;
        var wrap = NestedHost(vm, DesignerLayoutModes.Flex); wrap.Name = "FooterWrap"; wrap.GridRow = 2; wrap.LayoutOrientation = "Horizontal";
        for (var i = 0; i < 7; i++) { var item = NestedLeaf(vm, wrap, "Footer " + i, i); item.Name = "Footer" + i; item.Width = 100; item.Margin = "4"; }
        return vm;
    }

    private static void ConfigureNestedPhase5Export(MainWindowViewModel vm)
    {
        var source = NestedRuntimeScene(); vm.LoadDocumentJson(JsonSerializer.Serialize(source.CreatePreviewDocumentSnapshot()));
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
    }
    private static XElement Named(XDocument document, string name) => document.Descendants().Single(e => e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == name);

    private static void AssertNestedPhase5Export(SmokeContext context)
    {
        var document = XDocument.Parse(context.Xaml);
        RequireCapability(document.Root!.Elements().Any(e => e.Name.LocalName == "Grid"), "Generated root is not Grid.");
        var header = Named(document, "HeaderStack"); var button = Named(document, "ApplyButton");
        RequireCapability(button.Ancestors().Any(e => e.Name.LocalName == "StackPanel" && e.Parent == header), "Export flattened the moved toolbar button.");
        var dock = Named(document, "DockBody"); var commands = Named(document, "DockCommands"); var grid = Named(document, "NestedDataGrid");
        RequireCapability(dock.Name.LocalName == "DockPanel" && commands.Parent == dock && commands.Attribute("DockPanel.Dock")?.Value == "Left"
            && grid.Ancestors().Contains(dock), "DockPanel hierarchy/placement was flattened.");
        var wrap = Named(document, "FooterWrap").Elements().Single(e => e.Name.LocalName == "WrapPanel");
        RequireCapability(wrap.Elements().Select(e => e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value).SequenceEqual(Enumerable.Range(0, 7).Select(i => "Footer" + i)), "WrapPanel child order was lost.");
        RequireCapability(!context.Xaml.Contains("Canvas.Left") && !context.Xaml.Contains("GuideOverlay") && !context.Xaml.Contains("InsertionIndicator"), "Export contains stale placement or designer overlays.");
        RequireContains(context.CSharp, "nested runtime action", "Moved button lost generated Logic.");
    }

    private static void ConfigureNestedPhase5EremexExport(MainWindowViewModel vm)
    {
        ConfigureNestedPhase5Export(vm); LoadBuiltEremexPlugin(vm); vm.IncludePluginRuntimeReferences = true;
        var body = vm.Controls.Single(c => c.Name == "DockBody"); var header = vm.Controls.Single(c => c.Name == "HeaderStack");
        vm.Controls.Remove(vm.Controls.Single(c => c.Name == "NestedDataGrid"));
        // Explicit user setting for the intermediate empty DockPanel, never an automatic drop conversion.
        LayoutPlacementProperties.SetLiteral(body, LayoutPlacementProperties.LastChildFillKey, false);
        var grid = vm.CreateControl("Eremex.DataGridControl", 0, 0); grid.Name = "NestedEremexGrid";
        MeasureNested(vm); NestedMove(vm, grid, NestedPoint(vm, body.Id, .95, .5));
        LayoutPlacementProperties.SetLiteral(body, LayoutPlacementProperties.LastChildFillKey, true);
        var editor = vm.CreateControl("Eremex.TextEditor", 0, 0); editor.Name = "NestedEremexEditor"; editor.WidthMode = "Fixed"; editor.Width = 150; editor.HeightMode = "Fixed"; editor.Height = 32;
        MeasureNested(vm); NestedMove(vm, editor, NestedPoint(vm, header.Id, .95, .5));
    }
    private static void AssertNestedPhase5EremexExport(SmokeContext context)
    {
        var document = XDocument.Parse(context.Xaml); var grid = Named(document, "NestedEremexGrid"); var editor = Named(document, "NestedEremexEditor");
        RequireCapability(grid.Name.LocalName == "DataGridControl" && grid.Parent?.Name.LocalName == "DockPanel" && grid.Attribute("DockPanel.Dock") is not null
            && editor.Name.LocalName == "TextEditor" && editor.Parent?.Name.LocalName == "StackPanel" && grid.Attribute("Canvas.Left") is null, "Plugin descriptor lost native parent/placement.");
    }
    private static void ConfigureNestedPhase5MultiFormExport(MainWindowViewModel vm)
    {
        ConfigureNestedPhase5Export(vm); var gridForm = vm.ActiveFormDocument!.Id;
        vm.CreateNewForm(DesignerLayoutModes.Absolute); var button = vm.CreateControl("Button", 37, 53, null, true); button.Name = "ApplyButton";
        vm.SetActiveForm(gridForm, "nested-export");
    }
    private static void AssertNestedPhase5MultiFormExport(SmokeContext context)
    {
        AssertNestedPhase5Export(context);
        var windows = context.GeneratedFiles.Where(f => f.Path.EndsWith(".axaml") && f.Content.Contains("x:Class=") && f.Content.Contains("ApplyButton")).ToArray();
        RequireCapability(windows.Length == 2 && windows.Any(f => f.Content.Contains("<DockPanel")) && windows.Any(f => f.Content.Contains("RootCanvas") && f.Content.Contains("Canvas.Left=\"37\"")), "Mixed form export lost independent hierarchy/coordinates.");
    }

    private static void AssertNestedDockWrapPreview(bool axaml)
    {
        var vm = NestedRuntimeScene(); using var preview = ShowDataGridRegressionPreview(vm, axaml);
        if (axaml) RequireCapability(preview.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text == "AXAML Preview ready", "Native AXAML Preview fell back.");
        var dock = preview.Window.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Name == "DockBody");
        var wrap = preview.Window.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.GetVisualAncestors().OfType<Control>().Any(c => c.Name == "FooterWrap"));
        var grid = preview.Window.GetVisualDescendants().OfType<DataGrid>().Single();
        RequireCapability(dock.LastChildFill && grid.GetVisualAncestors().Contains(dock) && wrap.Children.Count == 7, "Preview lost Dock/Wrap/nested DataGrid hierarchy.");
        var before = grid.Bounds.Size; var wrapBefore = wrap.Bounds.Height;
        var rowsBefore = wrap.Children.Select(c => c.Bounds.Y).Distinct().Count();
        preview.Window.Width += 250; preview.Window.Height += 100; NativeItemsUiWait(180); preview.Window.UpdateLayout();
        Console.WriteLine($"OBSERVED nested-preview mode={axaml}; gridBefore={before}; gridAfter={grid.Bounds.Size}; wrapBefore={wrapBefore}; wrapAfter={wrap.Bounds}; dock={dock.Bounds}; client={preview.Window.ClientSize}; desired={dock.DesiredSize}");
        var rowsAfter = wrap.Children.Select(c => c.Bounds.Y).Distinct().Count();
        Console.WriteLine($"OBSERVED wrap rows={rowsBefore}->{rowsAfter}; allocatedHeight={wrap.Bounds.Height}; desiredHeight={wrap.DesiredSize.Height}");
        // Avalonia may retain the larger allocated Auto-row slot during this layout pass;
        // actual child positions and DesiredSize, rather than the slot alone, prove reflow.
        RequireCapability(grid.Bounds.Width > before.Width + 200 && grid.Bounds.Height > before.Height + 50 && rowsBefore > 1 && rowsAfter == 1
            && wrap.DesiredSize.Height < wrapBefore, "Preview resize did not preserve Dock fill/real child reflow.");
    }
}
