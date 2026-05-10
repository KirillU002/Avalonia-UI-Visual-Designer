using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace DemoDesignerPlugin.Controls;

public sealed class DemoGridControl : Border
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DemoGridControl, string>(nameof(Text), "Клиенты");

    public static readonly StyledProperty<string> AccentBrushProperty =
        AvaloniaProperty.Register<DemoGridControl, string>(nameof(AccentBrush), "#60A5FA");

    public static readonly StyledProperty<string> HeaderBadgeTextProperty =
        AvaloniaProperty.Register<DemoGridControl, string>(nameof(HeaderBadgeText), "LIVE");

    public static readonly StyledProperty<bool> ShowGlowProperty =
        AvaloniaProperty.Register<DemoGridControl, bool>(nameof(ShowGlow), true);

    public static readonly StyledProperty<bool> ShowFilterGlyphsProperty =
        AvaloniaProperty.Register<DemoGridControl, bool>(nameof(ShowFilterGlyphs), true);

    public static readonly StyledProperty<string> HeaderStyleProperty =
        AvaloniaProperty.Register<DemoGridControl, string>(nameof(HeaderStyle), "Classic");

    private readonly Border _accentStrip;
    private readonly Border _badgeShell;
    private readonly TextBlock _badgeText;
    private readonly TextBlock _titleText;
    private readonly Border _headerShell;
    private readonly Grid _headerGrid;
    private readonly StackPanel _rowsHost;
    private bool _isInitialized;

    private static readonly IReadOnlyList<GridRowSample> SampleRows = new[]
    {
        new GridRowSample("Northwind Traders", "A-1024", "Контакт обновлён", true),
        new GridRowSample("Tailwind Logistics", "A-1025", "Ожидает счёт", false),
        new GridRowSample("Skyline Retail", "A-1026", "Новый лид", false),
        new GridRowSample("Contoso Labs", "A-1027", "Закрыто сегодня", true),
        new GridRowSample("Blue Harbor", "A-1028", "В работе", false),
        new GridRowSample("Apex Systems", "A-1029", "Приоритет", true)
    };

    public DemoGridControl()
    {
        Background = DemoVisualHelper.ParseBrush("#0F172A", "#0F172A");
        BorderBrush = DemoVisualHelper.ParseBrush("#1E293B", "#1E293B");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(0);
        ClipToBounds = true;
        MinWidth = 280;
        MinHeight = 180;

        _accentStrip = new Border
        {
            Width = 5,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(999)
        };

        _titleText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
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

        var titleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("5,*,Auto"),
            ColumnSpacing = 12
        };
        titleGrid.Children.Add(_accentStrip);
        titleGrid.Children.Add(_titleText);
        titleGrid.Children.Add(_badgeShell);
        Grid.SetColumn(_accentStrip, 0);
        Grid.SetColumn(_titleText, 1);
        Grid.SetColumn(_badgeShell, 2);

        _headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2.2*,1.1*,1.6*"),
            ColumnSpacing = 0
        };

        _headerShell = new Border
        {
            Padding = new Thickness(16, 10, 16, 12)
        };

        _rowsHost = new StackPanel
        {
            Spacing = 0
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*")
        };

        root.Children.Add(new Border
        {
            Padding = new Thickness(16, 14, 16, 10),
            Child = titleGrid
        });

        Grid.SetRow(_headerShell, 1);
        _headerShell.Child = _headerGrid;
        root.Children.Add(_headerShell);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _rowsHost
        };
        Grid.SetRow(scrollViewer, 2);
        root.Children.Add(scrollViewer);

        Child = root;

        _isInitialized = true;
        UpdateVisual(rebuildRows: true);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public string HeaderBadgeText
    {
        get => GetValue(HeaderBadgeTextProperty);
        set => SetValue(HeaderBadgeTextProperty, value);
    }

    public bool ShowGlow
    {
        get => GetValue(ShowGlowProperty);
        set => SetValue(ShowGlowProperty, value);
    }

    public bool ShowFilterGlyphs
    {
        get => GetValue(ShowFilterGlyphsProperty);
        set => SetValue(ShowFilterGlyphsProperty, value);
    }

    public string HeaderStyle
    {
        get => GetValue(HeaderStyleProperty);
        set => SetValue(HeaderStyleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!_isInitialized)
            return;

        if (change.Property == TextProperty
            || change.Property == AccentBrushProperty
            || change.Property == HeaderBadgeTextProperty
            || change.Property == ShowGlowProperty
            || change.Property == ShowFilterGlyphsProperty
            || change.Property == HeaderStyleProperty
            || change.Property == BackgroundProperty
            || change.Property == BorderBrushProperty)
        {
            UpdateVisual(rebuildRows: true);
        }
    }

    private void UpdateVisual(bool rebuildRows)
    {
        var accentColor = DemoVisualHelper.ParseColor(AccentBrush, "#60A5FA");
        var backgroundColor = DemoVisualHelper.GetSolidColor(Background, "#0F172A");
        var foregroundColor = DemoVisualHelper.GetReadableForeground(backgroundColor);
        var chromeColor = DemoVisualHelper.Blend(backgroundColor, Colors.White, 0.08);
        var rowBaseColor = DemoVisualHelper.Blend(backgroundColor, Colors.White, 0.04);
        var rowAltColor = DemoVisualHelper.Blend(backgroundColor, Colors.White, 0.08);
        var lineColor = DemoVisualHelper.WithAlpha(accentColor, (byte)(ShowGlow ? 110 : 45));

        _accentStrip.Background = new SolidColorBrush(accentColor);
        _accentStrip.Width = ShowGlow ? 7 : 5;
        _accentStrip.Opacity = ShowGlow ? 1d : 0.7d;

        _titleText.Text = string.IsNullOrWhiteSpace(Text) ? "Клиенты" : Text.Trim();
        _titleText.Foreground = new SolidColorBrush(foregroundColor);

        var hasBadge = !string.IsNullOrWhiteSpace(HeaderBadgeText);
        _badgeShell.IsVisible = hasBadge;
        _badgeText.Text = hasBadge ? HeaderBadgeText.Trim() : string.Empty;
        _badgeText.Foreground = new SolidColorBrush(backgroundColor);
        _badgeShell.Background = new SolidColorBrush(DemoVisualHelper.Blend(accentColor, Colors.White, 0.2));
        _badgeShell.BorderBrush = new SolidColorBrush(DemoVisualHelper.WithAlpha(accentColor, 180));
        _badgeShell.BorderThickness = new Thickness(1);

        _headerShell.Background = new SolidColorBrush(chromeColor);
        _headerShell.BorderBrush = new SolidColorBrush(lineColor);
        _headerShell.BorderThickness = new Thickness(0, 1, 0, 1);

        BuildHeader(accentColor, foregroundColor);

        if (rebuildRows)
            BuildRows(accentColor, foregroundColor, rowBaseColor, rowAltColor, lineColor);
    }

    private void BuildHeader(Color accentColor, Color foregroundColor)
    {
        _headerGrid.Children.Clear();
        var columnTitles = ResolveHeaderTitles();

        for (var columnIndex = 0; columnIndex < columnTitles.Count; columnIndex++)
        {
            var titleText = new TextBlock
            {
                Text = columnTitles[columnIndex],
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(foregroundColor, 230)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var cellContent = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(ShowFilterGlyphs ? "*,Auto" : "*")
            };
            cellContent.Children.Add(titleText);

            if (ShowFilterGlyphs)
            {
                var filterGlyph = new TextBlock
                {
                    Text = ResolveHeaderGlyph(),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(accentColor, 210)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(filterGlyph, 1);
                cellContent.Children.Add(filterGlyph);
            }

            var headerCell = new Border
            {
                Padding = new Thickness(0, 0, 10, 0),
                Child = cellContent
            };

            Grid.SetColumn(headerCell, columnIndex);
            _headerGrid.Children.Add(headerCell);
        }
    }

    private IReadOnlyList<string> ResolveHeaderTitles()
    {
        return (HeaderStyle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "compact" => new[]
            {
                "Клиент",
                "Код",
                "Статус"
            },
            "analytics" => new[]
            {
                "Сегмент",
                "KPI",
                "Инсайт"
            },
            _ => new[]
            {
                "Компания",
                "Код",
                "Состояние"
            }
        };
    }

    private string ResolveHeaderGlyph()
    {
        return (HeaderStyle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "analytics" => "Σ",
            "compact" => "•",
            _ => "⌕"
        };
    }

    private void BuildRows(Color accentColor, Color foregroundColor, Color rowBaseColor, Color rowAltColor, Color lineColor)
    {
        _rowsHost.Children.Clear();

        for (var index = 0; index < SampleRows.Count; index++)
        {
            var sample = SampleRows[index];
            var rowBackground = index % 2 == 0 ? rowBaseColor : rowAltColor;
            var rowBorder = new Border
            {
                Background = new SolidColorBrush(rowBackground),
                BorderBrush = new SolidColorBrush(DemoVisualHelper.WithAlpha(lineColor, 80)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 11)
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2.2*,1.1*,1.6*")
            };

            rowGrid.Children.Add(CreatePrimaryCell(sample.Company, sample.IsFeatured ? accentColor : foregroundColor, sample.IsFeatured));
            rowGrid.Children.Add(CreateSecondaryCell(sample.Code, foregroundColor, 1));
            rowGrid.Children.Add(CreateStatusCell(sample.Status, sample.IsFeatured, accentColor, foregroundColor, 2));

            rowBorder.Child = rowGrid;
            _rowsHost.Children.Add(rowBorder);
        }
    }

    private static Control CreatePrimaryCell(string text, Color accentColor, bool isFeatured)
    {
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    FontWeight = isFeatured ? FontWeight.Bold : FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(isFeatured ? accentColor : Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = isFeatured ? "Высокий приоритет" : "Обычная запись",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(Colors.White, 145))
                }
            }
        };
    }

    private static Control CreateSecondaryCell(string text, Color foregroundColor, int columnIndex)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(foregroundColor, 215))
        };
        Grid.SetColumn(textBlock, columnIndex);
        return textBlock;
    }

    private static Control CreateStatusCell(string text, bool isFeatured, Color accentColor, Color foregroundColor, int columnIndex)
    {
        var background = isFeatured
            ? DemoVisualHelper.Blend(accentColor, Colors.White, 0.1)
            : DemoVisualHelper.Blend(accentColor, Colors.Black, 0.25);
        var foreground = isFeatured ? DemoVisualHelper.GetReadableForeground(background) : foregroundColor;

        var badge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(DemoVisualHelper.WithAlpha(accentColor, 170)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(foreground)
            }
        };

        Grid.SetColumn(badge, columnIndex);
        return badge;
    }

    private sealed record GridRowSample(string Company, string Code, string Status, bool IsFeatured);
}
