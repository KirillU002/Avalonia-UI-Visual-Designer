using FormDesigner.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public sealed class SqlConnectionStringBuildResult
{
    public bool Success { get; init; }

    public string ConnectionString { get; init; } = "";

    public string MaskedConnectionString { get; init; } = "";

    public string Summary { get; init; } = "";

    public string ErrorMessage { get; init; } = "";
}

public sealed class SqlConnectionTestResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = "";

    public long ElapsedMilliseconds { get; init; }
}

public sealed class SqlConnectionStringBuilderService
{
    public SqlConnectionStringBuildResult Build(SqlServerSettingsModel? settings)
    {
        if (settings is null)
            return Fail("SQL Server settings are not available.");

        var server = settings.ServerName?.Trim() ?? "";
        var database = settings.DatabaseName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(server))
            return Fail("Укажите Server name.");
        if (string.IsNullOrWhiteSpace(database))
            return Fail("Укажите Database name.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            TrustServerCertificate = settings.TrustServerCertificate,
            Encrypt = settings.EncryptConnection,
            ConnectTimeout = Math.Clamp(settings.ConnectionTimeoutSeconds, 1, 300)
        };

        var authMode = NormalizeAuthMode(settings.AuthenticationMode);
        if (string.Equals(authMode, SqlServerSettingsModel.AuthSqlLogin, StringComparison.Ordinal))
        {
            var userName = settings.UserName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(userName))
                return Fail("Укажите User name для SQL Login.");

            builder.IntegratedSecurity = false;
            builder.UserID = userName;
            builder.Password = settings.Password ?? "";
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        var connectionString = builder.ConnectionString;
        var masked = MaskPassword(connectionString);
        var summary = $"{server} / {database} / {authMode}";
        Debug.WriteLine($"SQL_CONNECTION_STRING_BUILT server={server}; database={database}; authMode={authMode}; connectionString={masked}");

        return new SqlConnectionStringBuildResult
        {
            Success = true,
            ConnectionString = connectionString,
            MaskedConnectionString = masked,
            Summary = summary
        };
    }

    public async Task<SqlConnectionTestResult> TestConnectionAsync(SqlServerSettingsModel settings, CancellationToken cancellationToken = default)
    {
        var build = Build(settings);
        if (!build.Success)
            return new SqlConnectionTestResult { Success = false, Message = build.ErrorMessage };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            Debug.WriteLine($"SQL_CONNECTION_TEST_START server={settings.ServerName}; database={settings.DatabaseName}");
            await using var connection = new SqlConnection(build.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            Debug.WriteLine($"SQL_CONNECTION_TEST_SUCCESS elapsedMs={stopwatch.ElapsedMilliseconds}");
            return new SqlConnectionTestResult
            {
                Success = true,
                Message = $"SQL connection OK ({stopwatch.ElapsedMilliseconds} ms).",
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Debug.WriteLine($"SQL_CONNECTION_TEST_FAILED reason={ex.Message}");
            return new SqlConnectionTestResult
            {
                Success = false,
                Message = $"Не удалось подключиться: {ex.Message}",
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
    }

    public static string MaskPassword(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "";

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
                builder.Password = "***";
            return builder.ConnectionString;
        }
        catch
        {
            return connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase)
                ? "SQL connection string (password masked)"
                : connectionString;
        }
    }

    public static string NormalizeAuthMode(string? value) =>
        string.Equals(value, SqlServerSettingsModel.AuthSqlLogin, StringComparison.OrdinalIgnoreCase)
            ? SqlServerSettingsModel.AuthSqlLogin
            : SqlServerSettingsModel.AuthWindows;

    private static SqlConnectionStringBuildResult Fail(string message) =>
        new()
        {
            Success = false,
            ErrorMessage = message,
            Summary = message
        };
}
