using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DemoDesignerPlugin.Controls;
using FormDesigner.PluginContracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DemoDesignerPlugin.Descriptors;

public sealed class DemoTreeListDescriptor : IControlDescriptor
{
    public const string TypeKeyValue = "Demo.TreeList";
    private const string AutoGenerateColumnsPropertyName = "AutoGenerateColumns";
    private const string BindingSourceIdPropertyName = "BindingSourceId";

    private const string AccentBrushPropertyKey = "AccentBrush";
    private const string ShowGlowPropertyKey = "ShowGlow";
    private const string IconModePropertyKey = "IconMode";
    private const string UniformIconGlyphPropertyKey = "UniformIconGlyph";
    private const string IconRulesTextPropertyKey = "IconRulesText";
    private const string ExpandAllByDefaultPropertyKey = "ExpandAllByDefault";
    private const string ColumnsDefinitionTextPropertyKey = "ColumnsDefinitionText";
    private const string ChildrenPathPropertyKey = "ChildrenPath";

    private static readonly IReadOnlyList<DesignPropertyDescriptor> PropertySchema = new[]
    {
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoTreeList.Text),
            Title = "Заголовок",
            Category = "Content",
            Editor = PropertyEditorKind.Text,
            BuiltInPropertyName = nameof(DemoTreeList.Text)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoTreeList.Width),
            Title = "Ширина",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoTreeList.Width)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoTreeList.Height),
            Title = "Высота",
            Category = "Layout",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoTreeList.Height)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoTreeList.Background),
            Title = "Фон",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = nameof(DemoTreeList.Background)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoTreeList.BorderBrush),
            Title = "Граница",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            BuiltInPropertyName = nameof(DemoTreeList.BorderBrush)
        },
        new DesignPropertyDescriptor
        {
            Key = nameof(DemoTreeList.CornerRadius),
            Title = "Скругление",
            Category = "Appearance",
            Editor = PropertyEditorKind.Number,
            BuiltInPropertyName = nameof(DemoTreeList.CornerRadius)
        },
        new DesignPropertyDescriptor
        {
            Key = AutoGenerateColumnsPropertyName,
            Title = "Автоколонки",
            Category = "Data",
            Editor = PropertyEditorKind.Bool,
            BuiltInPropertyName = AutoGenerateColumnsPropertyName
        },
        new DesignPropertyDescriptor
        {
            Key = BindingSourceIdPropertyName,
            Title = "Источник данных",
            Category = "Data",
            Editor = PropertyEditorKind.Binding,
            BuiltInPropertyName = BindingSourceIdPropertyName
        },
        new DesignPropertyDescriptor
        {
            Key = AccentBrushPropertyKey,
            Title = "Акцент",
            Category = "Appearance",
            Editor = PropertyEditorKind.Color,
            DefaultValueJson = JsonSerializer.Serialize("#38BDF8")
        },
        new DesignPropertyDescriptor
        {
            Key = ShowGlowPropertyKey,
            Title = "Мягкая подсветка",
            Category = "Appearance",
            Editor = PropertyEditorKind.Bool,
            DefaultValueJson = JsonSerializer.Serialize(true)
        },
        new DesignPropertyDescriptor
        {
            Key = ColumnsDefinitionTextPropertyKey,
            Title = "Колонки",
            Category = "Data",
            Editor = PropertyEditorKind.Collection,
            DefaultValueJson = JsonSerializer.Serialize(string.Empty)
        },
        new DesignPropertyDescriptor
        {
            Key = ChildrenPathPropertyKey,
            Title = "Путь к дочерним узлам",
            Category = "Data",
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize(string.Empty)
        },
        new DesignPropertyDescriptor
        {
            Key = IconModePropertyKey,
            Title = "Режим иконок",
            Category = "Appearance",
            Editor = PropertyEditorKind.Enum,
            DefaultValueJson = JsonSerializer.Serialize("Rules"),
            Options = new[]
            {
                new PropertyOption { Value = "None", Title = "Без иконок" },
                new PropertyOption { Value = "Uniform", Title = "Одна иконка" },
                new PropertyOption { Value = "Rules", Title = "По правилам" }
            }
        },
        new DesignPropertyDescriptor
        {
            Key = UniformIconGlyphPropertyKey,
            Title = "Единая иконка",
            Category = "Appearance",
            Editor = PropertyEditorKind.Text,
            DefaultValueJson = JsonSerializer.Serialize("◆")
        },
        new DesignPropertyDescriptor
        {
            Key = IconRulesTextPropertyKey,
            Title = "Правила иконок",
            Category = "Appearance",
            Editor = PropertyEditorKind.Collection,
            DefaultValueJson = JsonSerializer.Serialize("Project|◆|#38BDF8;Planning|◌|#F59E0B;Design|✎|#A78BFA;Development|▣|#22C55E;Testing|✔|#F97316")
        },
        new DesignPropertyDescriptor
        {
            Key = ExpandAllByDefaultPropertyKey,
            Title = "Разворачивать все узлы",
            Category = "Behavior",
            Editor = PropertyEditorKind.Bool,
            DefaultValueJson = JsonSerializer.Serialize(true)
        }
    };

    private readonly string _pluginId;
    private readonly string _pluginVersion;

    public DemoTreeListDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "TreeList";
    public string Category => "Данные";
    public string Description => "Иерархический список с привязкой к BindingSource. Может работать и как плоская таблица, и как дерево.";
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

        definition.BuiltInProperties[nameof(DemoTreeList.Text)] = "TreeList";
        definition.BuiltInProperties[nameof(DemoTreeList.Width)] = 520d;
        definition.BuiltInProperties[nameof(DemoTreeList.Height)] = 320d;
        definition.BuiltInProperties[nameof(DemoTreeList.Background)] = "#0F172A";
        definition.BuiltInProperties[nameof(DemoTreeList.BorderBrush)] = "#1E293B";
        definition.BuiltInProperties[nameof(DemoTreeList.BorderThickness)] = 1d;
        definition.BuiltInProperties[nameof(DemoTreeList.CornerRadius)] = 18d;
        definition.BuiltInProperties[nameof(DemoTreeList.Padding)] = 0d;
        definition.BuiltInProperties[nameof(DemoTreeList.Opacity)] = 1d;
        definition.BuiltInProperties[nameof(DemoTreeList.IsVisible)] = true;
        definition.BuiltInProperties[AutoGenerateColumnsPropertyName] = false;
        definition.BuiltInProperties[BindingSourceIdPropertyName] = context.BindingSources.FirstOrDefault()?.Id ?? string.Empty;

        definition.CustomProperties[AccentBrushPropertyKey] = JsonSerializer.Serialize("#38BDF8");
        definition.CustomProperties[ShowGlowPropertyKey] = JsonSerializer.Serialize(true);
        definition.CustomProperties[ColumnsDefinitionTextPropertyKey] = JsonSerializer.Serialize(string.Empty);
        definition.CustomProperties[ChildrenPathPropertyKey] = JsonSerializer.Serialize(string.Empty);
        definition.CustomProperties[IconModePropertyKey] = JsonSerializer.Serialize("Rules");
        definition.CustomProperties[UniformIconGlyphPropertyKey] = JsonSerializer.Serialize("◆");
        definition.CustomProperties[IconRulesTextPropertyKey] = JsonSerializer.Serialize("Project|◆|#38BDF8;Planning|◌|#F59E0B;Design|✎|#A78BFA;Development|▣|#22C55E;Testing|✔|#F97316");
        definition.CustomProperties[ExpandAllByDefaultPropertyKey] = JsonSerializer.Serialize(true);
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        var bindingSourceId = control.GetString(BindingSourceIdPropertyName, string.Empty);
        var bindingSource = context.GetBindingSource(bindingSourceId);
        var previewItemsProvider = context.Services.GetService(typeof(IPreviewBindingItemsProvider)) as IPreviewBindingItemsProvider;

        return new DemoTreeList
        {
            Width = Math.Max(300, control.GetDouble(nameof(DemoTreeList.Width), 520d)),
            Height = Math.Max(200, control.GetDouble(nameof(DemoTreeList.Height), 320d)),
            Text = control.GetString(nameof(DemoTreeList.Text), "TreeList"),
            AccentBrush = control.GetCustomValue(AccentBrushPropertyKey, "#38BDF8"),
            ShowGlow = control.GetCustomValue(ShowGlowPropertyKey, true),
            IconMode = control.GetCustomValue(IconModePropertyKey, "Rules"),
            UniformIconGlyph = control.GetCustomValue(UniformIconGlyphPropertyKey, "◆"),
            IconRulesText = control.GetCustomValue(IconRulesTextPropertyKey, "Project|◆|#38BDF8;Planning|◌|#F59E0B;Design|✎|#A78BFA;Development|▣|#22C55E;Testing|✔|#F97316"),
            ExpandAllByDefault = control.GetCustomValue(ExpandAllByDefaultPropertyKey, true),
            ColumnsDefinitionText = ResolveColumnsDefinition(control, bindingSource),
            ChildrenPath = control.GetCustomValue(ChildrenPathPropertyKey, string.Empty),
            AutoGenerateColumns = control.GetBool(AutoGenerateColumnsPropertyName, false),
            ItemsSource = previewItemsProvider?.GetItems(bindingSourceId),
            Background = ParseBrush(control.GetString(nameof(DemoTreeList.Background), "#0F172A"), "#0F172A"),
            BorderBrush = ParseBrush(control.GetString(nameof(DemoTreeList.BorderBrush), "#1E293B"), "#1E293B"),
            BorderThickness = new Thickness(Math.Max(0, control.GetDouble(nameof(DemoTreeList.BorderThickness), 1d))),
            CornerRadius = new CornerRadius(Math.Max(0, control.GetDouble(nameof(DemoTreeList.CornerRadius), 18d))),
            Padding = new Thickness(Math.Max(0, control.GetDouble(nameof(DemoTreeList.Padding), 0d))),
            Opacity = Math.Clamp(control.GetDouble(nameof(DemoTreeList.Opacity), 1d), 0d, 1d),
            IsVisible = control.GetBool(nameof(DemoTreeList.IsVisible), true)
        };
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        context.RegisterXmlNamespace("demo", "clr-namespace:DemoDesignerPlugin.Controls;assembly=DemoDesignerPlugin");

        var source = context.GetBindingSource(control.GetString(BindingSourceIdPropertyName, string.Empty));
        var itemsSourcePath = source?.Path ?? string.Empty;
        var columnsDefinition = ResolveColumnsDefinition(control, source);
        var attributes = new List<string>
        {
            $"x:Name=\"{EscapeXml(control.Name)}\"",
            $"Text=\"{EscapeXml(control.GetString(nameof(DemoTreeList.Text), "TreeList"))}\"",
            $"Width=\"{FormatDouble(control.GetDouble(nameof(DemoTreeList.Width), 520d))}\"",
            $"Height=\"{FormatDouble(control.GetDouble(nameof(DemoTreeList.Height), 320d))}\"",
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\"",
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\"",
            $"Background=\"{EscapeXml(control.GetString(nameof(DemoTreeList.Background), "#0F172A"))}\"",
            $"BorderBrush=\"{EscapeXml(control.GetString(nameof(DemoTreeList.BorderBrush), "#1E293B"))}\"",
            $"BorderThickness=\"{FormatDouble(control.GetDouble(nameof(DemoTreeList.BorderThickness), 1d))}\"",
            $"CornerRadius=\"{FormatDouble(control.GetDouble(nameof(DemoTreeList.CornerRadius), 18d))}\"",
            $"Padding=\"{FormatDouble(control.GetDouble(nameof(DemoTreeList.Padding), 0d))}\"",
            $"AccentBrush=\"{EscapeXml(control.GetCustomValue(AccentBrushPropertyKey, "#38BDF8"))}\"",
            $"ShowGlow=\"{BoolToXaml(control.GetCustomValue(ShowGlowPropertyKey, true))}\"",
            $"IconMode=\"{EscapeXml(control.GetCustomValue(IconModePropertyKey, "Rules"))}\"",
            $"UniformIconGlyph=\"{EscapeXml(control.GetCustomValue(UniformIconGlyphPropertyKey, "◆"))}\"",
            $"IconRulesText=\"{EscapeXml(control.GetCustomValue(IconRulesTextPropertyKey, "Project|◆|#38BDF8;Planning|◌|#F59E0B;Design|✎|#A78BFA;Development|▣|#22C55E;Testing|✔|#F97316"))}\"",
            $"ExpandAllByDefault=\"{BoolToXaml(control.GetCustomValue(ExpandAllByDefaultPropertyKey, true))}\"",
            $"AutoGenerateColumns=\"{BoolToXaml(control.GetBool(AutoGenerateColumnsPropertyName, false))}\""
        };

        if (!string.IsNullOrWhiteSpace(itemsSourcePath))
            attributes.Add($"ItemsSource=\"{{Binding {EscapeXml(itemsSourcePath)}}}\"");

        if (!string.IsNullOrWhiteSpace(columnsDefinition))
            attributes.Add($"ColumnsDefinitionText=\"{EscapeXml(columnsDefinition)}\"");

        var childrenPath = control.GetCustomValue(ChildrenPathPropertyKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(childrenPath))
            attributes.Add($"ChildrenPath=\"{EscapeXml(childrenPath)}\"");

        if (!control.GetBool(nameof(DemoTreeList.IsVisible), true))
            attributes.Add("IsVisible=\"False\"");

        var opacity = control.GetDouble(nameof(DemoTreeList.Opacity), 1d);
        if (Math.Abs(opacity - 1d) > 0.0001d)
            attributes.Add($"Opacity=\"{FormatDouble(opacity)}\"");

        writer.WriteLine(indentLevel, $"<demo:DemoTreeList {string.Join(" ", attributes)} />");
    }

    private static string ResolveColumnsDefinition(IDesignControlNode control, BindingSourceMetadata? source)
    {
        var explicitDefinition = control.GetCustomValue(ColumnsDefinitionTextPropertyKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(explicitDefinition))
            return explicitDefinition;

        if (control.GetBool(AutoGenerateColumnsPropertyName, false) || source is null)
            return string.Empty;

        var visibleFields = source.Fields
            .Where(field => field.IsVisible)
            .ToList();

        if (visibleFields.Count == 0)
            return string.Empty;

        return string.Join(";", visibleFields.Select(field => $"{field.Header}|{field.Path}"));
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

    private static string BoolToXaml(bool value) => value ? "True" : "False";
}
