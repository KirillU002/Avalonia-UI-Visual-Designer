using Avalonia.Media;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Services;

public sealed class DocumentDiagnosticsService
{
    private readonly IDesignerRegistry _registry;

    public DocumentDiagnosticsService(IDesignerRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<DocumentDiagnosticModel> Validate(
        IEnumerable<DesignControlModel> controls,
        IEnumerable<BindingSourceModel> bindingSources,
        IEnumerable<InteractionModel> interactions,
        string? currentDocumentPath,
        double designWidth,
        double designHeight)
    {
        var controlList = controls.ToList();
        var sourceList = bindingSources.ToList();
        var interactionList = interactions.ToList();
        var diagnostics = new List<DocumentDiagnosticModel>();

        ValidateDuplicateControlNames(controlList, diagnostics);
        ValidateBindingSources(sourceList, diagnostics);
        ValidateControls(controlList, sourceList, currentDocumentPath, designWidth, designHeight, diagnostics);
        ValidateInteractions(controlList, sourceList, interactionList, diagnostics);

        return diagnostics
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Message, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ValidateDuplicateControlNames(IReadOnlyList<DesignControlModel> controls, ICollection<DocumentDiagnosticModel> diagnostics)
    {
        var duplicates = controls
            .Where(control => !string.IsNullOrWhiteSpace(control.Name))
            .GroupBy(control => control.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicates)
        {
            var names = group
                .Select(control => control.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var control in group)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = "Документ",
                    Category = "Имена",
                    Message = $"Имя элемента '{control.Name}' используется несколько раз.",
                    Recommendation = "Задайте каждому элементу уникальное имя, иначе генерация XAML и C# может конфликтовать.",
                    RelatedControlId = control.Id,
                    RelatedControlName = control.Name
                });
            }
        }
    }

    private static void ValidateInteractionsV2(
        IReadOnlyList<DesignControlModel> controls,
        IReadOnlyList<BindingSourceModel> bindingSources,
        IReadOnlyList<InteractionModel> interactions,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        foreach (var interaction in interactions)
        {
            var eventName = InteractionModel.NormalizeEventName(interaction.EventName);
            var source = FindControlByName(controls, interaction.SourceControlName);
            var target = FindControlByName(controls, interaction.TargetControlName);
            var diagnosticSource = string.IsNullOrWhiteSpace(interaction.SourceControlName)
                ? "Логика формы"
                : interaction.SourceControlName;

            if (source is null)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = diagnosticSource,
                    Category = "Логика",
                    Message = $"Interaction ссылается на несуществующий source control '{interaction.SourceControlName}'.",
                    Recommendation = "Выберите существующий source control или удалите устаревшее правило логики."
                });
                continue;
            }

            if (!IsSupportedInteractionSourceEvent(source, eventName))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = $"Событие '{eventName}' не поддерживается для {source.Type}.",
                    Recommendation = "Используйте Button.Click, TextBox.TextChanged, CheckBox.Checked/Unchecked или DataGrid.SelectionChanged.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name
                });
            }

            var isShowMessage = string.Equals(interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase);
            if (!isShowMessage)
            {
                if (target is null)
                {
                    diagnostics.Add(new DocumentDiagnosticModel
                    {
                        Severity = DocumentDiagnosticSeverity.Error,
                        Source = source.NameOrFallback(),
                        Category = "Логика",
                        Message = $"Interaction ссылается на несуществующий target control '{interaction.TargetControlName}'.",
                        Recommendation = "Выберите существующий TextBox, TextBlock, Button или CheckBox.",
                        RelatedControlId = source.Id,
                        RelatedControlName = source.Name
                    });
                    continue;
                }

                if (!IsSupportedInteractionTargetAction(target, interaction))
                {
                    diagnostics.Add(new DocumentDiagnosticModel
                    {
                        Severity = DocumentDiagnosticSeverity.Error,
                        Source = target.NameOrFallback(),
                        Category = "Логика",
                        Message = $"Action '{interaction.ActionType}' или property '{interaction.TargetProperty}' не поддерживается для {target.Type}.",
                        Recommendation = "Для текста используйте SetProperty/ClearProperty, для видимости ToggleVisibility, для доступности EnableDisable.",
                        RelatedControlId = target.Id,
                        RelatedControlName = target.Name
                    });
                }

                ValidateInteractionLoop(interaction, source, target, diagnostics);
            }

            if (IsDataGridSelectionChangedEvent(eventName))
            {
                ValidateDataGridInteractionFields(bindingSources, interaction, source, target, diagnostics);
            }
            else
            {
                ValidateSimpleInteractionSourcePath(interaction, source, diagnostics);
            }
        }
    }

    private static void ValidateInteractionLoop(
        InteractionModel interaction,
        DesignControlModel source,
        DesignControlModel target,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (!string.Equals(source.Id, target.Id, StringComparison.Ordinal))
            return;

        var eventName = InteractionModel.NormalizeEventName(interaction.EventName);
        var targetProperty = string.IsNullOrWhiteSpace(interaction.TargetProperty)
            ? InteractionModel.TargetPropertyText
            : interaction.TargetProperty.Trim();

        var canTriggerItself =
            source.Type == DesignerControlTypes.TextBox
            && string.Equals(eventName, InteractionModel.EventTextBoxTextChanged, StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetProperty, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(interaction.ActionType, InteractionModel.ActionSetProperty, StringComparison.OrdinalIgnoreCase)
                || string.Equals(interaction.ActionType, InteractionModel.ActionClearProperty, StringComparison.OrdinalIgnoreCase));

        if (!canTriggerItself)
            return;

        diagnostics.Add(new DocumentDiagnosticModel
        {
            Severity = DocumentDiagnosticSeverity.Warning,
            Source = source.NameOrFallback(),
            Category = "Логика",
            Message = $"Interaction '{source.NameOrFallback()}' изменяет то же свойство, событие которого запускает правило.",
            Recommendation = "Лучше выберите другой target control или другое target property, чтобы не получить повторный TextChanged-цикл.",
            RelatedControlId = source.Id,
            RelatedControlName = source.Name
        });
    }

    private static void ValidateDataGridInteractionFields(
        IReadOnlyList<BindingSourceModel> bindingSources,
        InteractionModel interaction,
        DesignControlModel source,
        DesignControlModel? target,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        var sourceBinding = FindBindingSource(bindingSources, source.BindingSourceId);
        if (sourceBinding is null)
        {
            if (!string.IsNullOrWhiteSpace(interaction.SourcePath) || !string.IsNullOrWhiteSpace(interaction.TextTemplate))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = "DataGrid interaction не может проверить поля, потому что у DataGrid нет BindingSource.",
                    Recommendation = "Подключите BindingSource к DataGrid или используйте ShowMessage/статические значения без полей.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name
                });
            }

            return;
        }

        if (sourceBinding.Fields.Count == 0)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = string.IsNullOrWhiteSpace(interaction.SourcePath) && !ExtractTemplateTokens(interaction.TextTemplate).Any()
                    ? DocumentDiagnosticSeverity.Warning
                    : DocumentDiagnosticSeverity.Error,
                Source = source.NameOrFallback(),
                Category = "Логика",
                Message = $"DataGrid interaction не может использовать поля: BindingSource '{sourceBinding.NameOrFallback()}' пустой.",
                Recommendation = "Добавьте реальные поля в BindingSource или удалите ссылку на поле/шаблон.",
                RelatedControlId = source.Id,
                RelatedControlName = source.Name,
                RelatedBindingSourceId = sourceBinding.Id,
                RelatedBindingSourceName = sourceBinding.Name
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(interaction.SourcePath) && !BindingFieldExists(sourceBinding, interaction.SourcePath))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Error,
                Source = source.NameOrFallback(),
                Category = "Логика",
                Message = $"Поле interaction '{interaction.SourcePath}' не найдено в BindingSource '{sourceBinding.NameOrFallback()}'.",
                Recommendation = "Выберите поле из списка BindingSource или обновите схему данных.",
                RelatedControlId = source.Id,
                RelatedControlName = source.Name,
                RelatedBindingSourceId = sourceBinding.Id,
                RelatedBindingSourceName = sourceBinding.Name
            });
        }

        foreach (var token in ExtractTemplateTokens(interaction.TextTemplate))
        {
            if (BindingFieldExists(sourceBinding, token))
                continue;

            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Error,
                Source = source.NameOrFallback(),
                Category = "Логика",
                Message = $"В шаблоне interaction поле '{{{token}}}' не найдено в BindingSource '{sourceBinding.NameOrFallback()}'.",
                Recommendation = "Исправьте шаблон текста или добавьте поле в BindingSource.",
                RelatedControlId = source.Id,
                RelatedControlName = source.Name,
                RelatedBindingSourceId = sourceBinding.Id,
                RelatedBindingSourceName = sourceBinding.Name
            });
        }

        if (target?.Type == DesignerControlTypes.CheckBox
            && !string.IsNullOrWhiteSpace(interaction.SourcePath)
            && sourceBinding.Fields.FirstOrDefault(field => FieldPathMatches(field, interaction.SourcePath)) is { } boolField
            && !LooksLikeBoolType(boolField.TypeName))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = target.NameOrFallback(),
                Category = "Логика",
                Message = $"CheckBox.IsChecked получает поле '{interaction.SourcePath}' типа '{boolField.TypeName}'.",
                Recommendation = "Лучше выбирать bool/Boolean/bit поле, иначе runtime попробует преобразовать строку.",
                RelatedControlId = target.Id,
                RelatedControlName = target.Name
            });
        }
    }

    private static void ValidateSimpleInteractionSourcePath(
        InteractionModel interaction,
        DesignControlModel source,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        var allowedPath = source.Type switch
        {
            DesignerControlTypes.Button => InteractionModel.TargetPropertyContent,
            DesignerControlTypes.TextBox => InteractionModel.TargetPropertyText,
            DesignerControlTypes.CheckBox => InteractionModel.TargetPropertyIsChecked,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(interaction.SourcePath)
            || string.Equals(interaction.SourcePath, allowedPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(interaction.SourcePath, "Value", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        diagnostics.Add(new DocumentDiagnosticModel
        {
            Severity = DocumentDiagnosticSeverity.Warning,
            Source = source.NameOrFallback(),
            Category = "Логика",
            Message = $"SourcePath '{interaction.SourcePath}' не является стандартным значением для {source.Type}.",
            Recommendation = $"Используйте '{allowedPath}' или шаблон с '{{{allowedPath}}}'.",
            RelatedControlId = source.Id,
            RelatedControlName = source.Name
        });
    }

    private void ValidateBindingSources(IReadOnlyList<BindingSourceModel> bindingSources, ICollection<DocumentDiagnosticModel> diagnostics)
    {
        foreach (var source in bindingSources)
        {
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = "Источник данных",
                    Category = "Обязательные поля",
                    Message = "У источника данных не задано имя.",
                    Recommendation = "Заполните поле 'Имя', чтобы источник можно было выбрать в DataGrid и в кодогенерации.",
                    RelatedBindingSourceId = source.Id,
                    RelatedBindingSourceName = source.Name
                });
            }

            if (string.IsNullOrWhiteSpace(source.Path))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Привязка",
                    Message = "У источника данных пустой путь привязки.",
                    Recommendation = "Укажите путь коллекции, например 'Items' или имя свойства, которое будет использоваться как ItemsSource.",
                    RelatedBindingSourceId = source.Id,
                    RelatedBindingSourceName = source.Name
                });
            }

            var usesSql = !string.IsNullOrWhiteSpace(source.SourceConnectionString)
                || !string.IsNullOrWhiteSpace(source.SourceTableName)
                || !string.IsNullOrWhiteSpace(source.SourceQuery);

            if (usesSql
                && string.IsNullOrWhiteSpace(source.SourceQuery)
                && string.IsNullOrWhiteSpace(source.SourceTableName))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "SQL",
                    Message = "У SQL-источника не указаны таблица и SQL-запрос.",
                    Recommendation = "Заполните таблицу или задайте SELECT-запрос, иначе дизайнеру нечего подтягивать из БД.",
                    RelatedBindingSourceId = source.Id,
                    RelatedBindingSourceName = source.Name
                });
            }

            if (!string.IsNullOrWhiteSpace(source.SourceTypeFullName)
                && string.IsNullOrWhiteSpace(source.SourceAssemblyPath))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "DLL",
                    Message = "Источник ссылается на тип из DLL, но путь к сборке не сохранён.",
                    Recommendation = "Повторно импортируйте источник из DLL, чтобы дизайнер мог восстановить его метаданные.",
                    RelatedBindingSourceId = source.Id,
                    RelatedBindingSourceName = source.Name
                });
            }

            if (source.Fields.Count == 0)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Колонки",
                    Message = "У источника данных нет описанных полей.",
                    Recommendation = "Добавьте колонки вручную или подтяните схему из DLL/БД.",
                    RelatedBindingSourceId = source.Id,
                    RelatedBindingSourceName = source.Name
                });
                continue;
            }

            var duplicateFieldPaths = source.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Path))
                .GroupBy(field => field.Path.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var field in source.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Path))
                {
                    diagnostics.Add(new DocumentDiagnosticModel
                    {
                        Severity = DocumentDiagnosticSeverity.Error,
                        Source = source.NameOrFallback(),
                        Category = "Колонки",
                        Message = "У одной из колонок источника данных пустой путь.",
                        Recommendation = "Заполните путь свойства, иначе колонка не сможет связаться с данными.",
                        RelatedBindingSourceId = source.Id,
                        RelatedBindingSourceName = source.Name
                    });
                }

                if (string.IsNullOrWhiteSpace(field.Header))
                {
                    diagnostics.Add(new DocumentDiagnosticModel
                    {
                        Severity = DocumentDiagnosticSeverity.Warning,
                        Source = source.NameOrFallback(),
                        Category = "Колонки",
                        Message = $"Колонка '{field.PathOrFallback()}' не имеет заголовка.",
                        Recommendation = "Заполните заголовок, чтобы таблица и TreeList выглядели понятнее.",
                        RelatedBindingSourceId = source.Id,
                        RelatedBindingSourceName = source.Name
                    });
                }

                if (!string.IsNullOrWhiteSpace(field.Width) && !IsValidFieldWidth(field.Width))
                {
                    diagnostics.Add(new DocumentDiagnosticModel
                    {
                        Severity = DocumentDiagnosticSeverity.Warning,
                        Source = source.NameOrFallback(),
                        Category = "Колонки",
                        Message = $"У колонки '{field.HeaderOrFallback()}' задана некорректная ширина '{field.Width}'.",
                        Recommendation = "Используйте число в пикселях, '*', '1*' или '2*'.",
                        RelatedBindingSourceId = source.Id,
                        RelatedBindingSourceName = source.Name
                    });
                }
            }

            foreach (var duplicateGroup in duplicateFieldPaths)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Колонки",
                    Message = $"Путь поля '{duplicateGroup.Key}' повторяется несколько раз.",
                    Recommendation = "Сделайте пути колонок уникальными, иначе сортировка и привязка будут неоднозначными.",
                    RelatedBindingSourceId = source.Id,
                    RelatedBindingSourceName = source.Name
                });
            }
        }
    }

    private void ValidateControls(
        IReadOnlyList<DesignControlModel> controls,
        IReadOnlyList<BindingSourceModel> bindingSources,
        string? currentDocumentPath,
        double designWidth,
        double designHeight,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        foreach (var control in controls)
        {
            var descriptor = _registry.GetRequiredControl(control.Type);
            var relatedSource = FindBindingSource(bindingSources, control.BindingSourceId);

            if (string.IsNullOrWhiteSpace(control.Name))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = control.Type,
                    Category = "Обязательные поля",
                    Message = "У элемента пустое имя.",
                    Recommendation = "Задайте имя, чтобы элемент было проще находить в структуре и использовать в кодогенерации.",
                    RelatedControlId = control.Id,
                    RelatedControlName = control.Name
                });
            }

            if (descriptor is MissingPluginDescriptor)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = control.NameOrFallback(),
                    Category = "Плагин",
                    Message = $"Для контрола '{control.Type}' не найден descriptor. Плагин недоступен.",
                    Recommendation = "Подключите недостающий plugin или замените контрол на доступный.",
                    RelatedControlId = control.Id,
                    RelatedControlName = control.Name
                });
            }

            ValidateBindingUsage(control, descriptor, relatedSource, diagnostics);
            ValidateImagePath(control, currentDocumentPath, diagnostics);
            ValidateCustomProperties(control, descriptor, diagnostics);
            ValidateGeometry(control, designWidth, designHeight, diagnostics);
        }
    }

    private void ValidateInteractions(
        IReadOnlyList<DesignControlModel> controls,
        IReadOnlyList<BindingSourceModel> bindingSources,
        IReadOnlyList<InteractionModel> interactions,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (interactions.Count >= 0)
        {
            ValidateInteractionsV2(controls, bindingSources, interactions, diagnostics);
            return;
        }

        foreach (var interaction in interactions)
        {
            var source = FindControlByName(controls, interaction.SourceControlName);
            var target = FindControlByName(controls, interaction.TargetControlName);
            var diagnosticSource = string.IsNullOrWhiteSpace(interaction.SourceControlName)
                ? "Логика формы"
                : interaction.SourceControlName;

            if (source is null)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = diagnosticSource,
                    Category = "Логика",
                    Message = $"Interaction ссылается на несуществующий source control '{interaction.SourceControlName}'.",
                    Recommendation = "Выберите существующий DataGrid или удалите устаревшее действие логики."
                });
                continue;
            }

            if (!string.Equals(source.Type, DesignerControlTypes.DataGrid, StringComparison.Ordinal))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = "Событие SelectionChanged сейчас поддерживается только для DataGrid.",
                    Recommendation = "Используйте DataGrid как источник interaction или удалите это действие.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name
                });
            }

            if (!string.Equals(interaction.EventName, InteractionModel.EventSelectionChanged, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(interaction.ActionType, InteractionModel.ActionSetProperty, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = $"Interaction '{interaction.EventName}/{interaction.ActionType}' пока не поддерживается генератором.",
                    Recommendation = "Для первого сценария используйте SelectionChanged -> SetProperty.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name
                });
            }

            if (target is null)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = $"Interaction ссылается на несуществующий target control '{interaction.TargetControlName}'.",
                    Recommendation = "Выберите существующий TextBox, TextBlock, Button или CheckBox.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name
                });
                continue;
            }

            if (!IsSupportedInteractionTarget(target) || !IsSupportedInteractionTargetProperty(target, interaction.TargetProperty))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = target.NameOrFallback(),
                    Category = "Логика",
                    Message = $"Target property '{interaction.TargetProperty}' не поддерживается для {target.Type}.",
                    Recommendation = "Поддерживаются TextBox.Text, TextBlock.Text, Button.Content и CheckBox.IsChecked.",
                    RelatedControlId = target.Id,
                    RelatedControlName = target.Name
                });
            }

            var sourceBinding = FindBindingSource(bindingSources, source.BindingSourceId);
            if (sourceBinding is null)
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = "DataGrid interaction не может проверить поле, потому что у DataGrid нет BindingSource.",
                    Recommendation = "Подключите BindingSource к DataGrid или удалите interaction.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(interaction.SourcePath) && !BindingFieldExists(sourceBinding, interaction.SourcePath))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = $"Поле interaction '{interaction.SourcePath}' не найдено в BindingSource '{sourceBinding.NameOrFallback()}'.",
                    Recommendation = "Выберите поле из списка BindingSource или обновите схему данных.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name,
                    RelatedBindingSourceId = sourceBinding.Id,
                    RelatedBindingSourceName = sourceBinding.Name
                });
            }

            foreach (var token in ExtractTemplateTokens(interaction.TextTemplate))
            {
                if (BindingFieldExists(sourceBinding, token))
                    continue;

                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = source.NameOrFallback(),
                    Category = "Логика",
                    Message = $"В шаблоне interaction поле '{{{token}}}' не найдено в BindingSource '{sourceBinding.NameOrFallback()}'.",
                    Recommendation = "Исправьте шаблон текста или добавьте поле в BindingSource.",
                    RelatedControlId = source.Id,
                    RelatedControlName = source.Name,
                    RelatedBindingSourceId = sourceBinding.Id,
                    RelatedBindingSourceName = sourceBinding.Name
                });
            }

            if (target.Type == DesignerControlTypes.CheckBox
                && !string.IsNullOrWhiteSpace(interaction.SourcePath)
                && sourceBinding.Fields.FirstOrDefault(field => FieldPathMatches(field, interaction.SourcePath)) is { } boolField
                && !LooksLikeBoolType(boolField.TypeName))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = target.NameOrFallback(),
                    Category = "Логика",
                    Message = $"CheckBox.IsChecked получает поле '{interaction.SourcePath}' типа '{boolField.TypeName}'.",
                    Recommendation = "Лучше выбирать bool/Boolean/bit поле, иначе runtime попробует преобразовать строку.",
                    RelatedControlId = target.Id,
                    RelatedControlName = target.Name
                });
            }
        }
    }

    private void ValidateBindingUsage(
        DesignControlModel control,
        IControlDescriptor descriptor,
        BindingSourceModel? source,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        var requiresSource = string.Equals(control.Type, DesignerControlTypes.DataGrid, StringComparison.Ordinal)
            || descriptor.Properties.Any(property =>
                string.Equals(property.BuiltInPropertyName, nameof(DesignControlModel.BindingSourceId), StringComparison.Ordinal));

        if (requiresSource && string.IsNullOrWhiteSpace(control.BindingSourceId))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Привязка",
                Message = $"Элемент '{control.NameOrFallback()}' не привязан к источнику данных.",
                Recommendation = "Выберите BindingSource в панели свойств или во вкладке данных.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(control.BindingSourceId) && source is null)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Error,
                Source = control.NameOrFallback(),
                Category = "Привязка",
                Message = $"Элемент '{control.NameOrFallback()}' ссылается на несуществующий BindingSource.",
                Recommendation = "Переназначьте источник данных или восстановите удалённый BindingSource.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
            return;
        }

        if (string.Equals(control.Type, DesignerControlTypes.DataGrid, StringComparison.Ordinal)
            && source is not null
            && source.Fields.Count == 0)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Привязка",
                Message = $"DataGrid '{control.NameOrFallback()}' подключен к BindingSource без полей.",
                Recommendation = "Добавьте реальные поля в BindingSource или импортируйте схему из DLL/SQL.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name,
                RelatedBindingSourceId = source.Id,
                RelatedBindingSourceName = source.Name
            });
        }

        if (string.IsNullOrWhiteSpace(control.TextBindingPath))
            return;

        if (source is null)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Привязка поля",
                Message = $"У элемента '{control.NameOrFallback()}' указан путь поля, но не выбран BindingSource.",
                Recommendation = "Сначала выберите источник данных, затем настройте поле привязки.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
            return;
        }

        if (!BindingFieldExists(source, control.TextBindingPath))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Привязка поля",
                Message = $"Путь поля '{control.TextBindingPath}' не найден в источнике '{source.NameOrFallback()}'.",
                Recommendation = "Проверьте путь поля или обновите схему BindingSource.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name,
                RelatedBindingSourceId = source.Id,
                RelatedBindingSourceName = source.Name
            });
        }
    }

    private void ValidateImagePath(
        DesignControlModel control,
        string? currentDocumentPath,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (!string.Equals(control.Type, DesignerControlTypes.Image, StringComparison.Ordinal))
            return;

        var imageSource = control.ImageSource?.Trim();
        if (string.IsNullOrWhiteSpace(imageSource))
            return;

        if (IsUriLikeImagePath(imageSource))
            return;

        var resolvedPath = ResolveImagePath(imageSource, currentDocumentPath);
        if (resolvedPath is null)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Изображение",
                Message = $"Нельзя проверить относительный путь к изображению '{imageSource}', пока документ не сохранён.",
                Recommendation = "Сохраните документ или укажите абсолютный путь к файлу.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
            return;
        }

        if (!File.Exists(resolvedPath))
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Error,
                Source = control.NameOrFallback(),
                Category = "Изображение",
                Message = $"Файл изображения не найден: {imageSource}",
                Recommendation = "Исправьте путь к файлу или выберите существующее изображение.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
        }
    }

    private void ValidateCustomProperties(
        DesignControlModel control,
        IControlDescriptor descriptor,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        var customSchema = descriptor.Properties
            .Where(property => string.IsNullOrWhiteSpace(property.BuiltInPropertyName))
            .ToDictionary(property => property.Key, property => property, StringComparer.OrdinalIgnoreCase);

        foreach (var customProperty in control.CustomProperties)
        {
            if (!customSchema.TryGetValue(customProperty.Key, out var propertyDescriptor))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Warning,
                    Source = control.NameOrFallback(),
                    Category = "Custom properties",
                    Message = $"Свойство '{customProperty.Key}' больше не описано descriptor-ом.",
                    Recommendation = "Удалите устаревшее custom property или верните соответствующую схему в plugin descriptor.",
                    RelatedControlId = control.Id,
                    RelatedControlName = control.Name
                });
                continue;
            }

            if (!IsValidCustomPropertyValue(propertyDescriptor, customProperty.ValueJson, out var details))
            {
                diagnostics.Add(new DocumentDiagnosticModel
                {
                    Severity = DocumentDiagnosticSeverity.Error,
                    Source = control.NameOrFallback(),
                    Category = "Custom properties",
                    Message = $"Свойство '{propertyDescriptor.Title}' содержит некорректное значение.",
                    Recommendation = details,
                    RelatedControlId = control.Id,
                    RelatedControlName = control.Name
                });
            }
        }
    }

    private void ValidateGeometry(
        DesignControlModel control,
        double designWidth,
        double designHeight,
        ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (control.X < 0 || control.Y < 0)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Геометрия",
                Message = "Элемент расположен частично за пределами рабочей области по отрицательным координатам.",
                Recommendation = "Проверьте позицию элемента на поверхности формы.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
        }

        if (control.X + control.Width > designWidth + 1 || control.Y + control.Height > designHeight + 1)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Warning,
                Source = control.NameOrFallback(),
                Category = "Геометрия",
                Message = "Элемент выходит за границы формы.",
                Recommendation = "Уменьшите размер элемента или переместите его внутрь рабочей области.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
        }

        if (control.Width <= 48 || control.Height <= 24)
        {
            diagnostics.Add(new DocumentDiagnosticModel
            {
                Severity = DocumentDiagnosticSeverity.Info,
                Source = control.NameOrFallback(),
                Category = "Геометрия",
                Message = "Размер элемента очень маленький и может быть неудобен для пользователя.",
                Recommendation = "Проверьте, достаточно ли места для текста, клика и фокуса.",
                RelatedControlId = control.Id,
                RelatedControlName = control.Name
            });
        }
    }

    private static BindingSourceModel? FindBindingSource(IEnumerable<BindingSourceModel> bindingSources, string? bindingSourceId)
    {
        if (string.IsNullOrWhiteSpace(bindingSourceId))
            return null;

        return bindingSources.FirstOrDefault(source => string.Equals(source.Id, bindingSourceId, StringComparison.Ordinal));
    }

    private static DesignControlModel? FindControlByName(IEnumerable<DesignControlModel> controls, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return controls.FirstOrDefault(control => string.Equals(control.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedInteractionTarget(DesignControlModel control)
    {
        return control.Type is DesignerControlTypes.TextBox
            or DesignerControlTypes.TextBlock
            or DesignerControlTypes.Button
            or DesignerControlTypes.CheckBox;
    }

    private static bool IsDataGridSelectionChangedEvent(string? eventName)
    {
        return string.Equals(
            InteractionModel.NormalizeEventName(eventName),
            InteractionModel.EventDataGridSelectionChanged,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedInteractionSourceEvent(DesignControlModel source, string eventName)
    {
        return source.Type switch
        {
            DesignerControlTypes.Button => string.Equals(eventName, InteractionModel.EventButtonClick, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.TextBox => string.Equals(eventName, InteractionModel.EventTextBoxTextChanged, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.CheckBox => string.Equals(eventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.DataGrid => IsDataGridSelectionChangedEvent(eventName),
            _ => false
        };
    }

    private static bool IsSupportedInteractionTargetAction(DesignControlModel target, InteractionModel interaction)
    {
        return interaction.ActionType switch
        {
            InteractionModel.ActionSetProperty or InteractionModel.ActionClearProperty => IsSupportedInteractionTarget(target)
                && IsSupportedInteractionTargetProperty(target, interaction.TargetProperty),
            InteractionModel.ActionToggleVisibility => true,
            InteractionModel.ActionEnableDisable => true,
            _ => false
        };
    }

    private static bool IsSupportedInteractionTargetProperty(DesignControlModel control, string? targetProperty)
    {
        var property = string.IsNullOrWhiteSpace(targetProperty)
            ? InteractionModel.TargetPropertyText
            : targetProperty.Trim();

        return control.Type switch
        {
            DesignerControlTypes.TextBox => string.Equals(property, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.TextBlock => string.Equals(property, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.Button => string.Equals(property, InteractionModel.TargetPropertyContent, StringComparison.OrdinalIgnoreCase),
            DesignerControlTypes.CheckBox => string.Equals(property, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool BindingFieldExists(BindingSourceModel source, string bindingPath)
    {
        foreach (var field in source.Fields)
        {
            var directPath = field.Path?.Trim() ?? string.Empty;
            var sanitizedPath = SanitizeIdentifier(directPath, "Field");

            if (string.Equals(bindingPath, directPath, StringComparison.Ordinal)
                || string.Equals(bindingPath, sanitizedPath, StringComparison.Ordinal)
                || bindingPath.EndsWith("." + directPath, StringComparison.Ordinal)
                || bindingPath.EndsWith("." + sanitizedPath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FieldPathMatches(BindingFieldModel field, string bindingPath)
    {
        var directPath = field.Path?.Trim() ?? string.Empty;
        var sanitizedPath = SanitizeIdentifier(directPath, "Field");

        return string.Equals(bindingPath, directPath, StringComparison.Ordinal)
            || string.Equals(bindingPath, sanitizedPath, StringComparison.Ordinal)
            || bindingPath.EndsWith("." + directPath, StringComparison.Ordinal)
            || bindingPath.EndsWith("." + sanitizedPath, StringComparison.Ordinal);
    }

    private static bool LooksLikeBoolType(string? typeName)
    {
        var normalized = typeName?.Trim() ?? string.Empty;
        return normalized.Equals("bool", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("boolean", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("bit", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("System.Boolean", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractTemplateTokens(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            yield break;

        var start = -1;
        for (var index = 0; index < template.Length; index++)
        {
            var current = template[index];
            if (current == '{')
            {
                start = index + 1;
                continue;
            }

            if (current != '}' || start < 0)
                continue;

            var token = template[start..index].Trim();
            if (!string.IsNullOrWhiteSpace(token))
                yield return token;

            start = -1;
        }
    }

    private static string SanitizeIdentifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var characters = value.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray();
        if (characters.Length == 0)
            return fallback;

        var sanitized = new string(characters);
        if (char.IsDigit(sanitized[0]))
            sanitized = "_" + sanitized;

        return sanitized;
    }

    private static bool IsValidFieldWidth(string width)
    {
        var normalized = width.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(normalized, "*", StringComparison.Ordinal))
            return true;

        if (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            return double.TryParse(
                normalized[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var starValue)
                && starValue > 0;
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixelWidth)
            && pixelWidth > 0;
    }

    private static bool IsValidCustomPropertyValue(
        DesignPropertyDescriptor descriptor,
        string? valueJson,
        out string details)
    {
        details = "Откройте свойство в правой панели и введите значение корректного типа.";

        try
        {
            switch (descriptor.Editor)
            {
                case PropertyEditorKind.Bool:
                    JsonSerializer.Deserialize<bool>(valueJson ?? "false");
                    return true;

                case PropertyEditorKind.Number:
                    JsonSerializer.Deserialize<double>(valueJson ?? "0");
                    return true;

                case PropertyEditorKind.Text:
                case PropertyEditorKind.Binding:
                case PropertyEditorKind.Collection:
                    JsonSerializer.Deserialize<string>(valueJson ?? "\"\"");
                    return true;

                case PropertyEditorKind.Color:
                    var colorValue = JsonSerializer.Deserialize<string>(valueJson ?? "\"\"");
                    Brush.Parse(string.IsNullOrWhiteSpace(colorValue) ? "#FFFFFF" : colorValue);
                    return true;

                case PropertyEditorKind.Enum:
                    var enumValue = JsonSerializer.Deserialize<string>(valueJson ?? "\"\"");
                    if (enumValue is null)
                    {
                        details = "Enum-свойство должно хранить строковое значение.";
                        return false;
                    }

                    if (descriptor.Options.Count == 0
                        || descriptor.Options.Any(option => string.Equals(option.Value, enumValue, StringComparison.Ordinal)))
                    {
                        return true;
                    }

                    details = $"Допустимые значения: {string.Join(", ", descriptor.Options.Select(option => option.Value))}.";
                    return false;

                default:
                    JsonDocument.Parse(valueJson ?? "null");
                    return true;
            }
        }
        catch (Exception ex)
        {
            details = $"Исправьте значение свойства. Текущий JSON не соответствует типу '{descriptor.Editor}': {ex.Message}";
            return false;
        }
    }

    private static bool IsUriLikeImagePath(string imageSource)
    {
        return imageSource.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
            || imageSource.StartsWith("resm:", StringComparison.OrdinalIgnoreCase)
            || imageSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || imageSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveImagePath(string imageSource, string? currentDocumentPath)
    {
        if (Path.IsPathRooted(imageSource))
            return Path.GetFullPath(imageSource);

        if (string.IsNullOrWhiteSpace(currentDocumentPath))
            return null;

        var directory = Path.GetDirectoryName(currentDocumentPath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        return Path.GetFullPath(Path.Combine(directory, imageSource));
    }
}

internal static class DocumentDiagnosticsExtensions
{
    public static string NameOrFallback(this DesignControlModel control)
    {
        return string.IsNullOrWhiteSpace(control.Name) ? control.Type : control.Name;
    }

    public static string NameOrFallback(this BindingSourceModel source)
    {
        return string.IsNullOrWhiteSpace(source.Name) ? "BindingSource" : source.Name;
    }

    public static string PathOrFallback(this BindingFieldModel field)
    {
        return string.IsNullOrWhiteSpace(field.Path) ? "Field" : field.Path;
    }

    public static string HeaderOrFallback(this BindingFieldModel field)
    {
        return string.IsNullOrWhiteSpace(field.Header) ? field.PathOrFallback() : field.Header;
    }
}
