namespace FormDesigner.Models;

public class UndoRedoHistoryItemModel
{
    public int Index { get; init; }

    public string Snapshot { get; init; } = "";

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string PositionText { get; init; } = "";

    public string StateText { get; init; } = "";

    public bool IsCurrent { get; init; }

    public bool IsPast { get; init; }

    public bool IsFuture { get; init; }

    public bool CanNavigate => !IsCurrent;
}
