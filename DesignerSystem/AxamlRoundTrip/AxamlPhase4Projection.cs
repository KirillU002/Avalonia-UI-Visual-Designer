using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
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
        Control? control = type switch
        {
            "Expander" => new Expander
            {
                Header = Value("Header"), Content = Value("Content"),
                IsExpanded = bool.Parse(Value("IsExpanded")),
                ExpandDirection = Enum.Parse<ExpandDirection>(Value("ExpandDirection"))
            },
            "ComboBox" => new ComboBox { PlaceholderText = Value("PlaceholderText") },
            "ComboBoxItem" or "String" => new ComboBoxItem { Content = Value("Content") },
            "ItemsControl" => new ItemsControl(),
            "ListBox" => new ListBox { SelectionMode = Enum.Parse<SelectionMode>(Value("SelectionMode")) },
            "ListBoxItem" => new ListBoxItem { Content = Value("Content") },
            "TreeView" => new TreeView { SelectionMode = Enum.Parse<SelectionMode>(Value("SelectionMode")) },
            "TreeViewItem" => new TreeViewItem { Header = Value("Header"), IsExpanded = bool.Parse(Value("IsExpanded")) },
            "ProgressBar" => new ProgressBar
            {
                Minimum = double.Parse(Value("Minimum"), CultureInfo.InvariantCulture),
                Maximum = double.Parse(Value("Maximum"), CultureInfo.InvariantCulture),
                Value = double.Parse(Value("Value"), CultureInfo.InvariantCulture),
                IsIndeterminate = bool.Parse(Value("IsIndeterminate")),
                Orientation = Enum.Parse<Orientation>(Value("Orientation"))
            },
            _ => null
        };
        var phase5 = type is "ItemsControl" or "ListBox" or "ListBoxItem" or "TreeView" or "TreeViewItem" or "ProgressBar";
        if (phase5 && control is not null) control.DataContext = null;
        // The design projection realizes static items off-screen as well. User ItemsPanel templates
        // are preserved, not executed; Avalonia's StackPanel still owns item layout (no flattening).
        if (control is ItemsControl items && type is "ItemsControl" or "ListBox" or "TreeView" or "TreeViewItem")
            items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        // Bound collections stay empty. A small design-time extent keeps their owner selectable,
        // without executing a template, supplying fake data or writing dimensions to the source.
        if (control is ItemsControl && type is "ItemsControl" or "ListBox" or "TreeView" or "TreeViewItem"
            && AxamlControlMetadata.HasSourceValue(reference!.Element, "ItemsSource"))
        {
            control.MinWidth = 24;
            control.MinHeight = 24;
        }
        if (phase5 && control is not null) AxamlLayoutProjection.ApplySourceLayout(reference, model, control);
        return control;
    }

    public static void SelectStaticItem(SelectingItemsControl combo, AxamlSourceReference reference, DesignControlModel model,
        IEnumerable<DesignControlModel> children, AxamlSourceMap map)
    {
        var element = reference.Element;
        var metadata = AxamlControlMetadata.Find(element.LocalName)!;
        var items = element.Children.SelectMany(c => metadata.IsChildProperty(c) ? (IEnumerable<AxamlElementSyntax>)c.Children : new[] { c })
            .Where(c => !AxamlImportStructureReport.IsPropertyElement(c)).ToArray();
        var selected = int.Parse(metadata.LiteralProperties.Single(p => p.Key == "SelectedIndex").Read(model), CultureInfo.InvariantCulture);
        var projected = children.OrderBy(c => map.ByControlId[c.Id].Element.ElementSpan.Start).ToArray();
        combo.SelectedIndex = AxamlControlMetadata.HasSourceValue(element, "ItemsSource") || AxamlControlMetadata.HasSourceValue(element, "SelectedItem")
            || AxamlControlMetadata.HasSourceValue(element, "SelectedValue") || selected < 0 || selected >= items.Length
            ? -1 : Array.FindIndex(projected, c => map.ByControlId[c.Id].Element == items[selected]);
    }
}
