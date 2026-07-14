using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace FormDesigner.Models;

/// <summary>
/// Описание источника данных, к которому можно привязать DataGrid и другие контролы.
/// Источник может быть ручным, импортированным из DLL или собранным из SQL Server.
/// </summary>
public partial class BindingSourceModel : ObservableObject
{
    public const string PreviewRowModeSchemaOnly = "SchemaOnly";
    public const string PreviewRowModeSampleRows = "SampleRows";
    public const string PreviewRowModeTopN = "TopN";
    public const string PreviewRowModeAllRows = "AllRows";

    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = "Source";

    [ObservableProperty]
    private string path = "Items";

    [ObservableProperty]
    private string itemTypeName = "ItemRow";

    [ObservableProperty]
    private string description = "";

    [ObservableProperty]
    private string sourceKind = "Manual";

    [ObservableProperty]
    private string sourceAssemblyPath = "";

    [ObservableProperty]
    private string sourceTypeFullName = "";

    [ObservableProperty]
    private string sourceTableName = "";

    [ObservableProperty]
    private string sourceConnectionString = "";

    [ObservableProperty]
    private string sourceSchemaName = "dbo";

    [ObservableProperty]
    private string sourceQuery = "";

    [ObservableProperty]
    private string previewRowMode = PreviewRowModeTopN;

    [ObservableProperty]
    private int previewTopN = 50;

    [ObservableProperty]
    private string previewSortColumn = "";

    [ObservableProperty]
    private string previewSortDirection = BindingFieldModel.SortDirectionAscending;

    [ObservableProperty]
    private bool useRealPreviewRowsIfAvailable = true;

    [ObservableProperty]
    private bool useDemoData;

    [ObservableProperty]
    private bool allowPreviewSampleFallback;

    [ObservableProperty]
    private string previewRowsDataKind = "";

    [ObservableProperty]
    private string previewRowsStatus = "";

    public ObservableCollection<BindingFieldModel> Fields { get; } = new();

    /// <summary>
    /// Глубокая копия источника вместе с описанием колонок.
    /// Используется в сериализации документа, буфере обмена и истории.
    /// </summary>
    public BindingSourceModel Clone()
    {
        var clone = new BindingSourceModel
        {
            Id = Id,
            Name = Name,
            Path = Path,
            ItemTypeName = ItemTypeName,
            Description = Description,
            SourceKind = SourceKind,
            SourceAssemblyPath = SourceAssemblyPath,
            SourceTypeFullName = SourceTypeFullName,
            SourceTableName = SourceTableName,
            SourceConnectionString = SourceConnectionString,
            SourceSchemaName = SourceSchemaName,
            SourceQuery = SourceQuery,
            PreviewRowMode = NormalizePreviewRowMode(PreviewRowMode),
            PreviewTopN = Math.Max(1, PreviewTopN),
            PreviewSortColumn = PreviewSortColumn,
            PreviewSortDirection = NormalizePreviewSortDirection(PreviewSortDirection),
            UseRealPreviewRowsIfAvailable = UseRealPreviewRowsIfAvailable,
            UseDemoData = UseDemoData,
            AllowPreviewSampleFallback = AllowPreviewSampleFallback,
            PreviewRowsDataKind = PreviewRowsDataKind,
            PreviewRowsStatus = PreviewRowsStatus
        };

        foreach (var field in Fields)
            clone.Fields.Add(field.Clone());

        return clone;
    }

    /// <summary>
    /// Короткая подпись для списков и селекторов в UI.
    /// </summary>
    public override string ToString()
    {
        return $"{Name} ({Path})";
    }

    public static string NormalizePreviewRowMode(string? value)
    {
        return value switch
        {
            PreviewRowModeSchemaOnly => PreviewRowModeSchemaOnly,
            PreviewRowModeSampleRows => PreviewRowModeSampleRows,
            PreviewRowModeAllRows => PreviewRowModeAllRows,
            _ => PreviewRowModeTopN
        };
    }

    public static string NormalizePreviewSortDirection(string? value)
    {
        return string.Equals(value, BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase)
            ? BindingFieldModel.SortDirectionDescending
            : BindingFieldModel.SortDirectionAscending;
    }
}
