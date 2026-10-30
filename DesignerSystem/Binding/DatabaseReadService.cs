using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.DesignerSystem.Binding;

public sealed record DatabaseTable(string Name, string Kind);
public sealed record DatabaseColumn(string Name, string DataType, bool IsNullable, bool IsPrimaryKey);

/// <summary>Shared async ADO.NET discovery and bounded reads; each provider owns its SQL dialect.</summary>
public sealed class DatabaseReadService
{
    private readonly IDatabaseProviderAdapter _adapter;
    public DatabaseReadService(IDatabaseProviderAdapter adapter) => _adapter = adapter;

    public async Task<IReadOnlyList<string>> GetSchemasAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var rows = await ReadMetadataAsync(connectionString, _adapter.SchemasQuery, null, null, cancellationToken).ConfigureAwait(false);
        Report("POSTGRES_SCHEMA_LOADED", rows.Count);
        return rows.Select(row => Convert.ToString(row[0], CultureInfo.InvariantCulture) ?? "").ToArray();
    }
    public async Task<IReadOnlyList<DatabaseTable>> GetTablesAsync(string connectionString, string schema, CancellationToken cancellationToken = default)
    {
        var rows = await ReadMetadataAsync(connectionString, _adapter.TablesQuery, schema, null, cancellationToken).ConfigureAwait(false);
        Report("POSTGRES_TABLES_LOADED", rows.Count);
        return rows.Select(row => new DatabaseTable(Convert.ToString(row[0]) ?? "", Convert.ToString(row[1]) ?? "")).ToArray();
    }
    public async Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(string connectionString, string schema, string table, CancellationToken cancellationToken = default)
    {
        var rows = await ReadMetadataAsync(connectionString, _adapter.ColumnsQuery, schema, table, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0) throw new DatabaseSchemaException("Не удалось получить структуру таблицы: таблица не найдена или недоступна.");
        Report("POSTGRES_COLUMNS_LOADED", rows.Count);
        return rows.Select(row => new DatabaseColumn(Convert.ToString(row[0]) ?? "", Convert.ToString(row[1]) ?? "",
            row[2] is bool nullable ? nullable : string.Equals(Convert.ToString(row[2]), "YES", StringComparison.OrdinalIgnoreCase),
            row[3] is bool primary && primary)).ToArray();
    }

    public async Task<IReadOnlyList<BindingFieldModel>> DescribeAsync(BindingSourceModel source, CancellationToken cancellationToken = default)
    {
        var query = _adapter.Select(source.SourceSchemaName, source.SourceTableName, source.SourceQuery, 1);
        var metadata = string.IsNullOrWhiteSpace(source.SourceQuery)
            ? await GetColumnsAsync(source.SourceConnectionString, source.SourceSchemaName, source.SourceTableName, cancellationToken).ConfigureAwait(false)
            : Array.Empty<DatabaseColumn>();
        await using var connection = _adapter.CreateConnection(source.SourceConnectionString);
        await OpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginReadOnlyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query;
        command.CommandTimeout = CommandTimeout(source.SourceConnectionString);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
        var columns = reader.GetColumnSchema();
        var fields = columns.Select(column =>
        {
            var info = metadata.FirstOrDefault(c => c.Name == column.ColumnName);
            var nullable = info?.IsNullable ?? column.AllowDBNull ?? true;
            var type = FriendlyType(column.DataType);
            if (nullable && type is not ("string" or "byte[]")) type += "?";
            return new BindingFieldModel
            {
                Header = column.ColumnName ?? "Column", Path = column.ColumnName ?? "Column", SampleValue = "",
                TypeName = type, DbType = info?.DataType ?? column.DataTypeName ?? "",
                IsNullable = nullable,
                IsPrimaryKey = info?.IsPrimaryKey ?? column.IsKey ?? false,
                CanRead = true, CanWrite = !(column.IsReadOnly ?? false), IsVisible = true
            };
        }).ToArray();
        Report("POSTGRES_COLUMNS_LOADED", fields.Length);
        return fields;
    }

    public async Task<IReadOnlyList<Dictionary<string, string>>> LoadRowsAsync(BindingSourceModel source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = _adapter.Select(source.SourceSchemaName, source.SourceTableName, source.SourceQuery, Math.Clamp(source.PreviewTopN, 1, 10000));
        Report("POSTGRES_PREVIEW_START", 0);
        await using var connection = _adapter.CreateConnection(source.SourceConnectionString);
        await OpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await BeginReadOnlyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query;
        command.CommandTimeout = CommandTimeout(source.SourceConnectionString);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        var rows = new List<Dictionary<string, string>>();
        while (rows.Count < Math.Clamp(source.PreviewTopN, 1, 10000) && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = DisplayValue(reader.GetValue(i));
            rows.Add(row);
        }
        Report("POSTGRES_PREVIEW_ROWS_LOADED", rows.Count);
        return rows;
    }

    private async Task<List<object[]>> ReadMetadataAsync(string connectionString, string sql, string? schema, string? table, CancellationToken token)
    {
        await using var connection = _adapter.CreateConnection(connectionString);
        await OpenConnectionAsync(connection, token).ConfigureAwait(false);
        await using var transaction = await BeginReadOnlyAsync(connection, token).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeout(connectionString);
        if (schema is not null) AddParameter(command, "schema", schema);
        if (table is not null) AddParameter(command, "table", table);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var rows = new List<object[]>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row); rows.Add(row);
        }
        return rows;
    }

    private async Task<DbTransaction> BeginReadOnlyAsync(DbConnection connection, CancellationToken token)
    {
        var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        if (DataSourceIdentity.IsPostgreSql(_adapter.SourceKind))
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 5;
                command.CommandText = "SET TRANSACTION READ ONLY";
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            catch { await transaction.DisposeAsync().ConfigureAwait(false); throw; }
        }
        return transaction; // rolled back on dispose; nothing is committed
    }
    private async Task OpenConnectionAsync(DbConnection connection, CancellationToken token)
    {
        var postgres = DataSourceIdentity.IsPostgreSql(_adapter.SourceKind);
        if (postgres) PostgreSqlDiagnostics.Write("POSTGRES_CONNECTION_START", "provider=Npgsql");
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            if (postgres) PostgreSqlDiagnostics.Write("POSTGRES_CONNECTION_SUCCESS", "provider=Npgsql");
        }
        catch (Exception ex)
        {
            if (postgres) PostgreSqlDiagnostics.Write("POSTGRES_CONNECTION_FAILED", PostgreSqlErrors.Details(ex));
            throw;
        }
    }
    private int CommandTimeout(string connectionString) => DataSourceIdentity.IsPostgreSql(_adapter.SourceKind)
        ? Math.Clamp(new Npgsql.NpgsqlConnectionStringBuilder(connectionString).CommandTimeout, 1, 300) : 30;
    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
    private void Report(string code, int count)
    {
        if (DataSourceIdentity.IsPostgreSql(_adapter.SourceKind)) PostgreSqlDiagnostics.Write(code, $"count={count}");
    }
    private static string DisplayValue(object? value) => value switch
    {
        null or DBNull => "", byte[] bytes => $"<bytes {bytes.Length}>",
        Array array => string.Join(", ", array.Cast<object?>().Select(DisplayValue)),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? "", _ => value.ToString() ?? ""
    };
    private static string FriendlyType(Type? type) => type == typeof(int) ? "int" : type == typeof(long) ? "long"
        : type == typeof(short) ? "short" : type == typeof(decimal) ? "decimal" : type == typeof(double) ? "double"
        : type == typeof(float) ? "float" : type == typeof(bool) ? "bool" : type == typeof(Guid) ? "Guid"
        : type == typeof(DateTime) ? "DateTime" : type == typeof(DateTimeOffset) ? "DateTimeOffset"
        : type == typeof(TimeSpan) ? "TimeSpan" : type == typeof(byte[]) ? "byte[]" : "string";
}

public sealed class DatabaseSchemaException : Exception
{
    public DatabaseSchemaException(string message) : base(message) { }
}
