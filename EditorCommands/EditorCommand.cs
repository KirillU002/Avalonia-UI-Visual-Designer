using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows.Input;

namespace FormDesigner.EditorCommands;

public sealed class EditorCommand : ObservableObject, ICommand
{
    private readonly Action _execute;
    private readonly Func<EditorCommandState>? _getState;
    private bool _isVisible = true;
    private bool _canExecute = true;
    private bool _isChecked;
    private string _disabledReason = "";

    public EditorCommand(EditorCommandDefinition definition)
    {
        Id = definition.Id;
        Title = definition.Title;
        Description = definition.Description;
        Icon = definition.Icon;
        Shortcut = definition.Shortcut;
        Category = definition.Category;
        IsDangerous = definition.IsDangerous;
        _execute = definition.Execute;
        _getState = definition.GetState;
        RefreshState();
    }

    public event EventHandler? CanExecuteChanged;

    public EditorCommandId Id { get; }

    public string Title { get; }

    public string Description { get; }

    public string Icon { get; }

    public string Shortcut { get; }

    public EditorCommandCategory Category { get; }

    public string CategoryText => Category.ToString();

    public bool IsDangerous { get; }

    public string SearchText => $"{Title} {Description} {Shortcut} {Category}";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Icon) ? Title : $"{Icon} {Title}";

    public string Hint => string.IsNullOrWhiteSpace(Shortcut) ? Description : $"{Description} ({Shortcut})";

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool IsEnabled
    {
        get => _canExecute;
        private set
        {
            if (!SetProperty(ref _canExecute, value))
                return;

            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        private set => SetProperty(ref _isChecked, value);
    }

    public string DisabledReason
    {
        get => _disabledReason;
        private set => SetProperty(ref _disabledReason, value ?? "");
    }

    public bool CanExecute(object? parameter)
    {
        return IsVisible && IsEnabled;
    }

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _execute();
    }

    public void RefreshState()
    {
        var state = _getState?.Invoke() ?? EditorCommandState.Enabled;
        IsVisible = state.IsVisible;
        IsEnabled = state.CanExecute;
        IsChecked = state.IsChecked;
        DisabledReason = state.DisabledReason;
        OnPropertyChanged(nameof(Hint));
    }
}
