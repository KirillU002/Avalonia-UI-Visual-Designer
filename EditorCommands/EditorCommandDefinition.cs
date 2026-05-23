using System;

namespace FormDesigner.EditorCommands;

public sealed class EditorCommandDefinition
{
    public required EditorCommandId Id { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = "";

    public string Icon { get; init; } = "";

    public string Shortcut { get; init; } = "";

    public EditorCommandCategory Category { get; init; }

    public bool IsDangerous { get; init; }

    public required Action Execute { get; init; }

    public Func<EditorCommandState>? GetState { get; init; }
}
