using FormDesigner.DesignerSystem.Metadata;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia;
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
        ["DropDownButton"] = new("DropDownButton", "DropDownButton", AxamlContainerKind.SingleContent, "Content"),
        ["SplitButton"] = new("SplitButton", "SplitButton", AxamlContainerKind.SingleContent, "Content"),
        ["TextBox"] = new("TextBox", "TextBox", AxamlContainerKind.None),
        ["TextBlock"] = new("TextBlock", "TextBlock", AxamlContainerKind.None),
        ["Label"] = new("Label", "Group", AxamlContainerKind.SingleContent, "Content"),
        ["ToggleButton"] = new("ToggleButton", "Group", AxamlContainerKind.SingleContent, "Content"),
        ["ButtonSpinner"] = new("ButtonSpinner", "Group", AxamlContainerKind.SingleContent, "Content"),
        ["CheckBox"] = new("CheckBox", "CheckBox", AxamlContainerKind.None),
        ["Border"] = new("Border", "Border", AxamlContainerKind.Decorator, "Child"),
        ["Grid"] = new("Grid", "Grid", AxamlContainerKind.PanelChildren, "Children"),
        ["StackPanel"] = new("StackPanel", "StackPanel", AxamlContainerKind.PanelChildren, "Children"),
        ["DockPanel"] = new("DockPanel", "Group", AxamlContainerKind.PanelChildren, "Children"),
        ["Canvas"] = new("Canvas", "Group", AxamlContainerKind.PanelChildren, "Children"),
        ["WrapPanel"] = new("WrapPanel", "WrapPanel", AxamlContainerKind.PanelChildren, "Children"),
        ["Viewbox"] = new("Viewbox", "Group", AxamlContainerKind.Decorator, "Child"),
        ["UniformGrid"] = new("UniformGrid", "Group", AxamlContainerKind.PanelChildren, "Children"),
        ["ScrollViewer"] = new("ScrollViewer", "Group", AxamlContainerKind.SingleContent, "Content"),
        ["TabControl"] = new("TabControl", "Group", AxamlContainerKind.Items, "Items", "TabItem"),
        ["TabItem"] = new("TabItem", "Group", AxamlContainerKind.HeaderedContent, "Content"),
        ["Expander"] = new("Expander", "Group", AxamlContainerKind.HeaderedContent, "Content", TextContentProperty: "Content"),
        ["ComboBox"] = new("ComboBox", "Group", AxamlContainerKind.Items, "Items", "ComboBoxItem"),
        ["Menu"] = new("Menu", "Group", AxamlContainerKind.Items, "Items", "MenuItem"),
        ["MenuItem"] = new("MenuItem", "Group", AxamlContainerKind.Items, "Items", "MenuItem"),
        ["ContextMenu"] = new("ContextMenu", "Group", AxamlContainerKind.Items, "Items", "MenuItem"),
        ["ComboBoxItem"] = new("ComboBoxItem", "Group", AxamlContainerKind.None, TextContentProperty: "Content"),
        ["ItemsControl"] = new("ItemsControl", "Group", AxamlContainerKind.Items, "Items"),
        ["ListBox"] = new("ListBox", "Group", AxamlContainerKind.Items, "Items"),
        ["ListBoxItem"] = new("ListBoxItem", "Group", AxamlContainerKind.SingleContent, "Content", TextContentProperty: "Content"),
        ["TreeView"] = new("TreeView", "Group", AxamlContainerKind.Items, "Items", "TreeViewItem"),
        ["TreeViewItem"] = new("TreeViewItem", "Group", AxamlContainerKind.Items, "Items", "TreeViewItem"),
        ["ProgressBar"] = new("ProgressBar", "Group", AxamlContainerKind.None),
        ["String"] = new("String", "Group", AxamlContainerKind.None, TextContentProperty: "Content")
    };

    public static AxamlControlMetadata? Find(string sourceType) => Known.TryGetValue(sourceType, out var value) ? value
        : StandardControlProperties.SupportedTypes.ContainsKey(sourceType) ? new(sourceType, "Group", AxamlContainerKind.None) : null;
    internal static System.Collections.Generic.IEnumerable<string> SourceTypeNames => Known.Keys.Concat(StandardControlProperties.SupportedTypes.Keys).Distinct(StringComparer.Ordinal);
    public static AxamlControlMetadata? FindSourceElement(AxamlElementSyntax element, string parentType)
    {
        if (element.LocalName == "String")
            return parentType is "ComboBox" or "ItemsControl" or "ListBox" && element.NamespaceUri is "http://schemas.microsoft.com/winfx/2006/xaml" or "clr-namespace:System;assembly=mscorlib"
                ? Find("String") : null;
        return IsFrameworkNamespace(element) ? Find(element.LocalName) : null;
    }

    private static bool IsFrameworkNamespace(AxamlElementSyntax element)
    {
        if(element.NamespaceUri is "" or "https://github.com/avaloniaui")return true;
        var owner=element.LocalName.Split('.')[0];var type=Find(owner)?.NativeType;
        return type is not null && (element.NamespaceUri=="using:"+type.Namespace
            || element.NamespaceUri=="clr-namespace:"+type.Namespace+";assembly="+type.Assembly.GetName().Name);
    }

    public bool AcceptsItem(AxamlElementSyntax element) => ItemType is null
        || SourceType is "Menu" or "MenuItem" or "ContextMenu" && element.LocalName == "Separator" && element.NamespaceUri is "" or "https://github.com/avaloniaui"
        || element.LocalName == ItemType && element.NamespaceUri is "" or "https://github.com/avaloniaui"
        || SourceType == "ComboBox" && FindSourceElement(element, SourceType)?.SourceType == "String";

    public static bool HasSourceValue(AxamlElementSyntax element, string property)
    {
        static bool Qualified(string name, string key) => name.Length > key.Length
            && name[name.Length - key.Length - 1] == '.' && name.EndsWith(key, StringComparison.Ordinal);
        foreach (var attribute in element.Attributes)
            if (attribute.Name == property || Qualified(attribute.Name, property)) return true;
        foreach (var child in element.Children)
            if (Qualified(child.LocalName, property)) return true;
        return false;
    }

    public bool IsChildProperty(AxamlElementSyntax element) => ChildProperty is not null
        && IsFrameworkNamespace(element)
        && (element.LocalName == SourceType + "." + ChildProperty
            || ContainerKind == AxamlContainerKind.Items && element.LocalName == "ItemsControl.Items");

    // A reviewed source policy, not discovery-as-support. Types here are shipped framework
    // types only; collectible plugin types are never keys in this immutable cache.
    private static readonly string[] SourceScalarKeys = { "IsEnabled", "IsHitTestVisible", "Focusable", "ClipToBounds",
        "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Padding", "BorderThickness", "CornerRadius",
        "FontFamily", "FontStyle", "FontStretch", "HorizontalContentAlignment", "VerticalContentAlignment", "DockPanel.Dock" };
    private static readonly Lazy<IReadOnlyDictionary<string, Type>> SourceTypes = new(() =>
        AvaloniaMetadataCatalog.DiscoverControls()
            .ToDictionary(t => t.Name, StringComparer.Ordinal));
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<AxamlLiteralProperty>>> SourceLiterals = new(() =>
        Known.Keys.Concat(StandardControlProperties.SupportedTypes.Keys).Distinct(StringComparer.Ordinal).ToDictionary(key => key,
            key => BuildSourceLiterals(Find(key)!), StringComparer.Ordinal));

    public Type? NativeType => SourceTypes.Value.TryGetValue(SourceType, out var type) ? type : null;
    internal static Type? ResolveNativeType(string sourceType) => SourceTypes.Value.TryGetValue(sourceType, out var type) ? type : null;
    public IReadOnlyList<AxamlLiteralProperty> LiteralProperties => SourceLiterals.Value[SourceType];

    public static void ApplySourceScalars(AxamlSourceReference? reference, DesignControlModel model, Control native, bool isDesignMode = false)
    {
        // Pending Canvas insertions have no source reference until ApplyPatch/ACK.
        // They still use these same reviewed accessors/codecs for common model values.
        var sourceType = reference?.Element.LocalName ?? model.Type;
        var metadata = Find(sourceType);
        if (metadata?.NativeType?.IsInstanceOfType(native) != true) return; // A design backdrop is not the user's native type.
        var schema = AvaloniaMetadataCatalog.Get(metadata.NativeType);
        foreach (var accessor in AxamlRoundTripPropertyMap.PropertiesFor(sourceType))
        {
            if (accessor.Metadata is not null || reference is not null && !reference.Capability.CanEditProperty(accessor.Key)) continue;
            var text = accessor.Read(model);
            if (reference is not null && accessor.ResolveExistingAttribute(reference.Element) is null
                && reference.SnapshotValues.TryGetValue(accessor.Key, out var before) && before == text) continue;
            var nativeKey = accessor.Key == "Name" ? "Name" : accessor.PreferredAttributeName;
            var property = schema.Properties.FirstOrDefault(p => p.Key == nativeKey && !p.ReadOnly && p.Property is not null);
            if (property is null) continue;
            object? value;
            if (property.PropertyType == typeof(object) && property.Name is "Content" or "Header" or "Watermark") value = text;
            else if (!MetadataValueCodec.TryParse(property.PropertyType, text, out value)) continue;
            if (property.Property!.IsValidValue(value)) native.SetValue(property.Property, value);
        }
        foreach (var literal in metadata.LiteralProperties.Where(p => p.Metadata is not null))
        {
            if (reference is not null && !reference.Capability.CanEditProperty(literal.Key) || !model.CustomProperties.Any(p => p.Key == literal.Key)) continue;
            if (isDesignMode && literal.Key is "IsHitTestVisible" or "Focusable") continue;
            if (StandardControlProperties.TryParseValue(literal.Metadata!, literal.Read(model), out var value))
                native.SetValue(literal.Metadata!.Property!, value);
        }
    }

    internal static void ApplyRootLiterals(AxamlElementSyntax source, Control native, bool windowRoot = false)
    {
        var schema = AvaloniaMetadataCatalog.Get(native.GetType());
        foreach (var key in windowRoot ? new[] { "Background" } : new[] { "Background", "Width", "Height", "Margin", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Opacity", "IsVisible", "ClipToBounds", "HorizontalAlignment", "VerticalAlignment" })
        {
            var raw = source.GetAttributeValue(key);
            if (raw is null || raw.TrimStart().StartsWith('{')) continue;
            var property = schema.Properties.FirstOrDefault(p => p.Key == key && !p.ReadOnly && p.Property is not null);
            if (property is not null && MetadataValueCodec.TryParse(property.PropertyType, System.Net.WebUtility.HtmlDecode(raw), out var value)
                && property.Property!.IsValidValue(value)) native.SetValue(property.Property, value);
        }
    }

    private static IReadOnlyList<AxamlLiteralProperty> BuildSourceLiterals(AxamlControlMetadata control)
    {
        var legacy = control.LegacyLiteralProperties;
        var properties = control.NativeType is { } type ? AvaloniaMetadataCatalog.Get(type).Properties : Array.Empty<AvaloniaPropertyInfo>();
        AxamlLiteralProperty Literal(AvaloniaPropertyInfo property) => new(property.Key, property.DefaultText,
            Options: StandardControlProperties.EditorFor(property) == MetadataEditor.Enumeration ? StandardControlProperties.Options(property).ToArray() : null,
            IsBoolean: property.Editor == MetadataEditor.Boolean, IsNumber: property.Editor == MetadataEditor.Number, Metadata: property);
        var reviewed = SourceScalarKeys.Select(key => properties.FirstOrDefault(p => p.Key == key && !p.ReadOnly && p.Property is not null))
            .Where(p => p is not null).Cast<AvaloniaPropertyInfo>().ToDictionary(p => p.Key, StringComparer.Ordinal);
        // Preserve ordering of specialized accessors (SelectedIndex/Header etc.).
        return legacy.Select(p => reviewed.TryGetValue(p.Key, out var replacement) ? Literal(replacement) : p)
            .Concat(reviewed.Values.Where(p => !legacy.Any(l => l.Key == p.Key)).Select(Literal)).ToArray();
    }

    private IReadOnlyList<AxamlLiteralProperty> LegacyLiteralProperties => StandardControlProperties.SupportedTypes.ContainsKey(SourceType)
        ? StandardControlProperties.PropertiesFor(SourceType).Select(p => new AxamlLiteralProperty(p.Key, p.DefaultText,
            Options: StandardControlProperties.EditorFor(p) == MetadataEditor.Enumeration ? StandardControlProperties.Options(p).ToArray() : null,
            IsBoolean: p.Editor == MetadataEditor.Boolean, IsNumber: p.Editor == MetadataEditor.Number, Metadata: p))
            .Concat(SourceType=="Expander"?new[]{new AxamlLiteralProperty("Content","")}:Array.Empty<AxamlLiteralProperty>()).ToArray()
        : SourceType switch
    {
        "TabControl" => new[] { new AxamlLiteralProperty(nameof(TabControl.SelectedIndex), "0", IsInteger: true) },
        "MenuItem" => new[] { new AxamlLiteralProperty(nameof(MenuItem.Header), ""), new AxamlLiteralProperty(nameof(MenuItem.IsEnabled), "True", IsBoolean: true) },
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
            new AxamlLiteralProperty(nameof(ListBox.SelectionMode), "Single", Enum.GetNames<SelectionMode>()),
            new AxamlLiteralProperty("ScrollViewer.HorizontalScrollBarVisibility", "Disabled", Enum.GetNames<ScrollBarVisibility>()),
            new AxamlLiteralProperty("ScrollViewer.VerticalScrollBarVisibility", "Auto", Enum.GetNames<ScrollBarVisibility>())
        },
        "DropDownButton" or "SplitButton" => new[] { new AxamlLiteralProperty("IsEnabled", "True", IsBoolean: true) },
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
public sealed record AxamlLiteralProperty(string Key, string DefaultValue, string[]? Options = null, bool IsInteger = false, bool IsBoolean = false, bool IsNumber = false, AvaloniaPropertyInfo? Metadata = null)
{
    public string Read(DesignControlModel model)
    {
        var stored = model.CustomProperties.FirstOrDefault(p => p.Key == Key);
        if (stored is null) return DefaultValue; // Immutable registration default already formatted by the source policy.
        if (Metadata is not null) return StandardControlProperties.Read(Metadata, stored?.ValueJson);
        try { return JsonSerializer.Deserialize<string>(stored.ValueJson) ?? DefaultValue; }
        catch (JsonException) { return stored.ValueJson; }
    }

    public bool Write(DesignControlModel model, string value)
    {
        // Imported explicit Infinity must remain distinguishable from absence (Reset).
        // JSON/native layout keeps its existing unset normalization outside source import.
        if (Metadata?.Key is "MaxWidth" or "MaxHeight" && value == "Infinity")
        {
            var explicitValue = model.CustomProperties.FirstOrDefault(p => p.Key == Key);
            if (explicitValue is null) model.CustomProperties.Add(explicitValue = new DesignPropertyValueModel { Key = Key });
            explicitValue.ValueJson = JsonSerializer.Serialize(value); return true;
        }
        if (Metadata is not null) return StandardControlProperties.TryWriteModel(model, Metadata, value);
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
