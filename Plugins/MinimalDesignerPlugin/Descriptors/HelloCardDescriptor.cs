using Avalonia.Controls;
using Avalonia.Media;
using FormDesigner.PluginContracts;
using MinimalDesignerPlugin.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace MinimalDesignerPlugin.Descriptors;

public sealed class HelloCardDescriptor : IControlDescriptor
{
    public const string TypeKeyValue = "Minimal.HelloCard";
    private const string MessagePropertyKey = "Message";
    private const string AccentBrushPropertyKey = "AccentBrush";

    private static readonly IReadOnlyList<DesignPropertyDescriptor> PropertySchema = new[]
    {
        new DesignPropertyDescriptor
        {
            Key = "Text",
            Title = "Title",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            BuiltInPropertyName = "Text",
            IsBindable = true
        },
        new DesignPropertyDescriptor
        {
            Key = MessagePropertyKey,
            Title = "Message",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize("This control comes from an external plugin DLL.")
        },
        new DesignPropertyDescriptor
        {
            Key = AccentBrushPropertyKey,
            Title = "Accent",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            DefaultValueJson = JsonSerializer.Serialize("#2563EB")
        },
        new DesignPropertyDescriptor
        {
            Key = "Width",
            Title = "Width",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = "Width"
        },
        new DesignPropertyDescriptor
        {
            Key = "Height",
            Title = "Height",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = "Height"
        },
        new DesignPropertyDescriptor
        {
            Key = "Background",
            Title = "Background",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = "Background"
        }
    };

    private readonly string _pluginId;
    private readonly string _pluginVersion;

    public HelloCardDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "Hello Card";
    public string Category => "SDK Examples";
    public string Description => "Minimal plugin control with toolbox metadata, preview, custom properties and XAML export.";
    public bool IsContainer => false;
    public bool CanHostChildren => false;
    public string ChildLayoutMode => "Absolute";
    public IReadOnlyList<DesignPropertyDescriptor> Properties => PropertySchema;

    public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context)
    {
        var definition = new DesignerControlDefinition
        {
            TypeKey = TypeKey,
            DescriptorId = TypeKey,
            PluginId = _pluginId,
            PluginVersion = _pluginVersion
        };

        definition.BuiltInProperties["Text"] = "Hello plugin";
        definition.BuiltInProperties["Width"] = 280d;
        definition.BuiltInProperties["Height"] = 118d;
        definition.BuiltInProperties["Background"] = "#FFFFFF";
        definition.BuiltInProperties["IsVisible"] = true;
        definition.BuiltInProperties["Opacity"] = 1d;
        definition.CustomProperties[MessagePropertyKey] = JsonSerializer.Serialize("This card is registered by MinimalDesignerPlugin.");
        definition.CustomProperties[AccentBrushPropertyKey] = JsonSerializer.Serialize("#2563EB");
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        return new HelloCard
        {
            Width = Math.Max(140, control.GetDouble("Width", 280d)),
            Height = Math.Max(80, control.GetDouble("Height", 118d)),
            Title = control.GetString("Text", "Hello plugin"),
            Message = control.GetCustomValue(MessagePropertyKey, "This card is registered by MinimalDesignerPlugin."),
            AccentBrush = ParseBrush(control.GetCustomValue(AccentBrushPropertyKey, "#2563EB"), "#2563EB"),
            Background = ParseBrush(control.GetString("Background", "#FFFFFF"), "#FFFFFF"),
            Opacity = Math.Clamp(control.GetDouble("Opacity", 1d), 0d, 1d),
            IsVisible = control.GetBool("IsVisible", true)
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        context.RegisterXmlNamespace("minimal", "clr-namespace:MinimalDesignerPlugin.Controls;assembly=MinimalDesignerPlugin");

        var attributes = new List<string>
        {
            $"x:Name=\"{EscapeXml(control.Name)}\"",
            $"Title=\"{EscapeXml(control.GetString("Text", "Hello plugin"))}\"",
            $"Message=\"{EscapeXml(control.GetCustomValue(MessagePropertyKey, "This card is registered by MinimalDesignerPlugin."))}\"",
            $"AccentBrush=\"{EscapeXml(control.GetCustomValue(AccentBrushPropertyKey, "#2563EB"))}\"",
            $"Width=\"{FormatDouble(control.GetDouble("Width", 280d))}\"",
            $"Height=\"{FormatDouble(control.GetDouble("Height", 118d))}\"",
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\"",
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\"",
            $"Background=\"{EscapeXml(control.GetString("Background", "#FFFFFF"))}\""
        };

        if (!control.GetBool("IsVisible", true))
            attributes.Add("IsVisible=\"False\"");

        var opacity = control.GetDouble("Opacity", 1d);
        if (Math.Abs(opacity - 1d) > 0.0001d)
            attributes.Add($"Opacity=\"{FormatDouble(opacity)}\"");

        writer.WriteLine(indentLevel, $"<minimal:HelloCard {string.Join(" ", attributes)} />");
    }

    private static IBrush ParseBrush(string value, string fallback)
    {
        try
        {
            return Brush.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value);
        }
        catch
        {
            return Brush.Parse(fallback);
        }
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}

