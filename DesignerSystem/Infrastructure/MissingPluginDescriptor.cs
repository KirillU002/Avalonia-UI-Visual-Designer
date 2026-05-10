using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;

namespace FormDesigner.DesignerSystem.Infrastructure;

public sealed class MissingPluginDescriptor : IControlDescriptor
{
    public MissingPluginDescriptor(string typeKey)
    {
        TypeKey = string.IsNullOrWhiteSpace(typeKey) ? "Unknown.Control" : typeKey;
    }

    public string TypeKey { get; }
    public string Title => $"Missing: {TypeKey}";
    public string Category => "Unavailable";
    public string Description => "Документ ссылается на контрол, чей plugin сейчас недоступен.";
    public bool IsContainer => false;
    public bool CanHostChildren => false;
    public string ChildLayoutMode => "Absolute";
    public IReadOnlyList<DesignPropertyDescriptor> Properties { get; } = Array.Empty<DesignPropertyDescriptor>();

    public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context)
    {
        var definition = new DesignerControlDefinition
        {
            TypeKey = TypeKey
        };

        definition.BuiltInProperties[nameof(Models.DesignControlModel.Text)] = TypeKey;
        definition.BuiltInProperties[nameof(Models.DesignControlModel.Width)] = 180d;
        definition.BuiltInProperties[nameof(Models.DesignControlModel.Height)] = 48d;
        definition.BuiltInProperties[nameof(Models.DesignControlModel.Background)] = "#FFF7ED";
        definition.BuiltInProperties[nameof(Models.DesignControlModel.BorderBrush)] = "#FB923C";
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        return new Border
        {
            Width = control.GetDouble(nameof(Models.DesignControlModel.Width), 180),
            Height = control.GetDouble(nameof(Models.DesignControlModel.Height), 48),
            Background = new SolidColorBrush(Color.Parse("#FFF7ED")),
            BorderBrush = new SolidColorBrush(Color.Parse("#FB923C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = $"{control.TypeKey}\nPlugin missing",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12)
            }
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        writer.WriteLine(indentLevel, $"<!-- Missing plugin control: {control.TypeKey} -->");
        writer.WriteLine(indentLevel, $"<Border Width=\"{control.GetDouble(nameof(Models.DesignControlModel.Width), 180)}\" Height=\"{control.GetDouble(nameof(Models.DesignControlModel.Height), 48)}\" Background=\"#FFF7ED\" BorderBrush=\"#FB923C\" BorderThickness=\"1\">");
        writer.WriteLine(indentLevel + 1, $"<TextBlock Text=\"{Escape(control.TypeKey)}\" Margin=\"12\" />");
        writer.WriteLine(indentLevel, "</Border>");
    }

    private static string Escape(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
