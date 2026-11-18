using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Linq;
using System.Collections.Generic;

namespace FormDesigner.DesignerSystem.Metadata;

// Project only the checked declarations. Never feed imported AXAML to the runtime loader.
internal static class FormStyleProjection
{
    internal static void Apply(Control host, FormAppearance? sheet, string id,string name,string type,AxamlSourceReference? source = null)
    {
        if (sheet == null || sheet.Styles.Count == 0 || !FormStyleAuthoring.Targets.Contains(type)) return;
        var view = StandardControlProperties.NativePropertyTarget(host,type);
        if (view.GetType() != FormStyleAuthoring.TargetType(type)) return;
        var classes = source?.Element.GetAttributeValue("Classes") ?? sheet.Classes.GetValueOrDefault(id) ?? "";
        var classNames = classes.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        foreach (var definition in sheet.Styles.Where(s => s.Target == type))
        {
            var suffix = FormStyleAuthoring.SelectorSuffix(type,definition.Selector);
            if (suffix.StartsWith('.') && !classNames.Contains(suffix[1..]) || suffix.StartsWith('#') && name != suffix[1..]) continue;
            if (suffix.StartsWith('#') && string.IsNullOrEmpty(view.Name)) view.Name = name;
            // A selectorless style on the native view matches that host alone.
            // Class selectors need an activator, but adding a class condition to
            // plain type/name styles would incorrectly raise their Avalonia priority.
            var style = new Style();
            if (suffix.StartsWith('.'))
            {
                var marker = "fd_scope_" + id.Replace("-", "", StringComparison.Ordinal);
                if (!view.Classes.Contains(marker)) view.Classes.Add(marker);
                if (!view.Classes.Contains(suffix[1..])) view.Classes.Add(suffix[1..]);
                style = new Style(x => x.OfType(view.GetType()).Class(marker).Class(suffix[1..]));
            }
            foreach (var setter in definition.Setters)
            {
                var property = FormStyleAuthoring.Properties(type).First(p => p.Key == setter.Property);
                var kind = PropertyExpressions.Classify(setter.Value);
                object? value;
                if (PropertyExpressions.TryResource(setter.Value,kind,out var key))
                {
                    // A subordinate dictionary may shadow this key. Leave that source value unresolved.
                    if (HasNestedScope(source)) continue;
                    var matches = sheet.Resources.Where(r => r.Key == key).ToArray();
                    if (kind == PropertyValueKind.StaticResource)
                        value = FormStyleAuthoring.ResourceValue(matches.First(r => r.Variant.Length == 0));
                    else
                    {
                        foreach (var r in matches)
                        {
                            if (r.Variant.Length == 0) style.Resources[key] = FormStyleAuthoring.ResourceValue(r);
                            else
                            {
                                var variant = r.Variant switch { "Light" => ThemeVariant.Light,"Dark" => ThemeVariant.Dark,_ => ThemeVariant.Default };
                                if (!style.Resources.ThemeDictionaries.TryGetValue(variant,out var resources))
                                    style.Resources.ThemeDictionaries[variant] = resources = new ResourceDictionary();
                                ((ResourceDictionary)resources)[key] = FormStyleAuthoring.ResourceValue(r);
                            }
                        }
                        value = new DynamicResourceExtension(key);
                    }
                }
                else if (!StandardControlProperties.TryParseValue(property,setter.Value,out value)) continue;
                if (source != null && !AxamlControlMetadata.HasSourceValue(source.Element,setter.Property))
                    view.ClearValue(property.Property!);
                style.Setters.Add(new Setter(property.Property!,value));
            }
            view.Styles.Add(style);
        }
    }
    private static bool HasNestedScope(AxamlSourceReference? source)
    {
        for (var node = source?.Element; node?.Parent != null; node = node.Parent)
            if (node.Children.Any(c => c.LocalName.EndsWith(".Resources",StringComparison.Ordinal))) return true;
        return false;
    }
}
