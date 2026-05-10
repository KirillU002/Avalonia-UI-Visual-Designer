using Avalonia.Media;
using System;

namespace DemoDesignerPlugin.Controls;

internal static class DemoVisualHelper
{
    public static IBrush ParseBrush(string value, string fallback)
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

    public static Color ParseColor(string value, string fallback)
    {
        try
        {
            return Color.Parse(string.IsNullOrWhiteSpace(value) ? fallback : value);
        }
        catch
        {
            return Color.Parse(fallback);
        }
    }

    public static Color GetSolidColor(IBrush? brush, string fallback)
    {
        return brush is SolidColorBrush solidBrush
            ? solidBrush.Color
            : ParseColor(fallback, fallback);
    }

    public static Color GetReadableForeground(Color background)
    {
        var luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance >= 145 ? Color.Parse("#0F172A") : Colors.White;
    }

    public static Color Blend(Color source, Color target, double amount)
    {
        var normalized = Math.Clamp(amount, 0d, 1d);
        byte Mix(byte left, byte right) => (byte)Math.Clamp(left + ((right - left) * normalized), 0d, 255d);

        return Color.FromArgb(
            Mix(source.A, target.A),
            Mix(source.R, target.R),
            Mix(source.G, target.G),
            Mix(source.B, target.B));
    }

    public static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
