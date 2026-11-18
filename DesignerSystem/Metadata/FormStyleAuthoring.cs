using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FormDesigner.DesignerSystem.Metadata;

public static class FormStyleAuthoring
{
    public static readonly IReadOnlyList<string> Targets = Array.AsReadOnly(new[] { "Button", "TextBox", "TextBlock", "Border", "CheckBox" });
    public static readonly IReadOnlyList<string> ResourceTypes = Array.AsReadOnly(new[] { "SolidColorBrush", "Color", "Thickness", "CornerRadius", "Double", "String", "FontFamily", "FontWeight" });
    private static readonly HashSet<string> SetterKeys = new(StringComparer.Ordinal)
    {
        "Background","Foreground","BorderBrush","BorderThickness","CornerRadius","FontSize","FontWeight","Padding","Margin",
        "Opacity","HorizontalAlignment","VerticalAlignment","MinWidth","MinHeight","FontFamily","LetterSpacing","FontStyle"
    };

    private static bool Identifier(string? value) => value is { Length: > 0 and <= 100 }
        && Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);

    public static Type TargetType(string target) => target switch
    {
        "Button" => typeof(Button), "TextBox" => typeof(TextBox), "TextBlock" => typeof(TextBlock),
        "Border" => typeof(Border), "CheckBox" => typeof(CheckBox),
        _ => throw new InvalidOperationException("Тип стиля не поддерживается.")
    };

    public static IEnumerable<AvaloniaPropertyInfo> Properties(string target) => AvaloniaMetadataCatalog.Get(TargetType(target)).Properties
        .Where(p => SetterKeys.Contains(p.Key) && p.Property is { IsDirect: false } && !p.ReadOnly);

    // A bare name entered with a target type is emitted as Type#Name for the XAML compiler.
    public static string SelectorSuffix(string target, string selector)
    {
        TargetType(target);
        if (selector == target) return "";
        var suffix = selector.StartsWith(target, StringComparison.Ordinal) ? selector[target.Length..] : selector;
        if (suffix.Length > 1 && (suffix[0] == '#' || suffix[0] == '.' && selector.StartsWith(target, StringComparison.Ordinal))
            && Identifier(suffix[1..])) return suffix;
        throw new InvalidOperationException("Используйте тип, Тип.класс, Тип#имя или #имя.");
    }

    public static object ResourceValue(FormResource item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Key) || item.Key.Any(char.IsWhiteSpace) || !PropertyExpressions.IsResourceKey(item.Key) || item.Variant is not ("" or "Default" or "Light" or "Dark"))
            throw new InvalidOperationException("Некорректный ключ или вариант темы.");
        var text = item.Value;
        if (text is null || text.Length > 2048 || text.TrimStart().StartsWith('{'))
            throw new InvalidOperationException("Требуется простое значение ресурса.");
        XmlConvert.VerifyXmlChars(text);
        return item.Type switch
        {
            "String" when text == text.Trim() && !text.Any(char.IsControl) => text,
            "SolidColorBrush" when Color.TryParse(text, out var brushColor) => new SolidColorBrush(brushColor),
            "Color" when Color.TryParse(text, out var color) => color,
            "Thickness" when FiniteComponents(text) => Thickness.Parse(text),
            "CornerRadius" when FiniteComponents(text) => CornerRadius.Parse(text),
            "Double" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) => d,
            "FontWeight" when Enum.TryParse<FontWeight>(text, false, out var weight) && Enum.IsDefined(weight) => weight,
            "FontFamily" when LocalFont(text) => new FontFamily(text),
            _ => throw new InvalidOperationException("Значение не подходит к типу ресурса " + item.Type)
        };
    }

    internal static Type? RuntimeResourceType(string name) => name switch
    {
        "SolidColorBrush" => typeof(SolidColorBrush), "Color" => typeof(Color), "String" => typeof(string),
        "Double" => typeof(double), "Thickness" => typeof(Thickness), "CornerRadius" => typeof(CornerRadius),
        "FontWeight" => typeof(FontWeight), "FontFamily" => typeof(FontFamily), _ => null
    };
    private static bool FiniteComponents(string text) => text.Split(',', StringSplitOptions.TrimEntries)
        .All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n));

    private static bool LocalFont(string text) => text.Length is > 0 and <= 100 && text.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-');

    public static void Validate(FormAppearance sheet)
    {
        if (sheet.Version != 1 || sheet.Styles is null || sheet.Resources is null || sheet.Classes is null)
            throw new InvalidOperationException("Неизвестная версия или структура оформления.");
        var identities = new HashSet<(string,string)>();
        foreach (var item in sheet.Resources)
        {
            ResourceValue(item);
            if (!identities.Add((item.Key,item.Variant))) throw new InvalidOperationException("Ключ уже существует в этой области темы.");
        }
        foreach (var membership in sheet.Classes)
            if (string.IsNullOrWhiteSpace(membership.Key) || !Identifier(membership.Value)) throw new InvalidOperationException("Некорректный класс элемента.");
        foreach (var style in sheet.Styles)
        {
            if (style is null || style.Selector is null || style.Setters is null) throw new InvalidOperationException("Повреждён стиль.");
            SelectorSuffix(style.Target,style.Selector);
            foreach (var setter in style.Setters)
                CheckSetter(sheet,style.Target,setter);
        }
    }

    public static AvaloniaPropertyInfo CheckSetter(FormAppearance sheet,string target,FormStyleSetter setter)
    {
        var property = Properties(target).FirstOrDefault(p => p.Key == setter?.Property)
            ?? throw new InvalidOperationException("Свойство неприменимо к " + target);
        var value = setter!.Value;
        if (value is null || value.Length > 2048 || value == MetadataValueCodec.NullText)
            throw new InvalidOperationException("Недопустимое значение Setter. Для сброса удалите Setter.");
        XmlConvert.VerifyXmlChars(value);
        var kind = PropertyExpressions.Classify(value);
        if (kind is PropertyValueKind.DynamicResource or PropertyValueKind.StaticResource)
        {
            if (!PropertyExpressions.TryResource(value,kind,out var key)) throw new InvalidOperationException("Некорректная ссылка.");
            var matches = sheet.Resources.Where(r => r.Key == key).ToArray();
            if (matches.Length == 0 || kind == PropertyValueKind.StaticResource && !matches.Any(r => r.Variant == "")
                || matches.Any(r => !property.PropertyType.IsInstanceOfType(ResourceValue(r)) || !property.Property!.IsValidValue(ResourceValue(r))))
                throw new InvalidOperationException("Требуется объявленный ресурс совместимого типа в области формы.");
        }
        else if (kind != PropertyValueKind.Literal || value.TrimStart().StartsWith('{')
            || property.PropertyType == typeof(FontFamily) && !LocalFont(value)
            || !StandardControlProperties.TryEncode(property,value,out _))
            throw new InvalidOperationException("Недопустимое значение " + setter.Property);
        return property;
    }

    public static string ResourceXml(FormResource r)
    {
        // Element names come exclusively from the reviewed type set above.
        ResourceValue(r);
        var key = new XAttribute("Key",r.Key).ToString().Replace("Key=", "x:Key=", StringComparison.Ordinal);
        if (r.Type == "SolidColorBrush") return "<SolidColorBrush " + key + " " + new XAttribute("Color",r.Value) + " />";
        var name = r.Type is "Double" or "String" ? "x:" + r.Type : r.Type;
        return "<" + name + " " + key + ">" + new XText(r.Value) + "</" + name + ">";
    }

    public static string StyleXml(FormStyle s) => new XElement("Style",new XAttribute("Selector",s.Selector.StartsWith('#') ? s.Target+s.Selector : s.Selector),
        s.Setters.Select(v => new XElement("Setter",new XAttribute("Property",v.Property),
            new XAttribute("Value",v.Value)))).ToString(SaveOptions.DisableFormatting);
}
