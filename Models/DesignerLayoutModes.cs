namespace FormDesigner.Models;

/// <summary>
/// Нормализованные значения layout-режимов для формы и контейнеров.
/// Храним их как строки, чтобы они без лишних преобразований попадали в JSON-документ.
/// </summary>
public static class DesignerLayoutModes
{
    public const string Absolute = "Absolute";
    public const string Stack = "Stack";
    public const string Grid = "Grid";
    public const string Flex = "Flex";

    public const string Vertical = "Vertical";
    public const string Horizontal = "Horizontal";

    public const string AlignStretch = "Stretch";
    public const string AlignLeft = "Left";
    public const string AlignCenter = "Center";
    public const string AlignRight = "Right";
    public const string AlignTop = "Top";
    public const string AlignBottom = "Bottom";

    public static string NormalizeMode(string? value)
    {
        return value?.Trim() switch
        {
            Grid => Grid,
            Flex => Flex,
            Stack => Stack,
            _ => Absolute
        };
    }

    public static string NormalizeOrientation(string? value)
    {
        return value?.Trim() switch
        {
            Horizontal => Horizontal,
            _ => Vertical
        };
    }

    public static string NormalizeHorizontalAlignment(string? value)
    {
        return value?.Trim() switch
        {
            AlignLeft => AlignLeft,
            AlignCenter => AlignCenter,
            AlignRight => AlignRight,
            _ => AlignStretch
        };
    }

    public static string NormalizeVerticalAlignment(string? value)
    {
        return value?.Trim() switch
        {
            AlignBottom => AlignBottom,
            AlignCenter => AlignCenter,
            AlignStretch => AlignStretch,
            _ => AlignTop
        };
    }

    public static bool IsAbsolute(string? value)
    {
        return NormalizeMode(value) == Absolute;
    }

    public static bool IsFlow(string? value)
    {
        return NormalizeMode(value) is Stack or Flex;
    }

    public static string GetModeForControlType(string? controlType)
    {
        return controlType switch
        {
            DesignerControlTypes.StackLayout => Stack,
            DesignerControlTypes.LayoutGrid => Grid,
            DesignerControlTypes.FlexLayout => Flex,
            _ => Absolute
        };
    }
}
