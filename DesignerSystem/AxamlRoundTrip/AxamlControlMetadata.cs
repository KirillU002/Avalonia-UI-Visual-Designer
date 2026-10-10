using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public enum AxamlContainerKind { None, PanelChildren, SingleContent, HeaderedContent, Items, Decorator }

public sealed record AxamlControlMetadata(string SourceType, string ProjectionType, AxamlContainerKind ContainerKind,
    string? ChildProperty = null, string? ItemType = null, string? TextContentProperty = null)
{
    private static readonly Dictionary<string, AxamlControlMetadata> Known = new(StringComparer.Ordinal)
    {
        ["Button"] = new("Button", "Button", AxamlContainerKind.SingleContent, "Content"),
        ["TextBox"] = new("TextBox", "TextBox", AxamlContainerKind.None),
        ["TextBlock"] = new("TextBlock", "TextBlock", AxamlContainerKind.None),
        ["CheckBox"] = new("CheckBox", "CheckBox", AxamlContainerKind.None),
        ["Border"] = new("Border", "Border", AxamlContainerKind.Decorator, "Child"),
        ["Grid"] = new("Grid", "Grid", AxamlContainerKind.PanelChildren, "Children"),
        ["StackPanel"] = new("StackPanel", "StackPanel", AxamlContainerKind.PanelChildren, "Children"),
        ["DockPanel"] = new("DockPanel", "Group", AxamlContainerKind.PanelChildren, "Children"),
        ["Canvas"] = new("Canvas", "Group", AxamlContainerKind.PanelChildren, "Children"),
        ["WrapPanel"] = new("WrapPanel", "WrapPanel", AxamlContainerKind.PanelChildren, "Children"),
        ["ScrollViewer"] = new("ScrollViewer", "Group", AxamlContainerKind.SingleContent, "Content"),
        ["TabControl"] = new("TabControl", "Group", AxamlContainerKind.Items, "Items", "TabItem"),
        ["TabItem"] = new("TabItem", "Group", AxamlContainerKind.HeaderedContent, "Content"),
        ["Expander"] = new("Expander", "Group", AxamlContainerKind.HeaderedContent, "Content", TextContentProperty: "Content"),
        ["ComboBox"] = new("ComboBox", "Group", AxamlContainerKind.Items, "Items", "ComboBoxItem"),
        ["ComboBoxItem"] = new("ComboBoxItem", "Group", AxamlContainerKind.None, TextContentProperty: "Content"),
        ["ItemsControl"] = new("ItemsControl", "Group", AxamlContainerKind.Items, "Items"),
        ["ListBox"] = new("ListBox", "Group", AxamlContainerKind.Items, "Items"),
        ["ListBoxItem"] = new("ListBoxItem", "Group", AxamlContainerKind.SingleContent, "Content", TextContentProperty: "Content"),
        ["TreeView"] = new("TreeView", "Group", AxamlContainerKind.Items, "Items", "TreeViewItem"),
        ["TreeViewItem"] = new("TreeViewItem", "Group", AxamlContainerKind.Items, "Items", "TreeViewItem"),
        ["ProgressBar"] = new("ProgressBar", "Group", AxamlContainerKind.None),
        ["String"] = new("String", "Group", AxamlContainerKind.None, TextContentProperty: "Content")
    };

    public static AxamlControlMetadata? Find(string sourceType) => Known.TryGetValue(sourceType, out var value) ? value : null;
    public static AxamlControlMetadata? FindSourceElement(AxamlElementSyntax element, string parentType)
    {
        if (element.LocalName == "String")
            return parentType is "ComboBox" or "ItemsControl" or "ListBox" && element.NamespaceUri is "http://schemas.microsoft.com/winfx/2006/xaml" or "clr-namespace:System;assembly=mscorlib"
                ? Find("String") : null;
        return element.NamespaceUri is "" or "https://github.com/avaloniaui" ? Find(element.LocalName) : null;
    }

    public bool AcceptsItem(AxamlElementSyntax element) => ItemType is null
        || element.LocalName == ItemType && element.NamespaceUri is "" or "https://github.com/avaloniaui"
        || SourceType == "ComboBox" && FindSourceElement(element, SourceType)?.SourceType == "String";

    public static bool HasSourceValue(AxamlElementSyntax element, string property) => element.Attributes.Any(a => a.Name == property
            || a.Name.EndsWith("." + property, StringComparison.Ordinal))
        || element.Children.Any(c => c.LocalName.EndsWith("." + property, StringComparison.Ordinal));

    public bool IsChildProperty(AxamlElementSyntax element) => ChildProperty is not null
        && element.NamespaceUri is "" or "https://github.com/avaloniaui"
        && (element.LocalName == SourceType + "." + ChildProperty
            || ContainerKind == AxamlContainerKind.Items && element.LocalName == "ItemsControl.Items");

    public IReadOnlyList<AxamlLiteralProperty> LiteralProperties => SourceType switch
    {
        "TabControl" => new[] { new AxamlLiteralProperty(nameof(TabControl.SelectedIndex), "0", IsInteger: true) },
        "TabItem" => new[] { new AxamlLiteralProperty(nameof(TabItem.Header), "") },
        "Expander" => new[]
        {
            new AxamlLiteralProperty(nameof(Expander.Header), ""),
            new AxamlLiteralProperty(nameof(Expander.Content), ""),
            new AxamlLiteralProperty(nameof(Expander.IsExpanded), "False", IsBoolean: true),
            new AxamlLiteralProperty(nameof(Expander.ExpandDirection), "Down", Enum.GetNames<ExpandDirection>())
        },
        "ComboBox" => new[]
        {
            new AxamlLiteralProperty(nameof(ComboBox.SelectedIndex), "-1", IsInteger: true),
            new AxamlLiteralProperty(nameof(ComboBox.PlaceholderText), "")
        },
        "ComboBoxItem" or "String" => new[] { new AxamlLiteralProperty("Content", "") },
        "ListBox" => new[]
        {
            new AxamlLiteralProperty(nameof(ListBox.SelectedIndex), "-1", IsInteger: true),
            new AxamlLiteralProperty(nameof(ListBox.SelectionMode), "Single", Enum.GetNames<SelectionMode>())
        },
        "ListBoxItem" => new[] { new AxamlLiteralProperty(nameof(ListBoxItem.Content), "") },
        "TreeView" => new[] { new AxamlLiteralProperty(nameof(TreeView.SelectionMode), "Single", Enum.GetNames<SelectionMode>()) },
        "TreeViewItem" => new[]
        {
            new AxamlLiteralProperty(nameof(TreeViewItem.Header), ""),
            new AxamlLiteralProperty(nameof(TreeViewItem.IsExpanded), "False", IsBoolean: true)
        },
        "ProgressBar" => new[]
        {
            new AxamlLiteralProperty(nameof(ProgressBar.Minimum), "0", IsNumber: true),
            new AxamlLiteralProperty(nameof(ProgressBar.Maximum), "100", IsNumber: true),
            new AxamlLiteralProperty(nameof(ProgressBar.Value), "0", IsNumber: true),
            new AxamlLiteralProperty(nameof(ProgressBar.IsIndeterminate), "False", IsBoolean: true),
            new AxamlLiteralProperty(nameof(ProgressBar.Orientation), "Horizontal", Enum.GetNames<Orientation>())
        },
        "ScrollViewer" => new[]
        {
            new AxamlLiteralProperty(nameof(ScrollViewer.HorizontalScrollBarVisibility), "Disabled", Enum.GetNames<ScrollBarVisibility>()),
            new AxamlLiteralProperty(nameof(ScrollViewer.VerticalScrollBarVisibility), "Auto", Enum.GetNames<ScrollBarVisibility>())
        },
        "WrapPanel" => new[] { new AxamlLiteralProperty(nameof(WrapPanel.Orientation), "Horizontal", Enum.GetNames<Orientation>()) },
        _ => Array.Empty<AxamlLiteralProperty>()
    };
}

// Uses the existing serialized property bag; no new JSON schema or plugin ABI.
public sealed record AxamlLiteralProperty(string Key, string DefaultValue, string[]? Options = null, bool IsInteger = false, bool IsBoolean = false, bool IsNumber = false)
{
    public string Read(DesignControlModel model)
    {
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == Key);
        if (stored is null) return DefaultValue;
        try { return JsonSerializer.Deserialize<string>(stored.ValueJson) ?? DefaultValue; }
        catch (JsonException) { return stored.ValueJson; }
    }

    public bool Write(DesignControlModel model, string value)
    {
        if (Options is not null && !Options.Contains(value, StringComparer.Ordinal)) return false;
        if (IsInteger && (!int.TryParse(value, out var index) || index < -1)) return false;
        if (IsNumber && (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))) return false;
        if (IsBoolean)
        {
            if (!bool.TryParse(value, out var flag)) return false;
            value = flag ? "True" : "False";
        }
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == Key);
        if (stored is null) model.CustomProperties.Add(stored = new DesignPropertyValueModel { Key = Key });
        stored.ValueJson = JsonSerializer.Serialize(value);
        return true;
    }
}
