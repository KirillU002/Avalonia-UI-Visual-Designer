using FormDesigner.Models;
using Npgsql;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public static class PostgreSqlDiagnostics
{
    public static event Action<string, string>? Emitted;
    public static void Write(string code, string safeDetails)
    {
        Debug.WriteLine($"{code} {safeDetails}");
        Emitted?.Invoke(code, safeDetails);
    }
}

public static class PostgreSqlErrors
{
    public static string Message(Exception exception) => exception switch
    {
        OperationCanceledException => "Загрузка PostgreSQL отменена.",
        DesignerSystem.Binding.DatabaseSchemaException => "Не удалось получить структуру таблицы PostgreSQL: таблица не найдена или недоступна.",
        PostgresException { SqlState: "28P01" or "28000" } => "Неверные учётные данные PostgreSQL.",
        PostgresException { SqlState: "3D000" } => "База данных PostgreSQL не найдена.",
        PostgresException { SqlState: "42P01" } => "Таблица PostgreSQL не найдена.",
        PostgresException { SqlState: "3F000" } => "Схема PostgreSQL не найдена.",
        PostgresException { SqlState: "42601" or "42703" or "42883" } => "Ошибка SELECT-запроса PostgreSQL. Проверьте имена колонок и SQL-синтаксис.",
        PostgresException { SqlState: "57014" } => "Превышено время ожидания PostgreSQL.",
        PostgresException { SqlState: "25006" } => "Preview PostgreSQL разрешает только чтение данных.",
        PostgresException { SqlState: "42501" } => "Недостаточно прав для чтения PostgreSQL.",
        TimeoutException => "Превышено время ожидания PostgreSQL.",
        NpgsqlException { InnerException: TimeoutException } => "Превышено время ожидания PostgreSQL.",
        OverflowException or InvalidCastException or FormatException => "Не удалось прочитать значения PostgreSQL. Проверьте типы колонок.",
        ArgumentException or InvalidOperationException => "Проверьте параметры подключения и SELECT-запрос PostgreSQL.",
        _ => "Не удалось подключиться к PostgreSQL. Проверьте сервер, порт и доступ к базе данных."
    };

    // Server error messages may echo SQL literals. Keep exception identity, SQLSTATE and stack,
    // never connection strings, query text, server Detail/Where or credential-bearing messages.
    public static string Details(Exception exception) =>
        $"exception={exception.GetType().FullName}; sqlState={(exception as PostgresException)?.SqlState ?? "-"}; " +
        $"inner={exception.InnerException?.GetType().FullName ?? "-"}; stack={exception.StackTrace ?? "-"}";
}

public sealed class PostgreSqlConnectionService
{
    public const string PackageVersion = "8.0.9";
    private static readonly ConcurrentDictionary<string, string> SessionPasswords = new(StringComparer.Ordinal);

    public SqlConnectionStringBuildResult Build(PostgreSqlSettingsModel settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Database)
            || string.IsNullOrWhiteSpace(settings.Username))
            return new() { ErrorMessage = "Укажите сервер, базу данных и имя пользователя PostgreSQL." };
        if (settings.Port is < 1 or > 65535 || settings.ConnectionTimeoutSeconds is < 1 or > 300
            || settings.CommandTimeoutSeconds is < 1 or > 300 || settings.PreviewRowsLimit is < 1 or > 10000)
            return new() { ErrorMessage = "Проверьте порт, таймауты и лимит строк PostgreSQL." };
        if (!Enum.TryParse<SslMode>(settings.SslMode, out var ssl) || !Enum.IsDefined(ssl))
            return new() { ErrorMessage = "Неизвестный SSL Mode PostgreSQL." };
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host.Trim(), Port = settings.Port, Database = settings.Database.Trim(),
            Username = settings.Username.Trim(), Password = settings.Password,
            SslMode = ssl, Timeout = settings.ConnectionTimeoutSeconds,
            CommandTimeout = settings.CommandTimeoutSeconds, IncludeErrorDetail = false, PersistSecurityInfo = false
        };
        var safe = WithoutSecrets(builder.ConnectionString);
        SessionPasswords[CredentialKey(safe)] = settings.Password;
        return new()
        {
            Success = true, ConnectionString = builder.ConnectionString,
            MaskedConnectionString = safe, Summary = "PostgreSQL: параметры подключения заданы."
        };
    }

    public async Task<SqlConnectionTestResult> TestConnectionAsync(PostgreSqlSettingsModel settings, CancellationToken cancellationToken = default)
    {
        var build = Build(settings);
        if (!build.Success) return new() { Message = build.ErrorMessage };
        return await TestConnectionStringAsync(build.ConnectionString, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SqlConnectionTestResult> TestConnectionStringAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        PostgreSqlDiagnostics.Write("POSTGRES_CONNECTION_START", "provider=Npgsql");
        try
        {
            await using var connection = new NpgsqlConnection(Resolve(connectionString));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            PostgreSqlDiagnostics.Write("POSTGRES_CONNECTION_SUCCESS", $"elapsedMs={watch.ElapsedMilliseconds}");
            return new() { Success = true, Message = "Подключение к PostgreSQL установлено.", ElapsedMilliseconds = watch.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            PostgreSqlDiagnostics.Write("POSTGRES_CONNECTION_FAILED", PostgreSqlErrors.Details(ex));
            return new() { Message = PostgreSqlErrors.Message(ex), ElapsedMilliseconds = watch.ElapsedMilliseconds };
        }
    }

    public static string WithoutSecrets(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "";
        try
        {
            var source = new NpgsqlConnectionStringBuilder(connectionString);
            // Whitelist connection metadata. Also discard Passfile/Options/ErrorDetail.
            return new NpgsqlConnectionStringBuilder
            {
                Host = source.Host, Port = source.Port, Database = source.Database,
                Username = source.Username, SslMode = source.SslMode,
                Timeout = Math.Clamp(source.Timeout, 1, 300),
                CommandTimeout = Math.Clamp(source.CommandTimeout, 1, 300),
                IncludeErrorDetail = false, PersistSecurityInfo = false
            }.ConnectionString;
        }
        catch (ArgumentException) { return ""; }
    }

    public static string Resolve(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.IncludeErrorDetail = false;
        builder.PersistSecurityInfo = false;
        if (string.IsNullOrEmpty(builder.Password)
            && SessionPasswords.TryGetValue(CredentialKey(connectionString), out var password))
            builder.Password = password;
        return builder.ConnectionString;
    }

    private static string CredentialKey(string connectionString)
    {
        var profile = new NpgsqlConnectionStringBuilder(connectionString);
        return string.Join("\0", profile.Host?.ToLowerInvariant(), profile.Port, profile.Database, profile.Username);
    }
}
