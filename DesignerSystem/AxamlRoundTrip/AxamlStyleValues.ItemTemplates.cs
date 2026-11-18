using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FormDesigner.ViewModels;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public sealed partial class AxamlStyleValues
{
    private static bool CanEditTemplateValue(string type, string key, string text)
    {
        if (key is not ("Text" or "Content" or "IsChecked" or "Foreground" or "Background" or "Width" or "Height"
            or "Margin" or "Padding" or "FontSize" or "Opacity" or "Orientation" or "Spacing" or "HorizontalAlignment" or "VerticalAlignment"))
            return false;
        var property = PropertyExpressions.Find(type,key);
        if (property?.Bindable != true) return false;
        var kind = PropertyExpressions.Classify(text);
        if (kind == PropertyValueKind.Binding)
            return PropertyExpressions.TryParseBinding(text,out var binding) && binding.ConverterKey.Length == 0;
        if (kind is PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource)
            return PropertyExpressions.TryResource(text,kind,out _);
        if (kind != PropertyValueKind.Literal || text.TrimStart().StartsWith('{')) return false;
        if (key == "Content" || property.PropertyType == typeof(string)) return true;
        if (!MetadataValueCodec.TryParse(property.PropertyType,text,out var parsed)) return false;
        if (parsed is double n && (!double.IsFinite(n) || key == "Opacity" && (n < 0 || n > 1)
            || key is "Width" or "Height" or "FontSize" && n < 0)) return false;
        return property.Property!.IsValidValue(parsed);
    }

    private readonly Dictionary<AxamlElementSyntax, XElement> _previewTemplates = new();
    private readonly Dictionary<AxamlElementSyntax, IDataTemplate> _compiledTemplates = new();

    internal void ProjectItemTemplate(AxamlElementSyntax owner, ItemsControl native, string source)
    {
        if (!_previewTemplates.TryGetValue(owner,out var template)) return;
        if (!_compiledTemplates.TryGetValue(owner,out var compiled))
            _compiledTemplates.Add(owner,compiled = MainWindowViewModel.CompileTemplateProjection(new XElement(template),Sheet));
        native.ItemTemplate = compiled;
        var data = owner.Children.Where(n => !n.LocalName.Contains('.')).ToArray();
        if (native.ItemsSource != null || data.Length == 0 || data.Any(n => n.NamespaceUri != XamlNs || n.LocalName != "String" || n.Children.Count > 0 || n.IsSelfClosing)) return;
        native.Items.Clear();
        foreach (var item in data)
            native.Items.Add(System.Net.WebUtility.HtmlDecode(source.Substring(item.ContentStart,item.EndTagSpan.Start-item.ContentStart)));
    }

    private void ReadItemTemplateValues(AxamlSyntaxDocument syntax)
    {
        var slots = PropertyExpressionProjection.Descendants(syntax.Root)
            .Where(n => n.LocalName.EndsWith(".ItemTemplate",StringComparison.Ordinal) && n.Parent != null).ToArray();
        if (slots.Length == 0) return;
        var ownerNames = PropertyExpressionProjection.Descendants(syntax.Root)
            .Where(n => ItemTemplateDesign.SupportsOwner(n.LocalName))
            .ToLookup(n => PropertyExpressionProjection.XamlAttribute(n,"Name") ?? n.GetAttributeValue("Name"));
        foreach (var slot in slots)
        {
            var owner = slot.Parent!;
            var name = PropertyExpressionProjection.XamlAttribute(owner,"Name") ?? owner.GetAttributeValue("Name");
            if (string.IsNullOrEmpty(name) || ownerNames[name].Count() != 1 || !ItemTemplateDesign.SupportsOwner(owner.LocalName) || owner.NamespaceUri != AvaloniaNs ||
                !Is(slot,owner.LocalName + ".ItemTemplate") || owner.Children.Count(c => Is(c,slot.LocalName)) != 1 ||
                owner.FindAttribute("ItemTemplate") != null || slot.Children.Count != 1) continue;
            bool scoped = true;
            for (var scope = owner; scope != null; scope = scope.Parent)
                if (scope.NamespaceUri != AvaloniaNs || scope.LocalName.Contains('.') || scope.LocalName.EndsWith("Template",StringComparison.Ordinal)
                    || PropertyExpressionProjection.XamlAttribute(scope,"DataType") != null ||
                    string.Equals(PropertyExpressionProjection.XamlAttribute(scope,"CompileBindings"),"True",StringComparison.OrdinalIgnoreCase))
                { scoped = false; break; }
            var template = slot.Children[0];
            if (name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_'))) continue;
            if (!scoped || !Is(template,"DataTemplate") || template.Attributes.Count != 0 || template.Children.Count != 1) continue;
            void Visit(AxamlElementSyntax node, string path)
            {
                if (node.NamespaceUri != AvaloniaNs || !ItemTemplateDesign.SupportsChild(node.LocalName) ||
                    PropertyExpressionProjection.XamlAttribute(node,"DataType") != null || PropertyExpressionProjection.XamlAttribute(node,"CompileBindings") != null) return;
                foreach (var attr in node.Attributes)
                {
                    if (!TryReadReversibleScalar(syntax.Text.Substring(attr.ValueSpan.Start,attr.ValueSpan.Length),attr.Quote,out var value) ||
                        !CanEditTemplateValue(node.LocalName,attr.Name,value)) continue;
                    Fields.Add(new("itemtemplate/"+name+"/"+path+"/"+attr.Name, path+" · "+node.LocalName+"."+attr.Name,
                        value,attr.ValueSpan,attr.Quote,null,node.LocalName,attr.Name,name));
                }
                for (var i=0; i<node.Children.Count;i++) Visit(node.Children[i],path+"."+i);
            }
            Visit(template.Children[0],"1");

            // Compile only a newly constructed tree of known native nodes and reviewed values.
            // Keep every other template in its original source without attempting to load it.
            XElement? Project(AxamlElementSyntax node)
            {
                if (node.NamespaceUri != AvaloniaNs || !ItemTemplateDesign.SupportsChild(node.LocalName) ||
                    node.Children.Count > 0 && (node.LocalName is not ("Grid" or "DockPanel" or "WrapPanel" or "StackPanel" or "Border") ||
                    node.LocalName == "Border" && node.Children.Count > 1)) return null;
                var result = new XElement(XName.Get(node.LocalName,AvaloniaNs));
                foreach (var attr in node.Attributes)
                {
                    if (!TryReadReversibleScalar(syntax.Text.Substring(attr.ValueSpan.Start,attr.ValueSpan.Length),attr.Quote,out var value)
                        ) return null;
                    var key = attr.Name;
                    if (PropertyExpressionProjection.XamlAttribute(node,"Name") == value && key.EndsWith(":Name",StringComparison.Ordinal)) key = "Name";
                    if (key is "Name" or "Classes")
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(value, key == "Name" ? @"^[A-Za-z_][A-Za-z0-9_]*$" : @"^[A-Za-z0-9_ -]+$")) return null;
                    }
                    else if (key is "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan")
                    {
                        if (!int.TryParse(value,out var index) || index < (key.EndsWith("Span",StringComparison.Ordinal) ? 1 : 0)) return null;
                    }
                    else if (!CanEditTemplateValue(node.LocalName,key,value)) return null;
                    if (PropertyExpressions.TryResource(value,PropertyExpressions.Classify(value),out var resourceKey) &&
                        !Sheet.Resources.Any(r => r.Key == resourceKey)) return null;
                    result.SetAttributeValue(key,value);
                }
                var cursor = node.ContentStart;
                foreach (var child in node.Children)
                {
                    if (!string.IsNullOrWhiteSpace(syntax.Text[cursor..child.ElementSpan.Start])) return null;
                    if (Project(child) is not {} projected) return null;
                    result.Add(projected);
                    cursor = child.ElementSpan.End;
                }
                if (!node.IsSelfClosing && !string.IsNullOrWhiteSpace(syntax.Text[cursor..node.EndTagSpan.Start])) return null;
                return result;
            }
            var shadowed = false;
            for (var scope = owner; scope != syntax.Root && scope != null; scope = scope.Parent)
                shadowed |= scope.Children.Any(n => n.LocalName.EndsWith(".Resources",StringComparison.Ordinal));
            if (!shadowed && Project(template.Children[0]) is {} projection)
                _previewTemplates[owner] = new XElement(XName.Get("DataTemplate",AvaloniaNs),projection);
            else Notices.Add(name + " · ItemTemplate — Preview ограничен исходной разметкой.");

        }
    }
}
