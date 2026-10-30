using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public static partial class AxamlLayoutProjection
{
    // JSON v2 uses the same native panels and measurement root as source projection.
    // This is a tree adapter, not an arithmetic implementation of Avalonia layout.
    public sealed record JsonLayoutTree(Panel Root, IReadOnlyDictionary<string, Control> Controls,
        IReadOnlyDictionary<string, string> Parents)
    {
        public IReadOnlyDictionary<string, Rect> Measure(double width, double height)
        {
            var measurement = new MeasurementRoot { Child = Root };
            try
            {
                PrepareOffscreen(Root);
                measurement.Measure(new Size(width, height));
                measurement.Arrange(new Rect(0, 0, width, height));
                return Controls.ToDictionary(pair => pair.Key, pair =>
                {
                    var parent = Parents[pair.Key];
                    var relativeTo = !string.IsNullOrEmpty(parent) ? Controls[parent] : Root;
                    var point = pair.Value.TranslatePoint(default, relativeTo) ?? default;
                    return new Rect(point, pair.Value.Bounds.Size);
                });
            }
            finally { measurement.Child = null; }
        }
    }

    public static JsonLayoutTree BuildJson(DesignerDocumentFileModel document,
        IReadOnlyList<DesignControlModel> models, Func<DesignControlModel, Control> leafFactory,
        bool includeHidden = false)
    {
        if (document.LayoutSemanticsVersion != 2) throw new InvalidOperationException("Неизвестная версия native layout.");
        var byId = models.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var views = new Dictionary<string, Control>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
            if (!string.IsNullOrEmpty(model.ParentId) && !byId.ContainsKey(model.ParentId))
                throw new InvalidOperationException("Layout: родитель элемента не найден: " + model.Name);

        Panel PanelFor(string mode, string rows, string columns, int rowCount, int columnCount, string orientation, double spacing)
        {
            return DesignerLayoutModes.NormalizeMode(mode) switch
            {
                DesignerLayoutModes.Grid => new Grid
                {
                    RowDefinitions = RowDefinitions.Parse(Definitions(rows, rowCount)),
                    ColumnDefinitions = ColumnDefinitions.Parse(Definitions(columns, columnCount))
                },
                DesignerLayoutModes.Stack => new StackPanel
                {
                    Orientation = orientation == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical,
                    Spacing = Math.Max(0, spacing)
                },
                DesignerLayoutModes.Flex => new WrapPanel
                {
                    Orientation = orientation == "Horizontal" ? Orientation.Horizontal : Orientation.Vertical
                },
                _ => new Canvas()
            };
        }

        Control Build(DesignControlModel model)
        {
            if (!visiting.Add(model.Id)) throw new InvalidOperationException("Layout: циклический ParentId.");
            var children = models.Where(c => c.ParentId == model.Id).ToList();
            var isContainer = model.Type is DesignerControlTypes.Group or DesignerControlTypes.Border
                or DesignerControlTypes.LayoutGrid or DesignerControlTypes.StackLayout or DesignerControlTypes.FlexLayout;
            Control view;
            if (isContainer)
            {
                var mode = string.IsNullOrEmpty(model.ChildLayoutMode) ? DesignerLayoutModes.GetModeForControlType(model.Type) : model.ChildLayoutMode;
                var panel = PanelFor(mode, model.GridRowDefinitions, model.GridColumnDefinitions, model.Rows, model.Columns, model.LayoutOrientation, model.LayoutSpacing);
                AddChildren(panel, children);
                view = model.Type == DesignerControlTypes.Group ? panel : new Border
                {
                    Child = panel, Padding = new Thickness(model.Padding),
                    BorderThickness = new Thickness(model.BorderThickness), CornerRadius = new CornerRadius(model.CornerRadius),
                    BorderBrush = Brush.Parse(model.BorderBrush), Background = Brush.Parse(model.Background)
                };
            }
            else
            {
                if (children.Count != 0) throw new InvalidOperationException("Layout: вложенные элементы этого компонента пока не поддерживаются: " + model.Name);
                view = leafFactory(model);
            }
            ApplyJsonLayout(model, view, includeHidden);
            views.Add(model.Id, view);
            visiting.Remove(model.Id);
            return view;
        }

        void AddChildren(Panel panel, IEnumerable<DesignControlModel> children)
        {
            if (panel is StackPanel) children = children.OrderBy(c => c.StackOrder);
            foreach (var child in children)
            {
                var view = Build(child);
                Grid.SetRow(view, Math.Max(0, child.GridRow)); Grid.SetColumn(view, Math.Max(0, child.GridColumn));
                Grid.SetRowSpan(view, Math.Max(1, child.GridRowSpan)); Grid.SetColumnSpan(view, Math.Max(1, child.GridColumnSpan));
                if (panel is Canvas) { Canvas.SetLeft(view, child.X); Canvas.SetTop(view, child.Y); }
                panel.Children.Add(view);
            }
        }

        var root = PanelFor(document.SurfaceLayoutMode, document.SurfaceGridRowDefinitions, document.SurfaceGridColumnDefinitions,
            document.SurfaceLayoutRows, document.SurfaceLayoutColumns, document.SurfaceLayoutOrientation, document.SurfaceLayoutSpacing);
        root.Name = document.SurfaceLayoutMode == DesignerLayoutModes.Absolute ? "RootCanvas" : "RootLayout";
        AddChildren(root, models.Where(c => string.IsNullOrEmpty(c.ParentId)));
        if (views.Count != models.Count) throw new InvalidOperationException("Layout: дерево содержит недоступные или циклические элементы.");
        return new JsonLayoutTree(root, views, models.ToDictionary(c => c.Id, c => c.ParentId));
    }

    public static string Definitions(string text, int count) => string.IsNullOrWhiteSpace(text)
        ? string.Join(",", Enumerable.Repeat("*", Math.Max(1, count))) : text.Trim();

    public static void ApplyJsonLayout(DesignControlModel model, Control view, bool includeHidden = false)
    {
        if (model.WidthMode == "Auto") view.ClearValue(Control.WidthProperty); else view.Width = model.Width;
        if (model.HeightMode == "Auto") view.ClearValue(Control.HeightProperty); else view.Height = model.Height;
        // The existing runtime DataGrid factory returns Border -> Grid -> DataGrid.
        // The outer placement and its internal star-row host must both follow the viewport.
        if (model.Type == DesignerControlTypes.DataGrid && view is Border { Child: Grid gridHost })
        {
            gridHost.ClearValue(Control.WidthProperty);
            gridHost.ClearValue(Control.HeightProperty);
        }
        view.Margin = Thickness.Parse(model.Margin);
        view.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(model.HorizontalAlignment);
        view.VerticalAlignment = Enum.Parse<VerticalAlignment>(model.VerticalAlignment);
        view.Opacity = model.Opacity;
        view.IsVisible = includeHidden || model.IsVisible;
        if (!string.IsNullOrWhiteSpace(model.Name)) view.Name = model.Name;
    }
}
