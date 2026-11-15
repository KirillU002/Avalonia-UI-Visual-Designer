using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FormDesigner.DesignerSystem.Metadata;

public static class MetadataCoverageReport
{
    public static void Write(IDesignerRegistry registry, string directory) => Write(registry, directory, null);

    public static void Write(IDesignerRegistry registry, string directory, IReadOnlySet<string>? runtimeVerifiedTypes)
    {
        Directory.CreateDirectory(directory);
        var descriptors = registry.GetControls().Where(d => d.GetType().Assembly == typeof(MetadataCoverageReport).Assembly).ToArray();
        var aliases = new Dictionary<string, string> { ["Grid"] = "Grid", ["StackPanel"] = "StackPanel", ["WrapPanel"] = "WrapPanel", ["Canvas"] = "Group", ["DockPanel"] = "Group" };
        var controls = new List<string> { Csv("Type", "Namespace", "BaseType", "Visual", "Abstract", "ContainerKind", "ChildProperty", "LayoutSemantics", "ToolboxCandidate", "CandidateReason", "Toolbox", "ToolboxEntry", "Designer", "Inspector", "AxamlImport", "LegacyPreview", "AxamlPreview", "Export", "Assembly", "SupportCategory", "Json", "Runtime") };
        var properties = new List<string> { Csv("Control", "Key", "Name", "OwnerType", "PropertyType", "Kind", "DefaultValue", "HasDefault", "ReadOnly", "Nullable", "InheritedFromBase", "InheritsValue", "Attached", "BindableTarget", "DesignerEditable", "ImportSupported", "ExportSupported", "EditorType", "SupportState", "Discovered", "Displayable", "Editable", "Serializable", "Importable", "Exportable", "Unsupported", "ContextualLayout") };
        var types = AvaloniaMetadataCatalog.DiscoverControls();
        foreach (var type in types)
        {
            var metadata = AvaloniaMetadataCatalog.Get(type);
            var descriptorKey = aliases.GetValueOrDefault(type.Name, type.Name);
            var descriptor = descriptors.FirstOrDefault(d => d.TypeKey == descriptorKey);
            var imported = AxamlControlMetadata.Find(type.Name);
            var root = type == typeof(Window) || type == typeof(UserControl);
            var architectural=type.Name is "Control" or "Panel" or "Decorator" or "ContentControl" or "HeaderedContentControl" or "TemplatedControl" or "WindowBase";
            var specialized=type.Name is "Line" or "Path" or "Polygon" or "Polyline" or "MenuItem" or "TreeViewItem" or "TabItem";
            var category=type.IsAbstract||root||architectural?"6 Root/base; not ordinary Toolbox":runtimeVerifiedTypes?.Contains(type.Name)==true?"1 Verified vertical slice; bounded property subset"
                :descriptor is not null&&descriptor.TypeKey==type.Name?"2 Registered; partial properties or acceptance outside this report"
                :specialized?"5 Specialized item/geometry editor required":imported is not null?"3 Source subset; no direct Toolbox entry"
                :metadata.ToolboxCandidate||type.Name is "UniformGrid"?"4 Discovered; no Designer adapter":"6 Infrastructure; not ordinary Toolbox";
            var layout = type.Name is "Grid" or "Canvas" or "StackPanel" or "DockPanel" or "WrapPanel" ? type.Name : "Avalonia native";
            controls.Add(Csv(type.Name, type.Namespace, type.BaseType?.FullName, metadata.IsVisual, type.IsAbstract, metadata.ContainerKind,
                metadata.ChildProperty, layout, metadata.ToolboxCandidate, metadata.CandidateReason,
                descriptor is not null && !root && descriptor.TypeKey == type.Name, root ? "Root workflow" : descriptor?.TypeKey ?? "None",
                root ? "Root workflow" : descriptor is not null ? "Descriptor adapter" : "Not enabled",
                root ? "Form editor" : descriptor is not null ? "Descriptor + specialized rows" : "Not enabled",
                root ? "Root syntax" : imported is null ? "Opaque" : "Partial; source capability gate",
                descriptor is not null ? "Descriptor adapter" : "Not enabled", descriptor is not null ? "Export adapter" : "Not enabled",
                descriptor is not null ? "Descriptor adapter" : "Not enabled",type.Assembly.GetName().Name,category,
                root?"Form root contract":descriptor is not null?"Existing project contract":"No enabled JSON descriptor",
                runtimeVerifiedTypes?.Contains(type.Name)==true?"PASS native tree and behavior; Phase5A":descriptor is not null?"Adapter; see regression evidence":"Not verified"));
            // Detailed property audit is limited to types with an actual editing/root workflow.
            if (descriptor is null && imported is null && !root) continue;
            var sourceProperties = AxamlRoundTripPropertyMap.PropertiesFor(type.Name);
            foreach (var property in metadata.Properties)
            {
                var shared = StandardControlProperties.Find(type.Name, property.Key) is not null;
                var contextual = descriptor is not null && property.Key is "Margin" or "HorizontalAlignment" or "VerticalAlignment" or "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan" or "DockPanel.Dock" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight";
                var legacy = descriptor?.Properties.Any(p => NativeKey(p.Key, type.Name) == property.Key) == true || contextual;
                var import = sourceProperties.Any(p => p.AttributeNames.Contains(property.Key));
                var readOnlyDisplay = !root && (descriptor is not null || imported is not null) && StandardControlProperties.ReadOnlyInspectorKeys.Contains(property.Key);
                var displayable = shared || legacy || import || readOnlyDisplay;
                var editable = !property.ReadOnly && (shared || legacy);
                var serializable = shared || legacy || import;
                properties.Add(Csv(type.FullName, property.Key, property.Name, property.OwnerType.FullName, property.PropertyType.FullName,
                    property.Kind, property.DefaultText, property.HasDefaultValue, property.ReadOnly, property.Nullable,
                    property.Inherited, property.InheritsValue, property.Attached, property.Bindable,
                    editable, import, shared || legacy, shared ? StandardControlProperties.EditorFor(property) : property.Editor,
                    property.ReadOnly ? "ReadOnly" : shared ? "Editable literal; shared codec" : legacy || import ? "PartiallyEditable; legacy/source adapter" : readOnlyDisplay ? "ReadOnly representation; no literal adapter" : "Opaque/preserved or not exposed",
                    true, displayable, editable, serializable, import, shared || legacy, !displayable && !serializable, contextual));
            }
        }
        File.WriteAllLines(Path.Combine(directory, "UniversalControlCoverage.csv"), controls, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "UniversalPropertyCoverage.csv"), properties, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "UniversalCoverageSummary.json"), JsonSerializer.Serialize(new
        { Avalonia = typeof(Control).Assembly.GetName().Version?.ToString(), Types = types.Count, ControlRows = controls.Count - 1,
            PropertyRows = properties.Count - 1, BuiltInDescriptors = descriptors.Select(d => d.TypeKey).ToArray(),
            Policy = "Discovered != supported. Descriptor/legacy flags identify adapters; runtime verification is reported separately. Defaults are registration metadata, not resolved theme values." }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string NativeKey(string key, string type) => key switch
    {
        "Text" when type is "Button" or "CheckBox" or "ToggleSwitch" or "RadioButton" or "HyperlinkButton" or "RepeatButton" => "Content",
        "X" => "Canvas.Left", "Y" => "Canvas.Top", "LayoutOrientation" => "Orientation", "LayoutSpacing" => "Spacing",
        "GridRowDefinitions" => "RowDefinitions", "GridColumnDefinitions" => "ColumnDefinitions", _ => key
    };
    private static string Csv(params object?[] values) => string.Join(",", values.Select(v => "\"" + (v?.ToString() ?? "").Replace("\"", "\"\"") + "\""));
}
