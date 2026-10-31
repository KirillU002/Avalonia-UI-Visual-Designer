using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace FormDesigner.Services;

public sealed class DocumentBackupService
{
    private const int MaxBackupsPerDocument = 5;
    private const string BackupFolderName = ".formdesigner-backups";
    private readonly string _fallbackDirectory;

    public DocumentBackupService(string? fallbackDirectory = null)
    {
        _fallbackDirectory = string.IsNullOrWhiteSpace(fallbackDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : fallbackDirectory;
    }

    public async Task<BackupFileModel?> TryCreateBackupAsync(string documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath))
            return null;

        try
        {
            var backupDirectory = GetBackupDirectory(documentPath);
            Directory.CreateDirectory(backupDirectory);

            var sourceFileName = Path.GetFileName(documentPath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.fffffff", CultureInfo.InvariantCulture);
            var backupPath = Path.Combine(
                backupDirectory,
                $"{sourceFileName}.{timestamp}.{Guid.NewGuid():N}.backup.formdesigner.json");

            await using (var source = File.Open(documentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            await using (var target = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
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

            return Directory
                .EnumerateFiles(backupDirectory, "*.backup.formdesigner.json")
                .Where(path => IsBackupForDocument(path, documentPath))
                .Select(ToBackupFileModel)
                .OrderByDescending(item => item.CreatedUtc)
                .ToList();
        }
        catch
        {
            return Array.Empty<BackupFileModel>();
        }
    }

    private static bool IsBackupForDocument(string backupPath, string documentPath)
    {
        const string suffix = ".backup.formdesigner.json";
        var name = Path.GetFileName(backupPath);
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var prefix in new[] { Path.GetFileName(documentPath) + ".", Path.GetFileNameWithoutExtension(documentPath) + "." })
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.Length <= prefix.Length + suffix.Length) continue;
            var stamp = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            // Recognize the complete suffix, never another document with a shared name prefix.
            if (DateTime.TryParseExact(stamp, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return true;
            var parts = stamp.Split('.');
            if (parts.Length == 3 && Guid.TryParseExact(parts[2], "N", out _)
                && DateTime.TryParseExact(parts[0] + "." + parts[1], "yyyy-MM-dd_HH-mm-ss.fffffff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return true;
        }
        return false;
    }

    private string GetBackupDirectory(string documentPath)
    {
        var directory = Path.GetDirectoryName(documentPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = _fallbackDirectory;

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
