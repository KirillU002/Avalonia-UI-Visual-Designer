using FormDesigner.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace FormDesigner.DesignerSystem.Binding;

internal static class SqlPreviewDataLoader
{
    public static bool CanLoad(BindingSourceModel? source)
    {
        return source is not null
            && IsSqlServerSource(source.SourceKind)
            && !string.IsNullOrWhiteSpace(source.SourceConnectionString)
            && (!string.IsNullOrWhiteSpace(source.SourceQuery) || !string.IsNullOrWhiteSpace(source.SourceTableName));
    }

    public static bool CanLoad(BindingSourceFileModel? source)
    {
        return source is not null
            && IsSqlServerSource(source.SourceKind)
            && !string.IsNullOrWhiteSpace(source.SourceConnectionString)
            && (!string.IsNullOrWhiteSpace(source.SourceQuery) || !string.IsNullOrWhiteSpace(source.SourceTableName));
    }

    public static string BuildSignature(BindingSourceModel source)
    {
        return string.Join("|",
            Normalize(source.SourceKind),
            Normalize(source.SourceConnectionString),
            Normalize(source.SourceSchemaName),
            Normalize(source.SourceTableName),
            Normalize(source.SourceQuery));
    }

    public static string BuildSignature(BindingSourceFileModel source)
    {
        return string.Join("|",
            Normalize(source.SourceKind),
            Normalize(source.SourceConnectionString),
            Normalize(source.SourceSchemaName),
            Normalize(source.SourceTableName),
            Normalize(source.SourceQuery));
    }

    public static Task<IReadOnlyList<Dictionary<string, string>>> LoadRowsAsync(BindingSourceModel source)
    {
        return LoadRowsCoreAsync(
            source.SourceConnectionString,
            source.SourceSchemaName,
            source.SourceTableName,
            source.SourceQuery);
    }

    public static Task<IReadOnlyList<Dictionary<string, string>>> LoadRowsAsync(BindingSourceFileModel source)
    {
        return LoadRowsCoreAsync(
            source.SourceConnectionString,
            source.SourceSchemaName,
            source.SourceTableName,
            source.SourceQuery);
    }

    private static bool IsSqlServerSource(string? sourceKind)
    {
        return string.Equals(sourceKind, "SqlServer", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static async Task<IReadOnlyList<Dictionary<string, string>>> LoadRowsCoreAsync(
        string connectionString,
        string schemaName,
        string tableName,
        string? sourceQuery)
    {
        var connectionResult = await OpenSqlConnectionAsync(connectionString);
        await using var connection = connectionResult.Connection;

        using var command = connection.CreateCommand();
        command.CommandText = BuildSqlCommandText(schemaName, tableName, sourceQuery);
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 30;

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var schema = reader.GetColumnSchema();
        var rows = new List<Dictionary<string, string>>();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var columnIndex = 0; columnIndex < schema.Count; columnIndex++)
            {
                var columnName = schema[columnIndex].ColumnName ?? $"Column{columnIndex + 1}";
                var value = reader.IsDBNull(columnIndex) ? null : reader.GetValue(columnIndex);
                row[columnName] = ConvertDatabaseValueToDisplayText(value);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string BuildSqlCommandText(string schemaName, string tableName, string? sourceQuery)
    {
        if (!string.IsNullOrWhiteSpace(sourceQuery))
            return sourceQuery.Trim().TrimEnd(';');

        return $"SELECT * FROM {BuildSqlObjectReference(schemaName, tableName)}";
    }

    private static string BuildSqlObjectReference(string schemaName, string tableName)
    {
        var normalizedSchema = NormalizeSqlSchemaName(schemaName);
        var normalizedTable = NormalizeSqlTableName(tableName);

        if (normalizedTable.Contains('.', StringComparison.Ordinal))
        {
            var parts = normalizedTable.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                normalizedSchema = parts[^2];
                normalizedTable = parts[^1];
            }
        }

        return $"{QuoteSqlIdentifier(normalizedSchema)}.{QuoteSqlIdentifier(normalizedTable)}";
    }

    private static string NormalizeSqlSchemaName(string? schemaName)
    {
        return string.IsNullOrWhiteSpace(schemaName) ? "dbo" : schemaName.Trim().Trim('[', ']');
    }

    private static string NormalizeSqlTableName(string? tableName)
    {
        return string.IsNullOrWhiteSpace(tableName) ? string.Empty : tableName.Trim().Trim('[', ']');
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        var normalized = string.IsNullOrWhiteSpace(identifier) ? "dbo" : identifier.Trim().Trim('[', ']');
        return $"[{normalized.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static async Task<(SqlConnection Connection, string EffectiveConnectionString)> OpenSqlConnectionAsync(string connectionString)
    {
        var primaryConnection = new SqlConnection(connectionString);

        try
        {
            await primaryConnection.OpenAsync();
            return (primaryConnection, connectionString);
        }
        catch (Exception ex) when (ShouldRetryWithTrustedCertificate(connectionString, ex))
        {
            await primaryConnection.DisposeAsync();

            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                TrustServerCertificate = true
            };

            var retryConnection = new SqlConnection(builder.ConnectionString);
            await retryConnection.OpenAsync();
            return (retryConnection, builder.ConnectionString);
        }
    }

    private static bool ShouldRetryWithTrustedCertificate(string connectionString, Exception ex)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (builder.TrustServerCertificate)
                return false;
        }
        catch
        {
            return false;
        }

        var combinedMessage = string.Join(" ", EnumerateExceptionMessages(ex))
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(combinedMessage))
            return false;

        var mentionsCertificate = combinedMessage.Contains("certificate", StringComparison.Ordinal)
            || combinedMessage.Contains("сертификат", StringComparison.Ordinal)
            || combinedMessage.Contains("цепочк", StringComparison.Ordinal);
        var mentionsTrustFailure = combinedMessage.Contains("not trusted", StringComparison.Ordinal)
            || combinedMessage.Contains("не довер", StringComparison.Ordinal)
            || combinedMessage.Contains("ssl", StringComparison.Ordinal);

        return mentionsCertificate && mentionsTrustFailure;
    }

    private static IEnumerable<string> EnumerateExceptionMessages(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
                yield return current.Message;
        }
    }

    private static string ConvertDatabaseValueToDisplayText(object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                return string.Empty;
            case string text:
                return text;
            case DateTime dateTime:
                return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateTimeOffset dateTimeOffset:
                return dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            case TimeSpan timeSpan:
                return timeSpan.ToString("c", CultureInfo.InvariantCulture);
            case byte[] bytes:
                return $"<bytes {bytes.Length}>";
        }

        var type = value.GetType();
        if (IsSqlXml(type))
        {
            var isNull = TryReadBoolProperty(value, "IsNull");
            if (isNull == true)
                return string.Empty;

            var xmlText = TryReadStringProperty(value, "Value");
            if (xmlText is not null)
                return xmlText;
        }

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

        return value.ToString() ?? string.Empty;
    }

    private static bool IsSqlXml(Type type)
    {
        return string.Equals(type.FullName, "Microsoft.Data.SqlTypes.SqlXml", StringComparison.Ordinal)
            || string.Equals(type.FullName, "System.Data.SqlTypes.SqlXml", StringComparison.Ordinal);
    }

    private static bool? TryReadBoolProperty(object instance, string propertyName)
    {
        try
        {
            var value = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            if (value is bool boolValue)
                return boolValue;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadStringProperty(object instance, string propertyName)
    {
        try
        {
            return instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as string;
        }
        catch
        {
            return null;
        }
    }
}
