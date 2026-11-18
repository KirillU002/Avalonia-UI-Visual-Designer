using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.DesignerSystem.Infrastructure;
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
    // A column's literal title is safe to project. Bindings, data, templates and custom
    // column classes remain in source. No row objects are constructed during import.
    private static DataGrid CreateDataGrid(AxamlElementSyntax element)
    {
        var grid = new DataGrid { AutoGenerateColumns = false };
        foreach (var slot in element.Children.Where(c => c.LocalName == "DataGrid.Columns" && (c.NamespaceUri.Length != 0 || c.LegacyUnqualified) &&
                     AxamlTypeRecognition.FrameworkType(c.NamespaceUri, "DataGrid") == typeof(DataGrid)))
        {
            foreach (var child in slot.Children)
            {
                if (child.NamespaceUri.Length == 0 && !child.LegacyUnqualified) continue;
                var type = AxamlTypeRecognition.FrameworkType(child.NamespaceUri, child.LocalName);
                DataGridColumn? column = type == typeof(DataGridTextColumn) ? new DataGridTextColumn() :
                    type == typeof(DataGridCheckBoxColumn) ? new DataGridCheckBoxColumn() : null;
                if (column == null) continue;
                var header = child.GetAttributeValue("Header");
                if (header != null && !header.TrimStart().StartsWith('{')) column.Header = header;
                grid.Columns.Add(column);
            }
        }
        return grid;
    }

    public static Control? Create(AxamlSourceReference? reference, DesignControlModel model, bool isDesignMode = false, bool realizeOffscreen = true)
    {
        var type = reference?.Element.LocalName;
        if (type is not null && StandardControlProperties.SupportedTypes.ContainsKey(type))
        {
            var native = StandardControlProperties.Create(new DesignControlNodeAdapter(model), type, commonFilter: key =>
            {
                if (reference is null || key is "Width" or "Height") return false;
                var property = AxamlRoundTripPropertyMap.PropertiesFor(type).FirstOrDefault(p => p.Key == key);
                return property is not null && reference.Capability.CanEditProperty(key)
                    && (property.ResolveExistingAttribute(reference.Element) is not null
                        || reference.SnapshotValues.TryGetValue(key, out var before) && before != property.Read(model));
            });
            if (isDesignMode) native.IsHitTestVisible = false;
            AxamlLayoutProjection.ApplySourceLayout(reference, model, native);
            return native;
        }
        string Value(string key) => AxamlControlMetadata.Find(type!)!.LiteralProperties.Single(p => p.Key == key).Read(model);
        Control? control = type switch
        {
            "Panel" => new Panel(),
            "DataGrid" => CreateDataGrid(reference!.Element),
            "Image" => new Image(),
            "Button" => new Button { Content = reference!.Capability.CanEditProperty("Text") ? model.Text : null },
            "TextBox" => new TextBox(),
            "TextBlock" => new TextBlock(),
            "CheckBox" => new CheckBox(),
            "Border" => new Border(),
            "DropDownButton" or "SplitButton" => BuiltIn.StandardItemsControlFactory.Create(BuiltIn.StandardItemsControlFactory.Snapshot(model)),
            "Expander" => new Expander
            {
                Header = Value("Header"), Content = Value("Content"),
                IsExpanded = bool.Parse(Value("IsExpanded")),
                ExpandDirection = Enum.Parse<ExpandDirection>(Value("ExpandDirection"))
            },
            "ComboBox" => new ComboBox { PlaceholderText = Value("PlaceholderText") },
            "Menu" => new Menu(),
            "MenuItem" => new MenuItem { Header = Value("Header"), IsEnabled = bool.Parse(Value("IsEnabled")) },
            "ContextMenu" => new ContextMenu(),
            "ComboBoxItem" or "String" => new ComboBoxItem { Content = Value("Content") },
            "ItemsControl" => new ItemsControl(),
            "Carousel" => new Carousel(),
            "TabStrip" => new TabStrip(),
            "TabStripItem" => new TabStripItem { Content = Value("Content") },
            "ListBox" => BuiltIn.StandardItemsControlFactory.CreateListBox(Enum.Parse<SelectionMode>(Value("SelectionMode"))),
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
        if (realizeOffscreen && control is ItemsControl items && type is "ItemsControl" or "ListBox" or "TreeView" or "TreeViewItem")
            items.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        // Bound collections stay empty. A small design-time extent keeps their owner selectable,
        // without executing a template, supplying fake data or writing dimensions to the source.
        if (control is ItemsControl && type is "ItemsControl" or "ListBox" or "TreeView" or "TreeViewItem"
            && AxamlControlMetadata.HasSourceValue(reference!.Element, "ItemsSource"))
        {
            control.MinWidth = 24;
            control.MinHeight = 24;
        }
        if (control is not null) AxamlLayoutProjection.ApplySourceLayout(reference, model, control);
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
