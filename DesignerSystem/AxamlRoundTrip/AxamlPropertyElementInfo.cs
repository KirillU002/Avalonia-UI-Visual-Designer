using FormDesigner.DesignerSystem.Metadata;
using System;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>Source metadata only. Nested values are retained syntax, never instantiated.</summary>
public sealed record AxamlPropertyElementInfo(string OwnerType, string PropertyName, AxamlElementSyntax Element,
    AvaloniaPropertyInfo? Metadata, AxamlTextSpan? ValueSpan, AxamlPropertyCapabilityMode Mode, string Reason)
{
    internal static AxamlPropertyElementInfo Inspect(AxamlElementSyntax element, string source)
    {
        var dot = element.LocalName.IndexOf('.');
        var owner = element.LocalName[..dot]; var name = element.LocalName[(dot + 1)..];
        var type = element.Parent is { } parent && AxamlControlMetadata.IsFrameworkNamespace(parent)
            ? AxamlControlMetadata.ResolveNativeType(parent.LocalName) : null;
        var ownerType = type;
        while (ownerType is not null && ownerType.Name != owner) ownerType = ownerType.BaseType;
        var property = type is not null && ownerType is not null && AxamlControlMetadata.IsFrameworkNamespace(element)
            && AvaloniaMetadataCatalog.Get(ownerType).Properties.Any(p => p.Key == name || p.Key == owner + "." + name)
            ? AvaloniaMetadataCatalog.Get(type).Properties.FirstOrDefault(p => p.Key == name || p.Key == owner + "." + name) : null;
        // Preserve attributes, comments, CDATA, child objects and all expressions. Only a
        // single plain scalar text span is a candidate; admission still requires source policy.
        if (property is null) return new(owner, name, element, null, null, AxamlPropertyCapabilityMode.Preserved, "UnknownPropertyElement");
        if (element.IsSelfClosing || element.Children.Count != 0 || element.Attributes.Count != 0)
            return new(owner, name, element, property, null, AxamlPropertyCapabilityMode.Preserved, "ComplexPropertyElement");
        var start = element.ContentStart; var end = element.EndTagSpan.Start;
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        var raw = source[start..end];
        return new(owner, name, element, property, raw.Length > 0 && !raw.Contains('<') ? new(start, end - start) : null,
            AxamlPropertyCapabilityMode.Preserved, "PropertyElementPreserved");
    }
}
