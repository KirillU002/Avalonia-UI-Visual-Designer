using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace DemoDesignerPlugin.Controls;

public sealed class DemoDevButton : Border
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DemoDevButton, string>(nameof(Text), "Открыть карточку");

    public static readonly StyledProperty<string> BadgeTextProperty =
        AvaloniaProperty.Register<DemoDevButton, string>(nameof(BadgeText), string.Empty);

    public static readonly StyledProperty<string> AccentBrushProperty =
        AvaloniaProperty.Register<DemoDevButton, string>(nameof(AccentBrush), "#38BDF8");

    public static readonly StyledProperty<bool> ShowGlowProperty =
        AvaloniaProperty.Register<DemoDevButton, bool>(nameof(ShowGlow), true);

    private readonly Border _accentBar;
    private readonly Border _badgeShell;
    private readonly TextBlock _badgeText;
    private readonly TextBlock _textBlock;
    private bool _isInitialized;

    public DemoDevButton()
    {
        Padding = new Thickness(14, 10);
        CornerRadius = new CornerRadius(20);
        BorderThickness = new Thickness(1);
        Background = ParseBrush("#0F172A");
        BorderBrush = ParseBrush("#1E293B");
        MinWidth = 120;
        MinHeight = 44;

        _accentBar = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(999),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _textBlock = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _badgeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        _badgeShell = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _badgeText
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4,*,Auto"),
            ColumnSpacing = 12
        };
        grid.Children.Add(_accentBar);
        grid.Children.Add(_textBlock);
        grid.Children.Add(_badgeShell);

        Grid.SetColumn(_accentBar, 0);
        Grid.SetColumn(_textBlock, 1);
        Grid.SetColumn(_badgeShell, 2);

        Child = grid;
        _isInitialized = true;
        UpdateVisual();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public string AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public bool ShowGlow
    {
        get => GetValue(ShowGlowProperty);
        set => SetValue(ShowGlowProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!_isInitialized)
            return;

        if (change.Property == TextProperty
            || change.Property == BadgeTextProperty
            || change.Property == AccentBrushProperty
            || change.Property == ShowGlowProperty
            || change.Property == BackgroundProperty
            || change.Property == BorderBrushProperty)
        {
            UpdateVisual();
        }
    }

    private void UpdateVisual()
    {
        var accentColor = ParseColor(AccentBrush, "#38BDF8");
        var backgroundColor = GetSolidColor(Background) ?? ParseColor("#0F172A", "#0F172A");
        var foregroundColor = GetReadableForeground(backgroundColor);

        _textBlock.Text = string.IsNullOrWhiteSpace(Text) ? "Открыть карточку" : Text.Trim();
        _textBlock.Foreground = new SolidColorBrush(foregroundColor);

        var normalizedBadgeText = string.Equals(BadgeText?.Trim(), "DEMO", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : BadgeText?.Trim() ?? string.Empty;
        var hasBadge = !string.IsNullOrWhiteSpace(normalizedBadgeText);
        _badgeShell.IsVisible = hasBadge;
        _badgeText.Text = hasBadge ? normalizedBadgeText : string.Empty;
        _badgeText.Foreground = new SolidColorBrush(backgroundColor);
        _badgeShell.Background = new SolidColorBrush(Blend(accentColor, Colors.White, 0.18));
        _badgeShell.BorderBrush = new SolidColorBrush(WithAlpha(accentColor, 190));
        _badgeShell.BorderThickness = new Thickness(1);

        _accentBar.Background = new SolidColorBrush(accentColor);
        _accentBar.Width = ShowGlow ? 6 : 4;
        _accentBar.Opacity = ShowGlow ? 1.0 : 0.55;
    }

    private static IBrush ParseBrush(string value)
    {
        return Brush.Parse(value);
    }

    private static Color ParseColor(string value, string fallback)
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

    private static Color? GetSolidColor(IBrush? brush)
    {
        return brush is SolidColorBrush solidBrush ? solidBrush.Color : null;
    }

    private static Color GetReadableForeground(Color background)
    {
        var luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance >= 145 ? Color.Parse("#0F172A") : Colors.White;
    }

    private static Color Blend(Color source, Color target, double amount)
    {
        var normalized = Math.Clamp(amount, 0, 1);
        byte Mix(byte left, byte right) => (byte)Math.Clamp(left + ((right - left) * normalized), 0, 255);

        return Color.FromArgb(
            Mix(source.A, target.A),
            Mix(source.R, target.R),
            Mix(source.G, target.G),
            Mix(source.B, target.B));
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }
}
