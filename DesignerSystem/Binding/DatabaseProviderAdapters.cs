using Microsoft.Data.SqlClient;
using Npgsql;
using System;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace FormDesigner.DesignerSystem.Binding;

public interface IDatabaseProviderAdapter
{
    string SourceKind { get; }
    DbConnection CreateConnection(string connectionString);
    string QuoteIdentifier(string identifier);
    string Select(string schema, string table, string? query, int? rowLimit);
    string SchemasQuery { get; }
    string TablesQuery { get; }
    string ColumnsQuery { get; }
}

public static class DatabaseProviderAdapters
{
    public static IDatabaseProviderAdapter For(string sourceKind) => DataSourceIdentity.IsPostgreSql(sourceKind)
        ? PostgreSql : DataSourceIdentity.IsSqlServer(sourceKind) ? SqlServer
        : throw new ArgumentException("Источник не является источником базы данных.", nameof(sourceKind));
    public static IDatabaseProviderAdapter PostgreSql { get; } = new PostgreSqlProviderAdapter();
    public static IDatabaseProviderAdapter SqlServer { get; } = new SqlServerProviderAdapter();
}

public sealed class SqlServerProviderAdapter : IDatabaseProviderAdapter
{
    public string SourceKind => "SqlServer";
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);
    public string QuoteIdentifier(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    public string Select(string schema, string table, string? query, int? rowLimit) =>
        "SELECT " + (rowLimit.HasValue ? $"TOP ({Math.Clamp(rowLimit.Value, 1, 10000).ToString(CultureInfo.InvariantCulture)}) " : "")
        + "* FROM " + (string.IsNullOrWhiteSpace(query)
            ? $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}"
            : $"({query.Trim().TrimEnd(';')}) AS DesignerQuery");
    public string SchemasQuery => "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA ORDER BY SCHEMA_NAME";
    public string TablesQuery => "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=@schema ORDER BY TABLE_NAME";
    public string ColumnsQuery => @"SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS t
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE k ON k.CONSTRAINT_NAME=t.CONSTRAINT_NAME
          AND k.CONSTRAINT_SCHEMA=t.CONSTRAINT_SCHEMA AND k.TABLE_NAME=t.TABLE_NAME
        WHERE t.CONSTRAINT_TYPE='PRIMARY KEY' AND k.TABLE_SCHEMA=c.TABLE_SCHEMA
          AND k.TABLE_NAME=c.TABLE_NAME AND k.COLUMN_NAME=c.COLUMN_NAME) THEN 1 ELSE 0 END AS bit)
        FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_SCHEMA=@schema AND c.TABLE_NAME=@table ORDER BY c.ORDINAL_POSITION";
}

public sealed class PostgreSqlProviderAdapter : IDatabaseProviderAdapter
{
    public string SourceKind => "PostgreSql";
    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(Services.PostgreSqlConnectionService.Resolve(connectionString));
    public string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.IndexOf('\0') >= 0)
            throw new ArgumentException("Укажите корректное имя схемы или таблицы PostgreSQL.");
        // Names returned by discovery are raw identifiers: never split on dots or fold case.
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
    public string Select(string schema, string table, string? query, int? rowLimit)
    {
        var from = string.IsNullOrWhiteSpace(query)
            ? $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}"
            : $"({PostgreSqlReadQuery.Validate(query)}) AS \"__designer_query\"";
        return "SELECT * FROM " + from + (rowLimit.HasValue
            ? " LIMIT " + Math.Clamp(rowLimit.Value, 1, 10000).ToString(CultureInfo.InvariantCulture) : "");
    }
    public string SchemasQuery => "SELECT schema_name FROM information_schema.schemata WHERE schema_name NOT IN ('pg_catalog','information_schema') AND schema_name NOT LIKE 'pg_toast%' AND schema_name NOT LIKE 'pg_temp_%' ORDER BY schema_name";
    public string TablesQuery => "SELECT table_name, table_type FROM information_schema.tables WHERE table_schema=@schema AND table_type IN ('BASE TABLE','VIEW') ORDER BY table_name";
    public string ColumnsQuery => @"SELECT a.attname, pg_catalog.format_type(a.atttypid,a.atttypmod), NOT a.attnotnull,
        EXISTS(SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid=c.oid AND i.indisprimary AND a.attnum=ANY(i.indkey))
        FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid
        JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=@schema AND c.relname=@table AND c.relkind IN ('r','v','m','p','f')
        AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum";
}

/// <summary>Conservative single SELECT/WITH policy. Server READ ONLY is the authority for functions.</summary>
public static class PostgreSqlReadQuery
{
    public static string Validate(string query)
    {
        var sql = query.Trim();
        if (sql.EndsWith(';')) sql = sql[..^1].TrimEnd();
        var tokens = new System.Collections.Generic.List<string>();
        for (var i = 0; i < sql.Length;)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == ';') throw Invalid();
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            { while (i < sql.Length && sql[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                var depth = 1;
                while (i < sql.Length && depth > 0)
                {
                    if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; }
                    else if (i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }
                if (depth != 0) throw Invalid();
                continue;
            }
            if (c is '\'' or '"')
            {
                var quote = c;
                i++;
                var closed = false;
                while (i < sql.Length)
                {
                    // Reject backslash literals conservatively; avoids E-string ambiguity.
                    if (sql[i] == '\\') throw Invalid();
                    if (sql[i++] != quote) continue;
                    if (i < sql.Length && sql[i] == quote) { i++; continue; }
                    closed = true; break;
                }
                if (!closed) throw Invalid();
                continue;
            }
            if (c == '$')
            {
                var end = i + 1;
                while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_')) end++;
                if (end >= sql.Length || sql[end] != '$') throw Invalid();
                var delimiter = sql.Substring(i, end - i + 1);
                var close = sql.IndexOf(delimiter, end + 1, StringComparison.Ordinal);
                if (close < 0) throw Invalid();
                i = close + delimiter.Length; continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$')) i++;
                tokens.Add(sql[start..i].ToUpperInvariant());
            }
            else i++;
        }
        if (tokens.Count == 0 || tokens[0] is not ("SELECT" or "WITH")) throw Invalid();
        foreach (var token in tokens)
            if (token is "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "INTO" or "COPY" or "CREATE"
                or "ALTER" or "DROP" or "TRUNCATE" or "CALL" or "DO" or "SET" or "RESET" or "COMMIT"
                or "ROLLBACK" or "BEGIN" or "GRANT" or "REVOKE" or "VACUUM" or "ANALYZE") throw Invalid();
        // A newline keeps a trailing line comment from swallowing the wrapper's closing parenthesis.
        return sql + "\n";
    }
    private static ArgumentException Invalid() => new("Preview PostgreSQL разрешает один SELECT или WITH без изменяющих команд.");
}
