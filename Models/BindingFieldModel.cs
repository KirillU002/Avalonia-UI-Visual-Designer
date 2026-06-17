using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace FormDesigner.Models;

/// <summary>
/// Описание одного поля источника данных.
/// По этой модели дизайнер строит колонки DataGrid, подсказки и генерацию кода.
/// </summary>
public partial class BindingFieldModel : ObservableObject
{
    public const string AlignmentLeft = "Left";
    public const string AlignmentCenter = "Center";
    public const string AlignmentRight = "Right";

    public const string SortDirectionNone = "None";
    public const string SortDirectionAscending = "Ascending";
    public const string SortDirectionDescending = "Descending";

    public const string TextTrimmingNone = "None";
    public const string TextTrimmingCharacterEllipsis = "CharacterEllipsis";
    public const string TextTrimmingWordEllipsis = "WordEllipsis";

    public const string TextWrappingNoWrap = "NoWrap";
    public const string TextWrappingWrap = "Wrap";

    public const string SummaryTypeNone = "None";
    public const string SummaryTypeCount = "Count";
    public const string SummaryTypeSum = "Sum";
    public const string SummaryTypeAvg = "Avg";
    public const string SummaryTypeMin = "Min";
    public const string SummaryTypeMax = "Max";

    [ObservableProperty]
    private string header = "Column";

    [ObservableProperty]
    private string path = "Property";

    [ObservableProperty]
    private string sampleValue = "Value";

    [ObservableProperty]
    private string width = "*";

    [ObservableProperty]
    private string typeName = "string";

    [ObservableProperty]
    private string dbType = "";

    [ObservableProperty]
    private bool isPrimaryKey;

    [ObservableProperty]
    private bool isNullable = true;

    [ObservableProperty]
    private bool canRead = true;

    [ObservableProperty]
    private bool canWrite = true;

    [ObservableProperty]
    private bool isVisible = true;

    [ObservableProperty]
    private bool isSortable = true;

    [ObservableProperty]
    private string sortDirection = SortDirectionNone;

    [ObservableProperty]
    private int sortOrder = -1;

    [ObservableProperty]
    private int groupOrder = -1;

    [ObservableProperty]
    private string headerAlignment = AlignmentLeft;

    [ObservableProperty]
    private string cellAlignment = AlignmentLeft;

    [ObservableProperty]
    private string formatString = "";

    [ObservableProperty]
    private string nullText = "";

    [ObservableProperty]
    private string textTrimming = TextTrimmingCharacterEllipsis;

    [ObservableProperty]
    private string textWrapping = TextWrappingNoWrap;

    [ObservableProperty]
    private int maxLines = 1;

    [ObservableProperty]
    private double minWidth = 56;

    [ObservableProperty]
    private double maxWidth;

    [ObservableProperty]
    private bool allowResize = true;

    [ObservableProperty]
    private bool allowSort = true;

    [ObservableProperty]
    private bool allowFilter = true;

    [ObservableProperty]
    private int visibleIndex = -1;

    [ObservableProperty]
    private string summaryType = SummaryTypeNone;

    [ObservableProperty]
    private string summaryFormat = "";

    public bool IsGrouped
    {
        get => GroupOrder >= 0;
        set
        {
            if (value == IsGrouped)
                return;

            GroupOrder = value ? 0 : -1;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Копия поля нужна, чтобы безопасно переносить настройки между документом,
    /// импортом из БД и предпросмотром без ссылочного конфликта.
    /// </summary>
    public BindingFieldModel Clone()
    {
        return new BindingFieldModel
        {
            Header = Header,
            Path = Path,
            SampleValue = SampleValue,
            Width = Width,
            TypeName = TypeName,
            DbType = DbType,
            IsPrimaryKey = IsPrimaryKey,
            IsNullable = IsNullable,
            CanRead = CanRead,
            CanWrite = CanWrite,
            IsVisible = IsVisible,
            IsSortable = IsSortable,
            SortDirection = SortDirection,
            SortOrder = SortOrder,
            GroupOrder = GroupOrder,
            HeaderAlignment = HeaderAlignment,
            CellAlignment = CellAlignment,
            FormatString = FormatString,
            NullText = NullText,
            TextTrimming = TextTrimming,
            TextWrapping = TextWrapping,
            MaxLines = MaxLines,
            MinWidth = MinWidth,
            MaxWidth = MaxWidth,
            AllowResize = AllowResize,
            AllowSort = AllowSort,
            AllowFilter = AllowFilter,
            VisibleIndex = VisibleIndex,
            SummaryType = SummaryType,
            SummaryFormat = SummaryFormat
        };
    }

    partial void OnHeaderAlignmentChanged(string value)
    {
        var normalized = NormalizeAlignment(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            HeaderAlignment = normalized;
    }

    partial void OnCellAlignmentChanged(string value)
    {
        var normalized = NormalizeAlignment(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            CellAlignment = normalized;
    }

    partial void OnTextTrimmingChanged(string value)
    {
        var normalized = NormalizeTextTrimming(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            TextTrimming = normalized;
    }

    partial void OnTextWrappingChanged(string value)
    {
        var normalized = NormalizeTextWrapping(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            TextWrapping = normalized;
    }

    partial void OnAllowSortChanged(bool value)
    {
        if (IsSortable != value)
            IsSortable = value;
    }

    partial void OnGroupOrderChanged(int value)
    {
        if (value < -1)
        {
            GroupOrder = -1;
            return;
        }

        OnPropertyChanged(nameof(IsGrouped));
    }

    partial void OnMaxLinesChanged(int value)
    {
        if (value < 0)
            MaxLines = 0;
    }

    partial void OnMinWidthChanged(double value)
    {
        if (value < 0)
            MinWidth = 0;

        if (MaxWidth > 0 && MaxWidth < MinWidth)
            MaxWidth = MinWidth;
    }

    partial void OnMaxWidthChanged(double value)
    {
        if (value < 0)
        {
            MaxWidth = 0;
            return;
        }

        if (value > 0 && value < MinWidth)
            MaxWidth = MinWidth;
    }

    partial void OnVisibleIndexChanged(int value)
    {
        if (value < -1)
            VisibleIndex = -1;
    }

    partial void OnSummaryTypeChanged(string value)
    {
        var normalized = NormalizeSummaryType(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            SummaryType = normalized;
    }

    public string FormatDisplayValue(string? value)
    {
        return FormatDisplayValue(value, FormatString, NullText, TypeName);
    }

    public static string NormalizeAlignment(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, AlignmentCenter, StringComparison.OrdinalIgnoreCase))
            return AlignmentCenter;
        if (string.Equals(normalized, AlignmentRight, StringComparison.OrdinalIgnoreCase))
            return AlignmentRight;
        return AlignmentLeft;
    }

    public static string NormalizeTextTrimming(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, TextTrimmingNone, StringComparison.OrdinalIgnoreCase))
            return TextTrimmingNone;
        if (string.Equals(normalized, TextTrimmingWordEllipsis, StringComparison.OrdinalIgnoreCase))
            return TextTrimmingWordEllipsis;
        return TextTrimmingCharacterEllipsis;
    }

    public static string NormalizeTextWrapping(string? value)
    {
        return string.Equals(value?.Trim(), TextWrappingWrap, StringComparison.OrdinalIgnoreCase)
            ? TextWrappingWrap
            : TextWrappingNoWrap;
    }

    public static string NormalizeSummaryType(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, SummaryTypeCount, StringComparison.OrdinalIgnoreCase))
            return SummaryTypeCount;
        if (string.Equals(normalized, SummaryTypeSum, StringComparison.OrdinalIgnoreCase))
            return SummaryTypeSum;
        if (string.Equals(normalized, SummaryTypeAvg, StringComparison.OrdinalIgnoreCase))
            return SummaryTypeAvg;
        if (string.Equals(normalized, SummaryTypeMin, StringComparison.OrdinalIgnoreCase))
            return SummaryTypeMin;
        if (string.Equals(normalized, SummaryTypeMax, StringComparison.OrdinalIgnoreCase))
            return SummaryTypeMax;
        return SummaryTypeNone;
    }

    public static string FormatDisplayValue(string? value, string? formatString, string? nullText, string? typeName)
    {
        if (string.IsNullOrEmpty(value))
            return string.IsNullOrEmpty(nullText) ? string.Empty : nullText;

        if (string.IsNullOrWhiteSpace(formatString))
            return value;

        try
        {
            var compositeFormat = NormalizeCompositeFormat(formatString);
            var typedValue = CoerceFormatValue(value, typeName);
            return string.Format(CultureInfo.CurrentCulture, compositeFormat, typedValue);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static string NormalizeCompositeFormat(string formatString)
    {
        var trimmed = formatString.Trim();
        if (trimmed.StartsWith("{}", StringComparison.Ordinal))
            trimmed = trimmed[2..];

        return trimmed.Contains("{0", StringComparison.Ordinal)
            ? trimmed
            : "{0:" + trimmed + "}";
    }

    private static object CoerceFormatValue(string value, string? typeName)
    {
        var normalizedType = (typeName ?? string.Empty).ToLowerInvariant();

        if (normalizedType.Contains("date", StringComparison.Ordinal)
            && (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var localDate)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out localDate)))
        {
            return localDate;
        }

        if ((normalizedType.Contains("bool", StringComparison.Ordinal)
                || normalizedType.Contains("boolean", StringComparison.Ordinal))
            && bool.TryParse(value, out var booleanValue))
        {
            return booleanValue;
        }

        if (TryParseDecimal(value, out var decimalValue))
            return decimalValue;

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateValue)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateValue))
        {
            return dateValue;
        }

        return value;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out result)
            || decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }
}
