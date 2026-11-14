using Avalonia;
using Avalonia.Controls;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.Metadata;

/// <summary>
/// Reviewed support policy on top of discovery. This list enables three complete vertical
/// slices, not every discovered control/property. The same metadata/codec owns Inspector,
/// native creation, literal AXAML import and exported attributes.
/// </summary>
public static class StandardControlProperties
{
    public static readonly IReadOnlyDictionary<string, Type> PilotTypes = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Type>(
        new Dictionary<string, Type>(StringComparer.Ordinal)
        { ["Slider"] = typeof(Slider), ["ProgressBar"] = typeof(ProgressBar), ["ToggleSwitch"] = typeof(ToggleSwitch) });
    private static readonly IReadOnlyDictionary<string, string[]> Keys = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Slider"] = new[] { "Minimum", "Maximum", "Value", "SmallChange", "LargeChange", "Orientation", "IsSnapToTickEnabled", "TickFrequency", "TickPlacement", "IsEnabled", "Padding" },
        ["ProgressBar"] = new[] { "Minimum", "Maximum", "Value", "IsIndeterminate", "Orientation", "ShowProgressText", "IsEnabled", "Padding" },
        ["ToggleSwitch"] = new[] { "IsChecked", "IsThreeState", "IsEnabled", "Padding", "CornerRadius" }
    };
    // Cache only non-collectible, explicitly shipped types; never plugin Type/Assembly keys.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<AvaloniaPropertyInfo>>> Supported = new(() =>
        PilotTypes.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AvaloniaPropertyInfo>)Keys[pair.Key]
            .Select(key => AvaloniaMetadataCatalog.Get(pair.Value).Properties.FirstOrDefault(p => p.Key == key))
            .Where(p => p is { ReadOnly: false, Property: not null }).Cast<AvaloniaPropertyInfo>().ToArray(), StringComparer.Ordinal));
    public static IReadOnlyList<AvaloniaPropertyInfo> PropertiesFor(string? type) =>
        type is not null && Supported.Value.TryGetValue(type, out var properties) ? properties : Array.Empty<AvaloniaPropertyInfo>();
    public static AvaloniaPropertyInfo? Find(string? type, string key) => PropertiesFor(type).FirstOrDefault(p => p.Key == key);
    public static string CategoryFor(AvaloniaPropertyInfo property) => property.Editor is MetadataEditor.Thickness or MetadataEditor.CornerRadius ? "Layout" : "Behavior";
    public static DesignPropertyDescriptor Descriptor(AvaloniaPropertyInfo property) => new()
    {
        Key = property.Key, Title = property.Name, Category = CategoryFor(property),
        Description = $"Avalonia {property.OwnerType.Name}.{property.Name}. {Hint(property)}",
        Editor = property.Editor switch
        {
            MetadataEditor.Boolean => PropertyEditorKind.Bool, MetadataEditor.Enumeration => PropertyEditorKind.Enum,
            MetadataEditor.Number => PropertyEditorKind.Number, MetadataEditor.Color => PropertyEditorKind.Color, _ => PropertyEditorKind.Text
        },
        DefaultValueJson = Encode(property.DefaultValue), IsBindable = property.Bindable,
        Options = Options(property).Select(text => new PropertyOption { Value = text, Title = text == MetadataValueCodec.NullText ? "Не задано (null)" : text }).ToArray()
    };
    public static string Hint(AvaloniaPropertyInfo property) => property.Editor switch
    {
        MetadataEditor.Thickness => "Отступы в DIP: одно, два или четыре числа (left,top,right,bottom).",
        MetadataEditor.CornerRadius => "Радиусы углов в DIP; значение проверяется Avalonia.",
        MetadataEditor.GridLength => "Размер Grid: Auto, *, 2* или число DIP.",
        _ when property.Nullable => "Допускается явное значение (null). Bindings редактируются отдельным механизмом.",
        _ => "Типизированное литеральное свойство; недопустимое значение не изменяет документ."
    };
    public static IReadOnlyList<string> Options(AvaloniaPropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var options = type.IsEnum ? Enum.GetNames(type).ToList() : type == typeof(bool) ? new List<string> { "False", "True" } : new();
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) options.Insert(0, MetadataValueCodec.NullText);
        return options;
    }
    public static string Read(AvaloniaPropertyInfo property, string? json)
    {
        if (json is null) return property.DefaultText;
        using var value = JsonDocument.Parse(json);
        return value.RootElement.ValueKind switch
        {
            JsonValueKind.Null => MetadataValueCodec.NullText,
            JsonValueKind.True => "True", JsonValueKind.False => "False",
            JsonValueKind.String => value.RootElement.GetString()!,
            JsonValueKind.Number => value.RootElement.GetRawText(),
            _ => throw new FormatException("Complex JSON is not a scalar Avalonia property: " + property.Key)
        };
    }
    public static bool TryEncode(AvaloniaPropertyInfo property, string text, out string json)
    {
        json = "null";
        if (property.ReadOnly || !MetadataValueCodec.TryParse(property.PropertyType, text, out var value)
            || property.Property?.IsValidValue(value) == false) return false;
        json = Encode(value); return true;
    }
    private static string Encode(object? value) => value switch
    {
        null => "null", bool flag => JsonSerializer.Serialize(flag), double number when double.IsFinite(number) => JsonSerializer.Serialize(number),
        int number => JsonSerializer.Serialize(number), long number => JsonSerializer.Serialize(number),
        _ => JsonSerializer.Serialize(MetadataValueCodec.Display(value))
    };
    public static string Attributes(string type, IReadOnlyDictionary<string, string> values, Func<string, string> escape)
    {
        var result = new System.Text.StringBuilder();
        foreach (var property in PropertiesFor(type))
        {
            if (!values.TryGetValue(property.Key, out var json)) continue;
            var text = Read(property, json);
            if (!TryEncode(property, text, out _)) throw new FormatException("Invalid Avalonia property: " + property.Key);
            result.Append(' ').Append(property.Key).Append("=\"").Append(text == MetadataValueCodec.NullText ? "{x:Null}" : escape(text)).Append('"');
        }
        return result.ToString();
    }
    public static Control Create(IDesignControlNode node, string? sourceType = null, bool applyCommon = true, Func<string, bool>? commonFilter = null)
    {
        var type = sourceType ?? node.TypeKey;
        if (!PilotTypes.TryGetValue(type, out var clrType)) throw new NotSupportedException(type);
        var control = (Control)Activator.CreateInstance(clrType)!;
        control.Name = node.Name;
        // Old model fields remain authoritative; placement belongs to existing layout adapters.
        var common = new[] { "Width", "Height", "Opacity", "IsVisible", "Background", "Foreground", "FontFamily", "FontSize", "FontWeight" };
        foreach (var name in applyCommon ? common : Array.Empty<string>())
        {
            if (commonFilter is not null && !commonFilter(name)) continue;
            if (!node.BuiltInProperties.TryGetValue(name, out var value) || value is null) continue;
            var property = AvaloniaMetadataCatalog.Get(clrType).Properties.FirstOrDefault(p => p.Key == name && !p.ReadOnly && p.Property is not null);
            if (property is not null && MetadataValueCodec.TryParse(property.PropertyType, MetadataValueCodec.Display(value), out var parsed))
                control.SetValue(property.Property!, parsed);
        }
        if (control is ToggleSwitch toggle) toggle.Content = node.GetString("Text", "Переключатель");
        foreach (var property in PropertiesFor(type))
        {
            if (!node.CustomProperties.TryGetValue(property.Key, out var json)) continue;
            var text = Read(property, json);
            if (!TryEncode(property, text, out _) || !MetadataValueCodec.TryParse(property.PropertyType, text, out var value))
                throw new FormatException("Invalid Avalonia property: " + property.Key);
            control.SetValue(property.Property!, value);
        }
        return control;
    }
    public static bool TryWriteModel(DesignControlModel model, AvaloniaPropertyInfo property, string value)
    {
        if (!TryEncode(property, value, out var json)) return false;
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == property.Key);
        if (stored is null) model.CustomProperties.Add(stored = new DesignPropertyValueModel { Key = property.Key });
        stored.ValueJson = json; return true;
    }
}
