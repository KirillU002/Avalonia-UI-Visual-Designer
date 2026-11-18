using Avalonia;
using Avalonia.Data;
using Avalonia.Controls;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FormDesigner.DesignerSystem.Metadata;

public enum PropertyValueKind { Literal, Binding, StaticResource, DynamicResource, TemplateBinding, ComplexObject, Collection, Opaque, Unset }

// Serializable authoring data, never a Binding, converter, ViewModel or resource instance.
// Uses the existing extensible CustomProperties contract; old scalar values remain intact.
public sealed record PropertyExpressionValue(PropertyValueKind Kind, string Text, int Version = 1);

public sealed record BindingEditorValue(string Path = "", string Mode = "Default", string UpdateSourceTrigger = "Default",
    string ElementName = "", string RelativeSource = "", string? FallbackValue = null, string? TargetNullValue = null,
    string? StringFormat = null, string ConverterKey = "");

public static class PropertyExpressions
{
    public const string Prefix = "@Avalonia.Expression:";
    public static bool SupportsMarkupUpdateSourceTrigger { get; } = typeof(Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension)
        .GetProperty(nameof(BindingBase.UpdateSourceTrigger)) is not null;
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex PathPattern = new(@"^(?:\.|[A-Za-z_][A-Za-z0-9_]*(?:\[\d{1,5}\])?(?:\.[A-Za-z_][A-Za-z0-9_]*(?:\[\d{1,5}\])?)*)?$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new(@"^[A-Za-z0-9_][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant);
    public static bool IsResourceKey(string key) => KeyPattern.IsMatch(key);
    public static PropertyValueKind Classify(string text)
    {
        var t = text.TrimStart();
        if (t.StartsWith("{Binding ", StringComparison.Ordinal) || t == "{Binding}") return PropertyValueKind.Binding;
        if (t.StartsWith("{StaticResource ", StringComparison.Ordinal)) return PropertyValueKind.StaticResource;
        if (t.StartsWith("{DynamicResource ", StringComparison.Ordinal)) return PropertyValueKind.DynamicResource;
        if (t.StartsWith("{TemplateBinding ", StringComparison.Ordinal)) return PropertyValueKind.TemplateBinding;
        return t.StartsWith('{') && t != "{x:Null}" ? PropertyValueKind.Opaque : PropertyValueKind.Literal;
    }
    public static bool TryBinding(BindingEditorValue binding, out string text, out string error)
    {
        text = ""; error = "Недопустимые параметры Binding.";
        if (!SupportsMarkupUpdateSourceTrigger && binding.UpdateSourceTrigger != "Default")
        { error="UpdateSourceTrigger есть у runtime Binding, но не поддержан XAML markup extension установленной Avalonia. Выражение не изменено."; return false; }
        if (binding.Path.Length > 512 || !PathPattern.IsMatch(binding.Path)
            || !Enum.TryParse<BindingMode>(binding.Mode, false, out var mode) || !Enum.IsDefined(mode)
            || !Enum.TryParse<UpdateSourceTrigger>(binding.UpdateSourceTrigger, false, out var trigger) || !Enum.IsDefined(trigger)
            || binding.ElementName.Length > 128 || binding.ElementName.Length != 0 && !Identifier.IsMatch(binding.ElementName)
            || binding.RelativeSource is not ("" or "Self" or "TemplatedParent")
            || binding.ElementName.Length != 0 && binding.RelativeSource.Length != 0
            || binding.ConverterKey.Length != 0 && !IsResourceKey(binding.ConverterKey)) return false;
        var parts = new List<string>();
        if (binding.Path.Length != 0) parts.Add("Path=" + binding.Path);
        if (binding.Mode != "Default") parts.Add("Mode=" + binding.Mode);
        if (binding.UpdateSourceTrigger != "Default") parts.Add("UpdateSourceTrigger=" + binding.UpdateSourceTrigger);
        if (binding.ElementName.Length != 0) parts.Add("ElementName=" + binding.ElementName);
        if (binding.RelativeSource.Length != 0) parts.Add("RelativeSource={RelativeSource " + binding.RelativeSource + "}");
        foreach (var item in new[] { ("FallbackValue", binding.FallbackValue), ("TargetNullValue", binding.TargetNullValue), ("StringFormat", binding.StringFormat) })
        {
            if (item.Item2 is null) continue;
            if (item.Item1 != "StringFormat" && item.Item2.IndexOfAny(new[]{'{','}'}) >= 0) return false;
            if (!TryQuote(item.Item2, out var quoted)) return false;
            parts.Add(item.Item1 + "=" + quoted);
        }
        if (binding.ConverterKey.Length != 0) parts.Add("Converter={StaticResource " + binding.ConverterKey + "}");
        text = "{Binding" + (parts.Count == 0 ? "" : " " + string.Join(", ", parts)) + "}";
        error = ""; return text.Length <= 4096;
    }
    private static bool TryQuote(string value, out string quoted)
    {
        quoted = "";
        if (value.Length > 1024 || value.Any(c => char.IsControl(c) || c == '\\')) return false;
        if (value == MetadataValueCodec.NullText) { quoted = "{x:Null}"; return true; }
        var q = value.Contains('\'') ? '"' : '\'';
        if (value.Contains(q)) return false;
        quoted = q + value + q; return true;
    }
    public static bool TryParseBinding(string text, out BindingEditorValue binding)
    {
        binding = new();
        if (text.Length > 4096 || text != text.Trim() || !text.StartsWith("{Binding", StringComparison.Ordinal) || !text.EndsWith('}')) return false;
        var body = text[8..^1];
        if (body.Length != 0 && !char.IsWhiteSpace(body[0])) return false;
        var parts = new List<string>(); var start = 0; var depth = 0; char quote = '\0';
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') quote = c;
            else if (c == '{') { if (++depth > 1) return false; }
            else if (c == '}') { if (--depth < 0) return false; }
            else if (c == ',' && depth == 0) { parts.Add(body[start..i].Trim()); start = i + 1; }
        }
        if (quote != '\0' || depth != 0) return false;
        if (body.Trim().Length != 0) parts.Add(body[start..].Trim());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var eq = part.IndexOf('='); var key = eq < 0 ? "Path" : part[..eq].Trim(); var v = eq < 0 ? part : part[(eq + 1)..].Trim();
            if (!seen.Add(key) || eq < 0 && seen.Count != 1) return false;
            string? Scalar()
            {
                if (v == "{x:Null}") return MetadataValueCodec.NullText;
                if (v.Length >= 2 && v[0] is '\'' or '"' && v[^1] == v[0] && !v[1..^1].Contains(v[0])) return v[1..^1];
                return v.Length != 0 && v.IndexOfAny(new[] {'{','}','\'','"','\\'}) < 0 ? v : null;
            }
            switch (key)
            {
                case "Path": binding = binding with { Path = v }; break;
                case "Mode": binding = binding with { Mode = v }; break;
                case "UpdateSourceTrigger": binding = binding with { UpdateSourceTrigger = v }; break;
                case "ElementName": binding = binding with { ElementName = v }; break;
                case "RelativeSource":
                    if (v is not ("{RelativeSource Self}" or "{RelativeSource TemplatedParent}")) return false;
                    binding = binding with { RelativeSource = v[16..^1] }; break;
                case "Converter":
                    if (!TryResource(v, PropertyValueKind.StaticResource, out var converter)) return false;
                    binding = binding with { ConverterKey = converter }; break;
                case "FallbackValue": if (Scalar() is not { } f) return false; binding = binding with { FallbackValue = f }; break;
                case "TargetNullValue": if (Scalar() is not { } n) return false; binding = binding with { TargetNullValue = n }; break;
                case "StringFormat": if (Scalar() is not { } s) return false; binding = binding with { StringFormat = s }; break;
                default: return false;
            }
        }
        return TryBinding(binding, out _, out _);
    }
    public static bool TryResource(string text, PropertyValueKind kind, out string key)
    {
        key = ""; var prefix = "{" + kind + " ";
        if (kind is not (PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource) || !text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith('}')) return false;
        key = text[prefix.Length..^1].Trim(); return IsResourceKey(key);
    }
    public static bool CanTarget(AvaloniaPropertyInfo p) => p.Property is not null && p.Bindable && !p.ReadOnly
        && p.Key is not ("Name" or "DataContext" or "ItemsSource" or "SelectedItem" or "CaretIndex" or "SelectionStart" or "SelectionEnd" or "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan" or "Canvas.Left" or "Canvas.Top" or "DockPanel.Dock")
        && (p.Editor is MetadataEditor.Text or MetadataEditor.Boolean or MetadataEditor.Number or MetadataEditor.Enumeration
            or MetadataEditor.Color or MetadataEditor.Thickness or MetadataEditor.CornerRadius or MetadataEditor.Font
            || p.Key is "Content" or "Header" or "Watermark" or "Command" or "CommandParameter" or "RenderTransform" or "RenderTransformOrigin" or "Clip" or "OpacityMask" or "TextDecorations" or "BoxShadow");
    public static bool Validate(AvaloniaPropertyInfo property, PropertyExpressionValue value)
    {
        if (value.Version != 1 || value.Text is null || !CanTarget(property) || value.Text.Length > 4096) return false;
        try { System.Xml.XmlConvert.VerifyXmlChars(value.Text); } catch(System.Xml.XmlException) { return false; }
        return value.Kind switch
        {
            PropertyValueKind.Binding => TryParseBinding(value.Text, out _),
            PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource => TryResource(value.Text, value.Kind, out _),
            PropertyValueKind.Unset => value.Text.Length == 0,
            PropertyValueKind.Literal => property.PropertyType == typeof(object) && property.Key is "Content" or "Header" or "Watermark" or "CommandParameter"
                ? !value.Text.StartsWith('{') : StandardControlProperties.TryEncode(property, value.Text, out _),
            _ => false
        };
    }
    public static bool TryRead(IEnumerable<DesignPropertyValueModel> values, string key, out PropertyExpressionValue value)
    {
        return TryReadJson(values.FirstOrDefault(p => p.Key == Prefix + key)?.ValueJson,out value);
    }
    public static bool TryRead(IEnumerable<DesignPropertyValueFileModel> values,string key,out PropertyExpressionValue value) =>
        TryReadJson(values.FirstOrDefault(p=>p.Key==Prefix+key)?.ValueJson,out value);
    private static bool TryReadJson(string? json,out PropertyExpressionValue value)
    {
        value=null!;if(json is null)return false;
        try
        {
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)return false;
            var fields=doc.RootElement.EnumerateObject().ToArray();
            if(fields.Length!=3||fields.Select(f=>f.Name).Distinct().Count()!=3||fields.Any(f=>f.Name is not ("Kind" or "Text" or "Version")))return false;
            value=JsonSerializer.Deserialize<PropertyExpressionValue>(json)!;return value is not null&&value.Text is not null;
        }
        catch(JsonException){return false;}
    }
    public static void Write(DesignControlModel target, string key, PropertyExpressionValue value)
    {
        var stored = target.CustomProperties.FirstOrDefault(p => p.Key == Prefix + key);
        if (stored is null) target.CustomProperties.Add(stored = new() { Key = Prefix + key });
        stored.ValueJson = JsonSerializer.Serialize(value);
    }
    public static AvaloniaPropertyInfo? Find(string type, string key) => (StandardControlProperties.PropertyTypeFor(type)
        ?? FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlControlMetadata.Find(type)?.NativeType) is { } native
        ? AvaloniaMetadataCatalog.Get(native).Properties.FirstOrDefault(p => p.Key == key) : null;
    public static void ApplyPreview(Control view, DesignControlModel model, string? sourceType = null) => ApplyPreview(view,model.CustomProperties,sourceType??model.Type);
    public static void ApplyPreview(Control view,FormDesigner.PluginContracts.IDesignControlNode node)
    {
        if(!node.CustomProperties.Keys.Any(k=>k.StartsWith(Prefix,StringComparison.Ordinal)))return;
        ApplyPreview(view,node.CustomProperties.Select(p=>new DesignPropertyValueModel{Key=p.Key,ValueJson=p.Value}),node.TypeKey);
    }
    private static void ApplyPreview(Control view,IEnumerable<DesignPropertyValueModel> values,string type)
    {
        view = StandardControlProperties.NativePropertyTarget(view, type);
        foreach (var entry in values.Where(p => p.Key.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var key = entry.Key[Prefix.Length..]; var p = Find(type, key);
            if (p?.Property is null || !TryRead(values, key, out var value) || !Validate(p,value)) continue;
            // No DataContext, converter, command or resource execution in safe projection.
            if (value.Kind != PropertyValueKind.Literal) view.ClearValue(p.Property);
            else if (p.PropertyType == typeof(object)) view.SetValue(p.Property, value.Text);
            else if (StandardControlProperties.TryParseValue(p,value.Text,out var literal)) view.SetValue(p.Property,literal);
        }
    }
}
