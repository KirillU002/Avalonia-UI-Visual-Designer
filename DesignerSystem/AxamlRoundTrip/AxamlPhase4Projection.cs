using Avalonia.Controls;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

// Only trusted Avalonia controls and imported literal values; never evaluates user bindings.
public static class AxamlPhase4Projection
{
    public static Control? Create(AxamlSourceReference? reference, DesignControlModel model)
    {
        var type = reference?.Element.LocalName;
        string Value(string key) => AxamlControlMetadata.Find(type!)!.LiteralProperties.Single(p => p.Key == key).Read(model);
        return type switch
        {
            "Expander" => new Expander
            {
                Header = Value("Header"), Content = Value("Content"),
                IsExpanded = bool.Parse(Value("IsExpanded")),
                ExpandDirection = Enum.Parse<ExpandDirection>(Value("ExpandDirection"))
            },
            "ComboBox" => new ComboBox { PlaceholderText = Value("PlaceholderText") },
            "ComboBoxItem" or "String" => new ComboBoxItem { Content = Value("Content") },
            _ => null
        };
    }

    public static void SelectStaticItem(ComboBox combo, AxamlSourceReference reference, DesignControlModel model,
        IEnumerable<DesignControlModel> children, AxamlSourceMap map)
    {
        var element = reference.Element;
        var items = element.Children.SelectMany(c => c.LocalName == "ComboBox.Items" ? (IEnumerable<AxamlElementSyntax>)c.Children : new[] { c })
            .Where(c => !AxamlImportStructureReport.IsPropertyElement(c)).ToArray();
        var selected = int.Parse(AxamlControlMetadata.Find("ComboBox")!.LiteralProperties.Single(p => p.Key == "SelectedIndex").Read(model), CultureInfo.InvariantCulture);
        var projected = children.OrderBy(c => map.ByControlId[c.Id].Element.ElementSpan.Start).ToArray();
        combo.SelectedIndex = AxamlControlMetadata.HasSourceValue(element, "ItemsSource") || AxamlControlMetadata.HasSourceValue(element, "SelectedItem")
            || AxamlControlMetadata.HasSourceValue(element, "SelectedValue") || selected < 0 || selected >= items.Length
            ? -1 : Array.FindIndex(projected, c => map.ByControlId[c.Id].Element == items[selected]);
    }
}
