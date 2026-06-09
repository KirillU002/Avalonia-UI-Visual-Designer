using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FormDesigner.Services;

public sealed class ArtifactCleanupService
{
    private const int DefaultKeepLatestRuns = 5;
    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    public ArtifactCleanupResult Clean(string repositoryRoot, int keepLatestRuns = DefaultKeepLatestRuns, TimeSpan? maxAge = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var artifactsRoot = Path.GetFullPath(Path.Combine(root, "artifacts"));
        EnsureInsideRoot(root, artifactsRoot);

        if (!Directory.Exists(artifactsRoot))
            return ArtifactCleanupResult.Empty(artifactsRoot);

        var deletedFiles = 0;
        var deletedDirectories = 0;
        long deletedBytes = 0;
        var removedPaths = new List<string>();

        void DeleteDirectory(DirectoryInfo directory)
        {
            var fullName = Path.GetFullPath(directory.FullName);
            EnsureInsideRoot(artifactsRoot, fullName);
            if (!directory.Exists)
                return;

            var size = GetDirectorySize(directory);
            DeleteWithRetry(() => directory.Delete(recursive: true));
            deletedDirectories++;
            deletedBytes += size;
            removedPaths.Add(fullName);
        }

        void DeleteFile(FileInfo file)
        {
            var fullName = Path.GetFullPath(file.FullName);
            EnsureInsideRoot(artifactsRoot, fullName);
            if (!file.Exists)
                return;

            var size = file.Length;
            DeleteWithRetry(file.Delete);
            deletedFiles++;
            deletedBytes += size;
            removedPaths.Add(fullName);
        }

        PruneTimestampedRuns(Path.Combine(artifactsRoot, "smoke-tests"), keepLatestRuns, DeleteDirectory);
        PruneTimestampedRuns(Path.Combine(artifactsRoot, "export-validation"), keepLatestRuns, DeleteDirectory);
        PruneTimestampedRuns(Path.Combine(artifactsRoot, "export"), keepLatestRuns, DeleteDirectory);

        var threshold = DateTime.UtcNow - (maxAge ?? DefaultMaxAge);
        foreach (var directory in new DirectoryInfo(artifactsRoot).GetDirectories())
        {
            if (directory.Name.Equals("smoke-tests", StringComparison.OrdinalIgnoreCase)
                || directory.Name.Equals("export-validation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (directory.Name.Equals("export", StringComparison.OrdinalIgnoreCase))
                continue;

            if (directory.LastWriteTimeUtc < threshold)
                DeleteDirectory(directory);
        }

        foreach (var file in new DirectoryInfo(artifactsRoot).GetFiles())
        {
            if (file.LastWriteTimeUtc < threshold)
                DeleteFile(file);
        }

        return new ArtifactCleanupResult(
            artifactsRoot,
            deletedFiles,
            deletedDirectories,
            deletedBytes,
            removedPaths);
    }

    private static void PruneTimestampedRuns(string path, int keepLatestRuns, Action<DirectoryInfo> deleteDirectory)
    {
        var root = new DirectoryInfo(Path.GetFullPath(path));
        if (!root.Exists)
            return;

        var runs = root.GetDirectories()
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ThenByDescending(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var staleRun in runs.Skip(Math.Max(0, keepLatestRuns)))
            deleteDirectory(staleRun);
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean path outside artifacts root: {normalizedPath}");
        }
    }

    private static void DeleteWithRetry(Action delete)
    {
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                delete();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                System.Threading.Thread.Sleep(150 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                System.Threading.Thread.Sleep(150 * attempt);
            }
        }

        delete();
    }
}

public sealed record ArtifactCleanupResult(
    string ArtifactsRoot,
    int FilesDeleted,
    int DirectoriesDeleted,
    long BytesDeleted,
    IReadOnlyList<string> RemovedPaths)
{
    public double MegabytesDeleted => BytesDeleted / 1024d / 1024d;

    public string Summary => $"{FilesDeleted} files, {DirectoriesDeleted} folders, {MegabytesDeleted:0.##} MB freed";

    public static ArtifactCleanupResult Empty(string artifactsRoot) =>
        new(artifactsRoot, 0, 0, 0, Array.Empty<string>());
}
