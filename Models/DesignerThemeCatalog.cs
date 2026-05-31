using System;
using System.Collections.Generic;

namespace FormDesigner.Models;

/// <summary>
/// Набор готовых тем конструктора и экспортируемых форм.
/// Палитра хранит базовые цвета поверхности и стандартных контролов,
/// а также служит источником для Window.Resources при генерации XAML.
/// </summary>
public static class DesignerThemeCatalog
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    public static IReadOnlyList<string> AvailableThemes { get; } = new[]
    {
        Light,
        Dark
    };

    private static readonly FormThemePalette LightPalette = new()
    {
        Name = Light,
        SurfaceBackground = "#FFFFFF",
        SurfaceGridMinorColor = "#DCE4EE",
        SurfaceGridMajorColor = "#B7C7DA",
        TextBrush = "#0F172A",
        MutedTextBrush = "#475569",
        BorderBrush = "#94A3B8",
        InputBackground = "#FFFFFF",
        ContainerBackground = "#F8FAFC",
        ButtonBackground = "#2563EB",
        ButtonForeground = "#FFFFFF",
        ButtonBorderBrush = "#1D4ED8",
        AccentBrush = "#2563EB",
        AccentStrongBrush = "#1D4ED8",
        AccentForegroundBrush = "#FFFFFF",
        DataGridHeaderBackground = "#EAF2FF",
        DataGridHeaderForeground = "#0F172A",
        DataGridRowBackground = "#FFFFFF",
        DataGridAlternateRowBackground = "#F4F9FF"
    };

    private static readonly FormThemePalette DarkPalette = new()
    {
        Name = Dark,
        SurfaceBackground = "#0F172A",
        SurfaceGridMinorColor = "#1E293B",
        SurfaceGridMajorColor = "#334155",
        TextBrush = "#F8FAFC",
        MutedTextBrush = "#CBD5E1",
        BorderBrush = "#475569",
        InputBackground = "#111827",
        ContainerBackground = "#111827",
        ButtonBackground = "#3B82F6",
        ButtonForeground = "#F8FAFC",
        ButtonBorderBrush = "#60A5FA",
        AccentBrush = "#3B82F6",
        AccentStrongBrush = "#60A5FA",
        AccentForegroundBrush = "#F8FAFC",
        DataGridHeaderBackground = "#131C31",
        DataGridHeaderForeground = "#F8FAFC",
        DataGridRowBackground = "#0F172A",
        DataGridAlternateRowBackground = "#142038"
    };

    public static string NormalizeThemeName(string? value)
    {
        return value?.Trim() switch
        {
            Dark => Dark,
            _ => Light
        };
    }

    public static string InferThemeName(string? surfaceBackground)
    {
        if (AreEquivalent(surfaceBackground, DarkPalette.SurfaceBackground))
            return Dark;

        return Light;
    }

    public static FormThemePalette Get(string? themeName)
    {
        return NormalizeThemeName(themeName) == Dark ? DarkPalette : LightPalette;
    }

    public static ThemeControlDefaults GetControlDefaults(string controlType, string? themeName)
    {
        return GetControlDefaults(controlType, Get(themeName));
    }

    public static ThemeControlDefaults GetControlDefaults(string controlType, FormThemePalette palette)
    {
        return controlType switch
        {
            DesignerControlTypes.Group => new ThemeControlDefaults
            {
                Background = "Transparent",
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.Button => new ThemeControlDefaults
            {
                Background = palette.ButtonBackground,
                Foreground = palette.ButtonForeground,
                BorderBrush = palette.ButtonBorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.ButtonBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.ButtonForegroundBrush,
                BorderBrushResourceKey = ThemeResourceKeys.ButtonBorderBrush
            },
            DesignerControlTypes.TextBox => new ThemeControlDefaults
            {
                Background = palette.InputBackground,
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.InputBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.TextBlock => new ThemeControlDefaults
            {
                Foreground = palette.TextBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush
            },
            DesignerControlTypes.CheckBox => new ThemeControlDefaults
            {
                Foreground = palette.TextBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush
            },
            DesignerControlTypes.Border => new ThemeControlDefaults
            {
                Background = palette.ContainerBackground,
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.ContainerBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.StackLayout => new ThemeControlDefaults
            {
                Background = palette.ContainerBackground,
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.ContainerBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.FlexLayout => new ThemeControlDefaults
            {
                Background = palette.ContainerBackground,
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.ContainerBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.Image => new ThemeControlDefaults
            {
                Background = palette.ContainerBackground,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.ContainerBackgroundBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.LayoutGrid => new ThemeControlDefaults
            {
                Background = palette.SurfaceBackground,
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.WindowBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            },
            DesignerControlTypes.DataGrid => new ThemeControlDefaults
            {
                Background = palette.DataGridHeaderBackground,
                Foreground = palette.DataGridHeaderForeground,
                BorderBrush = palette.AccentStrongBrush,
                BackgroundResourceKey = ThemeResourceKeys.DataGridHeaderBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.DataGridHeaderForegroundBrush,
                BorderBrushResourceKey = ThemeResourceKeys.AccentStrongBrush
            },
            _ => new ThemeControlDefaults
            {
                Background = palette.SurfaceBackground,
                Foreground = palette.TextBrush,
                BorderBrush = palette.BorderBrush,
                BackgroundResourceKey = ThemeResourceKeys.WindowBackgroundBrush,
                ForegroundResourceKey = ThemeResourceKeys.TextBrush,
                BorderBrushResourceKey = ThemeResourceKeys.BorderBrush
            }
        };
    }

    public static bool AreEquivalent(string? left, string? right)
    {
        return string.Equals(NormalizeColorToken(left), NormalizeColorToken(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeColorToken(string? value)
    {
        return (value ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}

public static class ThemeResourceKeys
{
    public const string WindowBackgroundBrush = "ThemeWindowBackgroundBrush";
    public const string TextBrush = "ThemeTextBrush";
    public const string MutedTextBrush = "ThemeMutedTextBrush";
    public const string BorderBrush = "ThemeBorderBrush";
    public const string InputBackgroundBrush = "ThemeInputBackgroundBrush";
    public const string ContainerBackgroundBrush = "ThemeContainerBackgroundBrush";
    public const string ButtonBackgroundBrush = "ThemeButtonBackgroundBrush";
    public const string ButtonForegroundBrush = "ThemeButtonForegroundBrush";
    public const string ButtonBorderBrush = "ThemeButtonBorderBrush";
    public const string AccentBrush = "ThemeAccentBrush";
    public const string AccentStrongBrush = "ThemeAccentStrongBrush";
    public const string AccentForegroundBrush = "ThemeAccentForegroundBrush";
    public const string DataGridHeaderBackgroundBrush = "ThemeDataGridHeaderBackgroundBrush";
    public const string DataGridHeaderForegroundBrush = "ThemeDataGridHeaderForegroundBrush";
    public const string DataGridRowBackgroundBrush = "ThemeDataGridRowBackgroundBrush";
    public const string DataGridAlternateRowBackgroundBrush = "ThemeDataGridAlternateRowBackgroundBrush";
}

public sealed class FormThemePalette
{
    public string Name { get; init; } = "";
    public string SurfaceBackground { get; init; } = "";
    public string SurfaceGridMinorColor { get; init; } = "";
    public string SurfaceGridMajorColor { get; init; } = "";
    public string TextBrush { get; init; } = "";
    public string MutedTextBrush { get; init; } = "";
    public string BorderBrush { get; init; } = "";
    public string InputBackground { get; init; } = "";
    public string ContainerBackground { get; init; } = "";
    public string ButtonBackground { get; init; } = "";
    public string ButtonForeground { get; init; } = "";
    public string ButtonBorderBrush { get; init; } = "";
    public string AccentBrush { get; init; } = "";
    public string AccentStrongBrush { get; init; } = "";
    public string AccentForegroundBrush { get; init; } = "";
    public string DataGridHeaderBackground { get; init; } = "";
    public string DataGridHeaderForeground { get; init; } = "";
    public string DataGridRowBackground { get; init; } = "";
    public string DataGridAlternateRowBackground { get; init; } = "";
}

public sealed class ThemeControlDefaults
{
    public string? Background { get; init; }
    public string? Foreground { get; init; }
    public string? BorderBrush { get; init; }
    public string? BackgroundResourceKey { get; init; }
    public string? ForegroundResourceKey { get; init; }
    public string? BorderBrushResourceKey { get; init; }
}

