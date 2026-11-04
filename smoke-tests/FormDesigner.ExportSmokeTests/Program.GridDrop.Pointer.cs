using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static bool _gridDropSnapshotSaved;
    private static PointerFixture GridDropPointerFixture()
    {
        var f = GridEditorPointerFixture(); f.Vm.SurfaceGridColumnDefinitions = "200,*"; f.Refresh(); return f;
    }
    private static DataObject GridToolboxData(PointerFixture f, string type = "Button", string? source = null)
    {
        var data = new DataObject(); data.Set("formdesigner-control-type", type); data.Set("formdesigner-source-document", source ?? f.Vm.ActiveDocumentId);
        data.Set("formdesigner-toolbox-gesture", Guid.NewGuid().ToString("N")); return data;
    }
    private static DragEventArgs RouteGridToolbox(PointerFixture f, DataObject data, RoutedEvent<DragEventArgs> routed, Point canvasPoint)
    {
        var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { routed, data, f.Surface.Canvas, canvasPoint, KeyModifiers.None }, null)!;
        f.Surface.Canvas.RaiseEvent(e); return e;
    }
    private static void GridPointerMove(PointerFixture f, DesignControlModel model, Point destination)
    {
        var point = f.ControlPoint(model); f.Down(point); f.ExpectMode(DesignerInteractionMode.GridControlPending);
        var end = f.CanvasPoint(destination.X, destination.Y); f.Move(end); f.ExpectMode(DesignerInteractionMode.MovingGridControl);
        if (!_gridDropSnapshotSaved)
        {
            f.Refresh(); var content = (Avalonia.Controls.Control)f.Window.Content!;
            using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height)); image.Render(content);
            var folder = Path.Combine(FindRepositoryRoot(), "artifacts", "layout-phase4"); Directory.CreateDirectory(folder); image.Save(Path.Combine(folder, "grid-drop-preview.png")); _gridDropSnapshotSaved = true;
        }
        f.Up(end); f.AssertIdle();
    }
    private static void RunGridDropPointerChecks(Action<string, Action> check)
    {
        foreach (var cell in new[] { 0, 1 })
            check("RoutedToolboxCell" + cell, () =>
            {
                using var f = GridDropPointerFixture(); var point = GridCellPoint(f.Vm, "", cell, cell); var data = GridToolboxData(f);
                var over = RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); RequireCapability(over.Handled && over.DragEffects == DragDropEffects.Copy, "Real DragOver refused valid cell.");
                RequireCapability(f.Vm.Controls.Count == 0 && f.Surface.GuideOverlay.Children.Count > 0 && !f.Surface.GuideOverlay.IsHitTestVisible, "Preview mutated document or intercepted input.");
                var drop = RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.Refresh(); f.AssertIdle();
                RequireCapability(drop.Handled && f.Vm.Controls.Single().GridRow == cell && f.Vm.Controls.Single().GridColumn == cell && f.Vm.SelectedControl?.Id == f.Vm.Controls.Single().Id, "Routed drop failed.");
            });
        check("PointerMoveSelectionInspectorAndUndo", () =>
        {
            using var f = GridDropPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0); f.Refresh(); var id = c.Id;
            GridPointerMove(f, c, GridCellPoint(f.Vm, "", 1, 1));
            RequireCapability(c.GridRow == 1 && c.GridColumn == 1 && c.X == 0 && c.Y == 0 && f.Vm.SelectedControl?.Id == id, "Pointer move damaged state.");
            RequireCapability(f.Vm.PropertyGridCategories.SelectMany(g => g.Rows).Single(r => r.Key == "GridRow").Value == "1", "Inspector row was stale.");
            GridEditorUndo(f.Vm); RequireCapability(f.Vm.Controls.Single().GridRow == 0 && f.Vm.Controls.Single().Id == id, "Pointer move undo failed."); GridEditorUndo(f.Vm, true); RequireCapability(f.Vm.Controls.Single().GridColumn == 1, "Pointer redo failed.");
        });
        check("RoutedToolboxNestedGrid", () =>
        {
            using var f = GridDropPointerFixture();
            var nested = f.Vm.CreateControl("Grid", 0, 0); nested.GridColumn = 1; nested.GridRowSpan = 2;
            nested.GridRowDefinitions = "70,*"; nested.Rows = 2; nested.GridColumnDefinitions = "*"; nested.Columns = 1; nested.Margin = "8";
            f.Refresh(); var point = GridCellPoint(f.Vm, nested.Id, 1, 0); var data = GridToolboxData(f, "TextBox");
            var over = RouteGridToolbox(f, data, DragDrop.DragOverEvent, point);
            RequireCapability(over.DragEffects == DragDropEffects.Copy, "Nested Grid was not a valid visual Toolbox target.");
            RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.Refresh(); f.AssertIdle();
            var added = f.Vm.Controls.Single(c => c.Type == "TextBox");
            RequireCapability(added.ParentId == nested.Id && added.GridRow == 1 && added.GridColumn == 0 && added.X == 0 && added.Y == 0 && f.Vm.SelectedControl?.Id == added.Id, "Nested Toolbox drop selected the root or damaged placement.");
        });
        check("PointerInvalidSpanKeepsSelectionAndModel", () =>
        {
            using var f = GridDropPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0); c.GridColumnSpan = 2; f.Refresh();
            GridPointerMove(f, c, GridCellPoint(f.Vm, "", 1, 1));
            RequireCapability(c.GridRow == 0 && c.GridColumn == 0 && c.GridColumnSpan == 2 && f.Vm.SelectedControl?.Id == c.Id, "An invalid visual drop clamped span or lost selection.");
            RequireCapability(f.Window.GridDragDiagnostics.Any(d => d.Event == "GRID_DROP_CANCELLED" && d.Reason.Contains("Span")), "Invalid visual span drop was not diagnosed.");
        });
        check("PointerNestedReparentAndDiagnostics", () =>
        {
            using var f = GridDropPointerFixture(); f.Vm.SurfaceGridRowDefinitions = "*"; f.Vm.SurfaceLayoutRows = 1;
            var left = f.Vm.CreateControl("Grid", 0, 0); left.GridColumnDefinitions = "*"; left.Columns = 1; left.GridRowDefinitions = "70,*"; left.Rows = 2;
            var right = f.Vm.CreateControl("Grid", 0, 0); right.GridColumn = 1; right.GridColumnDefinitions = "*"; right.Columns = 1; right.GridRowDefinitions = "70,*"; right.Rows = 2;
            var c = f.Vm.CreateControl("Button", 0, 0, left.Id); f.Refresh(); GridPointerMove(f, c, GridCellPoint(f.Vm, right.Id, 1, 0));
            RequireCapability(c.ParentId == right.Id && c.GridRow == 1 && f.Vm.SelectedControl?.Id == c.Id, "Nested pointer reparent failed.");
            foreach (var code in new[] { "GRID_DRAG_START", "GRID_CELL_HIT_TEST", "GRID_DROP_TARGET_RESOLVED", "GRID_DROP_PREVIEW", "GRID_DROP_COMMITTED", "GRID_REPARENT_START", "GRID_REPARENT_SUCCESS" })
                RequireCapability(f.Window.GridDragDiagnostics.Any(d => d.Event == code && d.ControlId == c.Id), "Missing diagnostic " + code);
            RequireCapability(f.Window.GridDragDiagnostics.Last(d => d.Event == "GRID_REPARENT_SUCCESS").OldParentId == left.Id, "Diagnostics lost the original parent.");
        });
        foreach (var ending in new[] { "Escape", "CaptureLost", "Outside", "FormSwitch", "WindowClose" })
            check("PointerCancel" + ending, () =>
            {
                using var f = GridDropPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0); f.Refresh();
                var point = f.ControlPoint(c); var end = f.CanvasPoint(300, 160); f.Down(point); f.Move(end); f.NoMarquee();
                if (ending == "Escape") f.Escape(); else if (ending == "CaptureLost") f.Pointer.Capture(null);
                else if (ending == "Outside") { var outside = f.CanvasPoint(-12, 80); f.Move(outside); f.Up(outside); }
                else if (ending == "FormSwitch") f.Vm.CreateNewForm(DesignerLayoutModes.Absolute); else f.Window.CloseForExternalHost();
                f.AssertIdle(); RequireCapability(c.GridRow == 0 && c.GridColumn == 0 && c.X == 0 && c.Y == 0, "Cancelled drag mutated source.");
                RequireCapability(f.Window.GridDragDiagnostics.Any(d => d.Event == "GRID_DROP_CANCELLED"), "Cancellation was not diagnosed.");
            });
        check("ToolboxEscapeThenStaleDropRejected", () =>
        {
            using var f = GridDropPointerFixture(); var data = GridToolboxData(f); var point = GridCellPoint(f.Vm, "", 1, 1);
            RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); f.Escape(); f.AssertIdle(); RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.AssertIdle();
            RequireCapability(f.Vm.Controls.Count == 0, "A stale Drop created a control after Escape.");
            data = GridToolboxData(f); RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.AssertIdle(); RequireCapability(f.Vm.Controls.Count == 1, "A fresh drag could not recover.");
        });
        check("ToolboxFormSwitchAndStaleGeometryRejected", () =>
        {
            using var f = GridDropPointerFixture(); var data = GridToolboxData(f); var point = GridCellPoint(f.Vm, "", 1, 1);
            RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); f.Vm.CreateNewForm("Grid"); f.Refresh(); RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); RouteGridToolbox(f, data, DragDrop.DropEvent, point);
            f.AssertIdle(); RequireCapability(f.Vm.Controls.Count == 0, "Toolbox drag leaked into a new form.");
        });
        check("ToolboxOldGestureCannotCommitNewPreview", () =>
        {
            using var f = GridDropPointerFixture(); var oldData = GridToolboxData(f); var newData = GridToolboxData(f); var p = GridCellPoint(f.Vm, "", 1, 1);
            RouteGridToolbox(f, oldData, DragDrop.DragOverEvent, p); f.Escape(); RouteGridToolbox(f, newData, DragDrop.DragOverEvent, p);
            RouteGridToolbox(f, oldData, DragDrop.DropEvent, p); f.AssertIdle(); RequireCapability(f.Vm.Controls.Count == 0, "An old gesture committed a newer preview.");
        });
        check("RepeatedClickNeverMarqueeAndMultiSelectionSafe", () =>
        {
            using var f = GridDropPointerFixture(); var a = f.Vm.CreateControl("Button", 0, 0); var b = f.Vm.CreateControl("TextBox", 0, 0); b.GridColumn = 1; f.Refresh();
            var point = f.ControlPoint(a); f.Click(point); f.AssertIdle(); f.Click(point); f.AssertIdle(); RequireCapability(a.GridRow == 0, "Click changed placement.");
            f.Escape(); f.Refresh(); // The repeated click is a double-click and opens the existing inline text editor.
            a.GridRow = 1; // Keep the source below the existing multi-selection toolbar at the form's top edge.
            f.Vm.SelectControls(new[] { a, b }, a); f.Refresh(); NativeItemsUiWait(600); f.Down(f.ControlPoint(a)); f.Move(f.CanvasPoint(300, 170)); f.Up(f.CanvasPoint(300, 170)); f.AssertIdle();
            RequireCapability(f.Vm.HasMultipleSelection && a.GridRow == 1 && b.GridRow == 0, "Unsupported group drag collapsed selection or moved one member.");
        });
        check("RoutedUnknownToolboxTypeRejected", () =>
        {
            using var f = GridDropPointerFixture(); var data = GridToolboxData(f, "Unavailable.PluginControl"); var p = GridCellPoint(f.Vm, "", 1, 1);
            RequireCapability(RouteGridToolbox(f, data, DragDrop.DragOverEvent, p).DragEffects == DragDropEffects.None, "Unavailable type was advertised as valid.");
            RouteGridToolbox(f, data, DragDrop.DropEvent, p); f.AssertIdle(); RequireCapability(f.Vm.Controls.Count == 0, "Unavailable type created a damaged node.");
        });
        check("RoutedSourceAxamlGridReadOnly", () =>
        {
            using var f = GridDropPointerFixture(); var path = Path.Combine(FindRepositoryRoot(), "Samples", "RoundTrip", "Layout", "GridDefinitions.axaml");
            f.Vm.LoadAxamlImportedDocument(new AxamlImportService().Import(File.ReadAllText(path), path), path); f.Refresh(); var count = f.Vm.Controls.Count; var data = GridToolboxData(f);
            var over = RouteGridToolbox(f, data, DragDrop.DragOverEvent, new Point(200, 100)); RouteGridToolbox(f, data, DragDrop.DropEvent, new Point(200, 100)); f.AssertIdle();
            RequireCapability(over.DragEffects == DragDropEffects.None && f.Vm.Controls.Count == count && !f.Vm.CreateActiveAxamlPatch().HasChanges, "Routed source drop modified AXAML.");
        });
        check("PreviewDoesNotFloodDiagnostics", () =>
        {
            using var f = GridDropPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0); f.Refresh(); var start = f.ControlPoint(c); var cell = GridCellPoint(f.Vm, "", 1, 1);
            f.Down(start); for (var i = 0; i < 12; i++) f.Move(f.CanvasPoint(cell.X + i * 0.1, cell.Y)); f.Up(f.CanvasPoint(cell.X, cell.Y)); f.AssertIdle();
            RequireCapability(f.Window.GridDragDiagnostics.Count(d => d.Event == "GRID_DROP_PREVIEW") <= 2, "A diagnostic was emitted per pixel movement.");
        });
        foreach (var zoom in new[] { 0.75, 1.5 })
            check("NativePointerZoom" + zoom, () =>
            {
                using var f = GridDropPointerFixture(); var c = f.Vm.CreateControl("Button", 0, 0);
                typeof(FormDesigner.Views.MainWindow).GetMethod("SetSurfaceZoom", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, new object?[] { zoom, null }); f.Refresh();
                GridPointerMove(f, c, GridCellPoint(f.Vm, "", 1, 1)); RequireCapability(c.GridRow == 1 && c.GridColumn == 1, "Zoom caused wrong cell.");
                Console.WriteLine($"OBSERVED GridDrop zoom={zoom}; RenderScaling={f.Window.RenderScaling}");
            });
        foreach (var type in new[] { "DataGrid", "Eremex.DataGridControl" })
            check("RoutedToolbox" + type, () =>
            {
                using var f = GridDropPointerFixture(); if (type.StartsWith("Eremex")) LoadBuiltEremexPlugin(f.Vm); f.Vm.IncludePluginRuntimeReferences = true; f.Vm.DataGridExportMode = FormDesigner.ViewModels.MainWindowViewModel.DataGridExportModeReal; f.Refresh();
                var data = GridToolboxData(f, type); var point = GridCellPoint(f.Vm, "", 1, 1); RouteGridToolbox(f, data, DragDrop.DragOverEvent, point); RouteGridToolbox(f, data, DragDrop.DropEvent, point); f.Refresh(); f.AssertIdle();
                RequireCapability(f.Vm.Controls.Single().Type == type && f.Vm.Controls.Single().GridColumn == 1, "Grid control drop failed."); f.Vm.GenerateXaml(); RequireCapability(!f.Vm.GeneratedXaml.Contains("Canvas.Left"), "Grid control emitted Canvas coordinates.");
                // Move from the component frame, leaving DataGrid header resize/edit gestures intact.
                var model = f.Vm.Controls.Single();
                var wrappers = (Dictionary<string, Border>)typeof(FormDesigner.Views.MainWindow).GetField("_wrapperByControlId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Window)!;
                var frame = wrappers[model.Id].TranslatePoint(new Point(2, 2), f.Window)!.Value;
                var end = GridCellPoint(f.Vm, "", 0, 0); f.Down(frame); f.ExpectMode(DesignerInteractionMode.GridControlPending); f.Move(f.CanvasPoint(end.X, end.Y)); f.Up(f.CanvasPoint(end.X, end.Y)); f.AssertIdle();
                RequireCapability(model.GridRow == 0 && model.GridColumn == 0, "Grid control could not be moved from its frame.");
            });
    }
}
