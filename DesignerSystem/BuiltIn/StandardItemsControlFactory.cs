using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FormDesigner.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace FormDesigner.DesignerSystem.BuiltIn;

public static class StandardItemsControlFactory
{
    public static DesignerControlFileModel Snapshot(DesignControlModel model) => new()
    {
        Id = model.Id, Name = model.Name, Type = model.Type, Text = model.Text, Width = model.Width, Height = model.Height,
        Background = model.Background, Foreground = model.Foreground, BorderBrush = model.BorderBrush,
        BorderThickness = model.BorderThickness, CornerRadius = model.CornerRadius, Padding = model.Padding,
        FontFamily = model.FontFamily, FontSize = model.FontSize, FontWeight = model.FontWeight,
        IsVisible = model.IsVisible, Opacity = model.Opacity, BindingSourceId = model.BindingSourceId,
        CustomProperties = model.CustomProperties.Select(p => new DesignPropertyValueFileModel { Key = p.Key, ValueJson = p.ValueJson }).ToList()
    };
    // The same native ListBox used by the existing Phase 5 source projection.
    public static ListBox CreateListBox(SelectionMode mode) => new() { SelectionMode = mode };
    public static Control Create(DesignerControlFileModel model, IEnumerable? boundRows = null,
        Action? primaryClick = null, Action<StandardMenuItem>? menuClick = null)
    {
        T Value<T>(string key, T fallback) => StandardItemsProperties.Read(model, key, fallback);
        Control result;
        if (model.Type == DesignerControlTypes.ListBox)
        {
            Enum.TryParse<SelectionMode>(Value("SelectionMode", "Single"), out var mode);
            var list = CreateListBox(mode);
            if (!string.IsNullOrEmpty(model.BindingSourceId) || !string.IsNullOrEmpty(Value("ItemsSource", "")))
            {
                list.ItemsSource = boundRows ?? Array.Empty<object>();
                var display = Value("ItemTemplate.DisplayField", "");
                list.ItemTemplate = new FuncDataTemplate<object>((row, _) => new TextBlock { Text = Display(row, display) });
            }
            else list.ItemsSource = Value("Items", Array.Empty<string>());
            list.SelectedIndex = Value("SelectedIndex", -1);
            var selected = Value("SelectedItem", "");
            if (!string.IsNullOrEmpty(selected) && string.IsNullOrEmpty(model.BindingSourceId)) list.SelectedItem = selected;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, Enum.TryParse<ScrollBarVisibility>(Value("ScrollViewer.HorizontalScrollBarVisibility", "Disabled"), out var horizontal) ? horizontal : ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, Enum.TryParse<ScrollBarVisibility>(Value("ScrollViewer.VerticalScrollBarVisibility", "Auto"), out var vertical) ? vertical : ScrollBarVisibility.Auto);
            result = list;
        }
        else
        {
            var stack = new StackPanel { Spacing = 3 };
            var flyout = new Flyout { Content = stack };
            if (Enum.TryParse<PlacementMode>(Value("Flyout.Placement", "Bottom"), out var placement)) flyout.Placement = placement;
            foreach (var item in StandardItemsProperties.Menu(model))
            {
                var button = new Button { Name = StandardItemsProperties.MenuName(model.Id, item.Id), Content = item.Text,
                    IsEnabled = item.IsEnabled, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => { flyout.Hide(); menuClick?.Invoke(item); };
                stack.Children.Add(button);
            }
            if (model.Type == DesignerControlTypes.SplitButton)
            {
                var split = new SplitButton { Content = model.Text, Flyout = flyout };
                split.Click += (_, _) => primaryClick?.Invoke(); result = split;
            }
            else
            {
                var drop = new DropDownButton { Content = model.Text, Flyout = flyout };
                drop.Click += (_, _) => primaryClick?.Invoke(); result = drop;
            }
        }
        result.Name = model.Name;
        result.Width = model.Width; result.Height = model.Height;
        result.IsEnabled = Value("IsEnabled", true); result.IsVisible = model.IsVisible; result.Opacity = model.Opacity;
        if (result is TemplatedControl template)
        {
            template.Background = Brush.Parse(model.Background); template.Foreground = Brush.Parse(model.Foreground);
            template.BorderBrush = Brush.Parse(model.BorderBrush); template.BorderThickness = new Thickness(model.BorderThickness);
            template.CornerRadius = new CornerRadius(model.CornerRadius); template.Padding = new Thickness(model.Padding);
            template.FontFamily = new FontFamily(model.FontFamily); template.FontSize = model.FontSize;
            template.FontWeight = Enum.TryParse<FontWeight>(model.FontWeight, out var weight) ? weight : FontWeight.Normal;
        }
        return result;
    }
    public static string Display(object? row, string path)
    {
        if (row is null) return "";
        if (string.IsNullOrEmpty(path)) return Convert.ToString(row, CultureInfo.InvariantCulture) ?? "";
        if (row is IDictionary values) return Convert.ToString(values[path], CultureInfo.InvariantCulture) ?? "";
        return Convert.ToString(TypeDescriptor.GetProperties(row)[path]?.GetValue(row), CultureInfo.InvariantCulture) ?? "";
    }
}
