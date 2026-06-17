using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FormDesigner.Models;

/// <summary>
/// Краткая карточка импортированной DLL, которую показываем в отдельной вкладке слева.
/// Нужна для быстрого поиска уже загруженных сборок и понимания, какие BindingSource из них созданы.
/// </summary>
public partial class ImportedDllInfoModel : ObservableObject
{
    public const string StatusLoaded = "Loaded";
    public const string StatusPartial = "Partial";
    public const string StatusFailed = "Failed";

    [ObservableProperty]
    private string dllId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string fileName = "";

    [ObservableProperty]
    private string assemblyName = "";

    [ObservableProperty]
    private string assemblyPath = "";

    [ObservableProperty]
    private DateTime loadedAt = DateTime.UtcNow;

    [ObservableProperty]
    private string loadStatus = StatusLoaded;

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private string errorDetails = "";

    [ObservableProperty]
    private int sourceCount;

    [ObservableProperty]
    private int typeCount;

    [ObservableProperty]
    private int tableCount;

    [ObservableProperty]
    private int columnCount;

    [ObservableProperty]
    private int errorCount;

    [ObservableProperty]
    private int matchCount;

    [ObservableProperty]
    private string sourceNames = "";

    [ObservableProperty]
    private string typeNames = "";

    [ObservableProperty]
    private string summary = "";

    [ObservableProperty]
    private string searchText = "";

    public ObservableCollection<ImportedDllTypeInfoModel> Types { get; } = new();
    public ObservableCollection<ImportedDllTableInfoModel> Tables { get; } = new();
    public ObservableCollection<ImportedDllErrorInfoModel> Errors { get; } = new();

    public bool IsLoaded => string.Equals(LoadStatus, StatusLoaded, StringComparison.OrdinalIgnoreCase);
    public bool IsPartial => string.Equals(LoadStatus, StatusPartial, StringComparison.OrdinalIgnoreCase);
    public bool IsFailed => string.Equals(LoadStatus, StatusFailed, StringComparison.OrdinalIgnoreCase);
    public bool HasErrors => ErrorCount > 0 || !string.IsNullOrWhiteSpace(ErrorMessage) || Errors.Count > 0;
    public bool HasTables => Tables.Count > 0;
    public bool HasTypes => Types.Count > 0;
    public bool HasMatches => MatchCount > 0;
    public string StatusSummary => HasErrors
        ? $"{LoadStatus}: {ErrorMessage}"
        : LoadStatus;
    public string CountsSummary => $"Types: {TypeCount} | Tables: {TableCount} | Columns: {ColumnCount} | Errors: {ErrorCount}";

    public void RefreshComputedState()
    {
        TypeCount = Math.Max(TypeCount, Types.Count);
        TableCount = Math.Max(TableCount, Tables.Count);
        ColumnCount = Math.Max(ColumnCount, Tables.Sum(table => table.ColumnCount));
        ErrorCount = Math.Max(ErrorCount, Errors.Count + (string.IsNullOrWhiteSpace(ErrorMessage) ? 0 : 1));
        Summary = $"{LoadStatus} | {CountsSummary}";
        SearchText = BuildSearchText();

        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(IsPartial));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasTables));
        OnPropertyChanged(nameof(HasTypes));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(StatusSummary));
        OnPropertyChanged(nameof(CountsSummary));
    }

    private string BuildSearchText()
    {
        var tableText = string.Join(" ", Tables.Select(table => table.SearchText));
        var typeText = string.Join(" ", Types.Select(type => type.SearchText));
        var errorText = string.Join(" ", Errors.Select(error => $"{error.Title} {error.Message} {error.Details}"));
        return string.Join(" ", new[]
        {
            DllId,
            FileName,
            AssemblyName,
            AssemblyPath,
            LoadStatus,
            SourceNames,
            TypeNames,
            Summary,
            ErrorMessage,
            ErrorDetails,
            tableText,
            typeText,
            errorText
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}

public partial class ImportedDllTypeInfoModel : ObservableObject
{
    [ObservableProperty]
    private string typeId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string namespaceName = "";

    [ObservableProperty]
    private string typeName = "";

    [ObservableProperty]
    private string fullName = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string sourceKind = "DllTable";

    [ObservableProperty]
    private string tableName = "";

    [ObservableProperty]
    private bool isLinqToSqlTable;

    [ObservableProperty]
    private int columnCount;

    public string SearchText => $"{NamespaceName} {TypeName} {FullName} {DisplayName} {SourceKind} {TableName}";
}

public partial class ImportedDllTableInfoModel : ObservableObject
{
    [ObservableProperty]
    private string tableId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string sourceKey = "";

    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string tableName = "";

    [ObservableProperty]
    private string namespaceName = "";

    [ObservableProperty]
    private string typeName = "";

    [ObservableProperty]
    private string fullTypeName = "";

    [ObservableProperty]
    private bool isLinqToSqlTable;

    [ObservableProperty]
    private int columnCount;

    public ObservableCollection<ImportedDllColumnInfoModel> Columns { get; } = new();

    public string QualifiedDisplayName => string.IsNullOrWhiteSpace(DisplayName)
        ? $"{NamespaceName}.{TypeName} / {TableName}"
        : DisplayName;

    public string SearchText => string.Join(" ", new[]
    {
        SourceKey,
        DisplayName,
        TableName,
        NamespaceName,
        TypeName,
        FullTypeName,
        string.Join(" ", Columns.Select(column => column.SearchText))
    });
}

public partial class ImportedDllColumnInfoModel : ObservableObject
{
    [ObservableProperty]
    private string columnId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string columnName = "";

    [ObservableProperty]
    private string propertyName = "";

    [ObservableProperty]
    private string clrType = "";

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

    public string Flags => string.Join(", ", new[]
    {
        IsPrimaryKey ? "PK" : "",
        IsNullable ? "nullable" : "required",
        CanRead ? "read" : "",
        CanWrite ? "write" : ""
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string DisplaySummary => $"{ColumnName} -> {PropertyName} : {ClrType} {Flags}".Trim();

    public string SearchText => $"{ColumnName} {PropertyName} {ClrType} {DbType} {Flags}";
}

public partial class ImportedDllErrorInfoModel : ObservableObject
{
    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string message = "";

    [ObservableProperty]
    private string details = "";
}
