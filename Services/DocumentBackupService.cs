using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public sealed class DocumentBackupService
{
    private const int MaxBackupsPerDocument = 5;
    private const string BackupFolderName = ".formdesigner-backups";

    public async Task<BackupFileModel?> TryCreateBackupAsync(string documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath))
            return null;

        try
        {
            var backupDirectory = GetBackupDirectory(documentPath);
            Directory.CreateDirectory(backupDirectory);

            var sourceFileName = Path.GetFileNameWithoutExtension(documentPath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var backupPath = Path.Combine(
                backupDirectory,
                $"{sourceFileName}.{timestamp}.backup.formdesigner.json");

            await using (var source = File.Open(documentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await using (var target = File.Create(backupPath))
            {
                await source.CopyToAsync(target).ConfigureAwait(false);
            }

            TrimBackups(documentPath);
            return ToBackupFileModel(backupPath);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<BackupFileModel> ListBackups(string documentPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(documentPath))
                return Array.Empty<BackupFileModel>();

            var backupDirectory = GetBackupDirectory(documentPath);
            if (!Directory.Exists(backupDirectory))
                return Array.Empty<BackupFileModel>();

            var prefix = Path.GetFileNameWithoutExtension(documentPath) + ".";
            return Directory
                .EnumerateFiles(backupDirectory, "*.backup.formdesigner.json")
                .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(ToBackupFileModel)
                .OrderByDescending(item => item.CreatedUtc)
                .ToList();
        }
        catch
        {
            return Array.Empty<BackupFileModel>();
        }
    }

    private static string GetBackupDirectory(string documentPath)
    {
        var directory = Path.GetDirectoryName(documentPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(directory, BackupFolderName);
    }

    private void TrimBackups(string documentPath)
    {
        var backups = ListBackups(documentPath);
        foreach (var backup in backups.Skip(MaxBackupsPerDocument))
        {
            try
            {
                if (File.Exists(backup.FilePath))
                    File.Delete(backup.FilePath);
            }
            catch
            {
                // Backup cleanup must never block saving the main document.
            }
        }
    }

    private static BackupFileModel ToBackupFileModel(string path)
    {
        var info = new FileInfo(path);
        return new BackupFileModel
        {
            FilePath = path,
            DisplayName = info.Name,
            CreatedUtc = info.Exists ? info.CreationTimeUtc : DateTime.UtcNow,
            SizeBytes = info.Exists ? info.Length : 0
        };
    }
}
