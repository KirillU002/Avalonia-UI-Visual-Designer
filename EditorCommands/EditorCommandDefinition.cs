using System;

namespace FormDesigner.EditorCommands;

public sealed class EditorCommandDefinition
{
    public EditorCommandId Id { get; init; }

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string Icon { get; init; } = "";

    public string Shortcut { get; init; } = "";

    public EditorCommandCategory Category { get; init; }

    public bool IsDangerous { get; init; }

    public Action Execute { get; init; } = static () => { };

    public Func<EditorCommandState>? GetState { get; init; }
}

