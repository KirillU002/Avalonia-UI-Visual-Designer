using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DemoDesignerPlugin.Controls;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace DemoDesignerPlugin.Descriptors;

public sealed class DemoDevButtonDescriptor : IControlDescriptor
{
    public const string TypeKeyValue = "Demo.DevButton";
    private const string AccentBrushPropertyKey = "AccentBrush";
    private const string BadgeTextPropertyKey = "BadgeText";
    private const string ShowGlowPropertyKey = "ShowGlow";
    private static readonly IReadOnlyList<DesignPropertyDescriptor> PropertySchema = new[]
    {
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoDevButton.Text),
            Title = "Текст кнопки",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            BuiltInPropertyName = nameof(DemoDevButton.Text),
            IsBindable = true
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoDevButton.Width),
            Title = "Ширина",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoDevButton.Width)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoDevButton.Height),
            Title = "Высота",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoDevButton.Height)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoDevButton.Background),
            Title = "Фон",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = nameof(DemoDevButton.Background)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoDevButton.BorderBrush),
            Title = "Граница",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = nameof(DemoDevButton.BorderBrush)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoDevButton.CornerRadius),
            Title = "Скругление",
            Category = "Appearance",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoDevButton.CornerRadius)
        },
        new DesignPropertyDescriptor
        {
            Key = AccentBrushPropertyKey,
            Title = "Цвет акцента",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            DefaultValueJson = JsonSerializer.Serialize("#38BDF8")
        },
        new DesignPropertyDescriptor
        {
            Key = BadgeTextPropertyKey,
            Title = "Текст бейджа",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize(string.Empty)
        },
        new DesignPropertyDescriptor
        {
            Key = ShowGlowPropertyKey,
            Title = "Подсветка акцента",
            Category = "Appearance",
            Editor = PropertyEditorKind.Bool,
            DefaultValueJson = JsonSerializer.Serialize(true)
        }
    };

    private readonly string _pluginId;
    private readonly string _pluginVersion;

    public DemoDevButtonDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "Кнопка-карточка";
    public string Category => "Ввод";
    public string Description => "Широкая кнопка-карточка с акцентной полосой и мягкой подсветкой.";
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

        definition.BuiltInProperties[nameof(DemoDevButton.Text)] = "Открыть карточку";
        definition.BuiltInProperties[nameof(DemoDevButton.Width)] = 240d;
        definition.BuiltInProperties[nameof(DemoDevButton.Height)] = 68d;
        definition.BuiltInProperties[nameof(DemoDevButton.Background)] = "#0F172A";
        definition.BuiltInProperties[nameof(DemoDevButton.BorderBrush)] = "#1E293B";
        definition.BuiltInProperties[nameof(DemoDevButton.BorderThickness)] = 1d;
        definition.BuiltInProperties[nameof(DemoDevButton.CornerRadius)] = 20d;
        definition.BuiltInProperties[nameof(DemoDevButton.Padding)] = 14d;
        definition.BuiltInProperties[nameof(DemoDevButton.Opacity)] = 1d;
        definition.BuiltInProperties[nameof(DemoDevButton.IsVisible)] = true;
        definition.CustomProperties[AccentBrushPropertyKey] = JsonSerializer.Serialize("#38BDF8");
        definition.CustomProperties[BadgeTextPropertyKey] = JsonSerializer.Serialize(string.Empty);
        definition.CustomProperties[ShowGlowPropertyKey] = JsonSerializer.Serialize(true);
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        return new DemoDevButton
        {
            Width = Math.Max(120, control.GetDouble(nameof(DemoDevButton.Width), 240d)),
            Height = Math.Max(44, control.GetDouble(nameof(DemoDevButton.Height), 68d)),
            Text = control.GetString(nameof(DemoDevButton.Text), "Открыть карточку"),
            BadgeText = control.GetCustomValue(BadgeTextPropertyKey, string.Empty),
            AccentBrush = control.GetCustomValue(AccentBrushPropertyKey, "#38BDF8"),
            ShowGlow = control.GetCustomValue(ShowGlowPropertyKey, true),
            Background = ParseBrush(control.GetString(nameof(DemoDevButton.Background), "#0F172A"), "#0F172A"),
            BorderBrush = ParseBrush(control.GetString(nameof(DemoDevButton.BorderBrush), "#1E293B"), "#1E293B"),
            BorderThickness = new Thickness(Math.Max(0, control.GetDouble(nameof(DemoDevButton.BorderThickness), 1d))),
            CornerRadius = new CornerRadius(Math.Max(0, control.GetDouble(nameof(DemoDevButton.CornerRadius), 20d))),
            Padding = new Thickness(Math.Max(8, control.GetDouble(nameof(DemoDevButton.Padding), 14d))),
            Opacity = Math.Clamp(control.GetDouble(nameof(DemoDevButton.Opacity), 1d), 0d, 1d),
            IsVisible = control.GetBool(nameof(DemoDevButton.IsVisible), true)
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        context.RegisterXmlNamespace("demo", "clr-namespace:DemoDesignerPlugin.Controls;assembly=DemoDesignerPlugin");

        var attributes = new List<string>
        {
            $"x:Name=\"{EscapeXml(control.Name)}\"",
            $"Text=\"{EscapeXml(control.GetString(nameof(DemoDevButton.Text), "Открыть карточку"))}\"",
            $"Width=\"{FormatDouble(control.GetDouble(nameof(DemoDevButton.Width), 240d))}\"",
            $"Height=\"{FormatDouble(control.GetDouble(nameof(DemoDevButton.Height), 68d))}\"",
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\"",
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\"",
            $"Background=\"{EscapeXml(control.GetString(nameof(DemoDevButton.Background), "#0F172A"))}\"",
            $"BorderBrush=\"{EscapeXml(control.GetString(nameof(DemoDevButton.BorderBrush), "#1E293B"))}\"",
            $"BorderThickness=\"{FormatDouble(control.GetDouble(nameof(DemoDevButton.BorderThickness), 1d))}\"",
            $"CornerRadius=\"{FormatDouble(control.GetDouble(nameof(DemoDevButton.CornerRadius), 20d))}\"",
            $"Padding=\"{FormatDouble(control.GetDouble(nameof(DemoDevButton.Padding), 14d))}\"",
            $"AccentBrush=\"{EscapeXml(control.GetCustomValue(AccentBrushPropertyKey, "#38BDF8"))}\"",
            $"BadgeText=\"{EscapeXml(control.GetCustomValue(BadgeTextPropertyKey, string.Empty))}\"",
            $"ShowGlow=\"{(control.GetCustomValue(ShowGlowPropertyKey, true) ? "True" : "False")}\""
        };

        if (!control.GetBool(nameof(DemoDevButton.IsVisible), true))
            attributes.Add("IsVisible=\"False\"");

        var opacity = control.GetDouble(nameof(DemoDevButton.Opacity), 1d);
        if (Math.Abs(opacity - 1d) > 0.0001d)
            attributes.Add($"Opacity=\"{FormatDouble(opacity)}\"");

        writer.WriteLine(indentLevel, $"<demo:DemoDevButton {string.Join(" ", attributes)} />");
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
