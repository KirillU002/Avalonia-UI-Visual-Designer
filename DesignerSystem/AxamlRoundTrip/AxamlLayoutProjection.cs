using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

/// <summary>Measures safe controls with Avalonia's layout engine. Never loads user XAML or changes source/model coordinates.</summary>
public static partial class AxamlLayoutProjection
{
    private sealed class MeasurementRoot : Decorator, Avalonia.LogicalTree.ILogicalRoot, Avalonia.Styling.IStyleHost
    {
        Avalonia.Styling.IStyleHost? Avalonia.Styling.IStyleHost.StylingParent => Application.Current;
    }
    // Shared native tree for safe imported Preview and off-screen measurement.
    // No imported bindings, templates or user constructors are evaluated.
    public static (Control Root, IReadOnlyDictionary<string, Control> Controls) CreateNativeTree(AxamlRoundTripDocument source,
        IEnumerable<DesignControlModel> controls)
    {
        var tree = BuildNativeTree(source, controls, forPreview: true);
        return (tree.Root, tree.Native);
    }

    private static (Panel Root, Dictionary<string, Control> Native, Dictionary<string, DesignControlModel> Models,
        Dictionary<string, string> SelectedTabs) BuildNativeTree(AxamlRoundTripDocument source, IEnumerable<DesignControlModel> controls, bool forPreview)
    {
        var models = controls.ToDictionary(c => c.Id);
        var native = new Dictionary<string, Control>();
        var byParent = models.Values.ToLookup(c => c.ParentId ?? "");
        var selectedTabs = new Dictionary<string, string>();
        Control Build(DesignControlModel model)
        {
            source.SourceMap.TryGet(model.Id, out var reference);
            var type = reference?.Element.LocalName ?? model.Type;
            string Literal(string key) => AxamlControlMetadata.Find(type)!.LiteralProperties.First(p => p.Key == key).Read(model);
            var projected = AxamlPhase4Projection.Create(reference, model, realizeOffscreen: !forPreview);
            Control view = projected ?? type switch
            {
                "Grid" => new Grid(), "StackPanel" => new StackPanel
                {
                    Orientation = model.LayoutOrientation == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical,
                    Spacing = Math.Max(0, model.LayoutSpacing)
                },
                "DockPanel" => new DockPanel { LastChildFill = !bool.TryParse(reference?.Element.GetAttributeValue("LastChildFill"), out var fill) || fill },
                "WrapPanel" => new WrapPanel { Orientation = Enum.Parse<Orientation>(Literal("Orientation")) },
                "ScrollViewer" => new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Enum.Parse<ScrollBarVisibility>(Literal("HorizontalScrollBarVisibility")),
                    VerticalScrollBarVisibility = Enum.Parse<ScrollBarVisibility>(Literal("VerticalScrollBarVisibility"))
                },
                "TabControl" => new TabControl(),
                "TabItem" => new TabItem { Header = Literal("Header") },
                "Canvas" => new Canvas(), "Border" => new Border(),
                "Button" => new Button { Content = model.Text },
                "CheckBox" => new CheckBox { Content = model.Text },
                "TextBox" => new TextBox { Text = model.Text },
                _ => new TextBlock { Text = model.Text }
            };
            native[model.Id] = view;
            if (view is Grid grid)
            {
                try { grid.RowDefinitions = RowDefinitions.Parse(model.GridRowDefinitions); } catch (FormatException) { }
                try { grid.ColumnDefinitions = ColumnDefinitions.Parse(model.GridColumnDefinitions); } catch (FormatException) { }
                ApplyDefinitionConstraints(reference, grid);
            }
            if (projected is null) ApplySourceLayout(reference, model, view);
            var children = byParent[model.Id].OrderBy(c => SourceOrder(c.Id)).ToArray();
            foreach (var child in children)
            {
                var childView = Build(child);
                Grid.SetRow(childView, Math.Max(0, child.GridRow)); Grid.SetColumn(childView, Math.Max(0, child.GridColumn));
                Grid.SetRowSpan(childView, Math.Max(1, child.GridRowSpan)); Grid.SetColumnSpan(childView, Math.Max(1, child.GridColumnSpan));
                Canvas.SetLeft(childView, child.X); Canvas.SetTop(childView, child.Y);
                if (source.SourceMap.TryGet(child.Id, out var childReference)
                    && childReference.Capability.CanEditProperty("DockPanel.Dock")) DockPanel.SetDock(childView, LayoutPlacementProperties.GetDock(child));
                if (view is ItemsControl items) items.Items.Add(childView);
                else if (view is Panel panel) panel.Children.Add(childView);
                else if (view is Viewbox viewbox) viewbox.Child = childView;
                else if (view is Decorator host) host.Child = childView;
                else if (view is ContentControl content) content.Content = childView;
            }
            if (!forPreview && view is TabItem tabItem)
            {
                var child = tabItem.Content as Control;
                tabItem.Content = null;
                var content = new Border { Child = child };
                tabItem.Content = content;
                // The model TabItem owns its page, not the header generated by TabControl's template.
                native[model.Id] = content;
            }
            if (view is TabControl tabs)
            {
                var index = int.Parse(Literal("SelectedIndex"), CultureInfo.InvariantCulture);
                tabs.SelectedIndex = children.Length == 0 || index < 0 ? -1 : Math.Min(index, children.Length - 1);
                selectedTabs[model.Id] = tabs.SelectedIndex < 0 ? "" : children[tabs.SelectedIndex].Id;
            }
            if (view is ComboBox or ListBox or Carousel or TabStrip && view is SelectingItemsControl combo)
                AxamlPhase4Projection.SelectStaticItem(combo, reference!, model, children, source.SourceMap);
            FormDesigner.DesignerSystem.Metadata.PropertyExpressions.ApplyPreview(view,model,type);
            if (reference != null && view is ItemsControl templateOwner) source.StyleCatalog.ProjectItemTemplate(reference.Element,templateOwner,source.OriginalText);
            if (reference != null) FormDesigner.DesignerSystem.Metadata.FormStyleProjection.Apply(view,source.StyleCatalog.Sheet,model.Id,model.Name,type,reference);
            return view;
        }

        int SourceOrder(string id) => source.SourceMap.TryGet(id, out var r) ? r.Element.ElementSpan.Start : int.MaxValue;
        Panel root = source.SourceMap.CanvasElement is not null ? new Canvas() : new Grid();
        if (source.SourceMap.CanvasElement is { } canvas) AxamlControlMetadata.ApplyRootLiterals(canvas, root);
        if (!root.IsSet(Panel.BackgroundProperty)) AxamlControlMetadata.ApplyRootLiterals(source.Syntax.Root, root, windowRoot: true);
        foreach (var model in byParent[""].OrderBy(c => SourceOrder(c.Id)))
        {
            var child = Build(model);
            if (root is Canvas) { Canvas.SetLeft(child, model.X); Canvas.SetTop(child, model.Y); }
            root.Children.Add(child);
        }
        return (root, native, models, selectedTabs);
    }

    public static IReadOnlyDictionary<string, Rect> Arrange(AxamlRoundTripDocument source,
        IEnumerable<DesignControlModel> controls, double width, double height)
    {
        var (root, native, models, selectedTabs) = BuildNativeTree(source, controls, forPreview: false);
        // A logical styling root lets native controls create templates and measure Auto content,
        // without creating a second window or executing the imported document's styles.
        var measurementRoot = new MeasurementRoot { Child = root };
        // A Decorator has no Window theme. Carry the existing host theme's inherited
        // typography into off-screen measure, rather than forcing per-control literals.
        if (Application.Current?.TryGetResource("ControlContentThemeFontSize", null, out var fontSize) == true && fontSize is double size)
            measurementRoot.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, size);
        if (Application.Current?.TryGetResource("ContentControlThemeFontFamily", null, out var fontFamily) == true && fontFamily is Avalonia.Media.FontFamily family)
            measurementRoot.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, family);
        try
        {
            // ScrollContentPresenter normally connects to its owner on visual-root attachment.
            // This off-screen tree has no such event; initialize the native content/scroll axes.
            foreach (var content in native.Values.OfType<ContentControl>())
            {
                content.ApplyTemplate();
                foreach (var presenter in content.GetVisualDescendants().OfType<ContentPresenter>().ToArray())
                {
                    PreparePresenter(presenter);
                    presenter.UpdateChild();
                }
            }
            foreach (var items in native.Values.OfType<ItemsControl>().Where(i => i is not (TabControl or ComboBox)))
                PrepareOffscreen(items);
            measurementRoot.Measure(new Size(width, height));
            measurementRoot.Arrange(new Rect(0, 0, width, height));
            source.MeasuredGridTracks.Clear();
            foreach(var pair in native.Where(p=>p.Value is Grid))
            {
                var measured=(Grid)pair.Value;
                source.MeasuredGridTracks[pair.Key]=(measured.RowDefinitions.Select(r=>r.ActualHeight).ToArray(),measured.ColumnDefinitions.Select(c=>c.ActualWidth).ToArray());
            }
            return native.ToDictionary(pair => pair.Key, pair =>
            {
                for (var child = models[pair.Key]; !string.IsNullOrEmpty(child.ParentId); child = models[child.ParentId])
                {
                    if (selectedTabs.TryGetValue(child.ParentId, out var selected) && selected != child.Id) return default(Rect);
                    if (native[child.ParentId] is ComboBox or Expander { IsExpanded: false } or TreeViewItem { IsExpanded: false }) return default(Rect);
                }
                var parent = models[pair.Key].ParentId;
                var relativeTo = !string.IsNullOrEmpty(parent) && native.TryGetValue(parent, out var parentView) ? parentView : root;
                var point = pair.Value.TranslatePoint(default, relativeTo);
                return point.HasValue ? new Rect(point.Value, pair.Value.Bounds.Size) : default;
            });
        }
        finally { measurementRoot.Child = null; }
    }

    public static void PrepareOffscreen(Control root)
    {
        var pending = new Queue<Control>();
        var visited = new HashSet<Control>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var control))
        {
            if (!visited.Add(control)) continue;
            if (control is TemplatedControl templated) templated.ApplyTemplate();
            if (control is ItemsPresenter items) items.ApplyTemplate();
            if (control is ContentPresenter presenter)
            {
                PreparePresenter(presenter);
                presenter.UpdateChild();
            }
            foreach (var child in control.GetVisualChildren().OfType<Control>()) pending.Enqueue(child);
        }
    }

    private static void PreparePresenter(ContentPresenter presenter)
    {
        if (presenter is not ScrollContentPresenter scroll || scroll.TemplatedParent is not ScrollViewer owner) return;
        if (!scroll.IsSet(ContentPresenter.ContentProperty)) scroll.Content = owner.Content;
        if (!scroll.IsSet(ScrollContentPresenter.CanHorizontallyScrollProperty))
            scroll.CanHorizontallyScroll = owner.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
        if (!scroll.IsSet(ScrollContentPresenter.CanVerticallyScrollProperty))
            scroll.CanVerticallyScroll = owner.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
    }

    public static void ApplySourceLayout(AxamlSourceReference? reference, DesignControlModel model, Control view)
    {
        if (reference is null || IsExplicitOrChanged(reference, model, "Width")) view.Width = Math.Max(0, model.Width);
        if (reference is null || IsExplicitOrChanged(reference, model, "Height")) view.Height = Math.Max(0, model.Height);
        if (reference is null || IsExplicitOrChanged(reference, model, "Margin"))
            try { view.Margin = Thickness.Parse(model.Margin); } catch (FormatException) { }
        if ((reference is null || IsExplicitOrChanged(reference, model, "HorizontalAlignment"))
            && Enum.TryParse<HorizontalAlignment>(model.HorizontalAlignment, out var horizontal)) view.HorizontalAlignment = horizontal;
        if ((reference is null || IsExplicitOrChanged(reference, model, "VerticalAlignment"))
            && Enum.TryParse<VerticalAlignment>(model.VerticalAlignment, out var vertical)) view.VerticalAlignment = vertical;
        ApplyConstraint(reference, model, "MinWidth", v => view.MinWidth = v);
        ApplyConstraint(reference, model, "MinHeight", v => view.MinHeight = v);
        ApplyConstraint(reference, model, "MaxWidth", v => view.MaxWidth = v);
        ApplyConstraint(reference, model, "MaxHeight", v => view.MaxHeight = v);
        AxamlControlMetadata.ApplySourceScalars(reference, model, view);
    }

    private static bool IsExplicitOrChanged(AxamlSourceReference reference, DesignControlModel model, string key)
    {
        var property = AxamlRoundTripPropertyMap.PropertiesFor(reference.Element.LocalName).FirstOrDefault(p => p.Key == key);
        return property is not null && reference.Capability.CanEditProperty(key) && (reference.Element.FindAttribute(key) is not null
            || reference.SnapshotValues.TryGetValue(key, out var before) && before != property.Read(model));
    }

    private static void ApplyConstraint(AxamlSourceReference? reference, DesignControlModel model, string name, Action<double> apply)
    {
        var literal = reference?.Element.GetAttributeValue(name);
        if (reference is not null && reference.Capability.CanEditProperty(name))
        {
            var property = AxamlRoundTripPropertyMap.PropertiesFor(reference.Element.LocalName).FirstOrDefault(p => p.Key == name);
            if (property?.Metadata is not null && !model.CustomProperties.Any(p => p.Key == name)) return; // Pending Reset: leave native style/inheritance intact.
            if (property is not null && reference.SnapshotValues.TryGetValue(name, out var before) && before != property.Read(model)) literal = property.Read(model);
        }
        if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && (double.IsFinite(n) || name.StartsWith("Max", StringComparison.Ordinal) && double.IsPositiveInfinity(n)) && n >= 0) apply(n);
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
