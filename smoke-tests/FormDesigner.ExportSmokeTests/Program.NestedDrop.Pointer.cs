using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static PointerFixture NestedPointerFixture()
    {
        var fixture = GridEditorPointerFixture(); fixture.Vm.DesignWidth = 660; fixture.Vm.DesignHeight = 420;
        fixture.Vm.SurfaceGridRowDefinitions = "*"; fixture.Vm.SurfaceLayoutRows = 1;
        fixture.Vm.SurfaceGridColumnDefinitions = "220,220,*"; fixture.Vm.SurfaceLayoutColumns = 3;
        fixture.Refresh(); return fixture;
    }
    private static void NestedPointerMove(PointerFixture f, DesignControlModel model, Point target, bool frame = false, bool capture = false)
    {
        var start = f.ControlPoint(model);
        if (frame)
        {
            var wrappers = (Dictionary<string, Border>)typeof(MainWindow).GetField("_wrapperByControlId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Window)!;
            start = wrappers[model.Id].TranslatePoint(new Point(2, 2), f.Window)!.Value;
        }
        var hit = f.Window.InputHitTest(start) as Visual;
        f.Down(start);
        RequireCapability(f.Window.CurrentDesignerInteractionMode == DesignerInteractionMode.GridControlPending,
            "Native press failed: type=" + model.Type + "; point=" + start + "; hit=" + hit?.GetType().Name + "; ancestors="
            + string.Join("/", hit?.GetVisualAncestors().Prepend(hit).OfType<Control>().Select(c => c.GetType().Name + ":" + (c.Tag as DesignControlModel)?.Name) ?? Array.Empty<string>())
            + "; diagnostics=" + System.Text.Json.JsonSerializer.Serialize(f.Window.DesignerPointerDiagnostics.TakeLast(4)));
        var end = f.CanvasPoint(target.X, target.Y); f.Move(end); f.ExpectMode(DesignerInteractionMode.MovingGridControl); f.NoMarquee();
        RequireCapability(f.Surface.GuideOverlay.ZIndex > f.Surface.Canvas.ZIndex, "Native insertion guide is hidden behind opaque containers.");
        if (capture)
        {
            NativeItemsUiWait(40); f.Window.UpdateLayout();
            var content = (Control)f.Window.Content!;
            using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height));
            image.Render(content); image.Save(Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase5", "nested-insertion.png"));
        }
        f.Up(end); f.AssertIdle(); f.Refresh();
    }
    private static DesignControlModel RoutedNestedToolbox(PointerFixture f, string parent, string type = "Button", double x = .5, double y = .5)
    {
        var point = NestedPoint(f.Vm, parent, x, y); var data = GridToolboxData(f, type);
        var before = f.Vm.Controls.Count;
        var over = RouteGridToolbox(f, data, DragDrop.DragOverEvent, point);
        RequireCapability(over.Handled && over.DragEffects == DragDropEffects.Copy && f.Vm.Controls.Count == before, "Routed nested DragOver failed or mutated the document: " + f.Vm.StatusText);
        RequireCapability(f.Surface.GuideOverlay.Children.Count > 0 && !f.Surface.GuideOverlay.IsHitTestVisible && f.Surface.GuideOverlay.ZIndex > f.Surface.Canvas.ZIndex, "Nested overlay blocks input, is missing, or is hidden behind containers.");
        var drop = RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.Refresh(); f.AssertIdle();
        RequireCapability(drop.Handled && f.Vm.Controls.Count == before + 1 && f.Surface.GuideOverlay.Children.Count == 0 && f.Surface.GuideOverlay.ZIndex == 0, "Routed nested Drop failed or left an overlay/layer override: " + f.Vm.StatusText);
        return f.Vm.SelectedControl!;
    }

    private static void RunNestedPointerChecks(Action<string, Action> check)
    {
        foreach (var mode in new[] { DesignerLayoutModes.Stack, DesignerLayoutModes.Flex, DesignerLayoutModes.Dock, DesignerLayoutModes.Absolute })
            check("RoutedToolbox_" + mode, () =>
            {
                using var f = NestedPointerFixture(); var host = NestedHost(f.Vm, mode);
                if (mode == DesignerLayoutModes.Dock) LayoutPlacementProperties.SetLiteral(host, LayoutPlacementProperties.LastChildFillKey, false);
                f.Refresh(); var child = RoutedNestedToolbox(f, host.Id, "TextBox", .1, .1);
                RequireCapability(child.ParentId == host.Id && (mode == DesignerLayoutModes.Absolute || child.X == 0 && child.Y == 0), "Visual drop lost the nested parent.");
                f.Vm.GenerateXaml(); var tree = f.Vm.GeneratedXaml;
                RequireCapability(!tree.Contains("InsertionIndicator") && !tree.Contains("GuideOverlay"), "Designer overlay leaked into export.");
                // Repeat click must retain selection without arming marquee or pending move after release.
                f.Click(f.ControlPoint(child)); f.AssertIdle(); RequireCapability(f.Vm.SelectedControl?.Id == child.Id, "Selection/Inspector was lost after Toolbox drop.");
            });
        check("RoutedStackReorderAndSelectionInspector", () =>
        {
            using var f = NestedPointerFixture(); var host = NestedHost(f.Vm, DesignerLayoutModes.Stack);
            var a = NestedLeaf(f.Vm, host, "a"); var b = NestedLeaf(f.Vm, host, "b", 1); f.Refresh();
            var bounds = f.Vm.NativeContainerMeasurements[host.Id].Children[0].Bounds;
            NestedPointerMove(f, b, new Point(bounds.Center.X, bounds.Top + 1), capture: true);
            RequireCapability(NestedOrder(f.Vm, host.Id).First() == b.Id && f.Vm.SelectedControl?.Id == b.Id, "Raw pointer reorder lost order/selection.");
            RequireCapability(f.Vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "StackOrder").Value == "0", "Inspector order is stale.");
            GridEditorUndo(f.Vm); RequireCapability(NestedOrder(f.Vm, host.Id).First() == a.Id, "Pointer reorder is not one Undo.");
        });
        check("RoutedCrossContainerMoveAndCanvasGrabOffset", () =>
        {
            using var f = NestedPointerFixture(); var stack = NestedHost(f.Vm, DesignerLayoutModes.Stack); var canvas = NestedHost(f.Vm, DesignerLayoutModes.Absolute, 1);
            var c = NestedLeaf(f.Vm, stack, "cross-parent"); f.Refresh(); var dest = NestedPoint(f.Vm, canvas.Id, .6, .3);
            NestedPointerMove(f, c, dest);
            var bounds = f.Vm.NativeContainerMeasurements[canvas.Id].Bounds;
            RequireCapability(c.ParentId == canvas.Id && Math.Abs(c.X - f.Vm.Snap(dest.X - bounds.X - 35)) < .1 && Math.Abs(c.Y - f.Vm.Snap(dest.Y - bounds.Y - 18)) < .1, "Canvas move jumped under the cursor or ignored DIP conversion.");
            NestedPointerMove(f, c, NestedPoint(f.Vm, stack.Id)); RequireCapability(c.ParentId == stack.Id && c.X == 0 && c.Y == 0, "Canvas to Stack kept absolute coordinates.");
        });
        foreach (var ending in new[] { "Escape", "CaptureLost", "Outside", "FormSwitch", "ProjectSwitch", "TargetRemoved", "WindowClose" })
            check("PointerCancel_" + ending, () =>
            {
                using var f = NestedPointerFixture(); var source = NestedHost(f.Vm, DesignerLayoutModes.Stack); var target = NestedHost(f.Vm, DesignerLayoutModes.Stack, 1);
                var child = NestedLeaf(f.Vm, source, "cancel"); f.Refresh(); var start = f.ControlPoint(child); var point = NestedPoint(f.Vm, target.Id);
                f.Down(start); f.Move(f.CanvasPoint(point.X, point.Y)); f.NoMarquee();
                if (ending == "Escape") f.Escape();
                else if (ending == "CaptureLost") f.Pointer.Capture(null);
                else if (ending == "Outside") { var outside = f.CanvasPoint(-20, 30); f.Move(outside); f.Up(outside); }
                else if (ending == "FormSwitch") f.Vm.CreateNewForm("Grid");
                else if (ending == "ProjectSwitch") f.Vm.NewProjectEditorCommand!.Execute(null);
                else if (ending == "TargetRemoved") { f.Vm.Controls.Remove(target); f.Refresh(); }
                else f.Window.CloseForExternalHost();
                f.AssertIdle(); RequireCapability(child.ParentId == source.Id && child.StackOrder == 0, "Cancelled pointer operation changed the original placement.");
                RequireCapability(f.Surface.GuideOverlay.Children.Count == 0, "Cancellation retained an overlay.");
            });
        check("ToolboxEscapeAndDeletedTargetCannotCommit", () =>
        {
            using var f = NestedPointerFixture(); var host = NestedHost(f.Vm, DesignerLayoutModes.Stack); f.Refresh(); var point = NestedPoint(f.Vm, host.Id); var data = GridToolboxData(f);
            RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); f.Escape(); RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.AssertIdle();
            RequireCapability(f.Vm.Controls.Count == 1, "Toolbox committed after Escape.");
            data = GridToolboxData(f); RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); f.Vm.Controls.Remove(host); f.Refresh();
            RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.AssertIdle(); RequireCapability(f.Vm.Controls.Count == 0, "Deleted target accepted a stale Drop.");
        });
        foreach (var zoom in new[] { .75, 1.5 })
            check("NativeZoomDpiCoordinates_" + zoom, () =>
            {
                using var f = NestedPointerFixture(); var a = NestedHost(f.Vm, DesignerLayoutModes.Stack); var b = NestedHost(f.Vm, DesignerLayoutModes.Stack, 1);
                var child = NestedLeaf(f.Vm, a, "zoom");
                typeof(MainWindow).GetMethod("SetSurfaceZoom", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, new object?[] { zoom, null });
                f.Refresh();
                var scroll = f.Surface.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "DesignerViewportScrollViewer");
                // The zoom command legitimately preserves a centered viewport and may clip
                // column 0. Scroll to a visible source before generating real native input.
                scroll.Offset = zoom > 1 ? new Vector(60, 0) : default;
                NativeItemsUiWait(120); f.Window.UpdateLayout();
                var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(f.Window)!.Compositor.RequestCommitAsync();
                var deadline = DateTime.UtcNow.AddSeconds(3); while (!commit.IsCompleted && DateTime.UtcNow < deadline) NativeItemsUiWait(20);
                Console.WriteLine($"OBSERVED zoom={zoom}; canvasOrigin={f.Surface.Canvas.TranslatePoint(default, f.Window)}; viewport={scroll.Bounds}; offset={scroll.Offset}; source={f.Vm.GetAbsolutePosition(child)}");
                var content = (Control)f.Window.Content!;
                using (var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height)))
                {
                    image.Render(content); image.Save(Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase5", "nested-zoom-" + (int)(zoom * 100) + ".png"));
                }
                NestedPointerMove(f, child, NestedPoint(f.Vm, b.Id)); RequireCapability(child.ParentId == b.Id, "Zoom changed the target container.");
                Console.WriteLine($"OBSERVED nested zoom={zoom}; native RenderScaling={f.Window.RenderScaling}; DIPs preserved");
            });
        check("MultiFormAndStaleGeometry", () =>
        {
            using var f = NestedPointerFixture(); var host = NestedHost(f.Vm, DesignerLayoutModes.Stack); f.Refresh(); var form = f.Vm.ActiveFormDocument!.Id;
            var point = NestedPoint(f.Vm, host.Id); var data = GridToolboxData(f); RouteGridToolbox(f, data, DragDrop.DragOverEvent, point);
            host.LayoutOrientation = "Horizontal"; RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.AssertIdle(); RequireCapability(f.Vm.Controls.Count == 1, "Stale measured geometry was committed.");
            f.Refresh(); data = GridToolboxData(f); RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); f.Vm.CreateNewForm(); f.Refresh();
            RouteGridToolbox(f, data, DragDrop.DropEvent, point); RequireCapability(f.Vm.Controls.Count == 0, "Drop leaked into another form.");
            f.Vm.SetActiveForm(form, "nested-regression"); f.Refresh(); RequireCapability(f.Vm.Controls.Single().Id == host.Id, "Switching forms damaged the original hierarchy.");
        });
        foreach (var type in new[] { "DataGrid", "Eremex.DataGridControl", "Eremex.TextEditor" })
            check("BindingAndPluginMetadataPreserved_" + type, () =>
            {
                using var f = NestedPointerFixture(); if (type.StartsWith("Eremex")) LoadBuiltEremexPlugin(f.Vm);
                f.Vm.IncludePluginRuntimeReferences = true; f.Vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal;
                var a = NestedHost(f.Vm, DesignerLayoutModes.Absolute); var b = NestedHost(f.Vm, DesignerLayoutModes.Stack, 1);
                var control = f.Vm.CreateControl(type, 20, 20, a.Id, true); control.WidthMode = control.HeightMode = "Fixed"; control.Width = 150; control.Height = 80;
                var source = ProductsSource(); f.Vm.BindingSources.Add(source); control.BindingSourceId = source.Id;
                var properties = control.CustomProperties.Select(p => (p.Key, p.ValueJson)).ToArray(); var plugin = control.PluginId; var descriptor = control.DescriptorId; f.Refresh();
                NestedPointerMove(f, control, NestedPoint(f.Vm, b.Id), frame: true);
                RequireCapability(control.ParentId == b.Id && control.BindingSourceId == source.Id && control.PluginId == plugin && control.DescriptorId == descriptor
                    && control.CustomProperties.Select(p => (p.Key, p.ValueJson)).SequenceEqual(properties) && ReferenceEquals(f.Vm.BindingSources.Single(), source), "Move damaged DataGrid/plugin configuration.");
                f.Vm.GenerateXaml(); RequireCapability(!f.Vm.GeneratedXaml.Contains("Canvas.Left=\"20\""), "Plugin retained stale Canvas placement.");
            });
        check("SourceAxamlNestedDropReadOnly", () =>
        {
            using var f = NestedPointerFixture(); var imported = LayoutFixture("DockPanel"); f.Vm.LoadAxamlImportedDocument(imported.Result, "nested-source.axaml"); f.Refresh();
            var count = f.Vm.Controls.Count; var data = GridToolboxData(f); RouteGridToolbox(f, data, DragDrop.DragOverEvent, new Point(40, 40)); RouteGridToolbox(f, data, DragDrop.DropEvent, new Point(40, 40));
            RequireCapability(f.Vm.Controls.Count == count && !f.Vm.CreateActiveAxamlPatch().HasChanges, "Source-preserving session accepted structural regeneration.");
        });
        check("OverlayAndPreviewCacheDoNotAccumulate", () =>
        {
            using var f = NestedPointerFixture(); var host = NestedHost(f.Vm, DesignerLayoutModes.Stack); f.Refresh(); var data = GridToolboxData(f); var point = NestedPoint(f.Vm, host.Id);
            RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); var count = f.Surface.GuideOverlay.Children.Count;
            var field = typeof(MainWindow).GetField("_gridDropTarget", BindingFlags.Instance | BindingFlags.NonPublic)!; var first = field.GetValue(f.Window);
            for (var i = 0; i < 60; i++) RouteGridToolbox(f, data, DragDrop.DragOverEvent, point + new Vector(i * .1, i * .1));
            RequireCapability(ReferenceEquals(first, field.GetValue(f.Window)) && f.Surface.GuideOverlay.Children.Count == count, "Hover rebuilt native preview or accumulated overlays in the same insertion target.");
            RequireCapability(f.Window.GridDragDiagnostics.Count(d => d.Event == "GRID_DROP_PREVIEW") == 1, "Pointer hover flooded diagnostics.");
            f.Escape(); f.AssertIdle(); RequireCapability(f.Surface.GuideOverlay.Children.Count == 0 && f.Surface.GuideOverlay.ZIndex == 0, "Repeated hover retained overlays or a layer override after cancel.");
        });
        check("CanvasHoverTranslatesGhostWithoutLayoutRebuild", () =>
        {
            using var f = NestedPointerFixture(); var host = NestedHost(f.Vm, DesignerLayoutModes.Absolute); f.Refresh(); var data = GridToolboxData(f); var point = NestedPoint(f.Vm, host.Id, .1, .1);
            RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); var passes = f.Vm.NativeDropPreviewLayoutPasses; var count = f.Surface.GuideOverlay.Children.Count;
            for (var i = 0; i < 60; i++) RouteGridToolbox(f, data, DragDrop.DragOverEvent, point + new Vector(i, i));
            RequireCapability(f.Vm.NativeDropPreviewLayoutPasses == passes && f.Surface.GuideOverlay.Children.Count == count, "Canvas hover rebuilt native layout per pixel.");
            f.Escape(); f.AssertIdle();
        });
    }
}
