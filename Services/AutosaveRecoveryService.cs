using FormDesigner.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public sealed class AutosaveRecoveryService
{
    private const string RecoveryFileName = "active-session.recovery.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string RecoveryDirectoryPath { get; }

    public string RecoveryFilePath => Path.Combine(RecoveryDirectoryPath, RecoveryFileName);

    public AutosaveRecoveryService(string? recoveryDirectoryPath = null)
    {
        RecoveryDirectoryPath = string.IsNullOrWhiteSpace(recoveryDirectoryPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FormDesigner",
                "Recovery")
            : recoveryDirectoryPath;
    }

    public async Task SaveDraftAsync(RecoveryDraftFileModel draft)
    {
        Directory.CreateDirectory(RecoveryDirectoryPath);

        var tempPath = RecoveryFilePath + ".tmp";
        var json = JsonSerializer.Serialize(draft, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Copy(tempPath, RecoveryFilePath, overwrite: true);
        File.Delete(tempPath);
    }

    public async Task<RecoveryDraftFileModel?> TryLoadDraftAsync()
    {
        if (!File.Exists(RecoveryFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(RecoveryFilePath).ConfigureAwait(false);
            var draft = JsonSerializer.Deserialize<RecoveryDraftFileModel>(json, JsonOptions);
            return draft is null || string.IsNullOrWhiteSpace(draft.DocumentJson)
                ? null
                : draft;
        }
        catch
        {
            TryDeleteDraft();
            return null;
        }
    }

    public bool TryDeleteDraft()
    {
        try
        {
            if (File.Exists(RecoveryFilePath))
                File.Delete(RecoveryFilePath);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
