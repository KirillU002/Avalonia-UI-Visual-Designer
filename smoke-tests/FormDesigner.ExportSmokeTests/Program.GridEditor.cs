using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static MainWindowViewModel GridEditorModel(string rows = "80,*", string columns = "2*,*")
    {
        var vm = NativeGridModel("GridEditor");
        vm.SurfaceGridRowDefinitions = rows; vm.SurfaceLayoutRows = RowDefinitions.Parse(rows).Count;
        vm.SurfaceGridColumnDefinitions = columns; vm.SurfaceLayoutColumns = ColumnDefinitions.Parse(columns).Count;
        return vm;
    }
    private static void GridEditorUndo(MainWindowViewModel vm, bool redo = false)
    {
        NativeItemsUiWait(160);
        if (redo) vm.RedoEditorCommand!.Execute(null); else vm.UndoEditorCommand!.Execute(null);
        NativeItemsUiWait(100);
    }
    private static AxamlLayoutProjection.JsonLayoutTree GridEditorTree(MainWindowViewModel vm)
    {
        var tree = vm.BuildNativeLayout(c => new Button { Content = c.Text });
        vm.ApplyGridTrackResizePreview(tree); vm.NativeLayoutBounds = tree.Measure(vm.DesignWidth, vm.DesignHeight);
        vm.NativeGridMeasurements = tree.MeasuredGrids; return tree;
    }
    private static int RunGridEditorChecks()
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action action)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            try { action(); Console.WriteLine("PASS GridEditor_" + name); passed++; }
            catch (Exception ex) { Console.WriteLine("FAIL GridEditor_" + name + ": " + ex); failed++; }
        }
        Check("SingleCell", () => { var vm = NativeGridModel(); var tree = GridEditorTree(vm); Near(tree.MeasuredGrids[""].Rows.Single().Size, 400, "Single row"); Near(tree.MeasuredGrids[""].Columns.Single().Size, 600, "Single column"); });
        Check("ActualRowsAutoStar", () =>
        {
            var vm = GridEditorModel("80,Auto,*"); var c = vm.CreateControl("Button", 0, 0); c.GridRow = 1;
            var m = GridEditorTree(vm).MeasuredGrids[""]; Near(m.Rows[0].Size, 80, "Pixel track"); RequireCapability(m.Rows[1].Size > 10, "Auto did not measure real content."); Near(m.Rows.Sum(r => r.Size), 400, "Actual row total");
        });
        Check("ActualColumnsWeights", () => { var vm = GridEditorModel(columns: "100,2*,*"); var m = GridEditorTree(vm).MeasuredGrids[""]; Near(m.Columns[0].Size, 100, "Pixel column"); RequireCapability(Math.Abs(m.Columns[1].Size - 1000.0 / 3) <= 1, "Star ratio exceeds Avalonia pixel-rounding tolerance."); Near(m.Columns.Sum(c => c.Size), 600, "Actual column total"); });
        Check("ActualTrackIntervalsMatchChildBounds", () =>
        {
            var vm = GridEditorModel("50,60,*", "100,2*,*");
            for (var i = 0; i < 3; i++) { var c = vm.CreateControl("Button", 0, 0); c.GridRow = c.GridColumn = i; c.VerticalAlignment = "Stretch"; }
            GridEditorTree(vm); var grid = vm.NativeGridMeasurements[""]; double x = 0, y = 0;
            for (var i = 0; i < 3; i++) { var bounds = vm.NativeLayoutBounds[vm.Controls[i].Id]; Near(bounds.X, x, "Column start vs arranged child"); Near(bounds.Y, y, "Row start vs arranged child"); Near(bounds.Width, grid.Columns[i].Size, "Column width vs arranged child"); Near(bounds.Height, grid.Rows[i].Size, "Row height vs arranged child"); x += grid.Columns[i].Size; y += grid.Rows[i].Size; }
        });
        foreach (var rows in new[] { true, false })
        {
            var axis = rows ? "Row" : "Column";
            Check("Add" + axis, () =>
            {
                var vm = GridEditorModel(); var c = vm.CreateControl("Button", 0, 0); c.GridRow = c.GridColumn = 1;
                if (rows) vm.AddGridRowDefinitionCommand.Execute(null); else vm.AddGridColumnDefinitionCommand.Execute(null);
                RequireCapability((rows ? vm.SurfaceLayoutRows : vm.SurfaceLayoutColumns) == 3 && c.GridRow == 1 && c.GridColumn == 1, "Append changed existing placement.");
            });
            Check("Insert" + axis + "PreservesSpans", () =>
            {
                var vm = GridEditorModel("*,*,*", "*,*,*"); var a = vm.CreateControl("Button", 0, 0); var b = vm.CreateControl("TextBox", 0, 0);
                a.GridRow = a.GridColumn = 1; b.GridRowSpan = b.GridColumnSpan = 3;
                vm.InsertGridTrack(rows, 1);
                RequireCapability((rows ? a.GridRow : a.GridColumn) == 2 && (rows ? b.GridRowSpan : b.GridColumnSpan) == 4, "Insert did not shift starts/extend crossing spans.");
                var json = vm.ExportWorkspaceJson(); GridEditorUndo(vm); RequireCapability((rows ? vm.Controls.First().GridRow : vm.Controls.First().GridColumn) == 1 && (rows ? vm.Controls.Last().GridRowSpan : vm.Controls.Last().GridColumnSpan) == 3, "Undo did not restore placement atomically.");
                GridEditorUndo(vm, true); RequireCapability(vm.ExportWorkspaceJson().Contains(rows ? "GridRowSpan" : "GridColumnSpan"), "Redo lost serialization.");
            });
            Check("RemoveEmpty" + axis, () =>
            {
                var vm = GridEditorModel(); var c = vm.CreateControl("Button", 0, 0);
                if (rows) vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[1]); else vm.DeleteLayoutGridColumnCommand.Execute(vm.LayoutGridColumns[1]);
                RequireCapability(!vm.HasPendingGridRemoval && vm.Controls.Count == 1 && (rows ? vm.SurfaceLayoutRows : vm.SurfaceLayoutColumns) == 1, "Empty definition removal failed.");
            });
            Check("RemoveOccupied" + axis + "ConfirmUndo", () =>
            {
                var vm = GridEditorModel(); var a = vm.CreateControl("Button", 0, 0); var b = vm.CreateControl("TextBox", 0, 0); a.GridRow = a.GridColumn = 1; b.GridRowSpan = b.GridColumnSpan = 2;
                if (rows) vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[1]); else vm.DeleteLayoutGridColumnCommand.Execute(vm.LayoutGridColumns[1]);
                RequireCapability(vm.HasPendingGridRemoval && (rows ? vm.SurfaceLayoutRows : vm.SurfaceLayoutColumns) == 2, "Occupied removal must await an explicit choice.");
                vm.CancelGridTrackRemovalCommand.Execute(null); RequireCapability(vm.Controls.Count == 2 && (rows ? a.GridRow : a.GridColumn) == 1, "Cancel changed controls.");
                if (rows) vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[1]); else vm.DeleteLayoutGridColumnCommand.Execute(vm.LayoutGridColumns[1]);
                vm.ConfirmGridTrackRemovalCommand.Execute(null);
                RequireCapability(vm.Controls.Count == 2 && (rows ? a.GridRow : a.GridColumn) == 0 && (rows ? b.GridRowSpan : b.GridColumnSpan) == 1, "Transfer deleted controls or damaged spans.");
                GridEditorUndo(vm); RequireCapability(vm.Controls.Count == 2 && (rows ? vm.Controls[0].GridRow : vm.Controls[0].GridColumn) == 1 && (rows ? vm.Controls[1].GridRowSpan : vm.Controls[1].GridColumnSpan) == 2, "Removal + transfer was not one undo.");
                GridEditorUndo(vm, true); RequireCapability((rows ? vm.SurfaceLayoutRows : vm.SurfaceLayoutColumns) == 1, "Removal redo failed.");
            });
        }
        Check("LastTrackAndImplicitCell", () => { var vm = NativeGridModel(); vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[0]); vm.DeleteLayoutGridColumnCommand.Execute(vm.LayoutGridColumns[0]); RequireCapability(vm.SurfaceLayoutRows == 1 && vm.SurfaceLayoutColumns == 1 && !vm.HasPendingGridRemoval, "Last track was deleted."); });
        Check("StaleRemovalCannotMutate", () =>
        {
            var vm = GridEditorModel(); var c = vm.CreateControl("Button", 0, 0); vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[0]); c.GridRow = 1;
            vm.ConfirmGridTrackRemovalCommand.Execute(null); RequireCapability(vm.SurfaceLayoutRows == 2 && c.GridRow == 1, "Stale confirmation changed placement.");
        });
        foreach (var kind in new[] { "Auto", "Star", "Pixel" })
            Check("GridLength" + kind, () =>
            {
                var vm = GridEditorModel(); var item = vm.LayoutGridRows[0]; item.DisplaySizeKind = kind; if (kind != "Auto") item.Value = kind == "Star" ? 2.5 : 125;
                RequireCapability(vm.SurfaceGridRowDefinitions.StartsWith(kind == "Auto" ? "Auto," : kind == "Star" ? "2.5*," : "125,"), "Editor did not update canonical definitions.");
                RequireCapability(item.IsValueEditable == (kind != "Auto"), "Auto exposed a numeric editor.");
            });
        Check("PixelResizePreviewCommitUndo", () =>
        {
            var vm = GridEditorModel(); RequireCapability(vm.BeginGridTrackResize(true, 0, 80, 320), "Pixel boundary rejected."); vm.UpdateGridTrackResize(35);
            RequireCapability(vm.SurfaceGridRowDefinitions == "80,*", "A preview gesture wrote into the document."); Near(GridEditorTree(vm).MeasuredGrids[""].Rows[0].Size, 115, "Live native resize projection");
            vm.FinishGridTrackResize(true); RequireCapability(vm.SurfaceGridRowDefinitions == "115,*", "Pixel commit failed."); GridEditorUndo(vm); RequireCapability(vm.SurfaceGridRowDefinitions == "80,*", "Resize undo failed."); GridEditorUndo(vm, true); RequireCapability(vm.SurfaceGridRowDefinitions == "115,*", "Resize redo failed.");
        });
        Check("StarResizeKeepsAdaptiveSemantics", () =>
        {
            var vm = GridEditorModel(); RequireCapability(vm.BeginGridTrackResize(false, 0, 400, 200), "Star pair rejected."); vm.UpdateGridTrackResize(60); vm.FinishGridTrackResize(true);
            var defs = ColumnDefinitions.Parse(vm.SurfaceGridColumnDefinitions); RequireCapability(defs.All(d => d.Width.IsStar), "Star changed to Pixel."); Near(defs[0].Width.Value + defs[1].Width.Value, 3, "Total star weight"); Near(GridEditorTree(vm).MeasuredGrids[""].Columns[0].Size, 460, "Star resize layout");
            vm.DesignWidth = 900; Near(GridEditorTree(vm).MeasuredGrids[""].Columns[0].Size, 690, "Resized weights remain responsive");
        });
        Check("AutoAndMixedResizeSafety", () =>
        {
            var vm = GridEditorModel("Auto,*", "*,100"); RequireCapability(!vm.BeginGridTrackResize(true, 0, 30, 370) && !vm.BeginGridTrackResize(false, 0, 500, 100), "Ambiguous resize was enabled.");
            RequireCapability(vm.SurfaceGridRowDefinitions == "Auto,*" && vm.SurfaceGridColumnDefinitions == "*,100", "Resize safety changed definitions.");
        });
        Check("ResizeCancelAndStaleCommit", () =>
        {
            var vm = GridEditorModel(); vm.BeginGridTrackResize(true, 0, 80, 320); vm.UpdateGridTrackResize(90); vm.FinishGridTrackResize(false); RequireCapability(vm.SurfaceGridRowDefinitions == "80,*", "Cancel changed document.");
            vm.BeginGridTrackResize(true, 0, 80, 320); vm.UpdateGridTrackResize(90); vm.SurfaceGridRowDefinitions = "120,*"; vm.FinishGridTrackResize(true); RequireCapability(vm.SurfaceGridRowDefinitions == "120,*", "Stale resize overwrote an external edit.");
        });
        Check("GridLengthUndoIsIsolated", () =>
        {
            var vm = GridEditorModel(); vm.LayoutGridRows[0].Value = 125; GridEditorUndo(vm);
            RequireCapability(vm.SurfaceGridRowDefinitions == "80,*", "GridLength edit merged with earlier setup."); GridEditorUndo(vm, true); RequireCapability(vm.SurfaceGridRowDefinitions == "125,*", "GridLength redo failed.");
        });
        Check("RepeatedAtomicInsertUndo", () =>
        {
            var vm = GridEditorModel(); vm.InsertGridTrack(true, 0); vm.InsertGridTrack(true, 0); GridEditorUndo(vm); RequireCapability(vm.SurfaceLayoutRows == 3, "Adjacent inserts coalesced."); GridEditorUndo(vm); RequireCapability(vm.SurfaceLayoutRows == 2, "Second undo failed.");
        });
        Check("NestedGridActualPaddingAndScope", () =>
        {
            var vm = NativeGridModel(); var parent = vm.CreateControl("Grid", 0, 0); parent.Margin = "15"; parent.GridRowDefinitions = "70,*"; parent.Rows = 2;
            var child = vm.CreateControl("Button", 0, 0, parent.Id); child.GridRow = 1;
            var tree = GridEditorTree(vm); var actual = tree.MeasuredGrids[parent.Id]; RequireCapability(actual.Bounds.X > 15 && actual.Bounds.Y > 15, "Nested panel omitted border/padding."); Near(actual.Rows[0].Size, 70, "Nested track");
            vm.SelectSingleControl(parent); vm.InsertGridTrack(true, 0); RequireCapability(child.GridRow == 2 && vm.SurfaceLayoutRows == 1, "Nested edit affected root or lost ParentId.");
        });
        Check("SelectionPreservedAndInspectorSync", () =>
        {
            var vm = GridEditorModel("*,*,*"); vm.SelectGridTrack(true, 2); vm.SurfaceGridRowDefinitions = "50,60,*"; RequireCapability(vm.SelectedLayoutGridRow?.Index == 2, "Definition update reset selected track.");
            var row = vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "SurfaceGridRowDefinitions"); row.Value = "Auto,120,*"; row.CommitValue(); RequireCapability(vm.LayoutGridRows[0].DisplaySizeKind == "Auto" && vm.LayoutGridRows[1].Value == 120, "Inspector did not update the shared editor.");
            vm.LayoutGridRows[1].Value = 140; RequireCapability(vm.PropertyGridCategories.SelectMany(c => c.Rows).Single(r => r.Key == "SurfaceGridRowDefinitions").Value.Contains("140"), "Shared editor did not refresh Inspector.");
        });
        Check("InspectorShrinkCannotMoveSilently", () =>
        {
            var vm = GridEditorModel(); var c = vm.CreateControl("Button", 0, 0); c.GridRow = 1; vm.SelectSingleControl(null);
            var row = vm.PropertyGridCategories.SelectMany(g => g.Rows).Single(r => r.Key == "SurfaceGridRowDefinitions"); row.Value = "*"; row.CommitValue(); RequireCapability(vm.SurfaceLayoutRows == 2 && c.GridRow == 1, "Inspector silently removed occupied tracks.");
        });
        Check("ReorderCannotSplitSpan", () =>
        {
            var vm = GridEditorModel("50,60,70"); var c = vm.CreateControl("Button", 0, 0); c.GridRow = 1; c.GridRowSpan = 2;
            vm.MoveLayoutGridRowUpCommand.Execute(vm.LayoutGridRows[1]); RequireCapability(vm.SurfaceGridRowDefinitions == "50,60,70" && c.GridRow == 1 && c.GridRowSpan == 2, "Reorder split a contiguous span.");
        });
        Check("JsonFileSaveLoadAndOrder", () =>
        {
            var vm = GridEditorModel(); vm.CreateControl("Button", 0, 0); vm.InsertGridTrack(true, 0); vm.LayoutGridColumns[0].Value = 3;
            var path = Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase3", "grid-editor.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, vm.ExportWorkspaceJson());
            var restored = CreateViewModel("GridEditorReload"); restored.LoadDocumentJson(File.ReadAllText(path)); RequireCapability(restored.SurfaceGridRowDefinitions == vm.SurfaceGridRowDefinitions && restored.Controls[0].Id == vm.Controls[0].Id && restored.Controls[0].GridRow == 1 && restored.SurfaceGridColumnDefinitions.StartsWith("3*"), "File reload lost structure/order.");
        });
        Check("MultiFormPendingAndGestureIsolation", () =>
        {
            var vm = GridEditorModel(); vm.CreateControl("Button", 0, 0); vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[0]); vm.BeginGridTrackResize(true, 0, 80, 320); vm.UpdateGridTrackResize(60); var id = vm.ActiveFormDocument!.Id;
            var added = vm.CreateNewForm(); RequireCapability(!vm.HasPendingGridRemoval && !vm.UsesNativeLayout, $"Pending operation leaked into Canvas: pending={vm.HasPendingGridRemoval}; native={vm.UsesNativeLayout}; mode={vm.SurfaceLayoutMode}; version={vm.LayoutSemanticsVersion}; newVersion={added.Document.LayoutSemanticsVersion}; newMode={added.Document.SurfaceLayoutMode}."); vm.FinishGridTrackResize(true); vm.SetActiveForm(id, "verify editor isolation"); RequireCapability(vm.SurfaceGridRowDefinitions == "80,*" && vm.Controls.Single().GridRow == 0, "Switching form committed a temporary gesture.");
        });
        foreach (var axaml in new[] { false, true })
            Check((axaml ? "Axaml" : "Legacy") + "PreviewAndResize", () =>
            {
                var vm = NativeGridControls(); vm.LayoutGridColumns[0].Value = 2; vm.InsertGridTrack(true, 1);
                vm.LayoutGridRows[1].DisplaySizeKind = "Pixel"; vm.LayoutGridRows[1].Value = 40;
                using var preview = ShowDataGridRegressionPreview(vm, axaml); var window = preview.Window; var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(); var before = grid.Bounds.Size;
                window.Width += 180; window.Height += 90; window.UpdateLayout(); NativeItemsUiWait(180); grid = window.GetVisualDescendants().OfType<DataGrid>().Single(); RequireCapability(grid.Bounds.Width > before.Width + 160 && grid.Bounds.Height > before.Height + 70, "Edited layout did not resize in Preview.");
            });
        Check("ExportDoesNotContainOverlays", () => { var vm = NativeGridControls(); vm.InsertGridTrack(false, 0); vm.GenerateXaml(); RequireCapability(vm.GeneratedXaml.Contains("<Grid x:Name=\"RootLayout\"") && !vm.GeneratedXaml.Contains("GridDefinitionOverlay") && !vm.GeneratedXaml.Contains("Boundary_") && !vm.GeneratedXaml.Contains("Canvas.Left"), "Overlay/Canvas leaked to export."); });
        Check("AxamlRoundTripVisualReadOnly", () =>
        {
            var (source, vm) = LayoutFixture("GridDefinitions"); vm.SelectSingleControl(vm.Controls.First(c => c.Type == "Grid"));
            vm.AddGridRowDefinitionCommand.Execute(null); vm.InsertGridTrack(false, 0); vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[0]);
            var patch = vm.CreateActiveAxamlPatch(); RequireCapability(vm.IsGridDefinitionReadOnly && !vm.CanEditGridDefinitions && patch.CanApply && !patch.HasChanges && !vm.HasPendingGridRemoval, "Visual editor changed imported source.");
        });
        RunGridEditorPointerChecks(Check);
        Console.WriteLine($"Grid Editor checks: {passed}/{passed + failed}");
        return failed == 0 && passed > 0 ? 0 : 1;
    }

    private static void ConfigureGridEditorPhase3Export(MainWindowViewModel vm)
    {
        ConfigureNativeGridExport(vm);
        vm.LayoutGridRows[1].DisplaySizeKind = "Pixel"; vm.LayoutGridRows[1].Value = 62;
        vm.LayoutGridColumns[0].Value = 2; vm.LayoutGridColumns[1].DisplaySizeKind = "Star"; vm.LayoutGridColumns[1].Value = 1;
        vm.InsertGridTrack(true, 1); vm.DeleteLayoutGridRowCommand.Execute(vm.LayoutGridRows[1]);
    }

    private static PointerFixture GridEditorPointerFixture()
    {
        var fixture = new PointerFixture(); fixture.Vm.CreateNewForm("Grid"); fixture.Vm.DesignWidth = 420; fixture.Vm.DesignHeight = 260;
        fixture.Vm.SurfaceGridRowDefinitions = "80,*"; fixture.Vm.SurfaceLayoutRows = 2;
        fixture.Vm.SurfaceGridColumnDefinitions = "2*,*"; fixture.Vm.SurfaceLayoutColumns = 2;
        fixture.Refresh(); return fixture;
    }
    private static Point GridEditorHandlePoint(PointerFixture fixture, string name)
    {
        var handle = fixture.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name);
        var point = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), fixture.Window)!.Value;
        RequireCapability(ReferenceEquals(fixture.Window.InputHitTest(point), handle), "Real input hit test missed " + name);
        return point;
    }
    private static void RunGridEditorPointerChecks(Action<string, Action> check)
    {
        check("ExistingEditorUiComboAndOverlayToggle", () =>
        {
            using var f = GridEditorPointerFixture(); f.Vm.SetActiveLayoutToGridCommand.Execute(null); f.Refresh();
            var list = f.Window.GetVisualDescendants().OfType<ListBox>().Single(b => b.Name == "GridRowsEditorList"); list.SelectedItem = f.Vm.LayoutGridRows[1]; NativeItemsUiWait(80);
            var combo = list.GetVisualDescendants().OfType<ComboBox>().Last(); combo.SelectedItem = "Pixel"; NativeItemsUiWait(100); f.Vm.LayoutGridRows[1].Value = 90; NativeItemsUiWait(100);
            RequireCapability(f.Vm.SelectedLayoutGridRow?.Index == 1 && f.Vm.SurfaceGridRowDefinitions == "80,90", "Existing UI ComboBox did not update shared definitions.");
            combo.SelectedItem = "Auto"; NativeItemsUiWait(100); RequireCapability(f.Vm.SurfaceGridRowDefinitions == "80,Auto", "Auto selection did not update definitions.");
            var content = (Avalonia.Controls.Control)f.Window.Content!;
            using (var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height)))
            { image.Render(content); var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase3"); Directory.CreateDirectory(folder); image.Save(Path.Combine(folder, "grid-editor.png")); }
            var before = f.Vm.CreatePreviewDocumentSnapshot(); f.Vm.ToggleActiveGridOverlayCommand.Execute(null); f.Refresh();
            RequireCapability(!f.Window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "GridDefinitionOverlay").IsVisible && before.SurfaceGridRowDefinitions == f.Vm.SurfaceGridRowDefinitions, "Overlay toggle changed the document or remained visible.");
        });
        check("PointerRowSelectKeepsControlSelection", () => { using var f = GridEditorPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0); f.Refresh(); f.Click(GridEditorHandlePoint(f, "GridRowHeader_1")); f.AssertIdle(); RequireCapability(f.Vm.SelectedControl?.Id == c.Id && f.Vm.SelectedLayoutGridRow?.Index == 1, "Track selection replaced Inspector control selection."); });
        check("FixedTracksTerminalGuideUsesActualSize", () =>
        {
            using var f = GridEditorPointerFixture(); f.Vm.SurfaceGridRowDefinitions = "50,60"; f.Refresh();
            RequireCapability(f.Surface.GridOverlay.Children.All(c => c is Avalonia.Controls.Shapes.Line), "Legacy uniform tracks/labels overlap the native guides.");
            var overlay = f.Window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "GridDefinitionOverlay");
            var measured = f.Vm.NativeGridMeasurements[""];
            var actualBottom = measured.Bounds.Y + measured.Rows.Sum(row => row.Size);
            RequireCapability(actualBottom < measured.Bounds.Bottom - 100 && overlay.Children.OfType<Avalonia.Controls.Shapes.Line>().Any(l => Math.Abs(l.StartPoint.Y - actualBottom) < 0.1 && Math.Abs(l.EndPoint.Y - actualBottom) < 0.1), "Terminal guide must follow Avalonia actual tracks, leaving unallocated space visible: actualBottom=" + actualBottom);
        });
        foreach (var ending in new[] { "Release", "Escape", "CaptureLost", "FormSwitch" })
            check("PointerPixelResize" + ending, () =>
            {
                using var f = GridEditorPointerFixture(); var p = GridEditorHandlePoint(f, "GridRowBoundary_0"); f.Down(p); f.ExpectMode(DesignerInteractionMode.ResizingGridRow); f.Move(p + new Vector(0, 25)); f.NoMarquee();
                if (ending == "Escape") f.Escape(); else if (ending == "CaptureLost") f.Pointer.Capture(null); else if (ending == "FormSwitch") f.Vm.CreateNewForm(); else f.Up(p + new Vector(0, 25));
                f.AssertIdle(); if (ending != "FormSwitch") RequireCapability(f.Vm.SurfaceGridRowDefinitions == (ending == "Release" ? "105,*" : "80,*"), "Resize ending corrupted definitions.");
            });
        check("PointerStarResizeAndControlHit", () =>
        {
            using var f = GridEditorPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0); f.Refresh(); f.Click(f.ControlPoint(c)); f.AssertIdle();
            var p = GridEditorHandlePoint(f, "GridColumnBoundary_0"); f.Down(p); f.Move(p + new Vector(20, 0)); f.Up(p + new Vector(20, 0)); f.AssertIdle(); RequireCapability(ColumnDefinitions.Parse(f.Vm.SurfaceGridColumnDefinitions).All(d => d.Width.IsStar), "Pointer resize converted Star to Pixel.");
        });
        check("PointerReleaseOutsideRulerAndWindowClose", () =>
        {
            using var f = GridEditorPointerFixture(); var p = GridEditorHandlePoint(f, "GridRowBoundary_0"); f.Down(p); var moved = p + new Vector(50, 45); f.Move(moved); f.Up(moved); f.AssertIdle(); RequireCapability(f.Vm.SurfaceGridRowDefinitions == "125,*", "Captured release outside the handle was lost.");
            f.Refresh(); p = GridEditorHandlePoint(f, "GridRowBoundary_0"); f.Down(p); f.Move(p + new Vector(0, 30)); f.Window.CloseForExternalHost(); f.AssertIdle(); RequireCapability(f.Vm.SurfaceGridRowDefinitions == "125,*", "Window close committed a temporary resize.");
        });
        check("PointerAutoBoundaryReadOnly", () =>
        {
            using var f = GridEditorPointerFixture(); f.Vm.SurfaceGridRowDefinitions = "Auto,*"; f.Vm.CreateControl("Button", 0, 0); f.Refresh(); var p = GridEditorHandlePoint(f, "GridRowBoundary_0"); f.Down(p); f.Move(p + new Vector(0, 25)); f.Up(p); f.AssertIdle(); RequireCapability(f.Vm.SurfaceGridRowDefinitions == "Auto,*", "Auto pointer drag changed semantics.");
        });
        foreach (var zoom in new[] { 0.75, 1.5 })
            check("PointerZoom" + zoom, () =>
            {
                using var f = GridEditorPointerFixture(); typeof(FormDesigner.Views.MainWindow).GetMethod("SetSurfaceZoom", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, new object?[] { zoom, null }); f.Refresh();
                var p = GridEditorHandlePoint(f, "GridRowBoundary_0"); f.Down(p); f.Move(p + new Vector(0, 20 * zoom)); f.Up(p + new Vector(0, 20 * zoom)); f.AssertIdle(); Near(RowDefinitions.Parse(f.Vm.SurfaceGridRowDefinitions)[0].Height.Value, 100, "Zoom-correct DIP delta");
                RequireCapability(f.Window.RenderScaling > 0, "Native DPI scaling is unavailable."); Console.WriteLine($"OBSERVED GridEditor zoom={zoom}; nativeRenderScaling={f.Window.RenderScaling}");
            });
    }
}
