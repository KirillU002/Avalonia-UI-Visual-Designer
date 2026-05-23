namespace FormDesigner.EditorCommands;

public sealed class EditorCommandState
{
    public bool IsVisible { get; init; } = true;

    public bool CanExecute { get; init; } = true;

    public bool IsChecked { get; init; }

    public string DisabledReason { get; init; } = "";

    public static EditorCommandState Enabled { get; } = new();

    public static EditorCommandState Disabled(string reason)
    {
        return new EditorCommandState
        {
            CanExecute = false,
            DisabledReason = reason
        };
    }
}
