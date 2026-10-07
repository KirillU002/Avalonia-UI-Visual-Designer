using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public enum AxamlContainerKind { None, PanelChildren, SingleContent, HeaderedContent, Items, Decorator }

public sealed record AxamlControlMetadata(string SourceType, string ProjectionType, AxamlContainerKind ContainerKind,
    string? ChildProperty = null, string? ItemType = null)
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
        ["TabItem"] = new("TabItem", "Group", AxamlContainerKind.HeaderedContent, "Content")
    };

    public static AxamlControlMetadata? Find(string sourceType) => Known.TryGetValue(sourceType, out var value) ? value : null;
    public bool IsChildProperty(AxamlElementSyntax element) => ChildProperty is not null
        && element.NamespaceUri is "" or "https://github.com/avaloniaui"
        && element.LocalName == SourceType + "." + ChildProperty;

    public IReadOnlyList<AxamlLiteralProperty> LiteralProperties => SourceType switch
    {
        "TabControl" => new[] { new AxamlLiteralProperty(nameof(TabControl.SelectedIndex), "0", IsInteger: true) },
        "TabItem" => new[] { new AxamlLiteralProperty(nameof(TabItem.Header), "") },
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
public sealed record AxamlLiteralProperty(string Key, string DefaultValue, string[]? Options = null, bool IsInteger = false)
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
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == Key);
        if (stored is null) model.CustomProperties.Add(stored = new DesignPropertyValueModel { Key = Key });
        stored.ValueJson = JsonSerializer.Serialize(value);
        return true;
    }
}
