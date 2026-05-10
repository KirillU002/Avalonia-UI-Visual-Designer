using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FormDesigner.Models;

public partial class InteractionModel : ObservableObject
{
    public const string EventButtonClick = "Button.Click";
    public const string EventTextBoxTextChanged = "TextBox.TextChanged";
    public const string EventCheckBoxChecked = "CheckBox.Checked";
    public const string EventCheckBoxUnchecked = "CheckBox.Unchecked";
    public const string EventDataGridSelectionChanged = "DataGrid.SelectionChanged";

    // Legacy value kept so older documents continue to load.
    public const string EventSelectionChanged = "SelectionChanged";

    public const string ActionSetProperty = "SetProperty";
    public const string ActionClearProperty = "ClearProperty";
    public const string ActionToggleVisibility = "ToggleVisibility";
    public const string ActionEnableDisable = "EnableDisable";
    public const string ActionShowMessage = "ShowMessage";

    public const string TargetPropertyText = "Text";
    public const string TargetPropertyContent = "Content";
    public const string TargetPropertyIsChecked = "IsChecked";
    public const string TargetPropertyIsVisible = "IsVisible";
    public const string TargetPropertyIsEnabled = "IsEnabled";

    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string sourceControlName = "";

    [ObservableProperty]
    private string eventName = EventDataGridSelectionChanged;

    [ObservableProperty]
    private string actionType = ActionSetProperty;

    [ObservableProperty]
    private string targetControlName = "";

    [ObservableProperty]
    private string targetProperty = TargetPropertyText;

    [ObservableProperty]
    private string sourcePath = "";

    [ObservableProperty]
    private string textTemplate = "";

    public string NormalizedEventName => NormalizeEventName(EventName);

    public string Summary
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(SourceControlName) ? "Source" : SourceControlName;
            var target = string.IsNullOrWhiteSpace(TargetControlName) ? "Target" : TargetControlName;
            var property = string.IsNullOrWhiteSpace(TargetProperty) ? TargetPropertyText : TargetProperty;
            var value = string.IsNullOrWhiteSpace(TextTemplate)
                ? string.IsNullOrWhiteSpace(SourcePath) ? "event value" : SourcePath
                : TextTemplate;

            var eventName = GetEventDisplayName(NormalizedEventName);
            var actionName = GetActionDisplayName(ActionType);
            var propertyName = GetTargetPropertyDisplayName(property);

            return ActionType switch
            {
                ActionClearProperty => $"{source}: {eventName} -> очистить {target}.{propertyName}",
                ActionToggleVisibility => $"{source}: {eventName} -> переключить видимость {target}",
                ActionEnableDisable => $"{source}: {eventName} -> доступность {target} = {value}",
                ActionShowMessage => $"{source}: {eventName} -> сообщение: {value}",
                _ => $"{source}: {eventName} -> {target}.{propertyName} = {value}"
            };
        }
    }

    public string EventDisplayName => GetEventDisplayName(NormalizedEventName);

    public string ActionDisplayName => GetActionDisplayName(ActionType);

    public string TargetPropertyDisplayName => GetTargetPropertyDisplayName(TargetProperty);

    public InteractionModel Clone()
    {
        return new InteractionModel
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceControlName = SourceControlName,
            EventName = EventName,
            ActionType = ActionType,
            TargetControlName = TargetControlName,
            TargetProperty = TargetProperty,
            SourcePath = SourcePath,
            TextTemplate = TextTemplate
        };
    }

    public static string NormalizeEventName(string? eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return EventDataGridSelectionChanged;

        var trimmed = eventName.Trim();
        return trimmed.Equals(EventSelectionChanged, StringComparison.OrdinalIgnoreCase)
            ? EventDataGridSelectionChanged
            : trimmed;
    }

    public static string GetEventDisplayName(string? eventName)
    {
        return NormalizeEventName(eventName) switch
        {
            EventButtonClick => "Кнопка: клик",
            EventTextBoxTextChanged => "Текстовое поле: текст изменён",
            EventCheckBoxChecked => "Флажок: включён",
            EventCheckBoxUnchecked => "Флажок: выключен",
            EventDataGridSelectionChanged => "Таблица: выбрана строка",
            _ => string.IsNullOrWhiteSpace(eventName) ? "Событие" : eventName!
        };
    }

    public static string GetActionDisplayName(string? actionType)
    {
        return actionType switch
        {
            ActionSetProperty => "Записать значение",
            ActionClearProperty => "Очистить значение",
            ActionToggleVisibility => "Показать / скрыть",
            ActionEnableDisable => "Включить / отключить",
            ActionShowMessage => "Показать сообщение",
            _ => string.IsNullOrWhiteSpace(actionType) ? "Действие" : actionType!
        };
    }

    public static string GetTargetPropertyDisplayName(string? targetProperty)
    {
        return targetProperty switch
        {
            TargetPropertyText => "Текст",
            TargetPropertyContent => "Содержимое",
            TargetPropertyIsChecked => "Отмечено",
            TargetPropertyIsVisible => "Видимость",
            TargetPropertyIsEnabled => "Доступность",
            _ => string.IsNullOrWhiteSpace(targetProperty) ? "Свойство" : targetProperty!
        };
    }

    public override string ToString() => Summary;

    partial void OnSourceControlNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnEventNameChanged(string value)
    {
        OnPropertyChanged(nameof(NormalizedEventName));
        OnPropertyChanged(nameof(EventDisplayName));
        OnPropertyChanged(nameof(Summary));
    }
    partial void OnActionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(Summary));
    }
    partial void OnTargetControlNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnTargetPropertyChanged(string value)
    {
        OnPropertyChanged(nameof(TargetPropertyDisplayName));
        OnPropertyChanged(nameof(Summary));
    }
    partial void OnSourcePathChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnTextTemplateChanged(string value) => OnPropertyChanged(nameof(Summary));
}
