using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel NestedModel()
    {
        var vm = GridEditorModel("*", "200,200,*"); vm.DesignWidth = 660; vm.DesignHeight = 420;
        vm.DataGridExportMode = MainWindowViewModel.DataGridExportModeReal; return vm;
    }
    private static DesignControlModel NestedHost(MainWindowViewModel vm, string mode, int column = 0, string? parent = null)
    {
        var type = mode switch { DesignerLayoutModes.Stack => "StackPanel", DesignerLayoutModes.Flex => "WrapPanel", DesignerLayoutModes.Grid => "Grid", _ => "Group" };
        var host = vm.CreateControl(type, 0, 0, parent); host.ChildLayoutMode = mode; host.GridColumn = column;
        host.Padding = host.BorderThickness = 0; host.Margin = "0"; host.Rows = host.Columns = 1;
        host.GridRowDefinitions = host.GridColumnDefinitions = "*"; return host;
    }
    private static DesignControlModel NestedLeaf(MainWindowViewModel vm, DesignControlModel host, string text, int order = 0)
    {
        var child = vm.CreateControl("Button", 0, 0, host.Id); child.Text = text; child.WidthMode = child.HeightMode = "Fixed";
        child.Width = 80; child.Height = 32; child.StackOrder = order; return child;
    }
    private static AxamlLayoutProjection.JsonLayoutTree MeasureNested(MainWindowViewModel vm)
    {
        var tree = vm.BuildNativeLayout(model => model.Type switch
        {
            "TextBox" => new TextBox { Text = model.Text }, "TextBlock" => new TextBlock { Text = model.Text },
            "DataGrid" => new DataGrid(), _ => new Button { Content = model.Text }
        });
        vm.UpdateNativeLayoutMeasurements(tree, vm.PreviewFormWidth, vm.PreviewFormHeight); return tree;
    }
    private static Point NestedPoint(MainWindowViewModel vm, string parent, double x = 0.5, double y = 0.5)
    {
        var bounds = vm.NativeContainerMeasurements[parent].Bounds; return new(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);
    }
    private static DesignControlModel NestedToolbox(MainWindowViewModel vm, Point point, string type = "Button")
    {
        var prototype = vm.CreateGridDropPrototype(type); var target = vm.ResolveLayoutDropTarget(point, prototype);
        RequireCapability(target.IsValid, "Invalid Toolbox target: " + target.Reason);
        return vm.CommitGridToolboxDrop(type, target, point, vm.ActiveDocumentId) ?? throw new Exception("Toolbox commit failed: " + vm.StatusText);
    }
    private static void NestedMove(MainWindowViewModel vm, DesignControlModel model, Point point)
    {
        var target = vm.ResolveLayoutDropTarget(point, model);
        RequireCapability(target.IsValid && vm.CommitGridControlDrop(model, target, point), "Move failed: " + target.Reason + "; " + vm.StatusText);
    }
    private static string[] NestedOrder(MainWindowViewModel vm, string parent) => vm.GetChildControlsForDesignerRender(parent).Select(c => c.Id).ToArray();

    private static int RunNestedLayoutChecks()
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action test)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { test(); passed++; Console.WriteLine("PASS Nested_" + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL Nested_" + name + ": " + ex); }
        }
        foreach (var horizontal in new[] { false, true })
        {
            Check("EmptyStack_" + horizontal, () =>
            {
                var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Stack); host.LayoutOrientation = horizontal ? "Horizontal" : "Vertical";
                MeasureNested(vm); var child = NestedToolbox(vm, NestedPoint(vm, host.Id));
                RequireCapability(child.ParentId == host.Id && child.StackOrder == 0 && child.X == 0 && child.Y == 0, "Empty StackPanel did not receive the first item.");
                GridEditorUndo(vm); RequireCapability(vm.Controls.All(c => c.Id != child.Id), "Toolbox creation was not atomic.");
                GridEditorUndo(vm, true); RequireCapability(vm.Controls.Single(c => c.Id == child.Id).ParentId == host.Id, "Toolbox redo lost ID/parent.");
            });
            Check("StackInsertionOrderMarginSpacing_" + horizontal, () =>
            {
                var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Stack); host.LayoutOrientation = horizontal ? "Horizontal" : "Vertical"; host.LayoutSpacing = 14;
                var a = NestedLeaf(vm, host, "a"); var b = NestedLeaf(vm, host, "b", 1); a.Margin = "3,5"; b.Margin = "7,9";
                MeasureNested(vm); var children = vm.NativeContainerMeasurements[host.Id].Children; var bounds = children[1].Bounds;
                var point = horizontal ? new Point(bounds.Left + 1, bounds.Center.Y) : new Point(bounds.Center.X, bounds.Top + 1);
                var target = vm.ResolveLayoutDropTarget(point);
                RequireCapability(target.InsertionIndex == 1 && target.InsertionIndicator is not null, "Insertion ignored actual margin/spacing/orientation.");
                var added = NestedToolbox(vm, point); RequireCapability(NestedOrder(vm, host.Id).SequenceEqual(new[] { a.Id, added.Id, b.Id }), "Insertion order is wrong.");
            });
            Check("StackReorderUndoRedo_" + horizontal, () =>
            {
                var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Stack); host.LayoutOrientation = horizontal ? "Horizontal" : "Vertical";
                var a = NestedLeaf(vm, host, "a"); var b = NestedLeaf(vm, host, "b", 1); var c = NestedLeaf(vm, host, "c", 2);
                MeasureNested(vm); var rect = vm.NativeContainerMeasurements[host.Id].Children[0].Bounds;
                NestedMove(vm, c, new Point(rect.Left + 1, rect.Top + 1));
                RequireCapability(NestedOrder(vm, host.Id).SequenceEqual(new[] { c.Id, a.Id, b.Id }), "Stack reorder failed.");
                GridEditorUndo(vm); RequireCapability(NestedOrder(vm, host.Id).SequenceEqual(new[] { a.Id, b.Id, c.Id }), "Reorder undo was not atomic.");
                GridEditorUndo(vm, true); RequireCapability(NestedOrder(vm, host.Id).First() == c.Id && vm.SelectedControl?.Id == c.Id, "Reorder redo/selection failed.");
            });
        }
        foreach (var side in new[] { "Left", "Right", "Top", "Bottom" })
            Check("DockSide_" + side, () =>
            {
                var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Dock); LayoutPlacementProperties.SetLiteral(host, LayoutPlacementProperties.LastChildFillKey, false);
                MeasureNested(vm); var point = NestedPoint(vm, host.Id, side == "Left" ? .05 : side == "Right" ? .95 : .5, side == "Top" ? .05 : side == "Bottom" ? .95 : .5);
                var child = NestedToolbox(vm, point); var tree = MeasureNested(vm);
                RequireCapability(child.ParentId == host.Id && LayoutPlacementProperties.GetDock(child).ToString() == side && DockPanel.GetDock(tree.Controls[child.Id]).ToString() == side, "Dock side was not applied to the real native child.");
                RequireCapability(tree.ContainerPanels[host.Id] is DockPanel { LastChildFill: false }, "DockPanel was replaced or LastChildFill changed.");
            });
        Check("DockKeepsLastChildFillAndRejectsAmbiguousCenter", () =>
        {
            var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Dock); var fill = NestedLeaf(vm, host, "fill");
            MeasureNested(vm); RequireCapability(!vm.ResolveLayoutDropTarget(NestedPoint(vm, host.Id)).IsValid, "Occupied fill center silently displaced its child.");
            var side = NestedToolbox(vm, NestedPoint(vm, host.Id, .04, .5));
            RequireCapability(NestedOrder(vm, host.Id).SequenceEqual(new[] { side.Id, fill.Id }) && LayoutPlacementProperties.LastChildFill(host), "Side insertion stole the existing fill role.");
        });
        Check("DockEmptyFillAndAtomicPropertyUndo", () =>
        {
            var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Dock); MeasureNested(vm);
            var child = NestedToolbox(vm, NestedPoint(vm, host.Id)); child.WidthMode = child.HeightMode = "Auto"; child.VerticalAlignment = "Stretch";
            var tree = MeasureNested(vm); RequireCapability(tree.ContainerPanels[host.Id] is DockPanel { LastChildFill: true } && tree.Controls[child.Id].Bounds.Height > 300, "LastChildFill did not use native fill layout.");
        });
        foreach (var horizontal in new[] { false, true })
            Check("WrapMultipleLinesAndReorder_" + horizontal, () =>
            {
                var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Flex); host.LayoutOrientation = horizontal ? "Horizontal" : "Vertical";
                host.WidthMode = host.HeightMode = "Fixed"; host.Width = horizontal ? 190 : 260; host.Height = horizontal ? 120 : 90;
                var items = Enumerable.Range(0, 5).Select(i => NestedLeaf(vm, host, "wrap" + i, i)).ToArray(); MeasureNested(vm);
                var children = vm.NativeContainerMeasurements[host.Id].Children;
                RequireCapability(horizontal ? children[2].Bounds.Y > children[0].Bounds.Y : children[2].Bounds.X > children[0].Bounds.X, "WrapPanel never wrapped.");
                var last = children[^1].Bounds;
                var point = horizontal ? new Point(last.Right + 3, last.Center.Y) : new Point(last.Center.X, last.Bottom + 3);
                NestedMove(vm, items[0], point);
                RequireCapability(NestedOrder(vm, host.Id).Last() == items[0].Id, "Wrap insertion selected the wrong rendered line.");
            });
        Check("WrapRemeasuresAfterResize", () =>
        {
            var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Flex); host.LayoutOrientation = "Horizontal"; host.WidthMode = "Fixed"; host.Width = 180;
            for (var i = 0; i < 4; i++) NestedLeaf(vm, host, i.ToString(), i);
            var before = MeasureNested(vm).Controls[vm.GetChildControls(host.Id).Last().Id].Bounds.Y;
            host.Width = 400; var after = MeasureNested(vm).Controls[vm.GetChildControls(host.Id).Last().Id].Bounds.Y;
            RequireCapability(before > after, "Container resize did not invoke real WrapPanel layout.");
        });
        foreach (var sourceMode in new[] { DesignerLayoutModes.Grid, DesignerLayoutModes.Absolute, DesignerLayoutModes.Stack, DesignerLayoutModes.Flex, DesignerLayoutModes.Dock })
            Check("ReparentToStackPreservesIdentity_" + sourceMode, () =>
            {
                var vm = NestedModel(); var source = NestedHost(vm, sourceMode); var destination = NestedHost(vm, DesignerLayoutModes.Stack, 1);
                var child = NestedLeaf(vm, source, "preserved");
                // Managed children have no authored Canvas coordinates; only the Canvas case starts at 31/47.
                if (sourceMode == DesignerLayoutModes.Absolute) { child.X = 31; child.Y = 47; }
                child.BindingSourceId = "kept-source";
                child.CustomProperties.Add(new DesignPropertyValueModel { Key = "custom-color", ValueJson = "\"#ff3366\"" });
                var interaction = new InteractionModel { SourceControlName = child.Name, TargetControlName = child.Name, ActionType = InteractionModel.ActionSetProperty }; vm.Interactions.Add(interaction);
                var id = child.Id; var name = child.Name; var oldX = child.X; var oldY = child.Y; MeasureNested(vm); NestedMove(vm, child, NestedPoint(vm, destination.Id, .5, .8));
                RequireCapability(ReferenceEquals(vm.Controls.Single(c => c.Id == id), child) && child.Name == name && child.ParentId == destination.Id && child.X == 0 && child.Y == 0
                    && child.BindingSourceId == "kept-source" && child.CustomProperties.Single(p => p.Key == "custom-color").ValueJson == "\"#ff3366\"" && ReferenceEquals(vm.Interactions.Single(), interaction), "Reparent replaced the control/bindings/Logic.");
                GridEditorUndo(vm); var old = vm.Controls.Single(c => c.Id == id); RequireCapability(old.ParentId == source.Id && old.X == oldX && old.Y == oldY, $"Reparent undo lost original placement: expected={source.Id}/{oldX}/{oldY}; actual={old.ParentId}/{old.X}/{old.Y}.");
                GridEditorUndo(vm, true); RequireCapability(vm.Controls.Single(c => c.Id == id).ParentId == destination.Id, "Reparent redo failed.");
            });
        Check("StackToGridAndStackToCanvasDips", () =>
        {
            var vm = NestedModel(); var source = NestedHost(vm, DesignerLayoutModes.Stack); var canvas = NestedHost(vm, DesignerLayoutModes.Absolute, 1);
            var child = NestedLeaf(vm, source, "move"); MeasureNested(vm); NestedMove(vm, child, NestedPoint(vm, "", .85, .5));
            RequireCapability(child.ParentId == "" && child.GridColumn == 2 && child.X == 0, "Stack to Grid used coordinates.");
            MeasureNested(vm); var bounds = vm.NativeContainerMeasurements[canvas.Id].Bounds; var point = bounds.Position + new Vector(63, 77);
            var target = vm.ResolveLayoutDropTarget(point, child, new Point(13, 17), bypassSnap: true);
            RequireCapability(vm.CommitGridControlDrop(child, target, point) && child.ParentId == canvas.Id && child.X == 50 && child.Y == 60, "Canvas drop ignored local origin/grab offset/DIPs.");
        });
        Check("ContainerMoveKeepsDescendantsAndPreventsCycles", () =>
        {
            var vm = NestedModel(); var grid = NestedHost(vm, DesignerLayoutModes.Grid); var childHost = NestedHost(vm, DesignerLayoutModes.Stack, 0, grid.Id);
            var child = NestedLeaf(vm, childHost, "descendant"); var destination = NestedHost(vm, DesignerLayoutModes.Stack, 1); MeasureNested(vm);
            RequireCapability(!vm.ResolveLayoutDropTarget(NestedPoint(vm, childHost.Id), grid).IsValid, "Container to descendant cycle was accepted.");
            NestedMove(vm, grid, NestedPoint(vm, destination.Id));
            RequireCapability(grid.ParentId == destination.Id && childHost.ParentId == grid.Id && child.ParentId == childHost.Id && vm.Controls.Select(c => c.Id).Distinct().Count() == vm.Controls.Count, "Container move damaged descendants/IDs.");
        });
        Check("SpanPreservedAcrossFlowAndInvalidGridRejected", () =>
        {
            var vm = NestedModel(); var grid = NestedHost(vm, DesignerLayoutModes.Grid); grid.Rows = 2; grid.GridRowDefinitions = "*,*";
            var flow = NestedHost(vm, DesignerLayoutModes.Stack, 1); var small = NestedHost(vm, DesignerLayoutModes.Grid, 2);
            var child = NestedLeaf(vm, grid, "span"); child.GridRowSpan = 2; MeasureNested(vm); NestedMove(vm, child, NestedPoint(vm, flow.Id));
            MeasureNested(vm); var target = vm.ResolveLayoutDropTarget(NestedPoint(vm, small.Id), child);
            RequireCapability(child.GridRowSpan == 2 && !target.IsValid && !vm.CommitGridControlDrop(child, target, NestedPoint(vm, small.Id)) && child.ParentId == flow.Id, "Span was silently clamped.");
        });
        Check("PlacementBindingsAreNotOverwritten", () =>
        {
            var vm = NestedModel(); var source = NestedHost(vm, DesignerLayoutModes.Absolute); var flow = NestedHost(vm, DesignerLayoutModes.Stack, 1);
            var child = NestedLeaf(vm, source, "binding"); LayoutPlacementProperties.SetLiteral(child, "Canvas.Left", "{Binding PositionX}");
            MeasureNested(vm); var json = JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()); var point = NestedPoint(vm, flow.Id); var target = vm.ResolveLayoutDropTarget(point, child);
            RequireCapability(!target.IsValid && target.Reason.Contains("Canvas.Left") && !vm.CommitGridControlDrop(child, target, point), "Binding target was accepted: " + target);
            RequireCapability(JsonSerializer.Serialize(vm.CreatePreviewDocumentSnapshot()) == json, "Binding placement/document was overwritten.");
        });
        Check("PaintOrderClippingAndNestedBorder", () =>
        {
            var vm = NestedModel(); var canvas = NestedHost(vm, DesignerLayoutModes.Absolute); var back = NestedHost(vm, DesignerLayoutModes.Stack, 0, canvas.Id);
            back.WidthMode = back.HeightMode = "Fixed"; back.Width = 180; back.Height = 150;
            var front = NestedHost(vm, DesignerLayoutModes.Absolute, 0, canvas.Id); front.WidthMode = front.HeightMode = "Fixed"; front.Width = 180; front.Height = 150;
            MeasureNested(vm); RequireCapability(vm.ResolveLayoutDropTarget(NestedPoint(vm, back.Id)).ParentId == front.Id, "A deeper background branch defeated foreground native paint order.");
            front.IsVisible = false; MeasureNested(vm); RequireCapability(vm.ResolveLayoutDropTarget(NestedPoint(vm, back.Id)).ParentId == back.Id, "Hidden foreground container blocked placement.");
            back.X = 190; MeasureNested(vm); var point = new Point(210, 40);
            RequireCapability(vm.ResolveLayoutDropTarget(point).ParentId != back.Id, "Clipped descendant outside its Canvas remained a target.");
        });
        Check("InspectorContextAndJsonSaveLoad", () =>
        {
            var vm = NestedModel(); var canvas = NestedHost(vm, DesignerLayoutModes.Absolute); var dock = NestedHost(vm, DesignerLayoutModes.Dock, 1);
            LayoutPlacementProperties.SetLiteral(dock, LayoutPlacementProperties.LastChildFillKey, false); var child = NestedLeaf(vm, canvas, "json"); MeasureNested(vm);
            NestedMove(vm, child, NestedPoint(vm, dock.Id, .05, .5));
            var rows = vm.PropertyGridCategories.SelectMany(c => c.Rows).ToArray();
            RequireCapability(rows.Count(r => r.Key == LayoutPlacementProperties.DockKey) == 1 && rows.All(r => r.Key is not ("X" or "Y" or "GridRow")) && vm.SelectedControl?.Id == child.Id, "Inspector retained stale placement editors or lost selection.");
            var workspaceMode = vm.WorkspaceMode; vm.WorkspaceMode = MainWindowViewModel.WorkspaceModeLayout;
            rows = vm.PropertyGridCategories.SelectMany(c => c.Rows).ToArray();
            RequireCapability(rows.Single(r => r.Key == "StackOrder").Label == "Children order" && rows.Count(r => r.Key == LayoutPlacementProperties.DockKey) == 1,
                "Layout Inspector mislabeled Dock order or duplicated placement rows.");
            vm.WorkspaceMode = workspaceMode;
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase5"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "nested-roundtrip.json"); File.WriteAllText(path, vm.ExportWorkspaceJson());
            var restored = CreateViewModel("NestedReload"); restored.LoadDocumentJson(File.ReadAllText(path)); restored.GenerateXaml();
            File.WriteAllText(Path.Combine(folder, "nested-roundtrip.axaml"), restored.GeneratedXaml);
            var saved = restored.Controls.Single(c => c.Id == child.Id);
            RequireCapability(saved.ParentId == dock.Id && LayoutPlacementProperties.GetDock(saved) == Dock.Left && !LayoutPlacementProperties.LastChildFill(restored.Controls.Single(c => c.Id == dock.Id)), "JSON lost Dock/order/parent.");
            var xml = XDocument.Parse(restored.GeneratedXaml); var element = xml.Descendants().Single(e => e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == saved.Name);
            RequireCapability(element.Parent?.Name.LocalName == "DockPanel" && element.Attribute("DockPanel.Dock")?.Value == "Left" && element.Attribute("Canvas.Left") is null, "Export flattened DockPanel or emitted stale Canvas placement: " + element);
        });
        Check("EmptyNestedFlowContainerRemainsVisibleDropTarget", () =>
        {
            var vm = NestedModel(); var parent = NestedHost(vm, DesignerLayoutModes.Stack); var empty = NestedHost(vm, DesignerLayoutModes.Stack, 0, parent.Id);
            MeasureNested(vm); var bounds = vm.NativeContainerMeasurements[empty.Id].Bounds;
            RequireCapability(bounds.Width > 0 && bounds.Height > 0, "Empty nested flow container has zero native extent.");
            var added = NestedToolbox(vm, NestedPoint(vm, empty.Id)); RequireCapability(added.ParentId == empty.Id, "Empty nested flow host cannot receive its first item.");
        });
        Check("CanvasFutureParentPositionAndTranslatedPreview", () =>
        {
            var vm = NestedModel(); var stack = NestedHost(vm, DesignerLayoutModes.Stack); stack.LayoutSpacing = 12;
            var child = NestedLeaf(vm, stack, "move into sibling Canvas");
            var canvas = NestedHost(vm, DesignerLayoutModes.Absolute, 0, stack.Id); canvas.StackOrder = 1; canvas.HeightMode = "Fixed"; canvas.Height = 180;
            MeasureNested(vm); var originalCanvas = vm.NativeContainerMeasurements[canvas.Id].Bounds;
            var point = new Point(originalCanvas.X + 70, originalCanvas.Y + 70);
            var target = vm.EvaluateGridDropPreview(vm.ResolveLayoutDropTarget(point, child, bypassSnap: true), child, m => new Button { Content = m.Text });
            RequireCapability(target.IsValid && target.CanvasHostShift.Y > 30 && target.PreviewBounds is { } preview && Math.Abs(preview.Y - point.Y) < .1, "Canvas preview used its pre-reparent origin.");
            var count = vm.NativeDropPreviewLayoutPasses;
            var nextPoint = point + new Vector(15, 10);
            var translated = vm.TranslateCanvasDropPreview(target, vm.ResolveLayoutDropTarget(nextPoint, child, bypassSnap: true), child);
            RequireCapability(vm.NativeDropPreviewLayoutPasses == count && translated.PreviewBounds is { } next && Math.Abs(next.Y - nextPoint.Y) < .1, "Canvas hover remeasured or lost its future origin.");
            RequireCapability(vm.CommitGridControlDrop(child, translated, nextPoint), "Corrected Canvas placement was rejected.");
            var tree = MeasureNested(vm); var view = tree.Controls[child.Id]; var rootPoint = view.TranslatePoint(default, tree.Root)!.Value;
            Near(rootPoint.X, nextPoint.X, "Canvas committed X"); Near(rootPoint.Y, nextPoint.Y, "Canvas committed Y");
        });
        Check("NestedBorderUsesMeasuredContentBounds", () =>
        {
            var vm = NestedModel(); var border = vm.CreateControl("Border", 0, 0); border.Text = ""; border.Padding = 14; border.BorderThickness = 3;
            var stack = NestedHost(vm, DesignerLayoutModes.Stack, 0, border.Id); stack.Margin = "9";
            MeasureNested(vm); var point = NestedPoint(vm, stack.Id); var added = NestedToolbox(vm, point);
            RequireCapability(added.ParentId == stack.Id && vm.NativeContainerMeasurements[stack.Id].Bounds.X >= 26, "Border/padding/margin content offsets were ignored.");
        });
        Check("LockedSourceAndBindingDockTargetStayUnchanged", () =>
        {
            var vm = NestedModel(); var source = NestedHost(vm, DesignerLayoutModes.Stack); var dock = NestedHost(vm, DesignerLayoutModes.Dock, 1);
            var child = NestedLeaf(vm, source, "locked"); source.IsLocked = true;
            LayoutPlacementProperties.SetLiteral(dock, LayoutPlacementProperties.LastChildFillKey, "{Binding FillMode}");
            MeasureNested(vm); var point = NestedPoint(vm, dock.Id, .05, .5); var target = vm.ResolveLayoutDropTarget(point, child);
            RequireCapability(!target.IsValid && !vm.CommitGridControlDrop(child, target, point) && child.ParentId == source.Id
                && LayoutPlacementProperties.ReadString(dock, LayoutPlacementProperties.LastChildFillKey) == "{Binding FillMode}", "Protected source/Dock expression was modified.");
        });
        Check("OffscreenDropPreviewsReleaseNativeControls", () =>
        {
            var vm = NestedModel(); var host = NestedHost(vm, DesignerLayoutModes.Stack, 1); var grid = vm.CreateControl("DataGrid", 0, 0);
            grid.WidthMode = grid.HeightMode = "Fixed"; grid.Width = 160; grid.Height = 120; MeasureNested(vm);
            var references = CaptureTransientNestedPreviews(vm, grid, NestedPoint(vm, host.Id));
            NativeItemsUiWait(1000); ForceFullGc();
            RequireCapability(references.Count >= 12 && references.All(r => !r.IsAlive), "Value-only geometry retained a transient native control after repeated ghost layouts.");
            GC.KeepAlive(vm);
        });
        foreach (var axaml in new[] { false, true })
        {
            Check("RealPreviewHierarchyAndResize_" + axaml, () => AssertNestedPreview(axaml));
            Check("DockWrapPreviewAndResize_" + axaml, () => AssertNestedDockWrapPreview(axaml));
        }
        Check("SourceAxamlReadOnlyAndLegacyCanvasCompatibility", () =>
        {
            var (_, source) = LayoutFixture("DockPanel"); var text = source.CreateActiveAxamlPatch().PatchedText;
            RequireCapability(!source.ResolveLayoutDropTarget(new Point(30, 30)).IsValid && source.CreateActiveAxamlPatch().PatchedText == text, "Source AXAML was regenerated.");
            var old = CreateViewModel("LegacyCanvas"); var c = old.TryCreateControlFromToolboxDrop("Button", 53, 67, null, true, old.ActiveDocumentId)!;
            RequireCapability(!old.UsesNativeLayout && c.X == 53 && c.Y == 67, "Legacy Canvas placement changed.");
        });
        RunNestedPointerChecks(Check);
        Console.WriteLine($"Nested Layout checks: {passed}/{passed + failed}"); return failed == 0 && passed > 0 ? 0 : 1;
    }

    private static void AssertNestedPreview(bool axaml)
    {
        var vm = NestedModel(); vm.SurfaceGridColumnDefinitions = "*"; vm.SurfaceLayoutColumns = 1; vm.SurfaceGridRowDefinitions = "Auto,*"; vm.SurfaceLayoutRows = 2;
        var toolbar = NestedHost(vm, DesignerLayoutModes.Stack); toolbar.LayoutOrientation = "Horizontal"; toolbar.LayoutSpacing = 8;
        NestedLeaf(vm, toolbar, "nested-preview-button"); var input = vm.CreateControl("TextBox", 0, 0, toolbar.Id); input.WidthMode = "Fixed"; input.Width = 160;
        var grid = vm.CreateControl("DataGrid", 0, 0); grid.GridRow = 1; var source = ProductsSource(); source.UseDemoData = true; vm.BindingSources.Add(source); grid.BindingSourceId = source.Id;
        using var preview = ShowDataGridRegressionPreview(vm, axaml);
        if (axaml) RequireCapability(preview.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text == "AXAML Preview ready", "AXAML silently fell back.");
        var button = preview.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "nested-preview-button" || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "nested-preview-button"));
        RequireCapability(button.GetVisualAncestors().OfType<StackPanel>().Any(s => s.Orientation == Avalonia.Layout.Orientation.Horizontal), "Preview lost the real nested StackPanel: model=" + toolbar.ChildLayoutMode + "/" + toolbar.LayoutOrientation + "; ancestors=" + string.Join("/", button.GetVisualAncestors().Select(v => v.GetType().Name)));
        var actualGrid = preview.Window.GetVisualDescendants().OfType<DataGrid>().Single(); var before = actualGrid.Bounds.Size;
        RequireCapability(actualGrid.ItemsSource!.Cast<object>().Any(), "DataGrid lost its configured local BindingSource.");
        preview.Window.Width += 200; preview.Window.Height += 100; NativeItemsUiWait(160); preview.Window.UpdateLayout();
        RequireCapability(actualGrid.Bounds.Width > before.Width + 150 && actualGrid.Bounds.Height > before.Height + 50, "Nested Preview did not resize its real DataGrid.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<WeakReference> CaptureTransientNestedPreviews(MainWindowViewModel vm, DesignControlModel model, Point point)
    {
        var references = new List<WeakReference>(); var target = vm.ResolveLayoutDropTarget(point, model);
        for (var cycle = 0; cycle < 12; cycle++)
            vm.EvaluateGridDropPreview(target, model, leaf =>
            {
                Control view = leaf.Type == "DataGrid" ? new DataGrid { AutoGenerateColumns = true, ItemsSource = new[] { new { Value = "actual local row" } } } : new Button { Content = leaf.Text };
                references.Add(new WeakReference(view)); return view;
            });
        return references;
    }
}
