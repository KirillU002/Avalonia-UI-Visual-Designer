namespace FormDesigner.Models;

public enum DesignerInteractionMode
{
    None, Selecting, MarqueeSelecting, MovingControl, ResizingControl, ResizingSurface, ToolboxDragDrop,
    SelectingGridRow, SelectingGridColumn, ResizingGridRow, ResizingGridColumn
}

public sealed record DesignerPointerDiagnostic(string Event, string ControlId, string PointerPosition,
    string HitTestTarget, string InteractionMode, bool IsLeftButtonPressed, bool PointerCaptured,
    string DocumentId, string Reason = "");
