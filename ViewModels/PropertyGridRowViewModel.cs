using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FormDesigner.ViewModels;

public enum PropertyGridEditorKind
{
    Text,
    Number,
    Bool,
    Enum,
    Color,
    BindingSource,
    Action,
    ReadOnly
}

public sealed class PropertyGridRowViewModel : ObservableObject
{
    private readonly Action<PropertyGridRowViewModel, string>? _applyValue;
    private readonly Action<PropertyGridRowViewModel, bool>? _applyBool;
    private string _value;
    private bool _boolValue;
    private PropertyGridOptionViewModel? _selectedOption;
    private bool _isFavorite;
    private bool _isApplying;
    private string _validationMessage = "";

    public PropertyGridRowViewModel(
        string key,
        string label,
        string category,
        PropertyGridEditorKind editor,
        string value,
        string description,
        Action<PropertyGridRowViewModel, string>? applyValue = null,
        Action<PropertyGridRowViewModel, bool>? applyBool = null,
        bool boolValue = false,
        bool isFavorite = false,
        bool isAdvanced = false,
        bool isReadOnly = false,
        string actionText = "Edit...")
    {
        Key = key;
        Label = label;
        Category = category;
        Editor = editor;
        _value = value ?? string.Empty;
        Description = description ?? string.Empty;
        _applyValue = applyValue;
        _applyBool = applyBool;
        _boolValue = boolValue;
        _isFavorite = isFavorite;
        IsAdvanced = isAdvanced;
        IsReadOnly = isReadOnly;
        ActionText = actionText;
    }

    public string Key { get; }

    public string Label { get; }

    public string Category { get; }

    public PropertyGridEditorKind Editor { get; }

    public string Description { get; }

    public string ActionText { get; }

    public bool IsAdvanced { get; }

    public bool IsReadOnly { get; }

    public ObservableCollection<PropertyGridOptionViewModel> Options { get; } = new();

    public bool IsTextEditor => Editor == PropertyGridEditorKind.Text;

    public bool IsNumberEditor => Editor == PropertyGridEditorKind.Number;

    public bool IsBoolEditor => Editor == PropertyGridEditorKind.Bool;

    public bool IsEnumEditor => Editor == PropertyGridEditorKind.Enum;

    public bool IsColorEditor => Editor == PropertyGridEditorKind.Color;

    public bool IsBindingSourceEditor => Editor == PropertyGridEditorKind.BindingSource;

    public bool IsActionEditor => Editor == PropertyGridEditorKind.Action;

    public bool IsReadOnlyEditor => Editor == PropertyGridEditorKind.ReadOnly;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationMessage);

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? string.Empty);
    }

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (!SetProperty(ref _boolValue, value) || _isApplying)
                return;

            _applyBool?.Invoke(this, value);
        }
    }

    public PropertyGridOptionViewModel? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (!SetProperty(ref _selectedOption, value) || _isApplying || value is null)
                return;

            Value = value.Value;
            _applyValue?.Invoke(this, value.Value);
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (!SetProperty(ref _isFavorite, value))
                return;

            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        set
        {
            if (!SetProperty(ref _validationMessage, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public void SetOptions(params PropertyGridOptionViewModel[] options)
    {
        Options.Clear();
        foreach (var option in options)
            Options.Add(option);

        RefreshSelectedOption();
    }

    public void SetOptions(System.Collections.Generic.IEnumerable<PropertyGridOptionViewModel> options)
    {
        Options.Clear();
        foreach (var option in options)
            Options.Add(option);

        RefreshSelectedOption();
    }

    public void CommitValue()
    {
        if (IsReadOnly)
            return;

        _applyValue?.Invoke(this, Value);
    }

    public void Refresh(string value, bool boolValue, bool isFavorite)
    {
        _isApplying = true;
        Value = value ?? string.Empty;
        BoolValue = boolValue;
        IsFavorite = isFavorite;
        RefreshSelectedOption();
        _isApplying = false;
    }

    private void RefreshSelectedOption()
    {
        _isApplying = true;
        SelectedOption = Options.FirstOrDefault(option => string.Equals(option.Value, Value, StringComparison.OrdinalIgnoreCase));
        _isApplying = false;
    }
}
