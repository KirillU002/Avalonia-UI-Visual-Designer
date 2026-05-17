using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FormDesigner.Services;

public sealed class PreviewRuntimeService
{
    private readonly PreviewInteractionExecutor _interactionExecutor = new();

    public PreviewRuntimeContext? Current { get; private set; }

    public PreviewRuntimeContext Start(
        IEnumerable<DesignControlModel> controls,
        IEnumerable<BindingSourceModel> bindingSources,
        IEnumerable<InteractionModel> interactions)
    {
        Current = PreviewRuntimeContext.CreateSnapshot(controls, bindingSources, interactions);
        Current.ValidateInteractionReferences();
        return Current;
    }

    public PreviewRuntimeContext Reset()
    {
        var context = Current ?? throw new InvalidOperationException("Preview runtime is not active.");
        context.ResetRuntimeState();
        context.AddInfo(
            "Preview runtime",
            "Preview warnings",
            "Preview reset completed.",
            "Runtime-only values were cleared and the preview returned to the document snapshot.");
        return context;
    }

    public PreviewRuntimeContext Reload(
        IEnumerable<DesignControlModel> controls,
        IEnumerable<BindingSourceModel> bindingSources,
        IEnumerable<InteractionModel> interactions)
    {
        var context = Start(controls, bindingSources, interactions);
        context.AddInfo(
            "Preview runtime",
            "Preview warnings",
            "Preview reloaded.",
            "Controls, bindings and interactions were rebuilt from the current document.");
        return context;
    }

    public void End()
    {
        Current = null;
    }

    public PreviewInteractionExecutionResult ExecuteInteractions(
        DesignControlModel source,
        string eventName,
        IReadOnlyDictionary<string, string>? sourceValues)
    {
        return Current is null
            ? PreviewInteractionExecutionResult.Empty
            : _interactionExecutor.Execute(Current, source, eventName, sourceValues);
    }
}

public sealed class PreviewRuntimeContext
{
    private readonly List<DocumentDiagnosticModel> _diagnostics = new();
    private int _executionDepth;

    private PreviewRuntimeContext(
        IReadOnlyList<DesignControlModel> controls,
        IReadOnlyList<BindingSourceModel> bindingSources,
        IReadOnlyList<InteractionModel> interactions)
    {
        Controls = controls;
        BindingSources = bindingSources;
        Interactions = interactions;
    }

    public IReadOnlyList<DesignControlModel> Controls { get; }

    public IReadOnlyList<BindingSourceModel> BindingSources { get; }

    public IReadOnlyList<InteractionModel> Interactions { get; }

    public IReadOnlyList<DocumentDiagnosticModel> Diagnostics => _diagnostics;

    public int DiagnosticsVersion { get; private set; }

    public Dictionary<string, string> TextBoxValuesByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TextBlockValuesByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ButtonContentByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> CheckBoxValuesByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> VisibilityByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> EnabledByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ButtonClickCountByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, PreviewRuntimeDataGridSortState> DataGridSortByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> DataGridSelectedRowByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> DataGridFilterValuesByControlId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static PreviewRuntimeContext CreateSnapshot(
        IEnumerable<DesignControlModel> controls,
        IEnumerable<BindingSourceModel> bindingSources,
        IEnumerable<InteractionModel> interactions)
    {
        var controlCopies = controls
            .Select(CloneControlPreservingId)
            .ToList();
        var sourceCopies = bindingSources
            .Select(source => source.Clone())
            .ToList();
        var interactionCopies = interactions
            .Select(CloneInteractionPreservingId)
            .ToList();

        return new PreviewRuntimeContext(controlCopies, sourceCopies, interactionCopies);
    }

    public void ResetRuntimeState()
    {
        TextBoxValuesByControlId.Clear();
        TextBlockValuesByControlId.Clear();
        ButtonContentByControlId.Clear();
        CheckBoxValuesByControlId.Clear();
        VisibilityByControlId.Clear();
        EnabledByControlId.Clear();
        ButtonClickCountByControlId.Clear();
        DataGridSortByControlId.Clear();
        DataGridSelectedRowByControlId.Clear();
        DataGridFilterValuesByControlId.Clear();
        _diagnostics.Clear();
        DiagnosticsVersion++;
        ValidateInteractionReferences();
    }

    public IEnumerable<DesignControlModel> GetChildControls(string? parentId)
    {
        var normalized = NormalizeId(parentId);
        return Controls.Where(control => NormalizeId(control.ParentId) == normalized);
    }

    public BindingSourceModel? GetBindingSource(string? bindingSourceId)
    {
        if (string.IsNullOrWhiteSpace(bindingSourceId))
            return null;

        return BindingSources.FirstOrDefault(source =>
            string.Equals(source.Id, bindingSourceId, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<BindingFieldModel> GetBindingFields(string? bindingSourceId)
    {
        return GetBindingSource(bindingSourceId)?.Fields ?? Enumerable.Empty<BindingFieldModel>();
    }

    public DesignControlModel? FindControlById(string? controlId)
    {
        if (string.IsNullOrWhiteSpace(controlId))
            return null;

        return Controls.FirstOrDefault(control =>
            string.Equals(control.Id, controlId, StringComparison.OrdinalIgnoreCase));
    }

    public DesignControlModel? FindControlByName(string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            return null;

        return Controls.FirstOrDefault(control =>
            string.Equals(control.Name, controlName, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryEnterInteractionExecution()
    {
        const int maxExecutionDepth = 12;
        if (_executionDepth >= maxExecutionDepth)
        {
            AddError(
                "Preview interaction failed",
                "Preview errors",
                "Preview interaction loop guard stopped execution.",
                "Check interactions that update controls used by other preview interactions.");
            return false;
        }

        _executionDepth++;
        return true;
    }

    public void ExitInteractionExecution()
    {
        _executionDepth = Math.Max(0, _executionDepth - 1);
    }

    public void ValidateInteractionReferences()
    {
        foreach (var interaction in Interactions)
        {
            var source = FindControlByName(interaction.SourceControlName);
            if (source is null)
            {
                AddError(
                    FormatInteractionSource(interaction),
                    "Preview errors",
                    $"Source control not found in preview runtime: '{interaction.SourceControlName}'.",
                    "Select an existing source control or remove the stale interaction.");
            }

            if (!string.Equals(interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase)
                && FindControlByName(interaction.TargetControlName) is null)
            {
                AddError(
                    FormatInteractionSource(interaction),
                    "Preview errors",
                    $"Target control not found in preview runtime: '{interaction.TargetControlName}'.",
                    "Select an existing target control or remove the stale interaction.");
            }
        }
    }

    public void AddWarning(
        string source,
        string category,
        string message,
        string recommendation,
        DesignControlModel? relatedControl = null)
    {
        AddDiagnostic(DocumentDiagnosticSeverity.Warning, source, category, message, recommendation, relatedControl);
    }

    public void AddError(
        string source,
        string category,
        string message,
        string recommendation,
        DesignControlModel? relatedControl = null)
    {
        AddDiagnostic(DocumentDiagnosticSeverity.Error, source, category, message, recommendation, relatedControl);
    }

    public void AddInfo(
        string source,
        string category,
        string message,
        string recommendation,
        DesignControlModel? relatedControl = null)
    {
        AddDiagnostic(DocumentDiagnosticSeverity.Info, source, category, message, recommendation, relatedControl);
    }

    private void AddDiagnostic(
        DocumentDiagnosticSeverity severity,
        string source,
        string category,
        string message,
        string recommendation,
        DesignControlModel? relatedControl)
    {
        if (_diagnostics.Any(item =>
            item.Severity == severity
            && string.Equals(item.Source, source, StringComparison.Ordinal)
            && string.Equals(item.Category, category, StringComparison.Ordinal)
            && string.Equals(item.Message, message, StringComparison.Ordinal)))
        {
            return;
        }

        _diagnostics.Add(new DocumentDiagnosticModel
        {
            Severity = severity,
            Source = string.IsNullOrWhiteSpace(source) ? "Preview runtime" : source,
            Category = category,
            Message = message,
            Recommendation = recommendation,
            RelatedControlId = relatedControl?.Id ?? string.Empty,
            RelatedControlName = relatedControl?.Name ?? string.Empty
        });
        DiagnosticsVersion++;
    }

    private static DesignControlModel CloneControlPreservingId(DesignControlModel control)
    {
        var clone = control.Clone();
        clone.Id = control.Id;
        return clone;
    }

    private static InteractionModel CloneInteractionPreservingId(InteractionModel interaction)
    {
        return new InteractionModel
        {
            Id = interaction.Id,
            SourceControlName = interaction.SourceControlName,
            EventName = interaction.EventName,
            ActionType = interaction.ActionType,
            TargetControlName = interaction.TargetControlName,
            TargetProperty = interaction.TargetProperty,
            SourcePath = interaction.SourcePath,
            TextTemplate = interaction.TextTemplate,
            MessageTitle = interaction.MessageTitle
        };
    }

    private static string NormalizeId(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
    }

    private static string FormatInteractionSource(InteractionModel interaction)
    {
        return string.IsNullOrWhiteSpace(interaction.SourceControlName)
            ? "Preview interaction"
            : interaction.SourceControlName;
    }
}

public sealed class PreviewInteractionExecutor
{
    public PreviewInteractionExecutionResult Execute(
        PreviewRuntimeContext context,
        DesignControlModel source,
        string eventName,
        IReadOnlyDictionary<string, string>? sourceValues)
    {
        if (!context.TryEnterInteractionExecution())
            return PreviewInteractionExecutionResult.Empty;

        try
        {
            var values = sourceValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var runtimeSource = context.FindControlById(source.Id) ?? context.FindControlByName(source.Name);
            if (runtimeSource is null)
            {
                context.AddError(
                    "Preview interaction failed",
                    "Preview errors",
                    $"Source control not found in preview runtime: '{source.Name}'.",
                    "Reload preview to rebuild runtime controls.");
                return PreviewInteractionExecutionResult.Empty;
            }

            var normalizedEventName = InteractionModel.NormalizeEventName(eventName);
            var result = new PreviewInteractionExecutionResult();
            foreach (var interaction in context.Interactions
                .Where(interaction => string.Equals(interaction.SourceControlName, runtimeSource.Name, StringComparison.OrdinalIgnoreCase))
                .Where(interaction => string.Equals(InteractionModel.NormalizeEventName(interaction.EventName), normalizedEventName, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    result.HasVisualChanges |= ExecuteInteraction(context, interaction, normalizedEventName, values, result);
                }
                catch (Exception ex)
                {
                    context.AddError(
                        FormatInteractionSource(interaction),
                        "Preview errors",
                        $"Preview interaction failed: {FormatInteraction(interaction)}. {ex.Message}",
                        "Fix the interaction settings or reload preview after changing controls.",
                        runtimeSource);
                }
            }

            return result;
        }
        finally
        {
            context.ExitInteractionExecution();
        }
    }

    private static bool ExecuteInteraction(
        PreviewRuntimeContext context,
        InteractionModel interaction,
        string eventName,
        IReadOnlyDictionary<string, string> sourceValues,
        PreviewInteractionExecutionResult result)
    {
        var value = ResolveInteractionValue(context, interaction, sourceValues);
        if (string.Equals(interaction.ActionType, InteractionModel.ActionShowMessage, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                context.AddWarning(
                    FormatInteractionSource(interaction),
                    "Preview warnings",
                    $"ShowMessage is called without text: {FormatInteraction(interaction)}.",
                    "Add a text template or source path for the message.");
            }

            result.Messages.Add(new PreviewMessageRequest(value, interaction.MessageTitle));
            return false;
        }

        var target = context.FindControlByName(interaction.TargetControlName);
        if (target is null)
        {
            context.AddError(
                FormatInteractionSource(interaction),
                "Preview errors",
                $"Target control not found in preview runtime: '{interaction.TargetControlName}'. Action: {interaction.ActionType}.",
                "Select an existing target control or remove the stale interaction.");
            return false;
        }

        if (string.Equals(interaction.ActionType, InteractionModel.ActionToggleVisibility, StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetBooleanStateForInteractionEvent(eventName, out var nextVisible))
            {
                context.VisibilityByControlId[target.Id] = nextVisible;
            }
            else
            {
                var current = context.VisibilityByControlId.TryGetValue(target.Id, out var visible)
                    ? visible
                    : target.IsVisible;
                context.VisibilityByControlId[target.Id] = !current;
            }

            return true;
        }

        if (string.Equals(interaction.ActionType, InteractionModel.ActionEnableDisable, StringComparison.OrdinalIgnoreCase))
        {
            context.EnabledByControlId[target.Id] = TryGetBooleanStateForInteractionEvent(eventName, out var enabled)
                ? enabled
                : ParseBool(value);
            return true;
        }

        var targetProperty = string.IsNullOrWhiteSpace(interaction.TargetProperty)
            ? GetDefaultInteractionTargetProperty(target)
            : interaction.TargetProperty.Trim();

        var changed = string.Equals(interaction.ActionType, InteractionModel.ActionClearProperty, StringComparison.OrdinalIgnoreCase)
            ? ClearTargetProperty(context, target, targetProperty)
            : SetTargetProperty(context, target, targetProperty, value);

        if (!changed)
        {
            context.AddWarning(
                FormatInteractionSource(interaction),
                "Preview warnings",
                $"Target property '{targetProperty}' is not supported by '{target.Name}' ({target.Type}).",
                "Choose Text, Content, IsChecked, IsVisible or IsEnabled for a compatible control.",
                target);
        }

        return changed;
    }

    private static bool ClearTargetProperty(
        PreviewRuntimeContext context,
        DesignControlModel target,
        string targetProperty)
    {
        if (target.Type == DesignerControlTypes.CheckBox
            && string.Equals(targetProperty, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase))
        {
            context.CheckBoxValuesByControlId[target.Id] = false;
            return true;
        }

        if (string.Equals(targetProperty, InteractionModel.TargetPropertyIsVisible, StringComparison.OrdinalIgnoreCase))
        {
            context.VisibilityByControlId[target.Id] = false;
            return true;
        }

        if (string.Equals(targetProperty, InteractionModel.TargetPropertyIsEnabled, StringComparison.OrdinalIgnoreCase))
        {
            context.EnabledByControlId[target.Id] = false;
            return true;
        }

        return SetTargetProperty(context, target, targetProperty, string.Empty);
    }

    private static bool SetTargetProperty(
        PreviewRuntimeContext context,
        DesignControlModel target,
        string targetProperty,
        string value)
    {
        if (target.Type == DesignerControlTypes.TextBox
            && string.Equals(targetProperty, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase))
        {
            context.TextBoxValuesByControlId[target.Id] = value;
            return true;
        }

        if ((target.Type == DesignerControlTypes.TextBlock || target.Type == DesignerControlTypes.Border)
            && string.Equals(targetProperty, InteractionModel.TargetPropertyText, StringComparison.OrdinalIgnoreCase))
        {
            context.TextBlockValuesByControlId[target.Id] = value;
            return true;
        }

        if (target.Type == DesignerControlTypes.Button
            && string.Equals(targetProperty, InteractionModel.TargetPropertyContent, StringComparison.OrdinalIgnoreCase))
        {
            context.ButtonContentByControlId[target.Id] = value;
            return true;
        }

        if (target.Type == DesignerControlTypes.CheckBox
            && string.Equals(targetProperty, InteractionModel.TargetPropertyIsChecked, StringComparison.OrdinalIgnoreCase))
        {
            context.CheckBoxValuesByControlId[target.Id] = ParseBool(value);
            return true;
        }

        if (string.Equals(targetProperty, InteractionModel.TargetPropertyIsVisible, StringComparison.OrdinalIgnoreCase))
        {
            context.VisibilityByControlId[target.Id] = ParseBool(value);
            return true;
        }

        if (string.Equals(targetProperty, InteractionModel.TargetPropertyIsEnabled, StringComparison.OrdinalIgnoreCase))
        {
            context.EnabledByControlId[target.Id] = ParseBool(value);
            return true;
        }

        return false;
    }

    private static string ResolveInteractionValue(
        PreviewRuntimeContext context,
        InteractionModel interaction,
        IReadOnlyDictionary<string, string> rowValues)
    {
        var missingFields = new List<string>();
        var value = !string.IsNullOrWhiteSpace(interaction.TextTemplate)
            ? ApplyTemplate(interaction.TextTemplate, rowValues, missingFields)
            : GetRowValue(rowValues, interaction.SourcePath, missingFields);

        foreach (var missingField in missingFields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            context.AddWarning(
                FormatInteractionSource(interaction),
                "Preview warnings",
                $"Source field not found: '{missingField}'. Interaction: {FormatInteraction(interaction)}.",
                "Check BindingSource field names, SourcePath or the message text template.");
        }

        return value;
    }

    private static string ApplyTemplate(
        string template,
        IReadOnlyDictionary<string, string> rowValues,
        ICollection<string> missingFields)
    {
        return Regex.Replace(
            template,
            "\\{(?<name>[^{}]+)\\}",
            match => GetRowValue(rowValues, match.Groups["name"].Value.Trim(), missingFields));
    }

    private static string GetRowValue(
        IReadOnlyDictionary<string, string> rowValues,
        string? path,
        ICollection<string> missingFields)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmedPath = path.Trim();
        if (rowValues.TryGetValue(trimmedPath, out var directValue))
            return directValue;

        var match = rowValues.FirstOrDefault(pair => string.Equals(pair.Key, trimmedPath, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(match.Key))
            return match.Value;

        missingFields.Add(trimmedPath);
        return string.Empty;
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (int.TryParse(value, out var intValue))
            return intValue != 0;

        return false;
    }

    private static bool TryGetBooleanStateForInteractionEvent(string eventName, out bool value)
    {
        if (string.Equals(eventName, InteractionModel.EventCheckBoxChecked, StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(eventName, InteractionModel.EventCheckBoxUnchecked, StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static string GetDefaultInteractionTargetProperty(DesignControlModel target)
    {
        return target.Type switch
        {
            DesignerControlTypes.Button => InteractionModel.TargetPropertyContent,
            DesignerControlTypes.CheckBox => InteractionModel.TargetPropertyIsChecked,
            _ => InteractionModel.TargetPropertyText
        };
    }

    private static string FormatInteraction(InteractionModel interaction)
    {
        return $"{interaction.SourceControlName}.{InteractionModel.NormalizeEventName(interaction.EventName)} -> {interaction.ActionType} {interaction.TargetControlName}.{interaction.TargetProperty}";
    }

    private static string FormatInteractionSource(InteractionModel interaction)
    {
        return string.IsNullOrWhiteSpace(interaction.SourceControlName)
            ? "Preview interaction"
            : interaction.SourceControlName;
    }
}

public sealed class PreviewInteractionExecutionResult
{
    public static PreviewInteractionExecutionResult Empty { get; } = new();

    public bool HasVisualChanges { get; set; }

    public List<PreviewMessageRequest> Messages { get; } = new();
}

public sealed class PreviewMessageRequest
{
    public PreviewMessageRequest(string message, string? title)
    {
        Message = message;
        Title = title ?? string.Empty;
    }

    public string Message { get; }

    public string Title { get; }
}

public sealed class PreviewRuntimeDataGridSortState
{
    public PreviewRuntimeDataGridSortState(string fieldPath, string direction)
    {
        FieldPath = fieldPath;
        Direction = direction;
    }

    public string FieldPath { get; }

    public string Direction { get; }
}
