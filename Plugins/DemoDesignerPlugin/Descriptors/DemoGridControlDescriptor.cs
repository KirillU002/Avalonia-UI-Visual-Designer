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

public sealed class DemoGridControlDescriptor : IControlDescriptor
{
    public const string TypeKeyValue = "Demo.GridControl";
    private const string AccentBrushPropertyKey = "AccentBrush";
    private const string HeaderBadgeTextPropertyKey = "HeaderBadgeText";
    private const string ShowGlowPropertyKey = "ShowGlow";
    private const string ShowFilterGlyphsPropertyKey = "ShowFilterGlyphs";
    private const string HeaderStylePropertyKey = "HeaderStyle";

    private static readonly IReadOnlyList<PropertyOption> HeaderStyleOptions = new[]
    {
        new PropertyOption { Value = "Classic", Title = "Classic" },
        new PropertyOption { Value = "Compact", Title = "Compact" },
        new PropertyOption { Value = "Analytics", Title = "Analytics" }
    };

    private static readonly IReadOnlyList<DesignPropertyDescriptor> PropertySchema = new[]
    {
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoGridControl.Text),
            Title = "Заголовок",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            BuiltInPropertyName = nameof(DemoGridControl.Text)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoGridControl.Width),
            Title = "Ширина",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoGridControl.Width)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoGridControl.Height),
            Title = "Высота",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoGridControl.Height)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoGridControl.Background),
            Title = "Фон",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = nameof(DemoGridControl.Background)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoGridControl.BorderBrush),
            Title = "Граница",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = nameof(DemoGridControl.BorderBrush)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoGridControl.CornerRadius),
            Title = "Скругление",
            Category = "Appearance",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoGridControl.CornerRadius)
        },
        new DesignPropertyDescriptor
        {
            Key = AccentBrushPropertyKey,
            Title = "Цвет glow-акцента",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            DefaultValueJson = JsonSerializer.Serialize("#60A5FA")
        },
        new DesignPropertyDescriptor
        {
            Key = HeaderBadgeTextPropertyKey,
            Title = "Бейдж шапки",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize("LIVE")
        },
        new DesignPropertyDescriptor
        {
            Key = ShowGlowPropertyKey,
            Title = "Мягкий glow",
            Category = "Appearance",
            Editor = PropertyEditorKind.Bool,
            DefaultValueJson = JsonSerializer.Serialize(true)
        },
        new DesignPropertyDescriptor
        {
            Key = ShowFilterGlyphsPropertyKey,
            Title = "Иконки фильтра в шапке",
            Category = "Behavior",
            Editor = PropertyEditorKind.Bool,
            DefaultValueJson = JsonSerializer.Serialize(true)
        },
        new DesignPropertyDescriptor
        {
            Key = HeaderStylePropertyKey,
            Title = "Стиль шапки",
            Category = "Appearance",
            Editor = PropertyEditorKind.Enum,
            DefaultValueJson = JsonSerializer.Serialize("Classic"),
            Options = HeaderStyleOptions
        }
    };

    private readonly string _pluginId;
    private readonly string _pluginVersion;

    public DemoGridControlDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "Demo GridControl";
    public string Category => "Demo Plugins";
    public string Description => "Стилизованная таблица в стиле demo-плагина: тёмный chrome, glow-акцент и шапка с фильтрами.";
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

        definition.BuiltInProperties[nameof(DemoGridControl.Text)] = "Клиенты";
        definition.BuiltInProperties[nameof(DemoGridControl.Width)] = 420d;
        definition.BuiltInProperties[nameof(DemoGridControl.Height)] = 260d;
        definition.BuiltInProperties[nameof(DemoGridControl.Background)] = "#0F172A";
        definition.BuiltInProperties[nameof(DemoGridControl.BorderBrush)] = "#1E293B";
        definition.BuiltInProperties[nameof(DemoGridControl.BorderThickness)] = 1d;
        definition.BuiltInProperties[nameof(DemoGridControl.CornerRadius)] = 18d;
        definition.BuiltInProperties[nameof(DemoGridControl.Padding)] = 0d;
        definition.BuiltInProperties[nameof(DemoGridControl.Opacity)] = 1d;
        definition.BuiltInProperties[nameof(DemoGridControl.IsVisible)] = true;
        definition.CustomProperties[AccentBrushPropertyKey] = JsonSerializer.Serialize("#60A5FA");
        definition.CustomProperties[HeaderBadgeTextPropertyKey] = JsonSerializer.Serialize("LIVE");
        definition.CustomProperties[ShowGlowPropertyKey] = JsonSerializer.Serialize(true);
        definition.CustomProperties[ShowFilterGlyphsPropertyKey] = JsonSerializer.Serialize(true);
        definition.CustomProperties[HeaderStylePropertyKey] = JsonSerializer.Serialize("Classic");
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        return new DemoGridControl
        {
            Width = Math.Max(280, control.GetDouble(nameof(DemoGridControl.Width), 420d)),
            Height = Math.Max(180, control.GetDouble(nameof(DemoGridControl.Height), 260d)),
            Text = control.GetString(nameof(DemoGridControl.Text), "Клиенты"),
            AccentBrush = control.GetCustomValue(AccentBrushPropertyKey, "#60A5FA"),
            HeaderBadgeText = control.GetCustomValue(HeaderBadgeTextPropertyKey, "LIVE"),
            ShowGlow = control.GetCustomValue(ShowGlowPropertyKey, true),
            ShowFilterGlyphs = control.GetCustomValue(ShowFilterGlyphsPropertyKey, true),
            HeaderStyle = control.GetCustomValue(HeaderStylePropertyKey, "Classic"),
            Background = ParseBrush(control.GetString(nameof(DemoGridControl.Background), "#0F172A"), "#0F172A"),
            BorderBrush = ParseBrush(control.GetString(nameof(DemoGridControl.BorderBrush), "#1E293B"), "#1E293B"),
            BorderThickness = new Thickness(Math.Max(0, control.GetDouble(nameof(DemoGridControl.BorderThickness), 1d))),
            CornerRadius = new CornerRadius(Math.Max(0, control.GetDouble(nameof(DemoGridControl.CornerRadius), 18d))),
            Padding = new Thickness(Math.Max(0, control.GetDouble(nameof(DemoGridControl.Padding), 0d))),
            Opacity = Math.Clamp(control.GetDouble(nameof(DemoGridControl.Opacity), 1d), 0d, 1d),
            IsVisible = control.GetBool(nameof(DemoGridControl.IsVisible), true)
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        context.RegisterXmlNamespace("demo", "clr-namespace:DemoDesignerPlugin.Controls;assembly=DemoDesignerPlugin");

        var attributes = new List<string>
        {
            $"x:Name=\"{EscapeXml(control.Name)}\"",
            $"Text=\"{EscapeXml(control.GetString(nameof(DemoGridControl.Text), "Клиенты"))}\"",
            $"Width=\"{FormatDouble(control.GetDouble(nameof(DemoGridControl.Width), 420d))}\"",
            $"Height=\"{FormatDouble(control.GetDouble(nameof(DemoGridControl.Height), 260d))}\"",
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\"",
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\"",
            $"Background=\"{EscapeXml(control.GetString(nameof(DemoGridControl.Background), "#0F172A"))}\"",
            $"BorderBrush=\"{EscapeXml(control.GetString(nameof(DemoGridControl.BorderBrush), "#1E293B"))}\"",
            $"BorderThickness=\"{FormatDouble(control.GetDouble(nameof(DemoGridControl.BorderThickness), 1d))}\"",
            $"CornerRadius=\"{FormatDouble(control.GetDouble(nameof(DemoGridControl.CornerRadius), 18d))}\"",
            $"Padding=\"{FormatDouble(control.GetDouble(nameof(DemoGridControl.Padding), 0d))}\"",
            $"AccentBrush=\"{EscapeXml(control.GetCustomValue(AccentBrushPropertyKey, "#60A5FA"))}\"",
            $"HeaderBadgeText=\"{EscapeXml(control.GetCustomValue(HeaderBadgeTextPropertyKey, "LIVE"))}\"",
            $"ShowGlow=\"{(control.GetCustomValue(ShowGlowPropertyKey, true) ? "True" : "False")}\"",
            $"ShowFilterGlyphs=\"{(control.GetCustomValue(ShowFilterGlyphsPropertyKey, true) ? "True" : "False")}\"",
            $"HeaderStyle=\"{EscapeXml(control.GetCustomValue(HeaderStylePropertyKey, "Classic"))}\""
        };

        if (!control.GetBool(nameof(DemoGridControl.IsVisible), true))
            attributes.Add("IsVisible=\"False\"");

        var opacity = control.GetDouble(nameof(DemoGridControl.Opacity), 1d);
        if (Math.Abs(opacity - 1d) > 0.0001d)
            attributes.Add($"Opacity=\"{FormatDouble(opacity)}\"");

        writer.WriteLine(indentLevel, $"<demo:DemoGridControl {string.Join(" ", attributes)} />");
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
