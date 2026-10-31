using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static void MeasureGridDrop(MainWindowViewModel vm)
    {
        GridEditorTree(vm); vm.NativeGridMeasurementKey = vm.GridDropLayoutKey;
    }
    private static Point GridCellPoint(MainWindowViewModel vm, string parent, int row, int column)
    {
        var grid = vm.NativeGridMeasurements[parent];
        return new Point(grid.Bounds.X + grid.Columns.Take(column).Sum(c => c.Size) + grid.Columns[column].Size / 2,
            grid.Bounds.Y + grid.Rows.Take(row).Sum(r => r.Size) + grid.Rows[row].Size / 2);
    }
    private static int RunGridDropChecks()
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action action)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { action(); passed++; Console.WriteLine("PASS GridDrop_" + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL GridDrop_" + name + ": " + ex); }
        }
        Check("ActualPixelStarHitTest", () =>
        {
            var vm = GridEditorModel("80,*", "200,*"); MeasureGridDrop(vm);
            var a = vm.ResolveGridDropTarget(new Point(199.5, 40)); var b = vm.ResolveGridDropTarget(new Point(200.5, 100));
            RequireCapability(a.IsValid && a.Row == 0 && a.Column == 0 && b.IsValid && b.Row == 1 && b.Column == 1, "Hit test used uniform cells.");
            RequireCapability(!vm.ResolveGridDropTarget(new Point(600, 20)).IsValid, "Right edge must not clamp to the final cell.");
        });
        Check("ActualAutoHitTest", () =>
        {
            var vm = GridEditorModel("Auto,*", "*"); var header = vm.CreateControl("TextBlock", 0, 0); header.HeightMode = "Fixed"; header.Height = 45;
            MeasureGridDrop(vm); var height = vm.NativeGridMeasurements[""].Rows[0].Size;
            RequireCapability(height >= 45 && vm.ResolveGridDropTarget(new Point(20, height - 1)).Row == 0 && vm.ResolveGridDropTarget(new Point(20, height + 1)).Row == 1, "Auto boundary mismatch.");
        });
        Check("UnallocatedAndZeroTracks", () =>
        {
            var vm = GridEditorModel("0,60", "100,*"); MeasureGridDrop(vm);
            RequireCapability(vm.ResolveGridDropTarget(new Point(30, 10)).Row == 1 && !vm.ResolveGridDropTarget(new Point(30, 300)).IsValid, "Zero/unallocated space was made into a fake cell.");
        });
        foreach (var row in new[] { 0, 1 })
            Check("ToolboxCell" + row, () =>
            {
                var vm = GridEditorModel(); MeasureGridDrop(vm); var point = GridCellPoint(vm, "", row, row); var target = vm.ResolveGridDropTarget(point);
                var control = vm.CommitGridToolboxDrop("Button", target, point, vm.ActiveDocumentId) ?? throw new Exception("Toolbox drop was rejected.");
                RequireCapability(control.GridRow == row && control.GridColumn == row && control.ParentId == "" && control.X == 0 && control.Y == 0 && vm.SelectedControl?.Id == control.Id, "Toolbox placement/selection failed.");
                GridEditorUndo(vm); RequireCapability(vm.Controls.Count == 0, "Toolbox drop was not a single undo."); GridEditorUndo(vm, true); RequireCapability(vm.Controls.Single().Id == control.Id && vm.Controls.Single().GridRow == row, "Creation redo changed Id or placement.");
            });
        Check("MovePreservesIdPropertiesBindingsLogic", () =>
        {
            var vm = GridEditorModel(); var c = vm.CreateControl("Button", 0, 0); c.Text = "Keep me"; c.BindingSourceId = "binding-kept"; c.Margin = "7,3"; c.HorizontalAlignment = "Right";
            c.CustomProperties.Add(new DesignPropertyValueModel { Key = "custom", ValueJson = "\"kept\"" }); var id = c.Id; var width = c.Width; var sizeMode = c.WidthMode;
            var logic = new InteractionModel { SourceControlName = c.Name, TargetControlName = c.Name, EventName = InteractionModel.EventButtonClick, ActionType = InteractionModel.ActionSetProperty, TargetProperty = "IsVisible", TextTemplate = "true" }; vm.Interactions.Add(logic);
            MeasureGridDrop(vm); var point = GridCellPoint(vm, "", 1, 1); RequireCapability(vm.CommitGridControlDrop(c, vm.ResolveGridDropTarget(point, c), point), "Move failed.");
            RequireCapability(vm.Controls.Single().Id == id && c.GridRow == 1 && c.GridColumn == 1 && c.Text == "Keep me" && c.BindingSourceId == "binding-kept" && c.Width == width && c.WidthMode == sizeMode && c.Margin == "7,3" && c.HorizontalAlignment == "Right" && c.CustomProperties.Single(p => p.Key == "custom").ValueJson == "\"kept\"", "Move replaced or damaged the model.");
            RequireCapability(vm.Interactions.Single().Id == logic.Id && vm.Interactions.Single().SourceControlName == c.Name, "Move lost Logic.");
            GridEditorUndo(vm); RequireCapability(vm.Controls.Single().GridRow == 0 && vm.Controls.Single().Id == id, "Move undo failed."); GridEditorUndo(vm, true); RequireCapability(vm.Controls.Single().GridColumn == 1, "Move redo failed.");
        });
        Check("NestedReparentPaddingMargin", () =>
        {
            var vm = GridEditorModel("*", "*,*"); var left = vm.CreateControl("Grid", 0, 0); left.GridRowDefinitions = "70,*"; left.Rows = 2; left.Margin = "10";
            var right = vm.CreateControl("Grid", 0, 0); right.GridColumn = 1; right.GridRowDefinitions = "70,*"; right.Rows = 2; right.Margin = "15";
            var c = vm.CreateControl("Button", 0, 0, left.Id); MeasureGridDrop(vm); var point = GridCellPoint(vm, right.Id, 1, 0);
            var target = vm.ResolveGridDropTarget(point, c); RequireCapability(target.ParentId == right.Id && target.Row == 1 && target.GridBounds.X > 315, "Nested hit test ignored actual padding/margin.");
            RequireCapability(vm.CommitGridControlDrop(c, target, point) && c.ParentId == right.Id && c.GridRow == 1, "Reparent failed.");
            RequireCapability(vm.PropertyGridCategories.SelectMany(g => g.Rows).Any(r => r.Key == "ParentId" && r.Value.Contains(right.Id)), "Inspector lost ParentId.");
            var id = c.Id; GridEditorUndo(vm); RequireCapability(vm.Controls.Single(m => m.Id == id).ParentId == left.Id, "Reparent undo lost original parent."); GridEditorUndo(vm, true); RequireCapability(vm.Controls.Single(m => m.Id == id).ParentId == right.Id, "Reparent redo failed.");
        });
        Check("CycleSelfAndDescendantRejected", () =>
        {
            var vm = NativeGridModel(); var parent = vm.CreateControl("Grid", 0, 0); var child = vm.CreateControl("Grid", 0, 0, parent.Id); MeasureGridDrop(vm);
            var point = GridCellPoint(vm, child.Id, 0, 0); var target = vm.ResolveGridDropTarget(point, parent);
            RequireCapability(!target.IsValid && !vm.CommitGridControlDrop(parent, target, point) && parent.ParentId == "", "Cycle was accepted.");
        });
        foreach (var spanRows in new[] { true, false })
            Check((spanRows ? "Row" : "Column") + "SpanPreservedAndRejected", () =>
            {
                var vm = GridEditorModel("*,*,*", "*,*,*"); var c = vm.CreateControl("Button", 0, 0); if (spanRows) c.GridRowSpan = 2; else c.GridColumnSpan = 2;
                MeasureGridDrop(vm); var point = GridCellPoint(vm, "", 1, 1); RequireCapability(vm.CommitGridControlDrop(c, vm.ResolveGridDropTarget(point, c), point), "Valid span was rejected.");
                RequireCapability((spanRows ? c.GridRowSpan : c.GridColumnSpan) == 2, "Span was silently clamped.");
                MeasureGridDrop(vm); point = GridCellPoint(vm, "", 2, 2); var target = vm.ResolveGridDropTarget(point, c);
                RequireCapability(!target.IsValid && !vm.CommitGridControlDrop(c, target, point) && c.GridRow == 1 && c.GridColumn == 1 && (spanRows ? c.GridRowSpan : c.GridColumnSpan) == 2, "Invalid span damaged placement.");
            });
        Check("OverlapWarningUsesActualRectangles", () =>
        {
            var vm = NativeGridModel(); var a = vm.CreateControl("Button", 0, 0); MeasureGridDrop(vm); var p = GridCellPoint(vm, "", 0, 0); var prototype = vm.CreateGridDropPrototype("Button");
            var target = vm.EvaluateGridDropPreview(vm.ResolveGridDropTarget(p, prototype), prototype, m => new Button { Content = m.Text });
            RequireCapability(target.HasOverlap && vm.CommitGridToolboxDrop("Button", target, p, vm.ActiveDocumentId) is not null && vm.Controls.Count == 2 && a.GridRow == 0 && vm.StatusText.Contains("перекрываются"), "Overlap warning/create policy failed.");
        });
        Check("SameCellDisjointAlignmentHasNoFalseWarning", () =>
        {
            var vm = NativeGridModel(); var a = vm.CreateControl("Button", 0, 0); a.Width = 80; a.WidthMode = "Fixed"; a.HorizontalAlignment = "Left"; MeasureGridDrop(vm);
            var prototype = vm.CreateGridDropPrototype("Button"); prototype.Width = 80; prototype.WidthMode = "Fixed"; prototype.HorizontalAlignment = "Right"; var p = GridCellPoint(vm, "", 0, 0);
            var target = vm.EvaluateGridDropPreview(vm.ResolveGridDropTarget(p, prototype), prototype, _ => new Button());
            RequireCapability(!target.HasOverlap, "Same cell alone must not imply actual overlap.");
        });
        Check("InvalidStaleAndFormTargetRejected", () =>
        {
            var vm = GridEditorModel(); MeasureGridDrop(vm); var point = GridCellPoint(vm, "", 1, 1); var target = vm.ResolveGridDropTarget(point);
            vm.SurfaceGridColumnDefinitions = "300,*"; RequireCapability(vm.CommitGridToolboxDrop("Button", target, point, vm.ActiveDocumentId) is null && vm.Controls.Count == 0, "Stale geometry was accepted.");
            MeasureGridDrop(vm); target = vm.ResolveGridDropTarget(point); RequireCapability(vm.CommitGridToolboxDrop("Button", target, point, "another-form") is null, "Wrong document was accepted.");
            var old = target; vm.CreateNewForm("Grid"); MeasureGridDrop(vm); RequireCapability(vm.CommitGridToolboxDrop("Button", old, point, vm.ActiveDocumentId) is null, "Old session target was accepted.");
        });
        Check("LockedAndHiddenNestedGrid", () =>
        {
            var vm = NativeGridModel(); var grid = vm.CreateControl("Grid", 0, 0); grid.IsLocked = true; MeasureGridDrop(vm); var p = GridCellPoint(vm, grid.Id, 0, 0);
            RequireCapability(!vm.ResolveGridDropTarget(p).IsValid, "Locked grid accepted placement."); grid.IsVisible = false; MeasureGridDrop(vm); RequireCapability(vm.ResolveGridDropTarget(p).ParentId == "", "Hidden nested target was selected.");
        });
        Check("JsonSaveLoadAndAxamlNoCanvasPlacement", () =>
        {
            var vm = GridEditorModel(); var c = vm.CreateControl("TextBox", 0, 0); MeasureGridDrop(vm); var p = GridCellPoint(vm, "", 1, 1); vm.CommitGridControlDrop(c, vm.ResolveGridDropTarget(p, c), p);
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase4"); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "grid-drop.json"); File.WriteAllText(path, vm.ExportWorkspaceJson());
            var restored = CreateViewModel("GridDropReload"); restored.LoadDocumentJson(File.ReadAllText(path)); restored.GenerateXaml();
            RequireCapability(restored.Controls.Single().Id == c.Id && restored.Controls.Single().GridRow == 1 && restored.GeneratedXaml.Contains("Grid.Column=\"1\"") && !restored.GeneratedXaml.Contains("Canvas.Left"), "JSON/AXAML lost placement.");
        });
        foreach (var axaml in new[] { false, true })
            Check((axaml ? "Axaml" : "Legacy") + "PreviewActualCell", () =>
            {
                var vm = GridEditorModel("60,*", "200,*"); var c = vm.CreateControl("Button", 0, 0); c.Name = "MovedGridButton"; c.Text = "Moved-cell verification"; MeasureGridDrop(vm); var p = GridCellPoint(vm, "", 1, 1); vm.CommitGridControlDrop(c, vm.ResolveGridDropTarget(p, c), p);
                using var preview = ShowDataGridRegressionPreview(vm, axaml);
                if (axaml) RequireCapability(preview.Window.FindControl<TextBlock>("PreviewRuntimeStatusText")!.Text == "AXAML Preview ready", "AXAML Preview fell back.");
                // Existing runtime preview deliberately strips Names for forms without data/items controls.
                var button = preview.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == c.Text || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == c.Text));
                RequireCapability(Grid.GetRow(button) == 1 && Grid.GetColumn(button) == 1 && button.Bounds.X >= 200 && button.Bounds.Y >= 60, "Moved control did not reach the real preview cell.");
            });
        Check("SourceAxamlReadOnlyAndCanvasUnchanged", () =>
        {
            var (_, vm) = LayoutFixture("GridDefinitions"); RequireCapability(!vm.ResolveGridDropTarget(new Point(200, 100)).IsValid && !vm.CreateActiveAxamlPatch().HasChanges, "Source Grid was regenerated.");
            var canvas = CreateViewModel("OldCanvasDrop"); var c = canvas.TryCreateControlFromToolboxDrop("Button", 47, 63, null, true, canvas.ActiveDocumentId)!;
            RequireCapability(c.X == 47 && c.Y == 63 && !canvas.UsesNativeLayout, "Canvas free positioning changed.");
        });
        RunGridDropPointerChecks(Check);
        Console.WriteLine($"Grid Drop checks: {passed}/{passed + failed}"); return failed == 0 && passed > 0 ? 0 : 1;
    }

    private static void ConfigureGridDropPhase4Export(MainWindowViewModel vm)
    {
        ConfigureNativeGridExport(vm);
        // Keep the empty destination track measurable after removing its only Auto child.
        vm.SurfaceGridColumnDefinitions = "*,180";
        vm.Controls.Remove(vm.Controls.Single(c => c.Type == "Button"));
        MeasureGridDrop(vm); var p = GridCellPoint(vm, "", 1, 0); var target = vm.ResolveGridDropTarget(p); var added = vm.CommitGridToolboxDrop("Button", target, p, vm.ActiveDocumentId)!;
        added.Name = "NativeButton"; MeasureGridDrop(vm); p = GridCellPoint(vm, "", 1, 1);
        RequireCapability(vm.CommitGridControlDrop(added, vm.ResolveGridDropTarget(p, added), p), "Generated scenario move failed: " + vm.StatusText);
    }
    private static void ConfigureGridDropPhase4EremexExport(MainWindowViewModel vm)
    {
        ConfigureNativeGridEremexExport(vm); vm.SurfaceLayoutColumns = 2; vm.SurfaceGridColumnDefinitions = "200,*"; MeasureGridDrop(vm);
        var control = vm.Controls.Last(); var p = GridCellPoint(vm, "", 2, 1);
        RequireCapability(vm.CommitGridControlDrop(control, vm.ResolveGridDropTarget(p, control), p), "Eremex placement failed.");
    }
    private static void AssertGridDropEremexExport(SmokeContext context)
    {
        var grids = XDocument.Parse(context.Xaml).Descendants().Where(e => e.Name.LocalName == "DataGridControl").ToArray();
        RequireCapability(grids.Length == 3 && grids[2].Attribute("Grid.Column")?.Value == "1" && grids[2].Attribute("Canvas.Left") is null, "Eremex drop emitted wrong placement.");
    }
}
