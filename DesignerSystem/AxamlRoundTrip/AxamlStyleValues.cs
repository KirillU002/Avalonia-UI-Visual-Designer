using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public sealed record StyleSourceValue(string Id, string Label, string Value, AxamlTextSpan Span, char Quote,
    FormResource? Resource, string? Target, string? Property);

// Only direct Window declarations participate. Paths are local to the fixed Styles/Resources
// slot, independent of offsets and of insertions/reorders in the visual tree.
public sealed class AxamlStyleValues
{
    public FormAppearance Sheet { get; } = new();
    public List<StyleSourceValue> Fields { get; } = new();
    public List<string> Notices { get; } = new();

    private const string AvaloniaNs = "https://github.com/avaloniaui";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static bool Is(AxamlElementSyntax node,string name) => node.NamespaceUri == AvaloniaNs && node.LocalName == name;
    // Source history stores logical scalar values. A field is writable only when the
    // same encoder can reproduce its original XML lexeme, including after an ACK.
    private static string Encode(string value, char quote)
    {
        var escaped = value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
        return quote switch { '\'' => escaped.Replace("'","&apos;"), '"' => escaped.Replace("\"","&quot;"), _ => escaped };
    }

    private static bool TryReadReversibleScalar(string raw, char quote, out string value)
    {
        value = raw.Replace("&lt;","<").Replace("&gt;",">").Replace("&quot;","\"")
            .Replace("&apos;","'").Replace("&amp;","&");
        return !raw.AsSpan().ContainsAny('\r','\n','\t') && Encode(value,quote) == raw;
    }

    public static AxamlStyleValues Read(AxamlSyntaxDocument syntax)
    {
        var result = new AxamlStyleValues();
        var root = syntax.Root;
        if (!Is(root,"Window")) return result;
        foreach (var group in root.Children.Where(n => Is(n,"Window.Resources") || Is(n,"Window.Styles")).GroupBy(n => n.LocalName))
        {
            if (group.Count() != 1) result.Notices.Add(group.Key + " — неоднозначная область.");
        }
        var resources = root.Children.Where(n => Is(n,"Window.Resources")).ToArray();
        if (resources.Length == 1)
        {
            var entries = resources[0].Children;
            if (entries.Count == 1 && Is(entries[0],"ResourceDictionary")) entries = entries[0].Children;
            foreach (var node in entries)
            {
                if (Is(node,"ResourceDictionary.ThemeDictionaries"))
                {
                    foreach (var variant in node.Children)
                    {
                        var name = PropertyExpressionProjection.XamlAttribute(variant,"Key");
                        if (Is(variant,"ResourceDictionary") && name is ("Default" or "Light" or "Dark") && result.UniqueKeys(node).Contains(name))
                            foreach (var item in variant.Children) result.ReadResource(syntax,item,name);
                    }
                }
                else result.ReadResource(syntax,node,"");
            }
        }
        // An opaque sibling with the same key also makes lookup ambiguous.
        var styles = root.Children.Where(n => Is(n,"Window.Styles")).ToArray();
        if (styles.Length == 1)
        {
            for (var i = 0; i < styles[0].Children.Count; i++)
                result.ReadStyle(syntax,styles[0].Children[i],i);
        }
        var ambiguous = result.Fields.GroupBy(f => f.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        result.Fields.RemoveAll(f => ambiguous.Contains(f.Id));
        if (ambiguous.Count != 0) result.Notices.Add("Повторяющиеся ключи доступны только для чтения.");
        return result;
    }

    private readonly Dictionary<AxamlElementSyntax,HashSet<string>> _uniqueKeys = new();
    private HashSet<string> UniqueKeys(AxamlElementSyntax parent)
    {
        if (_uniqueKeys.TryGetValue(parent,out var keys)) return keys;
        keys = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in parent.Children)
            if (PropertyExpressionProjection.XamlAttribute(child,"Key") is {} key && !keys.Add(key)) duplicates.Add(key);
        keys.ExceptWith(duplicates);
        _uniqueKeys.Add(parent,keys);
        return keys;
    }

    private void ReadResource(AxamlSyntaxDocument syntax,AxamlElementSyntax node,string variant)
    {
        var key = PropertyExpressionProjection.XamlAttribute(node,"Key");
        if (key is not null && !UniqueKeys(node.Parent!).Contains(key))
        {
            Notices.Add(key + " — неоднозначный ключ, только чтение.");
            return;
        }
        var type = node.LocalName;
        var scope = variant.Length == 0 ? "Window" : variant;
        var id = "resource/" + variant + "/" + key;
        if (node.Children.Count != 0 || key is null || !FormStyleAuthoring.ResourceTypes.Contains(type)
            || node.NamespaceUri != (type is "String" or "Double" ? XamlNs : AvaloniaNs)
            || node.Attributes.Count(a => a.Name.EndsWith(":Key",StringComparison.Ordinal)) != 1
            || node.Attributes.Any(a => a.Name != "xmlns" && !a.Name.StartsWith("xmlns:",StringComparison.Ordinal)
                && !a.Name.EndsWith(":Key",StringComparison.Ordinal) && !(type == "SolidColorBrush" && a.Name == "Color")))
        {
            Notices.Add(scope + " · " + node.LocalName + " " + key + " — только чтение");
            return;
        }
        var attr = type == "SolidColorBrush" ? node.FindAttribute("Color") : null;
        var span = attr?.ValueSpan ?? new AxamlTextSpan(node.ContentStart,node.EndTagSpan.Start-node.ContentStart);
        if (attr is null && node.IsSelfClosing) return;
        var raw = syntax.Text.Substring(span.Start,span.Length);
        if (raw.Contains('<')) return;
        if (!TryReadReversibleScalar(raw,attr?.Quote ?? '\0',out var value))
        {
            Notices.Add(scope+" · "+key+" — исходная запись XML требует точного сохранения, только чтение.");
            return;
        }
        var item = new FormResource(key,type,value,variant);
        try { FormStyleAuthoring.ResourceValue(item); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or OverflowException) { Notices.Add(scope+" · "+key+" — сложное значение"); return; }
        Sheet.Resources.Add(item);
        Fields.Add(new(id,scope+" · "+key+" ("+type+")",item.Value,span,attr?.Quote ?? '\0',item,null,null));
    }

    private static bool UsesAvaloniaSelectorNamespace(AxamlElementSyntax node)
    {
        for (var scope = node; scope != null; scope = scope.Parent)
            if (scope.GetAttributeValue("xmlns") is {} uri)
                return uri == AvaloniaNs;
        return false;
    }

    private void ReadStyle(AxamlSyntaxDocument syntax,AxamlElementSyntax node,int index)
    {
        var selector = node.GetAttributeValue("Selector") ?? "";
        var target = FormStyleAuthoring.Targets.FirstOrDefault(t => selector == t || selector.StartsWith(t+".",StringComparison.Ordinal) || selector.StartsWith(t+"#",StringComparison.Ordinal));
        if (selector.StartsWith('#'))
        {
            var matches = PropertyExpressionProjection.Descendants(syntax.Root).Where(n => n.NamespaceUri == AvaloniaNs &&
                (PropertyExpressionProjection.XamlAttribute(n,"Name") ?? n.GetAttributeValue("Name")) == selector[1..]).Take(2).ToArray();
            if (matches.Length == 1 && FormStyleAuthoring.Targets.Contains(matches[0].LocalName)) target = matches[0].LocalName;
        }
        var pending = new List<StyleSourceValue>();
        try
        {
            if (!Is(node,"Style") || node.Attributes.Count != 1 || target is null || !UsesAvaloniaSelectorNamespace(node)) throw new InvalidOperationException();
            FormStyleAuthoring.SelectorSuffix(target,selector);
            var style = new FormStyle(target,selector,new());
            foreach (var child in node.Children)
            {
                if (!Is(child,"Setter") || child.Children.Count != 0 || child.Attributes.Count != 2 ||
                    child.FindAttribute("Property") is not {} p || child.FindAttribute("Value") is not {} attr) throw new InvalidOperationException();
                var key = p.Value.StartsWith(target+".",StringComparison.Ordinal) ? p.Value[(target.Length+1)..] : p.Value;
                if (!TryReadReversibleScalar(attr.Value,attr.Quote,out var value))
                {
                    Notices.Add(selector+" · "+key+" — исходная запись XML требует точного сохранения, только чтение.");
                    return;
                }
                var setter = new FormStyleSetter(key,value);
                FormStyleAuthoring.CheckSetter(Sheet,target,setter);
                pending.Add(new("setter/"+index+"/"+selector+"/"+style.Setters.Count+"/"+key,selector+" · "+key,setter.Value,attr.ValueSpan,attr.Quote,null,target,key));
                style.Setters.Add(setter);
            }
            Sheet.Styles.Add(style);
            Fields.AddRange(pending);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or OverflowException)
        { Notices.Add(node.Name+" "+selector+" — селектор или содержимое доступны только для чтения."); }
    }

    public static List<AxamlTextEdit> Edits(AxamlRoundTripDocument source, DesignerDocumentFileModel document)
    {
        var edits = new List<AxamlTextEdit>();
        var values = document.StyleSourceValues;
        if (values is null) return edits;
        var catalog = source.StyleCatalog;
        foreach (var field in catalog.Fields)
        {
            if (!values.TryGetValue(field.Id,out var value) || value == field.Value) continue;
            if (field.Resource is {} r)
            {
                var updated = r with { Value = value };
                FormStyleAuthoring.ResourceValue(updated);
                var candidate = catalog.Sheet.Copy();
                candidate.Resources[candidate.Resources.IndexOf(r)] = updated;
                foreach (var style in candidate.Styles)
                    foreach (var setter in style.Setters)
                        if (PropertyExpressions.TryResource(setter.Value,PropertyExpressions.Classify(setter.Value),out var key) && key == r.Key)
                            FormStyleAuthoring.CheckSetter(candidate,style.Target,setter);
            }
            else FormStyleAuthoring.CheckSetter(catalog.Sheet,field.Target!,new(field.Property!,value));
            var escaped = Encode(value,field.Quote);
            if (!TryReadReversibleScalar(escaped,field.Quote,out var decoded) || decoded != value)
                throw new InvalidOperationException("Изменение невозможно точно восстановить после Undo.");
            edits.Add(new(field.Span.Start,field.Span.Length,escaped));
        }
        if (values.Keys.Except(catalog.Fields.Select(f => f.Id)).Any() || catalog.Fields.Select(f=>f.Id).Distinct().Count()!=catalog.Fields.Count)
            throw new InvalidOperationException("Неоднозначное соответствие исходному стилю или ресурсу.");
        return edits;
    }
}
