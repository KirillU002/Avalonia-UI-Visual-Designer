using Avalonia;
using Avalonia.Controls;
using Eremex.AvaloniaUI.Controls.Editors;
using EremexDesignerPlugin.Services;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EremexDesignerPlugin.Descriptors;

public sealed class EremexTextEditorDescriptor : IControlDescriptor, IDesignerControlProviderMetadata
{
    public const string TypeKeyValue = "Eremex.TextEditor";
    public const string XmlNamespace = "https://schemas.eremexcontrols.net/avalonia/editors";
    private const string EditorValueProperty = "EditorValue";
    private const string WatermarkProperty = "Watermark";
    private const string ReadOnlyProperty = "ReadOnly";
    private const string MaskProperty = "Mask";
    private const string MaskTypeProperty = "MaskType";
    private const string TextWrappingProperty = "TextWrapping";
    private const string ValidateOnInputProperty = "ValidateOnInput";
    private const string DisplayFormatStringProperty = "DisplayFormatString";
    private const string EditorModeProperty = "EditorMode";
    private const string ErrorTextProperty = "ErrorText";
    private const string ErrorShowModeProperty = "ErrorShowMode";

    private readonly string _pluginId;
    private readonly string _pluginVersion;
    private readonly IReadOnlyList<DesignPropertyDescriptor> _propertySchema;

    public EremexTextEditorDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
        _propertySchema = BuildPropertySchema();
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "TextEditor";
    public string Category => "Editors";
    public string Description => "Eremex text editor. Требуется пакет Eremex и действующая лицензия или trial.";
    public bool IsContainer => false;
    public bool CanHostChildren => false;
    public string ChildLayoutMode => "Absolute";
    public IReadOnlyList<DesignPropertyDescriptor> Properties => _propertySchema;
    public string ProviderId => "Eremex";
    public string ProviderTitle => "Eremex";
    public string ToolboxGroup => "Eremex";
    public string ToolboxBadge => "EMX";
    public int ToolboxGroupOrder => 100;

    public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context)
    {
        var definition = new DesignerControlDefinition
        {
            TypeKey = TypeKey,
            DescriptorId = TypeKey,
            PluginId = _pluginId,
            PluginVersion = _pluginVersion
        };

        definition.BuiltInProperties["Width"] = 240d;
        definition.BuiltInProperties["Height"] = 36d;
        definition.BuiltInProperties["Margin"] = "0";
        definition.BuiltInProperties["Opacity"] = 1d;
        definition.BuiltInProperties["IsVisible"] = true;
        definition.CustomProperties[EditorValueProperty] = JsonSerializer.Serialize("Текст");
        definition.CustomProperties[WatermarkProperty] = JsonSerializer.Serialize("Введите значение");
        definition.CustomProperties[ReadOnlyProperty] = JsonSerializer.Serialize(false);
        definition.CustomProperties[MaskProperty] = JsonSerializer.Serialize(string.Empty);
        definition.CustomProperties[ValidateOnInputProperty] = JsonSerializer.Serialize(false);
        definition.CustomProperties[DisplayFormatStringProperty] = JsonSerializer.Serialize(string.Empty);
        definition.CustomProperties[ErrorTextProperty] = JsonSerializer.Serialize(string.Empty);
        AddDefaultEnumValue(definition, MaskTypeProperty);
        AddDefaultEnumValue(definition, TextWrappingProperty);
        AddDefaultEnumValue(definition, EditorModeProperty);
        AddDefaultEnumValue(definition, ErrorShowModeProperty);
        definition.CustomProperties["Eremex.ClrType"] = JsonSerializer.Serialize(typeof(TextEditor).FullName);
        definition.CustomProperties["Eremex.PackageId"] = JsonSerializer.Serialize(EremexPlugin.ControlsPackageId);
        definition.CustomProperties["Eremex.PackageVersion"] = JsonSerializer.Serialize(EremexPlugin.PackageVersion);
        definition.CustomProperties["Eremex.ThemePackageId"] = JsonSerializer.Serialize(EremexPlugin.ThemePackageId);
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var editor = new TextEditor
            {
                Width = Math.Max(80d, control.GetDouble("Width", 240d)),
                Height = Math.Max(24d, control.GetDouble("Height", 36d)),
                Margin = ParseThickness(control.GetString("Margin", "0")),
                Opacity = Math.Clamp(control.GetDouble("Opacity", 1d), 0d, 1d),
                IsVisible = control.GetBool("IsVisible", true)
            };

            EremexPreviewTheme.EnsureInstalled(
                editor,
                context.Mode == DesignerPreviewMode.Designer ? "DesignerCanvas" : "LegacyPreview");
            ApplyCustomProperties(editor, control);
            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_PREVIEW_CONTROL_CREATED controlType={typeof(TextEditor).FullName}; elapsedMs={stopwatch.ElapsedMilliseconds}");
            return editor;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_PREVIEW_CONTROL_FAILED controlType={typeof(TextEditor).FullName}; reason={ex.GetType().Name}:{ex.Message}; " +
                $"stackTrace={ex.StackTrace}; elapsedMs={stopwatch.ElapsedMilliseconds}");
            throw;
        }
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        context.RegisterXmlNamespace("mxe", XmlNamespace);
        var attributes = new List<string>
        {
            $"x:Name=\"{EscapeXml(control.Name)}\"",
            $"Width=\"{FormatDouble(control.GetDouble("Width", 240d))}\"",
            $"Height=\"{FormatDouble(control.GetDouble("Height", 36d))}\"",
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\"",
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\"",
            $"EditorValue=\"{EscapeXml(control.GetCustomValue(EditorValueProperty, ""))}\"",
            $"Watermark=\"{EscapeXml(control.GetCustomValue(WatermarkProperty, ""))}\"",
            $"ReadOnly=\"{ToXamlBoolean(control.GetCustomValue(ReadOnlyProperty, false))}\"",
            $"ValidateOnInput=\"{ToXamlBoolean(control.GetCustomValue(ValidateOnInputProperty, false))}\""
        };

        AppendOptionalStringAttribute(attributes, MaskProperty, control);
        AppendOptionalStringAttribute(attributes, DisplayFormatStringProperty, control);
        AppendOptionalStringAttribute(attributes, ErrorTextProperty, control);
        AppendOptionalEnumAttribute(attributes, MaskTypeProperty, control);
        AppendOptionalEnumAttribute(attributes, TextWrappingProperty, control);
        AppendOptionalEnumAttribute(attributes, EditorModeProperty, control);
        AppendOptionalEnumAttribute(attributes, ErrorShowModeProperty, control);

        var margin = control.GetString("Margin", "0");
        if (!string.IsNullOrWhiteSpace(margin) && !string.Equals(margin.Trim(), "0", StringComparison.Ordinal))
            attributes.Add($"Margin=\"{EscapeXml(margin)}\"");

        var opacity = control.GetDouble("Opacity", 1d);
        if (Math.Abs(opacity - 1d) > 0.0001d)
            attributes.Add($"Opacity=\"{FormatDouble(opacity)}\"");

        if (!control.GetBool("IsVisible", true))
            attributes.Add("IsVisible=\"False\"");

        writer.WriteLine(indentLevel, $"<mxe:TextEditor {string.Join(" ", attributes)} />");
    }

    private IReadOnlyList<DesignPropertyDescriptor> BuildPropertySchema()
    {
        return new[]
        {
            TextProperty(EditorValueProperty, "Значение", "Eremex", "Текстовое значение редактора."),
            TextProperty(WatermarkProperty, "Watermark", "Eremex", "Подсказка, отображаемая пока значение пустое."),
            BoolProperty(ReadOnlyProperty, "ReadOnly", "Eremex", "Запрещает редактирование значения."),
            TextProperty(MaskProperty, "Mask", "Формат", "Маска ввода Eremex. Оставьте пустой, если маска не требуется."),
            EnumProperty(MaskTypeProperty, "MaskType", "Формат", "Тип маски, применяемой к значению редактора."),
            EnumProperty(TextWrappingProperty, "TextWrapping", "Текст", "Правило переноса длинного текста."),
            BoolProperty(ValidateOnInputProperty, "ValidateOnInput", "Проверка", "Проверять значение во время ввода."),
            TextProperty(DisplayFormatStringProperty, "DisplayFormatString", "Формат", "Формат отображения значения."),
            EnumProperty(EditorModeProperty, "EditorMode", "Eremex", "Режим работы Eremex TextEditor."),
            TextProperty(ErrorTextProperty, "ErrorText", "Проверка", "Текст ошибки проверки."),
            EnumProperty(ErrorShowModeProperty, "ErrorShowMode", "Проверка", "Способ показа ошибки проверки.")
        };
    }

    private static DesignPropertyDescriptor TextProperty(string key, string title, string category, string hint)
    {
        return new DesignPropertyDescriptor
        {
            Key = key,
            Title = title,
            Description = hint,
            Category = category,
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize(string.Empty),
            IsBindable = string.Equals(key, EditorValueProperty, StringComparison.Ordinal)
        };
    }

    private static DesignPropertyDescriptor BoolProperty(string key, string title, string category, string hint)
    {
        return new DesignPropertyDescriptor
        {
            Key = key,
            Title = title,
            Description = hint,
            Category = category,
            Editor = PropertyEditorKind.Bool,
            DefaultValueJson = JsonSerializer.Serialize(false)
        };
    }

    private static DesignPropertyDescriptor EnumProperty(string key, string title, string category, string description)
    {
        var options = GetEnumOptions(key);
        return new DesignPropertyDescriptor
        {
            Key = key,
            Title = title,
            Description = description,
            Category = category,
            Editor = PropertyEditorKind.Enum,
            DefaultValueJson = JsonSerializer.Serialize(options.FirstOrDefault()?.Value ?? string.Empty),
            Options = options
        };
    }

    private static IReadOnlyList<PropertyOption> GetEnumOptions(string propertyName)
    {
        var property = typeof(TextEditor).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        var enumType = property?.PropertyType;
        if (enumType is null || !enumType.IsEnum)
            return Array.Empty<PropertyOption>();

        return Enum.GetNames(enumType)
            .Select(value => new PropertyOption { Value = value, Title = value })
            .ToList();
    }

    private static void AddDefaultEnumValue(DesignerControlDefinition definition, string propertyName)
    {
        var value = GetEnumOptions(propertyName).FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            definition.CustomProperties[propertyName] = JsonSerializer.Serialize(value);
    }

    private static void ApplyCustomProperties(TextEditor editor, IDesignControlNode control)
    {
        ApplyProperty(editor, EditorValueProperty, control.GetCustomValue(EditorValueProperty, string.Empty));
        ApplyProperty(editor, WatermarkProperty, control.GetCustomValue(WatermarkProperty, string.Empty));
        ApplyProperty(editor, ReadOnlyProperty, control.GetCustomValue(ReadOnlyProperty, false));
        ApplyProperty(editor, MaskProperty, control.GetCustomValue(MaskProperty, string.Empty));
        ApplyProperty(editor, MaskTypeProperty, control.GetCustomValue(MaskTypeProperty, string.Empty));
        ApplyProperty(editor, TextWrappingProperty, control.GetCustomValue(TextWrappingProperty, string.Empty));
        ApplyProperty(editor, ValidateOnInputProperty, control.GetCustomValue(ValidateOnInputProperty, false));
        ApplyProperty(editor, DisplayFormatStringProperty, control.GetCustomValue(DisplayFormatStringProperty, string.Empty));
        ApplyProperty(editor, EditorModeProperty, control.GetCustomValue(EditorModeProperty, string.Empty));
        ApplyProperty(editor, ErrorTextProperty, control.GetCustomValue(ErrorTextProperty, string.Empty));
        ApplyProperty(editor, ErrorShowModeProperty, control.GetCustomValue(ErrorShowModeProperty, string.Empty));
    }

    private static void ApplyProperty(TextEditor editor, string propertyName, object? value)
    {
        if (value is null)
            return;

        var property = editor.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanWrite)
            return;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        try
        {
            if (targetType == typeof(string))
            {
                property.SetValue(editor, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                return;
            }

            if (targetType == typeof(bool))
            {
                property.SetValue(editor, Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                return;
            }

            if (targetType.IsEnum)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(targetType, text, true, out var enumValue))
                    property.SetValue(editor, enumValue);
                return;
            }

            property.SetValue(editor, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EREMEX_PREVIEW_PROPERTY_SKIPPED property={propertyName}; reason={ex.Message}");
        }
    }

    private static void AppendOptionalStringAttribute(List<string> attributes, string key, IDesignControlNode control)
    {
        var value = control.GetCustomValue(key, string.Empty);
        if (!string.IsNullOrWhiteSpace(value))
            attributes.Add($"{key}=\"{EscapeXml(value)}\"");
    }

    private static void AppendOptionalEnumAttribute(List<string> attributes, string key, IDesignControlNode control)
    {
        var value = control.GetCustomValue(key, string.Empty);
        if (!string.IsNullOrWhiteSpace(value))
            attributes.Add($"{key}=\"{EscapeXml(value)}\"");
    }

    private static Thickness ParseThickness(string value)
    {
        try
        {
            return Thickness.Parse(value);
        }
        catch
        {
            return new Thickness(0);
        }
    }

    private static string ToXamlBoolean(bool value) => value ? "True" : "False";

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string FormatDouble(double value) => value.ToString(CultureInfo.InvariantCulture);
}
