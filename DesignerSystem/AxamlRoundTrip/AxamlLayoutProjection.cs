using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>Measures safe controls with Avalonia's layout engine. Never loads user XAML or changes source/model coordinates.</summary>
public static class AxamlLayoutProjection
{
    private sealed class MeasurementRoot : Decorator, Avalonia.LogicalTree.ILogicalRoot, Avalonia.Styling.IStyleHost
    {
        Avalonia.Styling.IStyleHost? Avalonia.Styling.IStyleHost.StylingParent => Application.Current;
    }
    public static IReadOnlyDictionary<string, Rect> Arrange(AxamlRoundTripDocument source,
        IEnumerable<DesignControlModel> controls, double width, double height)
    {
        var models = controls.ToDictionary(c => c.Id);
        var native = new Dictionary<string, Control>();
        Control Build(DesignControlModel model)
        {
            source.SourceMap.TryGet(model.Id, out var reference);
            var type = reference?.Element.LocalName ?? model.Type;
            Control view = type switch
            {
                "Grid" => new Grid(), "StackPanel" => new StackPanel
                {
                    Orientation = model.LayoutOrientation == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical,
                    Spacing = Math.Max(0, model.LayoutSpacing)
                },
                "DockPanel" => new DockPanel { LastChildFill = !bool.TryParse(reference?.Element.GetAttributeValue("LastChildFill"), out var fill) || fill },
                "Canvas" => new Canvas(), "Border" => new Border(),
                "Button" => new Button { Content = model.Text },
                "CheckBox" => new CheckBox { Content = model.Text },
                "TextBox" => new TextBox { Text = model.Text },
                _ => new TextBlock { Text = model.Text }
            };
            native[model.Id] = view;
            if (view is TextBlock text) text.FontSize = model.FontSize;
            if (view is Avalonia.Controls.Primitives.TemplatedControl templated) templated.FontSize = model.FontSize;
            if (reference is null || IsExplicitOrChanged(reference, model, "Width")) view.Width = Math.Max(0, model.Width);
            if (reference is null || IsExplicitOrChanged(reference, model, "Height")) view.Height = Math.Max(0, model.Height);
            try { view.Margin = Thickness.Parse(model.Margin); } catch (FormatException) { }
            if (Enum.TryParse<HorizontalAlignment>(model.HorizontalAlignment, out var horizontal)) view.HorizontalAlignment = horizontal;
            if (Enum.TryParse<VerticalAlignment>(model.VerticalAlignment, out var vertical)) view.VerticalAlignment = vertical;
            // Bindings and styles are not executed. Literal size constraints still participate in native measurement.
            ApplyConstraint(reference, "MinWidth", v => view.MinWidth = v);
            ApplyConstraint(reference, "MinHeight", v => view.MinHeight = v);
            ApplyConstraint(reference, "MaxWidth", v => view.MaxWidth = v);
            ApplyConstraint(reference, "MaxHeight", v => view.MaxHeight = v);
            if (view is Grid grid)
            {
                try { grid.RowDefinitions = RowDefinitions.Parse(model.GridRowDefinitions); } catch (FormatException) { }
                try { grid.ColumnDefinitions = ColumnDefinitions.Parse(model.GridColumnDefinitions); } catch (FormatException) { }
                ApplyDefinitionConstraints(reference, grid);
            }
            if (view is Border border)
            {
                border.Padding = new Thickness(Math.Max(0, model.Padding));
                border.BorderThickness = reference?.Element.FindAttribute("BorderThickness") is null ? new Thickness(0) : new Thickness(Math.Max(0, model.BorderThickness));
            }
            foreach (var child in models.Values.Where(c => c.ParentId == model.Id).OrderBy(c => SourceOrder(c.Id)))
            {
                var childView = Build(child);
                Grid.SetRow(childView, Math.Max(0, child.GridRow)); Grid.SetColumn(childView, Math.Max(0, child.GridColumn));
                Grid.SetRowSpan(childView, Math.Max(1, child.GridRowSpan)); Grid.SetColumnSpan(childView, Math.Max(1, child.GridColumnSpan));
                Canvas.SetLeft(childView, child.X); Canvas.SetTop(childView, child.Y);
                if (source.SourceMap.TryGet(child.Id, out var childReference)
                    && Enum.TryParse<Dock>(childReference.Element.GetAttributeValue("DockPanel.Dock"), out var dock)) DockPanel.SetDock(childView, dock);
                if (view is Panel panel) panel.Children.Add(childView);
                else if (view is Border host) host.Child = childView;
            }
            return view;
        }

        int SourceOrder(string id) => source.SourceMap.TryGet(id, out var r) ? r.Element.ElementSpan.Start : int.MaxValue;
        Panel root = source.SourceMap.CanvasElement is not null ? new Canvas() : new Grid();
        foreach (var model in models.Values.Where(c => string.IsNullOrEmpty(c.ParentId)).OrderBy(c => SourceOrder(c.Id)))
        {
            var child = Build(model);
            if (root is Canvas) { Canvas.SetLeft(child, model.X); Canvas.SetTop(child, model.Y); }
            root.Children.Add(child);
        }
        // A logical styling root lets native controls create templates and measure Auto content,
        // without creating a second window or executing the imported document's styles.
        var measurementRoot = new MeasurementRoot { Child = root };
        try
        {
            measurementRoot.Measure(new Size(width, height));
            measurementRoot.Arrange(new Rect(0, 0, width, height));
        }
        finally { measurementRoot.Child = null; }
        return native.ToDictionary(pair => pair.Key, pair => pair.Value.Bounds);
    }

    private static bool IsExplicitOrChanged(AxamlSourceReference reference, DesignControlModel model, string key)
    {
        var property = AxamlRoundTripPropertyMap.PropertiesFor(model.Type).First(p => p.Key == key);
        return reference.Capability.CanEditProperty(key) && (reference.Element.FindAttribute(key) is not null
            || reference.SnapshotValues.TryGetValue(key, out var before) && before != property.Read(model));
    }

    private static void ApplyConstraint(AxamlSourceReference? reference, string name, Action<double> apply)
    {
        if (double.TryParse(reference?.Element.GetAttributeValue(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= 0) apply(n);
    }

    private static void ApplyDefinitionConstraints(AxamlSourceReference? reference, Grid grid)
    {
        foreach (var axis in new[] { "Row", "Column" })
        {
            var definitions = reference?.Element.Children.FirstOrDefault(e => e.LocalName == "Grid." + axis + "Definitions");
            if (definitions is null) continue;
            for (var i = 0; i < definitions.Children.Count; i++)
            {
                var element = definitions.Children[i];
                if (axis == "Row" && i < grid.RowDefinitions.Count)
                {
                    if (double.TryParse(element.GetAttributeValue("MinHeight"), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 0) grid.RowDefinitions[i].MinHeight = n;
                }
                if (axis == "Column" && i < grid.ColumnDefinitions.Count)
                {
                    if (double.TryParse(element.GetAttributeValue("MinWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 0) grid.ColumnDefinitions[i].MinWidth = n;
                }
            }
        }
    }
}
