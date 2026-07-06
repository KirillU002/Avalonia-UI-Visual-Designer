using FormDesigner.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.DesignerSystem.Binding;

internal sealed record PreviewRowsLoadResult(
    IReadOnlyList<Dictionary<string, string>> Rows,
    bool IsRealData,
    string DataKind,
    string Status,
    string Reason);

internal static class PreviewRowsLoader
{
    public const string DataModeNoData = "NoData";
    public const string DataModeRealSqlData = "RealSqlData";
    public const string DataModeRealDllData = "RealDllData";
    public const string DataModeDemoData = "DemoData";
    public const string DataModeSchemaOnly = "SchemaOnly";

    private static readonly string[] StaticMethodCandidates =
    {
        "GetPreviewRows",
        "LoadPreviewRows",
        "CreatePreviewRows",
        "GetRows",
        "LoadRows",
        "CreateRows"
    };

    private static readonly string[] StaticPropertyCandidates =
    {
        "PreviewRows",
        "Rows",
        "Items",
        "Data",
        "SampleRows"
    };

    public static bool CanLoad(BindingSourceModel? source)
    {
        if (source is null)
            return false;

        if (SqlPreviewDataLoader.CanLoad(source))
            return true;

        return DataSourceIdentity.IsAssembly(source.SourceKind)
            && !string.IsNullOrWhiteSpace(source.SourceAssemblyPath)
            && !string.IsNullOrWhiteSpace(source.SourceTypeFullName);
    }

    public static string ResolveDataMode(BindingSourceModel? source)
    {
        if (source is null || !source.Fields.Any(field => field.IsVisible))
            return DataModeNoData;

        var mode = BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode);
        if (mode == BindingSourceModel.PreviewRowModeSchemaOnly)
            return DataModeSchemaOnly;

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
            return SqlPreviewDataLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable
                ? DataModeRealSqlData
                : AllowsDemoRows(source)
                    ? DataModeDemoData
                    : DataModeSchemaOnly;

        if (DataSourceIdentity.IsAssembly(source.SourceKind))
        {
            if (mode == BindingSourceModel.PreviewRowModeSampleRows)
                return AllowsDemoRows(source) ? DataModeDemoData : DataModeSchemaOnly;

            if (CanLoad(source) && source.UseRealPreviewRowsIfAvailable)
                return DataModeRealDllData;

            return AllowsDemoRows(source) ? DataModeDemoData : DataModeSchemaOnly;
        }

        return AllowsDemoRows(source) ? DataModeDemoData : DataModeSchemaOnly;
    }

    public static string ResolveDataMode(BindingSourceFileModel? source)
    {
        if (source is null || !source.Fields.Any(field => field.IsVisible))
            return DataModeNoData;

        var mode = BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode);
        if (mode == BindingSourceModel.PreviewRowModeSchemaOnly)
            return DataModeSchemaOnly;

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
            return SqlPreviewDataLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable
                ? DataModeRealSqlData
                : AllowsDemoRows(source)
                    ? DataModeDemoData
                    : DataModeSchemaOnly;

        if (DataSourceIdentity.IsAssembly(source.SourceKind))
        {
            if (mode == BindingSourceModel.PreviewRowModeSampleRows)
                return AllowsDemoRows(source) ? DataModeDemoData : DataModeSchemaOnly;

            if (!string.IsNullOrWhiteSpace(source.SourceAssemblyPath)
                && !string.IsNullOrWhiteSpace(source.SourceTypeFullName)
                && source.UseRealPreviewRowsIfAvailable)
            {
                return DataModeRealDllData;
            }

            return AllowsDemoRows(source) ? DataModeDemoData : DataModeSchemaOnly;
        }

        return AllowsDemoRows(source) ? DataModeDemoData : DataModeSchemaOnly;
    }

    public static bool ShouldSuppressSyntheticRows(BindingSourceModel? source)
    {
        if (source is null)
            return false;

        var mode = BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode);
        if (mode == BindingSourceModel.PreviewRowModeSchemaOnly)
            return true;

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
        {
            if (SqlPreviewDataLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable)
                return mode is BindingSourceModel.PreviewRowModeTopN or BindingSourceModel.PreviewRowModeAllRows;

            return !AllowsDemoRows(source);
        }

        return DataSourceIdentity.IsAssembly(source.SourceKind)
            && source.UseRealPreviewRowsIfAvailable
            && mode is BindingSourceModel.PreviewRowModeTopN or BindingSourceModel.PreviewRowModeAllRows;
    }

    public static bool ShouldSuppressSyntheticRows(BindingSourceFileModel? source)
    {
        if (source is null)
            return false;

        var mode = BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode);
        if (mode == BindingSourceModel.PreviewRowModeSchemaOnly)
            return true;

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
        {
            if (SqlPreviewDataLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable)
                return mode is BindingSourceModel.PreviewRowModeTopN or BindingSourceModel.PreviewRowModeAllRows;

            return !AllowsDemoRows(source);
        }

        return DataSourceIdentity.IsAssembly(source.SourceKind)
            && source.UseRealPreviewRowsIfAvailable
            && mode is BindingSourceModel.PreviewRowModeTopN or BindingSourceModel.PreviewRowModeAllRows;
    }

    public static string BuildSignature(BindingSourceModel source)
    {
        var baseSignature = SqlPreviewDataLoader.CanLoad(source)
            ? SqlPreviewDataLoader.BuildSignature(source)
            : DataSourceIdentity.BuildKey(source);
        var assemblyStamp = GetAssemblyStamp(source.SourceAssemblyPath);

        return string.Join("|",
            baseSignature,
            assemblyStamp,
            BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode),
            Math.Max(1, source.PreviewTopN).ToString(CultureInfo.InvariantCulture),
            Normalize(source.PreviewSortColumn),
            BindingSourceModel.NormalizePreviewSortDirection(source.PreviewSortDirection),
            source.UseRealPreviewRowsIfAvailable,
            source.AllowPreviewSampleFallback);
    }

    public static async Task<PreviewRowsLoadResult> LoadRowsAsync(
        BindingSourceModel source,
        CancellationToken cancellationToken = default,
        bool updateSourceStatus = true)
    {
        var mode = BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode);
        if (mode == BindingSourceModel.PreviewRowModeSchemaOnly)
            return Complete(source, Array.Empty<Dictionary<string, string>>(), false, "SchemaOnly", "Загружена только схема источника.", "schema-only", updateSourceStatus);

        if (mode == BindingSourceModel.PreviewRowModeSampleRows)
        {
            var rows = BuildSampleRows(source, Math.Max(1, source.PreviewTopN));
            return Complete(source, rows, false, "SampleRows", "Используются demo rows, реальные данные не загружались.", "sample-mode", updateSourceStatus);
        }

        if (SqlPreviewDataLoader.CanLoad(source))
        {
            var sqlStarted = Stopwatch.StartNew();
            var sourceKey = DataSourceIdentity.BuildKey(source);
            Debug.WriteLine(
                "PREVIEW_SQL_ROWS_LOAD_START " +
                $"sourceKey={sourceKey}; queryHash={SqlPreviewDataLoader.BuildQueryHash(source)}; topN={source.PreviewTopN}");
            try
            {
                var sqlRows = await SqlPreviewDataLoader.LoadRowsAsync(source).ConfigureAwait(false);
                var limitedRows = ApplySortAndLimit(source, sqlRows, out var beforeCount);
                Debug.WriteLine(
                    "PREVIEW_SQL_ROWS_LOAD_END " +
                    $"sourceKey={sourceKey}; rows={limitedRows.Count}; elapsedMs={sqlStarted.Elapsed.TotalMilliseconds:0.0}");
                Debug.WriteLine($"DATAGRID_PREVIEW_TOP_N_APPLIED sourceKey={DataSourceIdentity.BuildKey(source)}; topN={source.PreviewTopN}; rowsBefore={beforeCount}; rowsAfter={limitedRows.Count}");
                return Complete(source, limitedRows, true, "RealData", $"Загружены реальные SQL rows: {limitedRows.Count}.", "sql", updateSourceStatus);
            }
            catch (Exception ex) when (source.AllowPreviewSampleFallback)
            {
                Debug.WriteLine(
                    "PREVIEW_SQL_ROWS_LOAD_FAILED " +
                    $"sourceKey={sourceKey}; reason={ex.Message}");
                Debug.WriteLine(
                    "PREVIEW_DATAGRID_UNEXPECTED_DEMO_FALLBACK " +
                    $"sourceKind={source.SourceKind}; sourceConfigured=True; reason={ex.Message}");
                var fallbackRows = BuildSampleRows(source, Math.Max(1, source.PreviewTopN));
                return Complete(source, fallbackRows, false, "SampleRows", $"Используются demo rows, реальные SQL данные не загружены. Причина: {ex.Message}", ex.Message, updateSourceStatus);
            }
        }

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
        {
            if (source.AllowPreviewSampleFallback)
            {
                var rows = BuildSampleRows(source, Math.Max(1, source.PreviewTopN));
                Debug.WriteLine(
                    "PREVIEW_DATAGRID_DEMO_ROWS_USED " +
                    $"sourceKey={DataSourceIdentity.BuildKey(source)}; reason=sql-not-configured; explicitDemoEnabled=True; rows={rows.Count}");
                return Complete(source, rows, false, "DemoData", "Demo data: SQL source is not configured.", "sql-not-configured", updateSourceStatus);
            }

            return Complete(source, Array.Empty<Dictionary<string, string>>(), false, "SchemaOnly", "SQL source is not configured. Preview rows are empty.", "sql-not-configured", updateSourceStatus);
        }

        if (DataSourceIdentity.IsAssembly(source.SourceKind))
        {
            Debug.WriteLine(
                "DLL_TABLE_REAL_ROWS_LOAD_START " +
                $"sourceKey={DataSourceIdentity.BuildKey(source)}; table={source.SourceTableName}; mode={mode}; topN={source.PreviewTopN}; sortColumn={source.PreviewSortColumn}; sortDirection={source.PreviewSortDirection}");

            if (source.UseRealPreviewRowsIfAvailable)
            {
                var started = Stopwatch.StartNew();
                try
                {
                    var realRows = await Task.Run(() => LoadDllRows(source, cancellationToken), cancellationToken).ConfigureAwait(false);
                    var limitedRows = ApplySortAndLimit(source, realRows, out var beforeCount);
                    Debug.WriteLine(
                        "DLL_TABLE_REAL_ROWS_LOAD_END " +
                        $"sourceKey={DataSourceIdentity.BuildKey(source)}; rowsLoaded={limitedRows.Count}; elapsedMs={started.Elapsed.TotalMilliseconds:0.0}; realData=True");
                    Debug.WriteLine($"DATAGRID_PREVIEW_TOP_N_APPLIED sourceKey={DataSourceIdentity.BuildKey(source)}; topN={source.PreviewTopN}; rowsBefore={beforeCount}; rowsAfter={limitedRows.Count}");
                    return Complete(source, limitedRows, true, "RealData", $"Загружены реальные DLL rows: {limitedRows.Count}.", "dll-provider", updateSourceStatus);
                }
                catch (Exception ex) when (source.AllowPreviewSampleFallback)
                {
                    Debug.WriteLine($"DLL_TABLE_REAL_ROWS_LOAD_FAILED sourceKey={DataSourceIdentity.BuildKey(source)}; reason={ex.Message}");
                    var fallbackRows = BuildSampleRows(source, Math.Max(1, source.PreviewTopN));
                    return Complete(source, fallbackRows, false, "SampleRows", $"Используются demo rows, реальные DLL данные не загружены. Причина: {ex.Message}", ex.Message, updateSourceStatus);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DLL_TABLE_REAL_ROWS_LOAD_FAILED sourceKey={DataSourceIdentity.BuildKey(source)}; reason={ex.Message}");
                    return Complete(source, Array.Empty<Dictionary<string, string>>(), false, "SchemaOnly", $"Реальные DLL данные не загружены. Причина: {ex.Message}", ex.Message, updateSourceStatus);
                }
            }

            if (source.AllowPreviewSampleFallback)
            {
                var fallbackRows = BuildSampleRows(source, Math.Max(1, source.PreviewTopN));
                return Complete(source, fallbackRows, false, "SampleRows", "Используются demo rows, загрузка реальных DLL данных отключена.", "real-disabled", updateSourceStatus);
            }

            return Complete(source, Array.Empty<Dictionary<string, string>>(), false, "SchemaOnly", "Загружена только схема DLL table.", "real-disabled", updateSourceStatus);
        }

        var sampleRows = BuildSampleRows(source, Math.Max(1, source.PreviewTopN));
        return Complete(source, sampleRows, false, "SampleRows", "Используются demo rows для ручного источника данных.", "manual", updateSourceStatus);
    }

    public static IReadOnlyList<Dictionary<string, string>> BuildSampleRows(BindingSourceModel source, int rowCount)
    {
        return BuildDemoRows(source.Fields, rowCount);
    }

    public static IReadOnlyList<Dictionary<string, string>> BuildDemoRows(IEnumerable<BindingFieldModel> fields, int rowCount)
    {
        var selectedFields = fields.Where(field => field.IsVisible).ToList();
        return BuildDemoRows(
            selectedFields.Select(field => new DemoField(field.Header, field.Path, field.TypeName, field.SampleValue)),
            rowCount);
    }

    public static IReadOnlyList<Dictionary<string, string>> BuildDemoRows(IEnumerable<BindingFieldFileModel> fields, int rowCount)
    {
        var selectedFields = fields.Where(field => field.IsVisible).ToList();
        return BuildDemoRows(
            selectedFields.Select(field => new DemoField(field.Header, field.Path, field.TypeName, field.SampleValue)),
            rowCount);
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildDemoRows(IEnumerable<DemoField> fields, int rowCount)
    {
        var selectedFields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Path))
            .ToList();

        if (selectedFields.Count == 0)
            return Array.Empty<Dictionary<string, string>>();

        var totalRows = Math.Max(0, rowCount);
        var rows = new List<Dictionary<string, string>>(totalRows);
        for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in selectedFields)
                row[field.Path] = CreateDemoValue(field, rowIndex);

            rows.Add(row);
        }

        return rows;
    }

    private static PreviewRowsLoadResult Complete(
        BindingSourceModel source,
        IReadOnlyList<Dictionary<string, string>> rows,
        bool isRealData,
        string dataKind,
        string status,
        string reason,
        bool updateSourceStatus)
    {
        if (updateSourceStatus)
        {
            source.PreviewRowsDataKind = dataKind;
            source.PreviewRowsStatus = status;
        }

        Debug.WriteLine(isRealData
            ? $"DATAGRID_PREVIEW_REAL_ROWS_APPLIED sourceKey={DataSourceIdentity.BuildKey(source)}; rows={rows.Count}; source={DataSourceIdentity.BuildDisplayName(source)}"
            : $"DATAGRID_PREVIEW_SAMPLE_ROWS_USED sourceKey={DataSourceIdentity.BuildKey(source)}; reason={reason}; rows={rows.Count}");

        return new PreviewRowsLoadResult(rows, isRealData, dataKind, status, reason);
    }

    private static IReadOnlyList<Dictionary<string, string>> LoadDllRows(BindingSourceModel source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var assemblyPath = Path.GetFullPath(source.SourceAssemblyPath);
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException("DLL file was not found.", assemblyPath);

        var loadContext = new DesignerAssemblyLoadContext(assemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var sourceType = assembly.GetType(source.SourceTypeFullName, throwOnError: false, ignoreCase: false)
                ?? throw new InvalidOperationException($"Type '{source.SourceTypeFullName}' was not found in DLL.");

            var rowsObject = InvokeStaticRowsProvider(sourceType)
                ?? throw new InvalidOperationException("DLL table exposes schema only: no public static PreviewRows/GetPreviewRows provider was found.");

            if (rowsObject is Task task)
            {
                task.GetAwaiter().GetResult();
                rowsObject = GetTaskResult(task);
            }

            if (rowsObject is not IEnumerable enumerable)
                throw new InvalidOperationException("Preview rows provider does not return IEnumerable.");

            var rows = new List<Dictionary<string, string>>();
            foreach (var item in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is null)
                    continue;

                rows.Add(ConvertRow(source, item));
            }

            return rows;
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static object? InvokeStaticRowsProvider(Type sourceType)
    {
        foreach (var methodName in StaticMethodCandidates)
        {
            var method = sourceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.GetParameters().Length == 0
                    && string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase));
            if (method is not null)
                return method.Invoke(null, Array.Empty<object>());
        }

        foreach (var propertyName in StaticPropertyCandidates)
        {
            var property = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            if (property is not null)
                return property.GetValue(null);
        }

        return null;
    }

    private static object? GetTaskResult(Task task)
    {
        var taskType = task.GetType();
        return taskType.IsGenericType
            ? taskType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(task)
            : null;
    }

    private static Dictionary<string, string> ConvertRow(BindingSourceModel source, object rowObject)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dictionaryValues = ReadDictionaryRow(rowObject);
        var properties = rowObject.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var field in source.Fields.Where(field => field.IsVisible))
        {
            var value = TryReadDictionaryValue(dictionaryValues, field.Path, out var byPath)
                ? byPath
                : TryReadDictionaryValue(dictionaryValues, field.Header, out var byHeader)
                    ? byHeader
                    : TryReadPropertyValue(properties, rowObject, field.Path, out var propertyByPath)
                        ? propertyByPath
                        : TryReadPropertyValue(properties, rowObject, field.Header, out var propertyByHeader)
                            ? propertyByHeader
                            : null;

            result[field.Path] = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        return result;
    }

    private static Dictionary<string, object?> ReadDictionaryRow(object rowObject)
    {
        if (rowObject is IDictionary<string, object?> typedObjectDictionary)
            return new Dictionary<string, object?>(typedObjectDictionary, StringComparer.OrdinalIgnoreCase);

        if (rowObject is IDictionary<string, string> typedStringDictionary)
            return typedStringDictionary.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);

        if (rowObject is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(key))
                    result[key] = entry.Value;
            }

            return result;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryReadDictionaryValue(IReadOnlyDictionary<string, object?> values, string? key, out object? value)
    {
        if (!string.IsNullOrWhiteSpace(key) && values.TryGetValue(key, out value))
            return true;

        value = null;
        return false;
    }

    private static bool TryReadPropertyValue(
        IReadOnlyDictionary<string, PropertyInfo> properties,
        object rowObject,
        string? propertyName,
        out object? value)
    {
        if (!string.IsNullOrWhiteSpace(propertyName)
            && properties.TryGetValue(propertyName, out var property))
        {
            value = property.GetValue(rowObject);
            return true;
        }

        value = null;
        return false;
    }

    private static IReadOnlyList<Dictionary<string, string>> ApplySortAndLimit(
        BindingSourceModel source,
        IReadOnlyList<Dictionary<string, string>> rows,
        out int beforeCount)
    {
        beforeCount = rows.Count;
        IEnumerable<Dictionary<string, string>> orderedRows = rows;
        var sortColumn = ResolveSortColumn(source);
        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            orderedRows = string.Equals(BindingSourceModel.NormalizePreviewSortDirection(source.PreviewSortDirection), BindingFieldModel.SortDirectionDescending, StringComparison.OrdinalIgnoreCase)
                ? orderedRows.OrderByDescending(row => BuildSortValue(row, sortColumn))
                : orderedRows.OrderBy(row => BuildSortValue(row, sortColumn));
            Debug.WriteLine($"DATAGRID_PREVIEW_SORT_APPLIED sourceKey={DataSourceIdentity.BuildKey(source)}; column={sortColumn}; direction={source.PreviewSortDirection}");
        }

        if (BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) != BindingSourceModel.PreviewRowModeAllRows)
            orderedRows = orderedRows.Take(Math.Max(1, source.PreviewTopN));

        return orderedRows.ToList();
    }

    private static string ResolveSortColumn(BindingSourceModel source)
    {
        var configured = Normalize(source.PreviewSortColumn);
        if (string.IsNullOrWhiteSpace(configured))
            return string.Empty;

        var field = source.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, configured, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Header, configured, StringComparison.OrdinalIgnoreCase));
        return field?.Path ?? configured;
    }

    private static PreviewSortKey BuildSortValue(IReadOnlyDictionary<string, string> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || string.IsNullOrWhiteSpace(value))
            return PreviewSortKey.Empty;

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var dateTime))
            return PreviewSortKey.FromDate(dateTime);

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var number))
            return PreviewSortKey.FromNumber(number);

        return PreviewSortKey.FromText(value);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool AllowsDemoRows(BindingSourceModel source)
    {
        return source.AllowPreviewSampleFallback
            && BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) != BindingSourceModel.PreviewRowModeSchemaOnly;
    }

    private static bool AllowsDemoRows(BindingSourceFileModel source)
    {
        return source.AllowPreviewSampleFallback
            && BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) != BindingSourceModel.PreviewRowModeSchemaOnly;
    }

    private static string CreateDemoValue(DemoField field, int rowIndex)
    {
        var signature = $"{field.Header} {field.Path} {field.TypeName}".ToLowerInvariant();
        var sampleValue = field.SampleValue ?? string.Empty;

        if (LooksLikeBoolean(signature))
            return rowIndex % 2 == 0 ? "True" : "False";

        if (LooksLikeDate(signature))
            return DateTime.Today.AddDays(-rowIndex * 3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (LooksLikeCurrency(signature))
            return (12500 + (rowIndex * 1450)).ToString(CultureInfo.InvariantCulture);

        if (LooksLikePercentage(signature, sampleValue))
            return rowIndex switch
            {
                0 => "2.2%",
                1 => "-1.9%",
                2 => "4.7%",
                _ => $"{((rowIndex % 7) - 2) * 1.1:0.0}%"
            };

        if (LooksLikeNumeric(signature, sampleValue))
        {
            if (TryParseNumber(sampleValue, out var sampleNumber))
                return (sampleNumber + rowIndex).ToString("0.##", CultureInfo.InvariantCulture);

            return (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(sampleValue))
            return rowIndex == 0 ? sampleValue : $"{sampleValue} {rowIndex + 1}";

        var label = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header;
        return rowIndex == 0 ? label : $"{label} {rowIndex + 1}";
    }

    private static bool LooksLikeNumeric(string signature, string sampleValue)
    {
        if (TryParseNumber(sampleValue, out _))
            return true;

        return signature.Contains(" id", StringComparison.Ordinal)
            || signature.Contains("count", StringComparison.Ordinal)
            || signature.Contains("number", StringComparison.Ordinal)
            || signature.Contains("amount", StringComparison.Ordinal)
            || signature.Contains("price", StringComparison.Ordinal)
            || signature.Contains("total", StringComparison.Ordinal)
            || signature.Contains("qty", StringComparison.Ordinal)
            || signature.Contains("int", StringComparison.Ordinal)
            || signature.Contains("decimal", StringComparison.Ordinal)
            || signature.Contains("double", StringComparison.Ordinal)
            || signature.Contains("float", StringComparison.Ordinal);
    }

    private static bool LooksLikePercentage(string signature, string sampleValue)
    {
        return sampleValue.Contains('%', StringComparison.Ordinal)
            || signature.Contains("percent", StringComparison.Ordinal)
            || signature.Contains("percentage", StringComparison.Ordinal);
    }

    private static bool LooksLikeBoolean(string signature)
    {
        return signature.Contains("bool", StringComparison.Ordinal)
            || signature.Contains("is", StringComparison.Ordinal)
            || signature.Contains("active", StringComparison.Ordinal)
            || signature.Contains("enabled", StringComparison.Ordinal);
    }

    private static bool LooksLikeDate(string signature)
    {
        return signature.Contains("date", StringComparison.Ordinal)
            || signature.Contains("time", StringComparison.Ordinal)
            || signature.Contains("created", StringComparison.Ordinal)
            || signature.Contains("updated", StringComparison.Ordinal);
    }

    private static bool LooksLikeCurrency(string signature)
    {
        return signature.Contains("price", StringComparison.Ordinal)
            || signature.Contains("cost", StringComparison.Ordinal)
            || signature.Contains("amount", StringComparison.Ordinal)
            || signature.Contains("total", StringComparison.Ordinal);
    }

    private static bool TryParseNumber(string? value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var filtered = new string(value
            .Where(ch => char.IsDigit(ch) || ch is '-' or '+' or '.' or ',')
            .ToArray());

        return double.TryParse(filtered.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            || double.TryParse(filtered, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
    }

    private static string GetAssemblyStamp(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            return "0";

        try
        {
            var info = new FileInfo(assemblyPath);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return "0";
        }
    }

    private sealed class DesignerAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly Dictionary<string, Assembly> _sharedAssemblies;

        public DesignerAssemblyLoadContext(string assemblyPath)
            : base($"DllPreviewRows:{Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
            _sharedAssemblies = AssemblyLoadContext.Default.Assemblies
                .Where(assembly => !string.IsNullOrWhiteSpace(assembly.GetName().Name))
                .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (!string.IsNullOrWhiteSpace(assemblyName.Name)
                && _sharedAssemblies.TryGetValue(assemblyName.Name, out var sharedAssembly))
            {
                return sharedAssembly;
            }

            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath is null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }

    private sealed record DemoField(string Header, string Path, string TypeName, string SampleValue);

    private readonly record struct PreviewSortKey(int Kind, decimal Number, DateTime Date, string Text) : IComparable<PreviewSortKey>
    {
        public static PreviewSortKey Empty => new(0, 0, DateTime.MinValue, string.Empty);

        public static PreviewSortKey FromNumber(decimal number) => new(1, number, DateTime.MinValue, string.Empty);

        public static PreviewSortKey FromDate(DateTime date) => new(2, 0, date, string.Empty);

        public static PreviewSortKey FromText(string text) => new(3, 0, DateTime.MinValue, text);

        public int CompareTo(PreviewSortKey other)
        {
            var kindComparison = Kind.CompareTo(other.Kind);
            if (kindComparison != 0)
                return kindComparison;

            return Kind switch
            {
                1 => Number.CompareTo(other.Number),
                2 => Date.CompareTo(other.Date),
                3 => string.Compare(Text, other.Text, StringComparison.CurrentCultureIgnoreCase),
                _ => 0
            };
        }
    }
}
