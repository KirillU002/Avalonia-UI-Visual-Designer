using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using FormDesigner.Views;
using System.Reflection;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    private static int RunDesignerPointerChecks()
    {
        var failed = 0; var total = 0;
        void Check(string name, Action<PointerFixture> test)
        {
            var filter = Environment.GetEnvironmentVariable("SMOKE_SCENARIO_FILTER");
            if (!string.IsNullOrWhiteSpace(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
            total++;
            try { using var fixture = new PointerFixture(); test(fixture); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        Check("Pointer_SelectedClick", f => { f.Vm.SelectSingleControl(f.First); f.Refresh(); f.Click(f.ControlPoint(f.First)); f.NoMarquee(); });
        Check("Pointer_UnselectedClick", f => { f.Click(f.ControlPoint(f.First)); f.NoMarquee(); if (f.Vm.SelectedControl?.Id != f.First.Id) throw new Exception("Selection did not reach the Inspector/session."); });
        Check("Pointer_CtrlHandledClick", f => { f.Vm.SelectSingleControl(f.First); f.Refresh(); var p = f.ControlPoint(f.First); f.Down(p, RawInputModifiers.Control); f.NoMarquee(); f.Up(p, RawInputModifiers.Control); });
        Check("Pointer_LockedClick", f => { f.First.IsLocked = true; f.Refresh(); f.Down(f.ControlPoint(f.First)); f.NoMarquee(); f.Up(f.ControlPoint(f.First)); });
        Check("Pointer_RightClick", f => { var p = f.ControlPoint(f.First); f.Send(RawPointerEventType.RightButtonDown, p, RawInputModifiers.RightMouseButton); f.NoMarquee(); f.Send(RawPointerEventType.RightButtonUp, p); });
        Check("Pointer_EmptyCanvasThreshold", f =>
        {
            var point = f.CanvasPoint(30, 300); f.AssertEmpty(point); f.Down(point); if (f.MarqueeCount != 0) throw new Exception("Marquee was rendered on press.");
            f.Move(point + new Vector(2, 1)); if (f.MarqueeCount != 0) throw new Exception("Movement below threshold started Marquee.");
            f.Move(point + new Vector(80, 40)); if (f.MarqueeCount != 1) throw new Exception("Intentional empty-canvas drag did not create Marquee.");
            f.Up(point + new Vector(80, 40)); f.NoMarquee();
        });
        Check("Pointer_CaptureLost", f => { var p = f.CanvasPoint(30, 300); f.Down(p); f.Move(p + new Vector(80, 40)); f.Pointer.Capture(null); f.NoMarquee(); });
        Check("Pointer_ReleasedOutside", f => { var p = f.CanvasPoint(30, 300); f.Down(p); f.Move(p + new Vector(80, 40)); f.Up(new Point(5, 5)); f.NoMarquee(); });
        Check("Pointer_Escape", f =>
        {
            f.Vm.ClearSelection(); f.Refresh(); var p = f.CanvasPoint(30, 300); f.Down(p); f.Move(p + new Vector(80, 40));
            f.Surface.Canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); f.NoMarquee();
        });
        Check("Pointer_ButtonNotPressed", f => { var p = f.CanvasPoint(30, 300); f.Down(p); f.Move(p + new Vector(80, 40)); f.Send(RawPointerEventType.Move, p + new Vector(100, 60)); f.NoMarquee(); });
        Check("Pointer_FormSwitch", f => { var p = f.CanvasPoint(30, 300); f.Down(p); f.Move(p + new Vector(80, 40)); f.Vm.CreateNewForm(DesignerLayoutModes.Absolute); f.Refresh(); f.NoMarquee(); if (f.Pointer.Captured is not null) throw new Exception("Form switch retained capture."); });
        Check("Pointer_MoveControl", f =>
        {
            var p = f.ControlPoint(f.First); var before = new Point(f.First.X, f.First.Y);
            f.Down(p); f.ExpectMode(DesignerInteractionMode.MovingControl); f.NoMarquee();
            f.Move(p + new Vector(60, 70)); f.NoMarquee(); f.Up(p + new Vector(60, 70)); f.AssertIdle();
            if (new Point(f.First.X, f.First.Y) == before) throw new Exception("Native drag did not move the model.");
            if (f.Vm.SelectedControl?.Id != f.First.Id || f.Surface.Session?.SelectedControl?.Id != f.First.Id) throw new Exception("Move lost session/Inspector selection.");
        });
        Check("Pointer_ResizeControl", f =>
        {
            f.Vm.SelectSingleControl(f.First); f.Refresh(); var p = f.ResizePoint(); var before = new Size(f.First.Width, f.First.Height);
            f.Down(p); f.ExpectMode(DesignerInteractionMode.ResizingControl); f.NoMarquee();
            f.Move(p + new Vector(60, 40)); f.NoMarquee(); f.Up(p + new Vector(60, 40)); f.AssertIdle();
            if (f.First.Width <= before.Width || f.First.Height <= before.Height) throw new Exception("Native resize did not change model dimensions.");
        });
        Check("Pointer_ResizeSurface", f =>
        {
            var handle = f.Surface.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "DesignResizeHandle");
            var p = handle.TranslatePoint(new Point(5, 5), f.Window)!.Value; var before = f.Vm.DesignWidth;
            f.Down(p); f.ExpectMode(DesignerInteractionMode.ResizingSurface);
            if (!ReferenceEquals(f.Pointer.Captured, handle)) throw new Exception("Surface resize captured the relay sender instead of the handle.");
            f.Move(p + new Vector(40, 30)); f.NoMarquee(); f.Up(p + new Vector(40, 30)); f.AssertIdle();
            if (f.Vm.DesignWidth <= before) throw new Exception("Surface resize did not reach the VM.");
        });
        Check("Pointer_ToolboxDropThenClick", f =>
        {
            var p = f.CanvasPoint(30, 300); f.Down(p); f.Move(p + new Vector(80, 40));
            var count = f.Vm.Controls.Count;
            f.DropToolbox(new Point(200, 300)); f.AssertIdle();
            if (f.Vm.Controls.Count != count + 1) throw new Exception("Routed Toolbox Drop did not add a component.");
            var added = f.Vm.SelectedControl!; f.Refresh(); f.Click(f.ControlPoint(added)); f.AssertIdle();
            if (f.Vm.SelectedControl?.Id != added.Id) throw new Exception("Click after Drop did not retain selection.");
        });
        Check("Pointer_MultiSelection", f =>
        {
            var p = f.CanvasPoint(160, 110); f.Down(p); f.Move(f.CanvasPoint(600, 185)); f.Up(f.CanvasPoint(600, 185)); f.AssertIdle();
            if (f.Vm.GetSelectedControls().Count != 2) throw new Exception("Native Marquee did not select both components.");
            f.Refresh(); p = f.ControlPoint(f.First); f.Down(p, RawInputModifiers.Control); f.NoMarquee(); f.Up(p, RawInputModifiers.Control);
            if (f.Vm.GetSelectedControls().Count != 1 || f.Vm.IsControlSelected(f.First)) throw new Exception("Ctrl click did not toggle the selected component.");
        });
        Check("Pointer_CtrlMarqueeToggle", f =>
        {
            f.Vm.SelectSingleControl(f.First); f.Refresh(); var p = f.CanvasPoint(160, 110);
            f.Down(p, RawInputModifiers.Control); f.Move(f.CanvasPoint(600, 185), RawInputModifiers.Control); f.Up(f.CanvasPoint(600, 185), RawInputModifiers.Control); f.AssertIdle();
            if (f.Vm.GetSelectedControls().Count != 1 || f.Vm.IsControlSelected(f.First)) throw new Exception("Marquee lost its Ctrl toggle flag before applying selection.");
        });
        Check("Pointer_ShiftMarqueeAdd", f =>
        {
            f.Vm.SelectSingleControl(f.First); f.Refresh(); var p = f.CanvasPoint(400, 110);
            f.Down(p, RawInputModifiers.Shift); f.Move(f.CanvasPoint(600, 185), RawInputModifiers.Shift); f.Up(f.CanvasPoint(600, 185), RawInputModifiers.Shift); f.AssertIdle();
            if (f.Vm.GetSelectedControls().Count != 2) throw new Exception("Shift Marquee lost the existing selection.");
        });
        Check("Pointer_RepeatedClick", f => { var p = f.ControlPoint(f.First); f.Click(p); f.Refresh(); f.Click(f.ControlPoint(f.First)); f.NoMarquee(); });
        Check("Pointer_MoveCaptureLost", f =>
        {
            var p = f.ControlPoint(f.First); f.Down(p); f.Move(p + new Vector(60, 70));
            f.Pointer.Capture(f.Surface.Canvas); f.NoMarquee();
            if (!ReferenceEquals(f.Pointer.Captured, f.Surface.Canvas)) throw new Exception("Cancellation released another owner's capture.");
            f.Pointer.Capture(null); f.AssertIdle();
        });
        Check("Pointer_ResizeEscape", f =>
        {
            f.Vm.SelectSingleControl(f.First); f.Refresh(); var p = f.ResizePoint(); f.Down(p); f.Move(p + new Vector(40, 30));
            f.Escape(); f.AssertIdle();
        });
        Check("Pointer_ResizeCaptureLost", f =>
        {
            f.Vm.SelectSingleControl(f.First); f.Refresh(); var p = f.ResizePoint(); f.Down(p); f.Move(p + new Vector(40, 30));
            f.Pointer.Capture(null); f.AssertIdle();
        });
        Check("Pointer_MoveFormSwitch", f =>
        {
            var p = f.ControlPoint(f.First); f.Down(p); f.Move(p + new Vector(40, 30)); f.Vm.CreateNewForm(DesignerLayoutModes.Absolute); f.Refresh(); f.AssertIdle();
        });
        Check("Pointer_ToolboxDragLeave", f =>
        {
            f.ToolboxEvent(DragDrop.DragOverEvent, new Point(200, 300)); f.ExpectMode(DesignerInteractionMode.ToolboxDragDrop);
            f.ToolboxEvent(DragDrop.DragLeaveEvent, new Point(200, 300)); f.AssertIdle();
            f.Click(f.ControlPoint(f.First)); f.AssertIdle();
        });
        Check("Pointer_EmptyClickClearsSelection", f =>
        {
            f.Vm.SelectSingleControl(f.First); f.Refresh(); f.Click(f.CanvasPoint(30, 300)); f.AssertIdle();
            if (f.Vm.SelectedControl is not null) throw new Exception("Empty Canvas click did not clear selection.");
        });
        Check("Pointer_Diagnostics", f =>
        {
            var p = f.CanvasPoint(30, 300); f.Down(p);
            for (var i = 0; i < 20; i++) f.Move(p + new Vector(30 + i, 40));
            f.Up(p + new Vector(80, 40));
            var events = f.Window.DesignerPointerDiagnostics;
            foreach (var code in new[] { "DESIGNER_POINTER_PRESSED", "DESIGNER_POINTER_RELEASED", "DESIGNER_INTERACTION_STARTED", "DESIGNER_INTERACTION_COMPLETED", "MARQUEE_STARTED", "MARQUEE_COMPLETED" })
                if (events.Count(e => e.Event == code) != 1) throw new Exception("Missing or repeated structured event: " + code);
            if (events.Count > 8 || events.Any(e => string.IsNullOrEmpty(e.DocumentId) || string.IsNullOrEmpty(e.HitTestTarget) || string.IsNullOrEmpty(e.PointerPosition))) throw new Exception("Diagnostics lost context or logged every move.");
            f.Down(p); f.Move(p + new Vector(40, 40)); f.Pointer.Capture(null);
            if (!events.Any(e => e.Event == "DESIGNER_POINTER_CAPTURE_LOST") || !events.Any(e => e.Event == "MARQUEE_CANCELLED")) throw new Exception("Capture loss did not report cancellation.");
        });
        Console.WriteLine($"Designer pointer checks: {total - failed}/{total}");
        return failed == 0 ? 0 : 1;
    }

    private sealed class PointerFixture : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly IInputDevice _mouse;
        private readonly Action<RawInputEventArgs> _input;
        private ulong _timestamp = 1000;
        public MainWindowViewModel Vm { get; } = CreateViewModel("DesignerPointer");
        public MainWindow Window { get; }
        public DesignerSurface Surface { get; }
        public Avalonia.Input.Pointer Pointer { get; } = new(42, PointerType.Mouse, true);
        public DesignControlModel First => Vm.Controls.First();
        public PointerFixture()
        {
            Vm.DesignWidth = 900; Vm.DesignHeight = 600;
            Vm.Controls.Add(Control(DesignerControlTypes.Button, "PointerButton", 180, 130, 160, 40, text: "Button"));
            Vm.Controls.Add(Control(DesignerControlTypes.TextBox, "PointerInput", 420, 130, 160, 40));
            Window = CreateSmokeWindow(Vm); Window.Width = 1600; Window.Height = 1000;
            Window.ShowInTaskbar = false; Window.ShowActivated = false;
            Window.Position = new PixelPoint(-3000, -3000); Window.Show();
            NativeItemsUiWait(300); Vm.CloseStartScreenCommand.Execute(null); Refresh();
            Surface = Window.GetVisualDescendants().OfType<DesignerSurface>().Single();
            _mouse = (IInputDevice)Activator.CreateInstance(typeof(MouseDevice), Flags, null, new object[] { Pointer }, null)!;
            _input = (Action<RawInputEventArgs>)typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("Input")!.GetValue(Window.PlatformImpl)!;
        }
        public void Refresh()
        {
            typeof(MainWindow).GetMethod("RenderDesigner", Flags)!.Invoke(Window, null);
            NativeItemsUiWait(180); // Drain debounced selection/Inspector renders before committing the native scene.
            Window.UpdateLayout();
            Window.InvalidateVisual();
            var commit = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(Window)!.Compositor.RequestCommitAsync();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!commit.IsCompleted && DateTime.UtcNow < deadline) NativeItemsUiWait(20);
            if (!commit.IsCompletedSuccessfully) throw new Exception("Native composition frame did not commit.");
            NativeItemsUiWait(50);
        }
        public Point CanvasPoint(double x, double y) => Surface.Canvas.TranslatePoint(new Point(x, y), Window)!.Value;
        public Point ControlPoint(DesignControlModel model)
        {
            Window.UpdateLayout();
            var wrappers = (Dictionary<string, Border>)typeof(MainWindow).GetField("_wrapperByControlId", Flags)!.GetValue(Window)!;
            var point = wrappers[model.Id].TranslatePoint(new Point(35, 18), Window)!.Value;
            var target = Window.InputHitTest(point) as Visual;
            if (target is null || !target.GetVisualAncestors().Prepend(target).OfType<Control>().Any(c => c.Tag is DesignControlModel m && m.Id == model.Id))
                throw new Exception($"Native hit test missed component {model.Id} at {point}: {target?.GetType().Name}:{(target as Control)?.Name}.");
            return point;
        }
        public Point ResizePoint()
        {
            var wrappers = (Dictionary<string, Border>)typeof(MainWindow).GetField("_wrapperByControlId", Flags)!.GetValue(Window)!;
            var handle = wrappers[First.Id].GetVisualDescendants().OfType<Border>().Single(b => b.Width == 18 && b.Height == 18 && b.Tag is DesignControlModel && b.IsVisible);
            var p = handle.TranslatePoint(new Point(3, 3), Window)!.Value;
            if (!ReferenceEquals(Window.InputHitTest(p), handle)) throw new Exception("Native hit test missed the resize handle.");
            return p;
        }
        public void DropToolbox(Point canvasPoint)
        {
            foreach (var routed in new[] { DragDrop.DragOverEvent, DragDrop.DropEvent })
            {
                var e = ToolboxEvent(routed, canvasPoint);
                if (routed == DragDrop.DropEvent && !e.Handled) throw new Exception("Canvas did not handle Toolbox Drop.");
            }
        }
        public DragEventArgs ToolboxEvent(RoutedEvent<DragEventArgs> routed, Point canvasPoint)
        {
            var data = new DataObject(); data.Set("formdesigner-control-type", DesignerControlTypes.Button); data.Set("formdesigner-source-document", Vm.ActiveDocumentId);
            var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), Flags, null, new object[] { routed, data, Surface.Canvas, canvasPoint, KeyModifiers.None }, null)!;
            Surface.Canvas.RaiseEvent(e);
            return e;
        }
        public void Escape() => Surface.Canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        public void ExpectMode(DesignerInteractionMode mode) { if (Window.CurrentDesignerInteractionMode != mode) throw new Exception("Unexpected interaction mode: " + Window.CurrentDesignerInteractionMode + "; expected " + mode); }
        public void AssertIdle()
        {
            NoMarquee(); ExpectMode(DesignerInteractionMode.None);
            var undoDepth = (int)typeof(MainWindowViewModel).GetField("_undoBatchDepth", Flags)!.GetValue(Vm)!;
            if (Pointer.Captured is not null || Vm.IsInspectorInteractionActive || undoDepth != 0) throw new Exception($"Interaction retained state: capture={Pointer.Captured}; undoDepth={undoDepth}; Inspector={Vm.IsInspectorInteractionActive}.");
        }
        public void AssertEmpty(Point point) { if (!ReferenceEquals(Window.InputHitTest(point), Surface.Canvas)) throw new Exception("Test point did not hit the empty Designer Canvas."); }
        public void Send(RawPointerEventType type, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            _timestamp += 50;
            var raw = (RawInputEventArgs)Activator.CreateInstance(typeof(RawPointerEventArgs), Flags, null, new object[] { _mouse, _timestamp, Window, type, point, modifiers }, null)!;
            _input(raw); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
        }
        public void Down(Point p, RawInputModifiers modifiers = RawInputModifiers.None) => Send(RawPointerEventType.LeftButtonDown, p, modifiers | RawInputModifiers.LeftMouseButton);
        public void Move(Point p, RawInputModifiers modifiers = RawInputModifiers.None) => Send(RawPointerEventType.Move, p, modifiers | RawInputModifiers.LeftMouseButton);
        public void Up(Point p, RawInputModifiers modifiers = RawInputModifiers.None) => Send(RawPointerEventType.LeftButtonUp, p, modifiers);
        public void Click(Point p) { Down(p); Up(p); }
        public int MarqueeCount => Surface.GuideOverlay.Children.OfType<Rectangle>().Count(r => r.Fill is SolidColorBrush { Color.A: 36 } && r.StrokeDashArray?.Count > 0);
        public void NoMarquee()
        {
            if ((bool)typeof(MainWindow).GetField("_isMarqueeSelecting", Flags)!.GetValue(Window)! || MarqueeCount != 0) throw new Exception("Marquee is armed or visible after a non-Marquee operation. " + System.Text.Json.JsonSerializer.Serialize(Window.DesignerPointerDiagnostics.TakeLast(6)));
        }
        public void Dispose() { Pointer.Capture(null); Window.CloseForExternalHost(); Pointer.Dispose(); }
    }
}
