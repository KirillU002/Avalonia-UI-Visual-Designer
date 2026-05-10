using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace FormDesigner.Models;

/// <summary>
/// Модель одного элемента на дизайнерской поверхности.
/// Хранит и геометрию, и визуальные свойства, и данные для привязок.
/// </summary>
public partial class DesignControlModel : ObservableObject
{
    public const string DataGridTextAlignmentLeft = "Left";
    public const string DataGridTextAlignmentCenter = "Center";
    public const string DataGridTextAlignmentRight = "Right";
    public const string DataGridFilterModeContains = "Contains";
    public const string DataGridFilterModeStartsWith = "StartsWith";
    public const string DataGridFilterModeEquals = "Equals";

    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string type = "";

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string parentId = "";

    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private string placeholderText = "";

    [ObservableProperty]
    private string imageSource = "";

    [ObservableProperty]
    private string background = "#FFFFFF";

    [ObservableProperty]
    private string foreground = "#0F172A";

    [ObservableProperty]
    private string borderBrush = "#94A3B8";

    [ObservableProperty]
    private double borderThickness = 1;

    [ObservableProperty]
    private double cornerRadius = 6;

    [ObservableProperty]
    private string fontFamily = "Inter";

    [ObservableProperty]
    private double fontSize = 14;

    [ObservableProperty]
    private string fontWeight = "Normal";

    [ObservableProperty]
    private double opacity = 1;

    [ObservableProperty]
    private double padding = 8;

    [ObservableProperty]
    private string layoutOrientation = DesignerLayoutModes.Vertical;

    [ObservableProperty]
    private double layoutSpacing = 12;

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private bool isLocked;

    [ObservableProperty]
    private bool isTemplateInstance;

    [ObservableProperty]
    private string templateSourceId = "";

    [ObservableProperty]
    private string templateDisplayName = "";

    [ObservableProperty]
    private string stretch = "Uniform";

    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    [ObservableProperty]
    private double width = 140;

    [ObservableProperty]
    private double height = 36;

    [ObservableProperty]
    private bool anchorLeft = true;

    [ObservableProperty]
    private bool anchorTop = true;

    [ObservableProperty]
    private bool anchorRight;

    [ObservableProperty]
    private bool anchorBottom;

    [ObservableProperty]
    private int columns = 3;

    [ObservableProperty]
    private int rows = 3;

    [ObservableProperty]
    private bool showGridLines = true;

    [ObservableProperty]
    private bool autoGenerateColumns = false;

    [ObservableProperty]
    private string bindingSourceId = "";

    [ObservableProperty]
    private string textBindingPath = "";

    [ObservableProperty]
    private string generatedButtonActionKey = "";

    [ObservableProperty]
    private string dataGridRowBackground = "#FFFFFF";

    [ObservableProperty]
    private string dataGridAlternateRowBackground = "#F8FAFC";

    [ObservableProperty]
    private string dataGridTextAlignment = DataGridTextAlignmentLeft;

    [ObservableProperty]
    private string dataGridGlowColor = "#60A5FA";

    [ObservableProperty]
    private string dataGridHeaderBackground = "#E2E8F0";

    [ObservableProperty]
    private string dataGridHeaderForeground = "#0F172A";

    [ObservableProperty]
    private string dataGridRowForeground = "#0F172A";

    [ObservableProperty]
    private string dataGridHoverRowBackground = "#EFF6FF";

    [ObservableProperty]
    private string dataGridSelectedRowBackground = "#DBEAFE";

    [ObservableProperty]
    private string dataGridSelectedRowForeground = "#0F172A";

    [ObservableProperty]
    private string dataGridGridLineBrush = "#D7E2EE";

    [ObservableProperty]
    private string dataGridOuterBorderBrush = "#60A5FA";

    [ObservableProperty]
    private double dataGridHeaderFontSize = 13;

    [ObservableProperty]
    private string dataGridHeaderFontWeight = "SemiBold";

    [ObservableProperty]
    private double dataGridRowFontSize = 13;

    [ObservableProperty]
    private string dataGridRowFontWeight = "Normal";

    [ObservableProperty]
    private double dataGridHeaderHeight = 46;

    [ObservableProperty]
    private double dataGridRowHeight = 36;

    [ObservableProperty]
    private double dataGridCellPadding = 14;

    [ObservableProperty]
    private bool dataGridShowHeader = true;

    [ObservableProperty]
    private bool dataGridShowRowLines = true;

    [ObservableProperty]
    private bool dataGridShowColumnLines = true;

    [ObservableProperty]
    private bool dataGridShowAlternatingRows = true;

    [ObservableProperty]
    private bool showFilterRow = true;

    [ObservableProperty]
    private string filterMode = DataGridFilterModeContains;

    [ObservableProperty]
    private bool showGroupPanel = true;

    [ObservableProperty]
    private bool allowGrouping = true;

    [ObservableProperty]
    private bool showFooter = true;

    [ObservableProperty]
    private string descriptorId = "";

    [ObservableProperty]
    private string pluginId = "";

    [ObservableProperty]
    private string pluginVersion = "";

    public ObservableCollection<DesignPropertyValueModel> CustomProperties { get; } = new();

    // Для grid-контейнера не даем создать "пустую" сетку без колонок.
    partial void OnColumnsChanged(int value)
    {
        if (value < 1)
            Columns = 1;
    }

    // Для grid-контейнера не даем создать "пустую" сетку без строк.
    partial void OnRowsChanged(int value)
    {
        if (value < 1)
            Rows = 1;
    }

    // Ограничения снизу нужны, чтобы элемент не становился совсем неуловимым для мыши.
    partial void OnWidthChanged(double value)
    {
        if (value < 40)
            Width = 40;
    }

    // Минимальная высота подбирается так, чтобы у контрола оставалась видимая зона ресайза.
    partial void OnHeightChanged(double value)
    {
        if (value < 24)
            Height = 24;
    }

    partial void OnBorderThicknessChanged(double value)
    {
        if (value < 0)
            BorderThickness = 0;
    }

    partial void OnCornerRadiusChanged(double value)
    {
        if (value < 0)
            CornerRadius = 0;
    }

    partial void OnFontSizeChanged(double value)
    {
        if (value < 8)
            FontSize = 8;
    }

    partial void OnOpacityChanged(double value)
    {
        if (value < 0)
        {
            Opacity = 0;
            return;
        }

        if (value > 1)
            Opacity = 1;
    }

    partial void OnPaddingChanged(double value)
    {
        if (value < 0)
            Padding = 0;
    }

    partial void OnLayoutOrientationChanged(string value)
    {
        var normalized = DesignerLayoutModes.NormalizeOrientation(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            LayoutOrientation = normalized;
    }

    partial void OnLayoutSpacingChanged(double value)
    {
        if (value < 0)
            LayoutSpacing = 0;
    }

    partial void OnDataGridTextAlignmentChanged(string value)
    {
        var normalized = NormalizeDataGridTextAlignment(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            DataGridTextAlignment = normalized;
    }

    partial void OnDataGridGlowColorChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            DataGridGlowColor = "#60A5FA";
    }

    partial void OnDataGridHeaderFontSizeChanged(double value)
    {
        if (value < 8)
            DataGridHeaderFontSize = 8;
    }

    partial void OnDataGridRowFontSizeChanged(double value)
    {
        if (value < 8)
            DataGridRowFontSize = 8;
    }

    partial void OnDataGridHeaderHeightChanged(double value)
    {
        if (value < 24)
            DataGridHeaderHeight = 24;
    }

    partial void OnDataGridRowHeightChanged(double value)
    {
        if (value < 18)
            DataGridRowHeight = 18;
    }

    partial void OnDataGridCellPaddingChanged(double value)
    {
        if (value < 0)
            DataGridCellPadding = 0;
    }

    partial void OnDataGridHeaderFontWeightChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            DataGridHeaderFontWeight = "SemiBold";
    }

    partial void OnDataGridRowFontWeightChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            DataGridRowFontWeight = "Normal";
    }

    partial void OnFilterModeChanged(string value)
    {
        var normalized = NormalizeDataGridFilterMode(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            FilterMode = normalized;
    }

    public static string NormalizeDataGridTextAlignment(string? value)
    {
        return value?.Trim() switch
        {
            DataGridTextAlignmentCenter => DataGridTextAlignmentCenter,
            DataGridTextAlignmentRight => DataGridTextAlignmentRight,
            _ => DataGridTextAlignmentLeft
        };
    }

    public static string NormalizeDataGridFilterMode(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, DataGridFilterModeStartsWith, StringComparison.OrdinalIgnoreCase))
            return DataGridFilterModeStartsWith;
        if (string.Equals(normalized, DataGridFilterModeEquals, StringComparison.OrdinalIgnoreCase))
            return DataGridFilterModeEquals;
        return DataGridFilterModeContains;
    }

    /// <summary>
    /// Создает копию контрола для буфера обмена, дублирования и undo/redo снимков.
    /// Id специально не копируется здесь, чтобы при вставке можно было назначить новый.
    /// </summary>
    public DesignControlModel Clone()
    {
        var clone = new DesignControlModel
        {
            Type = Type,
            Name = Name,
            ParentId = ParentId,
            Text = Text,
            PlaceholderText = PlaceholderText,
            ImageSource = ImageSource,
            Background = Background,
            Foreground = Foreground,
            BorderBrush = BorderBrush,
            BorderThickness = BorderThickness,
            CornerRadius = CornerRadius,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeight,
            Opacity = Opacity,
            Padding = Padding,
            LayoutOrientation = LayoutOrientation,
            LayoutSpacing = LayoutSpacing,
            IsVisible = IsVisible,
            IsLocked = IsLocked,
            IsTemplateInstance = IsTemplateInstance,
            TemplateSourceId = TemplateSourceId,
            TemplateDisplayName = TemplateDisplayName,
            Stretch = Stretch,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            AnchorLeft = AnchorLeft,
            AnchorTop = AnchorTop,
            AnchorRight = AnchorRight,
            AnchorBottom = AnchorBottom,
            Columns = Columns,
            Rows = Rows,
            ShowGridLines = ShowGridLines,
            AutoGenerateColumns = AutoGenerateColumns,
            BindingSourceId = BindingSourceId,
            TextBindingPath = TextBindingPath,
            GeneratedButtonActionKey = GeneratedButtonActionKey,
            DataGridRowBackground = DataGridRowBackground,
            DataGridAlternateRowBackground = DataGridAlternateRowBackground,
            DataGridTextAlignment = DataGridTextAlignment,
            DataGridGlowColor = DataGridGlowColor,
            DataGridHeaderBackground = DataGridHeaderBackground,
            DataGridHeaderForeground = DataGridHeaderForeground,
            DataGridRowForeground = DataGridRowForeground,
            DataGridHoverRowBackground = DataGridHoverRowBackground,
            DataGridSelectedRowBackground = DataGridSelectedRowBackground,
            DataGridSelectedRowForeground = DataGridSelectedRowForeground,
            DataGridGridLineBrush = DataGridGridLineBrush,
            DataGridOuterBorderBrush = DataGridOuterBorderBrush,
            DataGridHeaderFontSize = DataGridHeaderFontSize,
            DataGridHeaderFontWeight = DataGridHeaderFontWeight,
            DataGridRowFontSize = DataGridRowFontSize,
            DataGridRowFontWeight = DataGridRowFontWeight,
            DataGridHeaderHeight = DataGridHeaderHeight,
            DataGridRowHeight = DataGridRowHeight,
            DataGridCellPadding = DataGridCellPadding,
            DataGridShowHeader = DataGridShowHeader,
            DataGridShowRowLines = DataGridShowRowLines,
            DataGridShowColumnLines = DataGridShowColumnLines,
            DataGridShowAlternatingRows = DataGridShowAlternatingRows,
            ShowFilterRow = ShowFilterRow,
            FilterMode = FilterMode,
            ShowGroupPanel = ShowGroupPanel,
            AllowGrouping = AllowGrouping,
            ShowFooter = ShowFooter,
            DescriptorId = DescriptorId,
            PluginId = PluginId,
            PluginVersion = PluginVersion
        };

        foreach (var property in CustomProperties)
            clone.CustomProperties.Add(property.Clone());

        return clone;
    }

    /// <summary>
    /// Удобное строковое представление для дерева структуры и отладки.
    /// </summary>
    public override string ToString()
    {
        return $"{Name} ({Type})";
    }
}
