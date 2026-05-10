using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DemoDesignerPlugin.Controls;

public sealed class DemoTreeList : Border
{
    private const int MaxRenderedNodes = 60;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(Text), "TreeList");

    public static readonly StyledProperty<string> AccentBrushProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(AccentBrush), "#38BDF8");

    public static readonly StyledProperty<bool> ShowGlowProperty =
        AvaloniaProperty.Register<DemoTreeList, bool>(nameof(ShowGlow), true);

    public static readonly StyledProperty<string> IconModeProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(IconMode), "Rules");

    public static readonly StyledProperty<string> UniformIconGlyphProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(UniformIconGlyph), "◆");

    public static readonly StyledProperty<string> IconRulesTextProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(IconRulesText), "Project|◆|#38BDF8;Planning|◌|#F59E0B;Design|✎|#A78BFA;Development|▣|#22C55E;Testing|✔|#F97316");

    public static readonly StyledProperty<bool> ExpandAllByDefaultProperty =
        AvaloniaProperty.Register<DemoTreeList, bool>(nameof(ExpandAllByDefault), true);

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DemoTreeList, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> ColumnsDefinitionTextProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(ColumnsDefinitionText), string.Empty);

    public static readonly StyledProperty<bool> AutoGenerateColumnsProperty =
        AvaloniaProperty.Register<DemoTreeList, bool>(nameof(AutoGenerateColumns), false);

    public static readonly StyledProperty<string> ChildrenPathProperty =
        AvaloniaProperty.Register<DemoTreeList, string>(nameof(ChildrenPath), string.Empty);

    private readonly Border _accentStrip;
    private readonly Border _modeShell;
    private readonly TextBlock _modeText;
    private readonly TextBlock _titleText;
    private readonly Border _headerShell;
    private readonly Grid _headerGrid;
    private readonly StackPanel _rowsHost;
    private bool _isInitialized;

    private IReadOnlyList<DemoColumnDefinition> _activeColumns = Array.Empty<DemoColumnDefinition>();
    private IReadOnlyList<TreeListRowNode> _rootNodes = Array.Empty<TreeListRowNode>();

    public DemoTreeList()
    {
        Background = DemoVisualHelper.ParseBrush("#0F172A", "#0F172A");
        BorderBrush = DemoVisualHelper.ParseBrush("#1E293B", "#1E293B");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(0);
        ClipToBounds = true;
        MinWidth = 320;
        MinHeight = 220;

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

        _modeText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        _modeShell = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _modeText
        };

        var titleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("5,*,Auto"),
            ColumnSpacing = 12
        };
        titleGrid.Children.Add(_accentStrip);
        titleGrid.Children.Add(_titleText);
        titleGrid.Children.Add(_modeShell);
        Grid.SetColumn(_accentStrip, 0);
        Grid.SetColumn(_titleText, 1);
        Grid.SetColumn(_modeShell, 2);

        _headerGrid = new Grid();

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

        _headerShell.Child = _headerGrid;
        Grid.SetRow(_headerShell, 1);
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
        UpdateVisual(resetExpansion: true);
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

    public bool ShowGlow
    {
        get => GetValue(ShowGlowProperty);
        set => SetValue(ShowGlowProperty, value);
    }

    public string IconMode
    {
        get => GetValue(IconModeProperty);
        set => SetValue(IconModeProperty, value);
    }

    public string UniformIconGlyph
    {
        get => GetValue(UniformIconGlyphProperty);
        set => SetValue(UniformIconGlyphProperty, value);
    }

    public string IconRulesText
    {
        get => GetValue(IconRulesTextProperty);
        set => SetValue(IconRulesTextProperty, value);
    }

    public bool ExpandAllByDefault
    {
        get => GetValue(ExpandAllByDefaultProperty);
        set => SetValue(ExpandAllByDefaultProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string ColumnsDefinitionText
    {
        get => GetValue(ColumnsDefinitionTextProperty);
        set => SetValue(ColumnsDefinitionTextProperty, value);
    }

    public bool AutoGenerateColumns
    {
        get => GetValue(AutoGenerateColumnsProperty);
        set => SetValue(AutoGenerateColumnsProperty, value);
    }

    public string ChildrenPath
    {
        get => GetValue(ChildrenPathProperty);
        set => SetValue(ChildrenPathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!_isInitialized)
            return;

        var resetExpansion = change.Property == ExpandAllByDefaultProperty
            || change.Property == ItemsSourceProperty
            || change.Property == ChildrenPathProperty;

        if (resetExpansion
            || change.Property == TextProperty
            || change.Property == AccentBrushProperty
            || change.Property == ShowGlowProperty
            || change.Property == IconModeProperty
            || change.Property == UniformIconGlyphProperty
            || change.Property == IconRulesTextProperty
            || change.Property == ColumnsDefinitionTextProperty
            || change.Property == AutoGenerateColumnsProperty
            || change.Property == BackgroundProperty
            || change.Property == BorderBrushProperty)
        {
            UpdateVisual(resetExpansion);
        }
    }

    private void UpdateVisual(bool resetExpansion)
    {
        _activeColumns = ResolveColumns();
        _rootNodes = ResolveNodes(_activeColumns);

        if (resetExpansion)
            ApplyExpandedState(ExpandAllByDefault);

        var accentColor = DemoVisualHelper.ParseColor(AccentBrush, "#38BDF8");
        var backgroundColor = DemoVisualHelper.GetSolidColor(Background, "#0F172A");
        var foregroundColor = DemoVisualHelper.GetReadableForeground(backgroundColor);
        var chromeColor = DemoVisualHelper.Blend(backgroundColor, Colors.White, 0.08);
        var rowBaseColor = DemoVisualHelper.Blend(backgroundColor, Colors.White, 0.045);
        var rowAltColor = DemoVisualHelper.Blend(backgroundColor, Colors.White, 0.085);
        var lineColor = DemoVisualHelper.WithAlpha(accentColor, (byte)(ShowGlow ? 105 : 45));

        _accentStrip.Background = new SolidColorBrush(accentColor);
        _accentStrip.Width = ShowGlow ? 7 : 5;
        _titleText.Text = string.IsNullOrWhiteSpace(Text) ? "TreeList" : Text.Trim();
        _titleText.Foreground = new SolidColorBrush(foregroundColor);

        _modeText.Text = GetModeBadgeTitle();
        _modeText.Foreground = new SolidColorBrush(backgroundColor);
        _modeShell.Background = new SolidColorBrush(DemoVisualHelper.Blend(accentColor, Colors.White, 0.18));
        _modeShell.BorderBrush = new SolidColorBrush(DemoVisualHelper.WithAlpha(accentColor, 185));
        _modeShell.BorderThickness = new Thickness(1);

        _headerShell.Background = new SolidColorBrush(chromeColor);
        _headerShell.BorderBrush = new SolidColorBrush(lineColor);
        _headerShell.BorderThickness = new Thickness(0, 1, 0, 1);

        BuildHeader(_activeColumns, accentColor, foregroundColor);
        BuildRows(_activeColumns, accentColor, foregroundColor, rowBaseColor, rowAltColor, lineColor);
    }

    private IReadOnlyList<DemoColumnDefinition> ResolveColumns()
    {
        var columns = DemoDataBindingHelper.ResolveColumns(ColumnsDefinitionText, ItemsSource, AutoGenerateColumns, maxColumns: 4);
        if (columns.Count > 0)
            return columns;

        return new[]
        {
            new DemoColumnDefinition("Элемент", "Title"),
            new DemoColumnDefinition("Исполнитель", "Owner"),
            new DemoColumnDefinition("Статус", "Status")
        };
    }

    private IReadOnlyList<TreeListRowNode> ResolveNodes(IReadOnlyList<DemoColumnDefinition> columns)
    {
        if (ItemsSource is not null)
        {
            var budget = MaxRenderedNodes;
            var nodes = BuildNodes(ItemsSource, columns, NormalizeChildrenPath(ChildrenPath), depth: 0, ref budget);
            if (nodes.Count > 0)
                return nodes;
        }

        return CreateSampleTree();
    }

    private static IReadOnlyList<TreeListRowNode> BuildNodes(
        IEnumerable itemsSource,
        IReadOnlyList<DemoColumnDefinition> columns,
        string childrenPath,
        int depth,
        ref int remainingBudget)
    {
        var result = new List<TreeListRowNode>();

        foreach (var item in itemsSource.Cast<object?>())
        {
            if (remainingBudget <= 0)
                break;

            if (item is null)
                continue;

            remainingBudget--;

            var cells = columns
                .Select(column => FormatValue(DemoDataBindingHelper.ResolveValue(item, column.Path)))
                .ToList();

            if (cells.Count == 0)
                cells.Add(FormatValue(item));

            var node = new TreeListRowNode(cells, depth);

            if (!string.IsNullOrWhiteSpace(childrenPath))
            {
                var children = DemoDataBindingHelper.ResolveChildren(item, childrenPath);
                if (children is not null)
                {
                    foreach (var child in BuildNodes(children, columns, childrenPath, depth + 1, ref remainingBudget))
                        node.Children.Add(child);
                }
            }

            result.Add(node);
        }

        return result;
    }

    private void BuildHeader(IReadOnlyList<DemoColumnDefinition> columns, Color accentColor, Color foregroundColor)
    {
        _headerGrid.Children.Clear();
        _headerGrid.ColumnDefinitions = CreateColumnDefinitions(columns.Count);

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var title = columns[columnIndex].Header;
            var textBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(title) ? $"Колонка {columnIndex + 1}" : title,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(foregroundColor, 230))
            };

            Control content = textBlock;
            if (columnIndex == 0)
            {
                var cell = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto")
                };
                cell.Children.Add(textBlock);
                var treeGlyph = new TextBlock
                {
                    Text = "⇅",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(accentColor, 200)),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(treeGlyph, 1);
                cell.Children.Add(treeGlyph);
                content = cell;
            }

            var headerCell = new Border
            {
                Padding = new Thickness(0, 0, 10, 0),
                Child = content
            };
            Grid.SetColumn(headerCell, columnIndex);
            _headerGrid.Children.Add(headerCell);
        }
    }

    private void BuildRows(
        IReadOnlyList<DemoColumnDefinition> columns,
        Color accentColor,
        Color foregroundColor,
        Color rowBaseColor,
        Color rowAltColor,
        Color lineColor)
    {
        _rowsHost.Children.Clear();
        var iconMode = ParseIconMode(IconMode);
        var rules = ParseRules(IconRulesText);
        var visibleNodes = EnumerateVisibleNodes(_rootNodes).ToList();

        for (var rowIndex = 0; rowIndex < visibleNodes.Count; rowIndex++)
        {
            var node = visibleNodes[rowIndex];
            var rowBackground = rowIndex % 2 == 0 ? rowBaseColor : rowAltColor;
            var rowForeground = foregroundColor;

            var rowBorder = new Border
            {
                Background = new SolidColorBrush(rowBackground),
                BorderBrush = new SolidColorBrush(DemoVisualHelper.WithAlpha(lineColor, 80)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 9)
            };

            var rowGrid = new Grid
            {
                ColumnDefinitions = CreateColumnDefinitions(columns.Count)
            };

            rowGrid.Children.Add(CreateNameCell(node, iconMode, rules, accentColor, rowForeground));
            for (var columnIndex = 1; columnIndex < columns.Count; columnIndex++)
            {
                var value = node.GetCell(columnIndex);
                rowGrid.Children.Add(CreateSecondaryCell(value, rowForeground, columnIndex, columnIndex == columns.Count - 1));
            }

            rowBorder.Child = rowGrid;
            _rowsHost.Children.Add(rowBorder);
        }
    }

    private Control CreateNameCell(TreeListRowNode node, DemoTreeIconMode iconMode, IReadOnlyList<IconRule> rules, Color accentColor, Color foregroundColor)
    {
        var host = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(node.Depth * 18, 0, 10, 0)
        };

        var toggleHost = CreateToggleGlyph(node, accentColor, foregroundColor);
        host.Children.Add(toggleHost);

        var icon = ResolveIcon(node, iconMode, rules, accentColor);
        if (!string.IsNullOrWhiteSpace(icon.Glyph))
        {
            var iconText = new TextBlock
            {
                Text = icon.Glyph,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(icon.Color),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconText, 1);
            host.Children.Add(iconText);
        }

        var content = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(node.GetCell(0)) ? "Элемент" : node.GetCell(0),
            FontSize = 13,
            FontWeight = node.HasChildren ? FontWeight.SemiBold : FontWeight.Medium,
            Foreground = new SolidColorBrush(foregroundColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(content, 2);
        host.Children.Add(content);
        return host;
    }

    private Control CreateToggleGlyph(TreeListRowNode node, Color accentColor, Color foregroundColor)
    {
        if (!node.HasChildren)
        {
            return new Border
            {
                Width = 14,
                Height = 14,
                Background = Brushes.Transparent
            };
        }

        var glyph = new TextBlock
        {
            Text = node.IsExpanded ? "▾" : "▸",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(ShowGlow ? accentColor : foregroundColor, 220)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var shell = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(DemoVisualHelper.WithAlpha(accentColor, 24)),
            Child = glyph,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        shell.PointerPressed += (_, e) =>
        {
            node.IsExpanded = !node.IsExpanded;
            UpdateVisual(resetExpansion: false);
            e.Handled = true;
        };

        return shell;
    }

    private static Control CreateSecondaryCell(string value, Color foregroundColor, int columnIndex, bool emphasize)
    {
        Control content;
        if (emphasize && LooksLikeStatus(value))
        {
            var stateColor = value switch
            {
                "Completed" or "Готово" or "Завершено" => Color.Parse("#22C55E"),
                "In progress" or "В работе" or "Активно" => Color.Parse("#F59E0B"),
                "Needs review" or "На проверке" => Color.Parse("#F97316"),
                _ => Color.Parse("#38BDF8")
            };

            content = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(DemoVisualHelper.WithAlpha(stateColor, 40)),
                BorderBrush = new SolidColorBrush(DemoVisualHelper.WithAlpha(stateColor, 180)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5),
                Child = new TextBlock
                {
                    Text = value,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(stateColor)
                }
            };
        }
        else
        {
            content = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(DemoVisualHelper.WithAlpha(foregroundColor, 220)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        Grid.SetColumn(content, columnIndex);
        return content;
    }

    private string GetModeBadgeTitle()
    {
        if (ItemsSource is null)
            return "демо";

        if (!string.IsNullOrWhiteSpace(NormalizeChildrenPath(ChildrenPath)))
            return AutoGenerateColumns ? "иерархия auto" : "иерархия";

        return AutoGenerateColumns ? "данные auto" : "данные";
    }

    private void ApplyExpandedState(bool expanded)
    {
        foreach (var node in _rootNodes)
            SetExpandedRecursive(node, expanded);
    }

    private static void SetExpandedRecursive(TreeListRowNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        foreach (var child in node.Children)
            SetExpandedRecursive(child, expanded);
    }

    private static IEnumerable<TreeListRowNode> EnumerateVisibleNodes(IEnumerable<TreeListRowNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            if (!node.IsExpanded)
                continue;

            foreach (var child in EnumerateVisibleNodes(node.Children))
                yield return child;
        }
    }

    private static ColumnDefinitions CreateColumnDefinitions(int columnCount)
    {
        var definitions = new ColumnDefinitions();
        for (var index = 0; index < Math.Max(1, columnCount); index++)
        {
            var width = index == 0 ? 2.4 : 1.25;
            definitions.Add(new ColumnDefinition(width, GridUnitType.Star));
        }

        return definitions;
    }

    private static string NormalizeChildrenPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("g", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool LooksLikeStatus(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("work", StringComparison.OrdinalIgnoreCase)
            || value.Contains("review", StringComparison.OrdinalIgnoreCase)
            || value.Contains("completed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("готов", StringComparison.OrdinalIgnoreCase)
            || value.Contains("работ", StringComparison.OrdinalIgnoreCase)
            || value.Contains("провер", StringComparison.OrdinalIgnoreCase);
    }

    private static DemoTreeIconMode ParseIconMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "uniform" => DemoTreeIconMode.Uniform,
            "rules" => DemoTreeIconMode.Rules,
            _ => DemoTreeIconMode.None
        };
    }

    private (string Glyph, Color Color) ResolveIcon(TreeListRowNode node, DemoTreeIconMode iconMode, IReadOnlyList<IconRule> rules, Color accentColor)
    {
        return iconMode switch
        {
            DemoTreeIconMode.None => (string.Empty, accentColor),
            DemoTreeIconMode.Uniform => (string.IsNullOrWhiteSpace(UniformIconGlyph) ? "◆" : UniformIconGlyph.Trim(), accentColor),
            DemoTreeIconMode.Rules => ResolveRuleIcon(node, rules, accentColor),
            _ => (string.Empty, accentColor)
        };
    }

    private static (string Glyph, Color Color) ResolveRuleIcon(TreeListRowNode node, IReadOnlyList<IconRule> rules, Color accentColor)
    {
        var title = node.GetCell(0);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.MatchText))
                continue;

            if (!title.Contains(rule.MatchText, StringComparison.OrdinalIgnoreCase))
                continue;

            return (rule.Glyph, rule.Color);
        }

        return (string.Empty, accentColor);
    }

    private static IReadOnlyList<IconRule> ParseRules(string? rulesText)
    {
        if (string.IsNullOrWhiteSpace(rulesText))
            return Array.Empty<IconRule>();

        var rules = new List<IconRule>();
        var chunks = rulesText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var chunk in chunks)
        {
            var parts = chunk.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                continue;

            var match = parts[0];
            var glyph = parts[1];
            var color = parts.Length > 2
                ? DemoVisualHelper.ParseColor(parts[2], "#38BDF8")
                : Color.Parse("#38BDF8");

            if (string.IsNullOrWhiteSpace(match) || string.IsNullOrWhiteSpace(glyph))
                continue;

            rules.Add(new IconRule(match, glyph, color));
        }

        return rules;
    }

    private static IReadOnlyList<TreeListRowNode> CreateSampleTree()
    {
        var planning = new TreeListRowNode(new[] { "Planning", "Анна Миронова", "Завершено" }, 1);
        var design = new TreeListRowNode(new[] { "Design", "Максим Орлов", "В работе" }, 1);
        var development = new TreeListRowNode(new[] { "Development", "Ирина Волкова", "В работе" }, 1);
        var testing = new TreeListRowNode(new[] { "Testing", "Егор Соколов", "На проверке" }, 1);

        var project = new TreeListRowNode(new[] { "Project: CRM", "Команда A", "В работе" }, 0);
        project.Children.Add(planning);
        project.Children.Add(design);
        project.Children.Add(development);
        project.Children.Add(testing);

        var release = new TreeListRowNode(new[] { "Release 2.1", "Команда B", "Готово" }, 0);
        release.Children.Add(new TreeListRowNode(new[] { "Preparation", "Мария Белова", "Готово" }, 1));
        release.Children.Add(new TreeListRowNode(new[] { "Rollout", "Олег Климов", "В работе" }, 1));

        return new[] { project, release };
    }

    private enum DemoTreeIconMode
    {
        None,
        Uniform,
        Rules
    }

    private sealed class TreeListRowNode
    {
        public TreeListRowNode(IEnumerable<string> cells, int depth)
        {
            Cells = cells.ToList();
            Depth = depth;
        }

        public List<string> Cells { get; }
        public int Depth { get; }
        public bool IsExpanded { get; set; } = true;
        public List<TreeListRowNode> Children { get; } = new();
        public bool HasChildren => Children.Count > 0;

        public string GetCell(int index)
        {
            return index >= 0 && index < Cells.Count
                ? Cells[index]
                : string.Empty;
        }
    }

    private sealed record IconRule(string MatchText, string Glyph, Color Color);
}
