using Avalonia;

namespace FormDesigner.Models;

// Transient interaction data. Never part of the project file or exported tree.
public sealed record GridDropTarget(string SessionId, string ParentId, int Row, int Column,
    int RowSpan, int ColumnSpan, Rect GridBounds, Rect CellBounds, string GeometryKey,
    bool IsValid, string Reason, Rect? PreviewBounds = null, bool HasOverlap = false,
    string LayoutMode = DesignerLayoutModes.Grid, int InsertionIndex = -1, string DockSide = "Left",
    Point CanvasPosition = default, Rect? InsertionIndicator = null, Point GrabOffset = default, bool BypassSnap = false,
    bool UsesContainerHitTest = false, Point CanvasHostShift = default, Rect? PreviewClip = null);

public sealed record GridDragDiagnostic(string Event, string DocumentId, string ControlId,
    string OldParentId, string NewParentId, int OldRow, int OldColumn, int NewRow, int NewColumn,
    double Zoom, double Dpi, string Reason);
