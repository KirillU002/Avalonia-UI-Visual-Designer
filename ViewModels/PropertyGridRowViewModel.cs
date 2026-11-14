using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace FormDesigner.ViewModels;

public enum PropertyGridEditorKind
{
    Text,
    Number,
    Bool,
    Enum,
    Color,
    Thickness,
    Size,
    BindingSource,
    ColumnCollection,
    Interaction,
    Asset,
    Action,
    ReadOnly,
    CornerRadius,
    GridLength,
    Font,
    GridDefinitions
}

public enum PropertyGridValidationState
{
    None,
    Warning,
    Error
}

public sealed class PropertyGridRowViewModel : ObservableObject
{
    public static Action<PropertyGridRowViewModel, string, string, string>? ValueTrace;

    private readonly Action<PropertyGridRowViewModel, string>? _applyValue;
    private readonly Action<PropertyGridRowViewModel, bool>? _applyBool;
    private string _value;
    private bool _boolValue;
    private PropertyGridOptionViewModel? _selectedOption;
    private bool _isFavorite;
    private bool _isApplying;
    private string _validationMessage = "";
    private PropertyGridValidationState _validationState;

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
        string actionText = "Edit...",
        string defaultValue = "",
        string aliases = "",
        string contextDocumentId = "",
        string contextControlId = "",
        string contextControlName = "",
        string contextControlType = "")
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
        DefaultValue = defaultValue ?? string.Empty;
        Aliases = aliases ?? string.Empty;
        ContextDocumentId = contextDocumentId ?? string.Empty;
        ContextControlId = contextControlId ?? string.Empty;
        ContextControlName = contextControlName ?? string.Empty;
        ContextControlType = contextControlType ?? string.Empty;
        RefreshModifiedState();
    }

    public string Key { get; }

    public string Label { get; }

    public string Category { get; }

    public PropertyGridEditorKind Editor { get; private set; }
    public bool UsesMetadata { get; internal set; }
    internal Func<string, string, bool>? LiteralEquals { get; set; }
    public PropertyGridPropertyMetadata? PropertyMetadata { get; private set; }
    public PropertyGridValueSource ValueSource { get; private set; } = PropertyGridValueSource.Model;
    public bool? HasLocalValue { get; private set; }
    public string EffectiveValue { get; private set; } = "";
    public string SourceDetail { get; private set; } = "";
    public string CurrentValue => Value;
    internal void UseEditor(PropertyGridEditorKind kind) { if (!IsReadOnly) { Editor = kind; OnPropertyChanged(nameof(Editor)); } }
    internal Action? RemoveLocalValue { get; private set; }
    internal bool ResetBlocked { get; private set; }
    public bool CanSetNull => UsesMetadata && PropertyMetadata?.IsNullable == true && !IsReadOnly;
    public string ValueSourceLabel => ValueSource switch
    {
        PropertyGridValueSource.LocalValue => "Локальное значение", PropertyGridValueSource.Style => "Style",
        PropertyGridValueSource.Binding => "Binding (сохраняется)", PropertyGridValueSource.Resource => "Resource (сохраняется)",
        PropertyGridValueSource.Inherited => "Унаследованное значение", PropertyGridValueSource.Complex => "Сложное значение (read-only)",
        PropertyGridValueSource.RegistrationDefault => "Metadata default", PropertyGridValueSource.Unknown => "Источник не определён",
        _ => "Значение модели (legacy adapter)"
    };
    public string PresentationDescription => Description + "\nИсточник: " + ValueSourceLabel +
        (SourceDetail.Length == 0 ? "" : "\n" + SourceDetail) + (EffectiveValue.Length == 0 ? "" : "\nNative effective: " + EffectiveValue);

    internal void SetPropertyMetadata(PropertyGridPropertyMetadata metadata, PropertyGridValueSource source, bool? local,
        string detail = "", Action? removeLocal = null, bool blockReset = false)
    {
        PropertyMetadata = metadata; ValueSource = source; HasLocalValue = local; SourceDetail = detail;
        RemoveLocalValue = removeLocal; ResetBlocked = blockReset;
        OnPropertyChanged(nameof(PropertyMetadata)); OnPropertyChanged(nameof(CanSetNull)); NotifySource();
    }
    internal void SetNativeValueSource(PropertyGridValueSource source, string effective)
    {
        ValueSource = source; EffectiveValue = effective; NotifySource();
    }
    internal void ProtectValue(string detail)
    {
        IsReadOnly = true; Editor = PropertyGridEditorKind.ReadOnly; SourceDetail = detail;
        if (PropertyMetadata is { } metadata) PropertyMetadata = metadata with { Editable = false };
        OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(Editor)); OnPropertyChanged(nameof(CanSetNull)); NotifySource();
    }
    private void NotifySource()
    {
        OnPropertyChanged(nameof(ValueSource)); OnPropertyChanged(nameof(ValueSourceLabel));
        OnPropertyChanged(nameof(PresentationDescription)); OnPropertyChanged(nameof(EffectiveValue));
        OnPropertyChanged(nameof(HasLocalValue)); RefreshModifiedState();
    }

    public string Description { get; }

    public string ActionText { get; }

    public string DefaultValue { get; }

    public string Aliases { get; }

    public string ContextDocumentId { get; }

    public string ContextControlId { get; }

    public string ContextControlName { get; }

    public string ContextControlType { get; }
    public long ContextRevision { get; internal set; }

    public bool IsAdvanced { get; }

    public bool IsReadOnly { get; private set; }

    public ObservableCollection<PropertyGridOptionViewModel> Options { get; } = new();

    public bool IsTextEditor => Editor is PropertyGridEditorKind.Text or PropertyGridEditorKind.Thickness
        or PropertyGridEditorKind.CornerRadius or PropertyGridEditorKind.GridLength or PropertyGridEditorKind.Font;

    public bool IsNumberEditor => Editor == PropertyGridEditorKind.Number;

    public bool IsBoolEditor => Editor == PropertyGridEditorKind.Bool;

    public bool IsEnumEditor => Editor == PropertyGridEditorKind.Enum;

    public bool IsColorEditor => Editor == PropertyGridEditorKind.Color;

    public bool IsThicknessEditor => Editor == PropertyGridEditorKind.Thickness;

    public bool IsSizeEditor => Editor == PropertyGridEditorKind.Size;

    public bool IsBindingSourceEditor => Editor == PropertyGridEditorKind.BindingSource;

    public bool IsColumnCollectionEditor => Editor == PropertyGridEditorKind.ColumnCollection;

    public bool IsInteractionEditor => Editor == PropertyGridEditorKind.Interaction;

    public bool IsAssetEditor => Editor == PropertyGridEditorKind.Asset;

    public bool IsActionEditor => Editor == PropertyGridEditorKind.Action
        || Editor == PropertyGridEditorKind.ColumnCollection
        || Editor == PropertyGridEditorKind.Interaction
        || Editor == PropertyGridEditorKind.Asset;

    public bool IsReadOnlyEditor => Editor == PropertyGridEditorKind.ReadOnly;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";

    public bool HasDefaultValue => PropertyMetadata?.Native.HasDefaultValue ?? !string.IsNullOrWhiteSpace(DefaultValue);

    public bool IsModified => HasLocalValue ?? (HasDefaultValue && !ValueEqualsDefault());

    public bool CanResetToDefault => !ResetBlocked && !IsReadOnly && (RemoveLocalValue is not null ? HasLocalValue == true : HasDefaultValue && IsModified);

    public string ModifiedGlyph => ValueSource switch
    {
        PropertyGridValueSource.Binding => "ƒ ", PropertyGridValueSource.Resource => "R ",
        PropertyGridValueSource.Style when HasLocalValue != true => "S ", PropertyGridValueSource.Inherited => "↳ ",
        _ => IsModified ? "\u25CF " : ""
    };

    public string DefaultTooltip => RemoveLocalValue is not null ? "Удалить локальное значение; восстановить Style / inheritance / metadata default"
        : ResetBlocked ? "Unset не поддерживается этим source adapter" : HasDefaultValue ? $"Восстановить значение модели: {DefaultValue}" : "";

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool HasValidationWarning => ValidationState == PropertyGridValidationState.Warning;

    public bool HasValidationIssue => ValidationState != PropertyGridValidationState.None;

    public string Value
    {
        get => _value;
        set
        {
            var oldValue = _value;
            var newValue = value ?? string.Empty;
            if (!SetProperty(ref _value, newValue))
                return;

            ValueTrace?.Invoke(this, oldValue, newValue, new StackTrace(skipFrames: 1, fNeedFileInfo: false).ToString());
            RefreshModifiedState();
            OnPropertyChanged(nameof(CurrentValue));
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (!SetProperty(ref _boolValue, value) || _isApplying)
                return;

            Value = value ? "True" : "False";
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

            if (string.IsNullOrWhiteSpace(_validationMessage))
                ValidationState = PropertyGridValidationState.None;
            else if (ValidationState == PropertyGridValidationState.None)
                ValidationState = PropertyGridValidationState.Error;

            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public PropertyGridValidationState ValidationState
    {
        get => _validationState;
        set
        {
            if (!SetProperty(ref _validationState, value))
                return;

            OnPropertyChanged(nameof(HasValidationWarning));
            OnPropertyChanged(nameof(HasValidationIssue));
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
        RefreshModifiedState();
    }

    public void ResetToDefault()
    {
        if (!CanResetToDefault)
            return;

        if (RemoveLocalValue is { } remove) { remove(); return; }

        if (IsBoolEditor && bool.TryParse(DefaultValue, out var boolValue))
        {
            _isApplying = true;
            BoolValue = boolValue;
            _isApplying = false;
            _applyBool?.Invoke(this, boolValue);
            Value = boolValue ? "True" : "False";
        }
        else
        {
            Value = DefaultValue;
            _applyValue?.Invoke(this, DefaultValue);
            RefreshSelectedOption();
        }

        RefreshModifiedState();
    }

    public void Refresh(string value, bool boolValue, bool isFavorite)
    {
        _isApplying = true;
        Value = value ?? string.Empty;
        BoolValue = boolValue;
        IsFavorite = isFavorite;
        RefreshSelectedOption();
        RefreshModifiedState();
        _isApplying = false;
    }

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(Label, query)
            || Contains(Key, query)
            || Contains(Category, query)
            || Contains(Description, query)
            || Contains(Editor.ToString(), query)
            || Contains(ActionText, query)
            || Contains(Aliases, query);
    }

    private static bool Contains(string? value, string query)
    {
        return value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool ValueEqualsDefault()
    {
        if (!HasDefaultValue)
            return true;
        if (LiteralEquals is not null) return LiteralEquals(Value, DefaultValue);

        if (IsNumberEditor
            && double.TryParse(Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var valueNumber)
            && double.TryParse(DefaultValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var defaultNumber))
        {
            return valueNumber.Equals(defaultNumber) || Math.Abs(valueNumber - defaultNumber) < 0.0001;
        }

        return string.Equals(Value, DefaultValue, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshModifiedState()
    {
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(CanResetToDefault));
        OnPropertyChanged(nameof(ModifiedGlyph));
        OnPropertyChanged(nameof(DefaultTooltip));
    }

    private void RefreshSelectedOption()
    {
        _isApplying = true;
        SelectedOption = Options.FirstOrDefault(option => string.Equals(option.Value, Value, StringComparison.OrdinalIgnoreCase));
        _isApplying = false;
    }
}
