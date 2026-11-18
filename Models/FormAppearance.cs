using System.Collections.Generic;

namespace FormDesigner.Models;

// Values are authoring text. Native objects are created only by the reviewed codecs.
public sealed record FormStyleSetter(string Property, string Value);
public sealed record FormStyle(string Target, string Selector, List<FormStyleSetter> Setters);
public sealed record FormResource(string Key, string Type, string Value, string Variant = "");
public sealed record FormAppearance
{
    public int Version { get; init; } = 1;
    public List<FormStyle> Styles { get; init; } = new();
    public List<FormResource> Resources { get; init; } = new();
    public Dictionary<string,string> Classes { get; init; } = new();
    public FormAppearance Copy() => this with
    {
        Styles = Styles.ConvertAll(style => style with { Setters = new(style.Setters) }),
        Resources = new(Resources), Classes = new(Classes)
    };
}
