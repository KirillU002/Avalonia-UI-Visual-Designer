using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FormDesigner.EditorCommands;

public sealed class EditorCommandService
{
    private readonly Dictionary<EditorCommandId, EditorCommand> _commands = new();

    public ObservableCollection<EditorCommand> Commands { get; } = new();

    public EditorCommand Register(EditorCommandDefinition definition)
    {
        var command = new EditorCommand(definition);
        _commands[command.Id] = command;
        Commands.Add(command);
        return command;
    }

    public EditorCommand? Find(EditorCommandId id)
    {
        return _commands.TryGetValue(id, out var command) ? command : null;
    }

    public bool CanExecute(EditorCommandId id)
    {
        return Find(id)?.CanExecute(null) == true;
    }

    public bool TryExecute(EditorCommandId id)
    {
        var command = Find(id);
        if (command?.CanExecute(null) != true)
            return false;

        command.Execute(null);
        return true;
    }

    public void Refresh()
    {
        foreach (var command in Commands)
            command.RefreshState();
    }

    public IEnumerable<EditorCommand> Search(string? query, bool includeDisabled = true)
    {
        var normalizedQuery = query?.Trim();
        return Commands
            .Where(command => command.IsVisible)
            .Where(command => includeDisabled || command.IsEnabled)
            .Where(command => string.IsNullOrWhiteSpace(normalizedQuery)
                || command.SearchText.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .OrderBy(command => command.Category)
            .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase);
    }
}
