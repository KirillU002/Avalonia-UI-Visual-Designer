using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FormDesigner.DesignerSystem.Binding;

internal static class DataSourceIdentity
{
    public static string BuildKey(BindingSourceModel? source)
    {
        if (source is null)
            return string.Empty;

        return BuildKey(
            source.Id,
            source.SourceKind,
            source.SourceAssemblyPath,
            source.SourceTypeFullName,
            source.SourceTableName,
            source.SourceConnectionString,
            source.SourceSchemaName,
            source.SourceQuery,
            source.Fields.Select(field => (field.Path, field.TypeName, field.IsVisible, field.DbType, field.IsPrimaryKey, field.IsNullable)));
    }

    public static string BuildKey(BindingSourceFileModel? source)
    {
        if (source is null)
            return string.Empty;

        return BuildKey(
            source.Id,
            source.SourceKind,
            source.SourceAssemblyPath,
            source.SourceTypeFullName,
            source.SourceTableName,
            source.SourceConnectionString,
            source.SourceSchemaName,
            source.SourceQuery,
            source.Fields.Select(field => (field.Path, field.TypeName, field.IsVisible, field.DbType, field.IsPrimaryKey, field.IsNullable)));
    }

    public static string BuildDisplayName(BindingSourceModel? source)
    {
        if (source is null)
            return "No source";

        var sourceName = string.IsNullOrWhiteSpace(source.Name) ? "BindingSource" : source.Name.Trim();
        if (IsSqlServer(source.SourceKind))
        {
            var queryLabel = string.IsNullOrWhiteSpace(source.SourceQuery)
                ? BuildSqlObjectLabel(source.SourceSchemaName, source.SourceTableName)
                : $"query:{ShortHash(source.SourceQuery)}";
            return $"{sourceName} | SQL | {queryLabel}";
        }

        if (IsAssembly(source.SourceKind))
        {
            var assemblyName = string.IsNullOrWhiteSpace(source.SourceAssemblyPath)
                ? "assembly"
                : Path.GetFileNameWithoutExtension(source.SourceAssemblyPath);
            var typeName = string.IsNullOrWhiteSpace(source.SourceTypeFullName)
                ? source.SourceTableName
                : source.SourceTypeFullName;
            return $"{sourceName} | DLL {assemblyName} | {typeName}";
        }

        return $"{sourceName} | {Normalize(source.SourceKind, "Manual")}";
    }

    public static string BuildDisplayName(BindingSourceFileModel? source)
    {
        if (source is null)
            return "No source";

        var sourceName = string.IsNullOrWhiteSpace(source.Name) ? "BindingSource" : source.Name.Trim();
        if (IsSqlServer(source.SourceKind))
        {
            var queryLabel = string.IsNullOrWhiteSpace(source.SourceQuery)
                ? BuildSqlObjectLabel(source.SourceSchemaName, source.SourceTableName)
                : $"query:{ShortHash(source.SourceQuery)}";
            return $"{sourceName} | SQL | {queryLabel}";
        }

        if (IsAssembly(source.SourceKind))
        {
            var assemblyName = string.IsNullOrWhiteSpace(source.SourceAssemblyPath)
                ? "assembly"
                : Path.GetFileNameWithoutExtension(source.SourceAssemblyPath);
            var typeName = string.IsNullOrWhiteSpace(source.SourceTypeFullName)
                ? source.SourceTableName
                : source.SourceTypeFullName;
            return $"{sourceName} | DLL {assemblyName} | {typeName}";
        }

        return $"{sourceName} | {Normalize(source.SourceKind, "Manual")}";
    }

    public static bool IsSqlServer(string? sourceKind)
    {
        return string.Equals(sourceKind, "SqlServer", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAssembly(string? sourceKind)
    {
        return string.Equals(sourceKind, "Assembly", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceKind, "DllTable", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceKind, "Dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildKey(
        string id,
        string sourceKind,
        string assemblyPath,
        string typeFullName,
        string tableName,
        string connectionString,
        string schemaName,
        string query,
        IEnumerable<(string Path, string TypeName, bool IsVisible, string DbType, bool IsPrimaryKey, bool IsNullable)> fields)
    {
        var kind = Normalize(sourceKind, "Manual");
        var schemaHash = Hash(string.Join("\n", fields
            .OrderBy(field => Normalize(field.Path, string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(field => $"{Normalize(field.Path, string.Empty)}:{Normalize(field.TypeName, "string")}:{field.IsVisible}:{Normalize(field.DbType, string.Empty)}:{field.IsPrimaryKey}:{field.IsNullable}")));

        if (IsSqlServer(kind))
        {
            return string.Join("|",
                "SQL",
                Normalize(id, string.Empty),
                Hash(connectionString),
                Normalize(schemaName, "dbo"),
                Normalize(tableName, string.Empty),
                Hash(query),
                schemaHash);
        }

        if (IsAssembly(kind))
        {
            var normalizedPath = NormalizePath(assemblyPath);
            var assemblyName = string.IsNullOrWhiteSpace(assemblyPath)
                ? "assembly"
                : Path.GetFileNameWithoutExtension(assemblyPath);
            var (namespaceName, typeName) = SplitTypeName(typeFullName);

            return string.Join("|",
                "DLL",
                Normalize(assemblyName, "assembly"),
                Hash(normalizedPath),
                Normalize(namespaceName, string.Empty),
                Normalize(typeName, string.Empty),
                Normalize(tableName, string.Empty),
                schemaHash);
        }

        return string.Join("|",
            "MANUAL",
            Normalize(id, string.Empty),
            Normalize(tableName, string.Empty),
            schemaHash);
    }

    private static string BuildSqlObjectLabel(string schemaName, string tableName)
    {
        var schema = Normalize(schemaName, "dbo");
        var table = Normalize(tableName, "table");
        return $"{schema}.{table}";
    }

    private static (string NamespaceName, string TypeName) SplitTypeName(string? typeFullName)
    {
        var normalized = Normalize(typeFullName, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
            return (string.Empty, string.Empty);

        var lastDot = normalized.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= normalized.Length - 1)
            return (string.Empty, normalized);

        return (normalized[..lastDot], normalized[(lastDot + 1)..]);
    }

    private static string NormalizePath(string? value)
    {
        var text = Normalize(value, string.Empty);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        try
        {
            return Path.GetFullPath(text).Trim().ToUpperInvariant();
        }
        catch
        {
            return text.Trim().ToUpperInvariant();
        }
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ShortHash(string? value)
    {
        var hash = Hash(value);
        return hash.Length <= 10 ? hash : hash[..10];
    }

    private static string Hash(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "0";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes);
    }
}
