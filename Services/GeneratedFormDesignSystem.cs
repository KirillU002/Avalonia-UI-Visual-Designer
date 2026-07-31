using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FormDesigner.Services;

/// <summary>
/// Produces the semantic colour tokens and shared Avalonia styles embedded into
/// generated forms.  Keeping this in one place makes Export and AXAML Preview
/// render the same visual system without changing a document's model.
/// </summary>
public static class GeneratedFormDesignSystem
{
    public static string ResolveDefaultOrCustom(
        string? currentValue,
        string legacyDefaultValue,
        FormDesignTokens tokens,
        string tokenKey)
    {
        if (string.IsNullOrWhiteSpace(currentValue)
            || DesignerThemeCatalog.AreEquivalent(currentValue, legacyDefaultValue))
        {
            return tokens[tokenKey];
        }

        return currentValue.Trim();
    }

    public static FormDesignTokens CreateTokens(FormThemePalette palette, string? surfaceBackground)
    {
        var surface = ParseColor(surfaceBackground, palette.SurfaceBackground);
        var input = ParseColor(palette.InputBackground, palette.SurfaceBackground);
        var container = ParseColor(palette.ContainerBackground, palette.SurfaceBackground);
        var text = ParseColor(palette.TextBrush, "#0F172A");
        var mutedText = ParseColor(palette.MutedTextBrush, "#475569");
        var border = ParseColor(palette.BorderBrush, "#94A3B8");
        var accent = ParseColor(palette.AccentBrush, "#2563EB");
        var accentForeground = ParseColor(palette.AccentForegroundBrush, "#FFFFFF");
        var darkSurface = RelativeLuminance(surface) < 0.45;

        // The accent changes toward the readable text colour. This creates a
        // predictable hover/pressed ramp for both light and dark themes.
        var accentHover = Blend(accent, text, darkSurface ? 0.15 : 0.12);
        var accentPressed = Blend(accent, text, darkSurface ? 0.28 : 0.24);
        var accentStrong = Blend(accent, text, darkSurface ? 0.08 : 0.16);
        var controlSurface = Blend(input, surface, 0.38);
        var controlHover = Blend(controlSurface, accent, darkSurface ? 0.09 : 0.045);
        var controlPressed = Blend(controlSurface, accent, darkSurface ? 0.15 : 0.085);
        var disabledSurface = Blend(container, text, darkSurface ? 0.10 : 0.045);
        var disabledText = Blend(mutedText, surface, darkSurface ? 0.34 : 0.42);
        var dataGridHeader = Blend(surface, accent, darkSurface ? 0.14 : 0.075);
        var dataGridHeaderHover = Blend(surface, accent, darkSurface ? 0.22 : 0.12);
        var dataGridRow = surface;
        var dataGridAlternate = Blend(surface, accent, darkSurface ? 0.055 : 0.025);
        var dataGridGridLine = Blend(border, accent, darkSurface ? 0.10 : 0.16);

        return new FormDesignTokens(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ThemeResourceKeys.WindowBackgroundBrush] = ToHex(surface),
            [ThemeResourceKeys.TextBrush] = ToHex(text),
            [ThemeResourceKeys.MutedTextBrush] = ToHex(mutedText),
            [ThemeResourceKeys.BorderBrush] = ToHex(border),
            [ThemeResourceKeys.InputBackgroundBrush] = ToHex(input),
            [ThemeResourceKeys.ContainerBackgroundBrush] = ToHex(container),
            [ThemeResourceKeys.ControlSurfaceBrush] = ToHex(controlSurface),
            [ThemeResourceKeys.ControlHoverBrush] = ToHex(controlHover),
            [ThemeResourceKeys.ControlPressedBrush] = ToHex(controlPressed),
            [ThemeResourceKeys.ControlDisabledBrush] = ToHex(disabledSurface),
            [ThemeResourceKeys.DisabledTextBrush] = ToHex(disabledText),
            [ThemeResourceKeys.ButtonBackgroundBrush] = ToHex(accent),
            [ThemeResourceKeys.ButtonForegroundBrush] = ToHex(accentForeground),
            [ThemeResourceKeys.ButtonBorderBrush] = ToHex(accentStrong),
            [ThemeResourceKeys.AccentBrush] = ToHex(accent),
            [ThemeResourceKeys.AccentHoverBrush] = ToHex(accentHover),
            [ThemeResourceKeys.AccentPressedBrush] = ToHex(accentPressed),
            [ThemeResourceKeys.AccentStrongBrush] = ToHex(accentStrong),
            [ThemeResourceKeys.AccentForegroundBrush] = ToHex(accentForeground),
            [ThemeResourceKeys.AccentSubtleBrush] = ToHex(WithAlpha(accent, darkSurface ? 0x38 : 0x18)),
            [ThemeResourceKeys.AccentSubtleHoverBrush] = ToHex(WithAlpha(accent, darkSurface ? 0x52 : 0x2A)),
            [ThemeResourceKeys.FocusBorderBrush] = ToHex(WithAlpha(accent, 0xC8)),
            [ThemeResourceKeys.DataGridHeaderBackgroundBrush] = ToHex(dataGridHeader),
            [ThemeResourceKeys.DataGridHeaderHoverBrush] = ToHex(dataGridHeaderHover),
            [ThemeResourceKeys.DataGridHeaderForegroundBrush] = ToHex(text),
            [ThemeResourceKeys.DataGridRowBackgroundBrush] = ToHex(dataGridRow),
            [ThemeResourceKeys.DataGridAlternateRowBackgroundBrush] = ToHex(dataGridAlternate),
            [ThemeResourceKeys.DataGridHoverRowBackgroundBrush] = ToHex(WithAlpha(accent, darkSurface ? 0x3D : 0x14)),
            [ThemeResourceKeys.DataGridSelectedRowBackgroundBrush] = ToHex(WithAlpha(accent, darkSurface ? 0x66 : 0x26)),
            [ThemeResourceKeys.DataGridSelectedRowForegroundBrush] = ToHex(text),
            [ThemeResourceKeys.DataGridGridLineBrush] = ToHex(dataGridGridLine),
            [ThemeResourceKeys.GroupChipBackgroundBrush] = ToHex(WithAlpha(accent, darkSurface ? 0x54 : 0x1C)),
            [ThemeResourceKeys.GroupChipBorderBrush] = ToHex(WithAlpha(accent, darkSurface ? 0xBB : 0x8A)),
            [ThemeResourceKeys.GroupChipForegroundBrush] = ToHex(darkSurface ? Blend(accentForeground, accent, 0.18) : Blend(accent, text, 0.20))
        });
    }

    public static void AppendWindowResources(StringBuilder sb, int indentLevel, FormDesignTokens tokens)
    {
        foreach (var token in tokens.Values)
        {
            sb.Append(' ', indentLevel * 2)
                .Append("<SolidColorBrush x:Key=\"")
                .Append(token.Key)
                .Append("\" Color=\"")
                .Append(token.Value)
                .AppendLine("\" />");
        }
    }

    public static void AppendWindowStyles(StringBuilder sb, int indentLevel, bool includeDataGridStyles)
    {
        AppendStyle(sb, indentLevel, "TextBlock", new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush))
        });

        AppendStyle(sb, indentLevel, "Button", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ButtonBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.ButtonForegroundBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.ButtonBorderBrush)),
            Setter("BorderThickness", "1"),
            Setter("CornerRadius", "8"),
            Setter("Padding", "14,8"),
            Setter("MinHeight", "32")
        });
        AppendStyle(sb, indentLevel, "Button:pointerover", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.AccentHoverBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.AccentHoverBrush))
        });
        AppendStyle(sb, indentLevel, "Button:pressed", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.AccentPressedBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.AccentPressedBrush))
        });
        AppendStyle(sb, indentLevel, "Button:focus", new[]
        {
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.FocusBorderBrush)),
            Setter("BorderThickness", "2")
        });
        AppendStyle(sb, indentLevel, "Button:disabled", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ControlDisabledBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.DisabledTextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush)),
            Setter("Opacity", "0.72")
        });

        AppendInputStyles(sb, indentLevel, "TextBox", includeMinHeight: true);
        AppendInputStyles(sb, indentLevel, "ComboBox", includeMinHeight: true);
        AppendStyle(sb, indentLevel, "ComboBoxItem", new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("Padding", "12,7"),
            Setter("MinHeight", "32")
        });
        AppendStyle(sb, indentLevel, "ComboBoxItem:pointerover", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.AccentSubtleHoverBrush))
        });
        AppendStyle(sb, indentLevel, "ComboBoxItem:selected", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.AccentSubtleBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush))
        });

        AppendChoiceStyles(sb, indentLevel, "CheckBox");
        AppendChoiceStyles(sb, indentLevel, "RadioButton");

        AppendStyle(sb, indentLevel, "Border", new[]
        {
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush))
        });
        AppendStyle(sb, indentLevel, "ListBox", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ControlSurfaceBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush)),
            Setter("BorderThickness", "1"),
            Setter("CornerRadius", "8"),
            Setter("Padding", "4")
        });
        AppendSelectingItemStyles(sb, indentLevel, "ListBoxItem");
        AppendStyle(sb, indentLevel, "TreeView", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ControlSurfaceBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush)),
            Setter("BorderThickness", "1"),
            Setter("Padding", "4")
        });
        AppendSelectingItemStyles(sb, indentLevel, "TreeViewItem");
        AppendStyle(sb, indentLevel, "TabControl", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ContainerBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush))
        });
        AppendSelectingItemStyles(sb, indentLevel, "TabItem");
        AppendStyle(sb, indentLevel, "Menu", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ContainerBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush)),
            Setter("BorderThickness", "0,0,0,1")
        });
        AppendSelectingItemStyles(sb, indentLevel, "MenuItem");

        if (!includeDataGridStyles)
            return;

        AppendStyle(sb, indentLevel, "DataGrid", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.DataGridRowBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush)),
            Setter("BorderThickness", "1"),
            Setter("RowBackground", ThemeResource(ThemeResourceKeys.DataGridRowBackgroundBrush))
        });
        AppendStyle(sb, indentLevel, "DataGridColumnHeader", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.DataGridHeaderBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.DataGridHeaderForegroundBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.DataGridGridLineBrush)),
            Setter("BorderThickness", "0,0,1,1"),
            Setter("Padding", "10,6"),
            Setter("MinHeight", "42")
        });
        AppendStyle(sb, indentLevel, "DataGridColumnHeader:pointerover", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.DataGridHeaderHoverBrush))
        });
        AppendStyle(sb, indentLevel, "DataGridCell", new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.DataGridGridLineBrush)),
            Setter("BorderThickness", "0,0,1,1"),
            Setter("Padding", "10,4"),
            Setter("VerticalContentAlignment", "Center")
        });
        AppendStyle(sb, indentLevel, "DataGridCell:pointerover", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.DataGridHoverRowBackgroundBrush))
        });
        AppendStyle(sb, indentLevel, "DataGridCell:selected", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.DataGridSelectedRowBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.DataGridSelectedRowForegroundBrush))
        });
    }

    private static void AppendInputStyles(StringBuilder sb, int indentLevel, string selector, bool includeMinHeight)
    {
        var setters = new List<string>
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.InputBackgroundBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.BorderBrush)),
            Setter("BorderThickness", "1"),
            Setter("CornerRadius", "7"),
            Setter("Padding", "10,6")
        };
        if (includeMinHeight)
            setters.Add(Setter("MinHeight", "32"));

        AppendStyle(sb, indentLevel, selector, setters);
        AppendStyle(sb, indentLevel, selector + ":pointerover", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ControlHoverBrush)),
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.AccentHoverBrush))
        });
        AppendStyle(sb, indentLevel, selector + ":focus", new[]
        {
            Setter("BorderBrush", ThemeResource(ThemeResourceKeys.FocusBorderBrush)),
            Setter("BorderThickness", "2")
        });
        AppendStyle(sb, indentLevel, selector + ":disabled", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.ControlDisabledBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.DisabledTextBrush)),
            Setter("Opacity", "0.72")
        });
    }

    private static void AppendChoiceStyles(StringBuilder sb, int indentLevel, string selector)
    {
        AppendStyle(sb, indentLevel, selector, new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("Padding", "4,3")
        });
        AppendStyle(sb, indentLevel, selector + ":pointerover", new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.AccentHoverBrush))
        });
        AppendStyle(sb, indentLevel, selector + ":disabled", new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.DisabledTextBrush)),
            Setter("Opacity", "0.72")
        });
    }

    private static void AppendSelectingItemStyles(StringBuilder sb, int indentLevel, string selector)
    {
        AppendStyle(sb, indentLevel, selector, new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush)),
            Setter("Padding", "10,6"),
            Setter("CornerRadius", "6")
        });
        AppendStyle(sb, indentLevel, selector + ":pointerover", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.AccentSubtleHoverBrush))
        });
        AppendStyle(sb, indentLevel, selector + ":selected", new[]
        {
            Setter("Background", ThemeResource(ThemeResourceKeys.AccentSubtleBrush)),
            Setter("Foreground", ThemeResource(ThemeResourceKeys.TextBrush))
        });
        AppendStyle(sb, indentLevel, selector + ":disabled", new[]
        {
            Setter("Foreground", ThemeResource(ThemeResourceKeys.DisabledTextBrush)),
            Setter("Opacity", "0.72")
        });
    }

    private static void AppendStyle(StringBuilder sb, int indentLevel, string selector, IEnumerable<string> setters)
    {
        sb.Append(' ', indentLevel * 2).Append("<Style Selector=\"").Append(selector).AppendLine("\">");
        foreach (var setter in setters)
            sb.Append(' ', (indentLevel + 1) * 2).AppendLine(setter);
        sb.Append(' ', indentLevel * 2).AppendLine("</Style>");
    }

    private static string Setter(string property, string value) => $"<Setter Property=\"{property}\" Value=\"{value}\" />";

    private static string ThemeResource(string key) => "{DynamicResource " + key + "}";

    private static RgbaColor ParseColor(string? value, string fallback)
    {
        return TryParseColor(value, out var color)
            ? color
            : TryParseColor(fallback, out color)
                ? color
                : new RgbaColor(255, 37, 99, 235);
    }

    private static bool TryParseColor(string? value, out RgbaColor color)
    {
        color = default;
        var text = (value ?? string.Empty).Trim();
        if (!text.StartsWith("#", StringComparison.Ordinal))
            return false;

        var hex = text[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }
        else if (hex.Length == 4)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], hex[3], hex[3]);
        }

        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var packed))
            return false;

        color = new RgbaColor(
            (byte)(packed >> 24),
            (byte)(packed >> 16),
            (byte)(packed >> 8),
            (byte)packed);
        return true;
    }

    private static RgbaColor Blend(RgbaColor from, RgbaColor to, double amount)
    {
        var ratio = Math.Clamp(amount, 0d, 1d);
        static byte Mix(byte a, byte b, double value) => (byte)Math.Round(a + ((b - a) * value));
        return new RgbaColor(
            Mix(from.A, to.A, ratio),
            Mix(from.R, to.R, ratio),
            Mix(from.G, to.G, ratio),
            Mix(from.B, to.B, ratio));
    }

    private static RgbaColor WithAlpha(RgbaColor color, int alpha) =>
        new((byte)Math.Clamp(alpha, byte.MinValue, byte.MaxValue), color.R, color.G, color.B);

    private static double RelativeLuminance(RgbaColor color)
    {
        static double Linear(byte channel)
        {
            var normalized = channel / 255d;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    private static string ToHex(RgbaColor color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private readonly record struct RgbaColor(byte A, byte R, byte G, byte B);
}

public sealed class FormDesignTokens
{
    public FormDesignTokens(IReadOnlyDictionary<string, string> values)
    {
        Values = values;
    }

    public IReadOnlyDictionary<string, string> Values { get; }

    public string this[string key] => Values[key];
}
