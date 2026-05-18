using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace DesignerPluginTemplate;

public sealed class MyControlDescriptor : IControlDescriptor
{
    public const string TypeKeyValue = "Company.MyControl";
    private const string CaptionPropertyKey = "Caption";
    private const string AccentBrushPropertyKey = "AccentBrush";

    private static readonly IReadOnlyList<DesignPropertyDescriptor> PropertySchema = new[]
    {
        new DesignPropertyDescriptor
        {
            Key = CaptionPropertyKey,
            Title = "Caption",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize("My plugin control")
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
        }
    };

    private readonly string _pluginId;
    private readonly string _pluginVersion;

    public MyControlDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "My Control";
    public string Category => "My Plugin";
    public string Description => "Template descriptor for a custom designer control.";
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

        definition.BuiltInProperties["Width"] = 220d;
        definition.BuiltInProperties["Height"] = 88d;
        definition.BuiltInProperties["Background"] = "#FFFFFF";
        definition.CustomProperties[CaptionPropertyKey] = JsonSerializer.Serialize("My plugin control");
        definition.CustomProperties[AccentBrushPropertyKey] = JsonSerializer.Serialize("#2563EB");
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        var accent = control.GetCustomValue(AccentBrushPropertyKey, "#2563EB");
        return new Border
        {
            Width = Math.Max(120, control.GetDouble("Width", 220d)),
            Height = Math.Max(56, control.GetDouble("Height", 88d)),
            Background = ParseBrush(control.GetString("Background", "#FFFFFF"), "#FFFFFF"),
            BorderBrush = ParseBrush(accent, "#2563EB"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = control.GetCustomValue(CaptionPropertyKey, "My plugin control"),
                Foreground = Brush.Parse("#0F172A"),
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(12)
            }
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        writer.WriteLine(indentLevel,
            $"<Border Width=\"{FormatDouble(control.GetDouble("Width", 220d))}\" " +
            $"Height=\"{FormatDouble(control.GetDouble("Height", 88d))}\" " +
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\" " +
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\" " +
            $"Background=\"{EscapeXml(control.GetString("Background", "#FFFFFF"))}\" " +
            $"BorderBrush=\"{EscapeXml(control.GetCustomValue(AccentBrushPropertyKey, "#2563EB"))}\" " +
            "BorderThickness=\"1\" CornerRadius=\"10\">");
        writer.WriteLine(indentLevel + 1,
            $"<TextBlock Text=\"{EscapeXml(control.GetCustomValue(CaptionPropertyKey, "My plugin control"))}\" " +
            "Margin=\"12\" FontWeight=\"SemiBold\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" />");
        writer.WriteLine(indentLevel, "</Border>");
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

