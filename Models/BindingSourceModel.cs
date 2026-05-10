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
            SourceQuery = SourceQuery
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
}
