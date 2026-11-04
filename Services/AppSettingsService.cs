using FormDesigner.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public sealed class AppSettingsService
{
    private const string SettingsFileName = "app-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsDirectoryPath { get; }

    public string SettingsFilePath => Path.Combine(SettingsDirectoryPath, SettingsFileName);

    public string? LastError { get; private set; }

    public AppSettingsService(string? settingsDirectoryPath = null)
    {
        SettingsDirectoryPath = string.IsNullOrWhiteSpace(settingsDirectoryPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FormDesigner")
            : settingsDirectoryPath;
    }

    public AppSettingsModel Load()
    {
        LastError = null;

        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettingsModel();

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettingsModel>(json, JsonOptions) ?? new AppSettingsModel();
            settings.Preview ??= new PreviewSettingsModel();
            settings.PostgreSql ??= new PostgreSqlSettingsModel();
            settings.PostgreSql.Password = settings.PostgreSql.RememberPassword
                ? WindowsSecretProtection.Unprotect(settings.PostgreSql.ProtectedPassword) : "";
            return settings;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new AppSettingsModel();
        }
    }

    public async Task SaveAsync(AppSettingsModel settings)
    {
        LastError = null;

        try
        {
            Directory.CreateDirectory(SettingsDirectoryPath);

            settings.PostgreSql.ProtectedPassword = settings.PostgreSql.RememberPassword
                ? WindowsSecretProtection.Protect(settings.PostgreSql.Password) : "";

            var tempPath = SettingsFilePath + ".tmp";
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
