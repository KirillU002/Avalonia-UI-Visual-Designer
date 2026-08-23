using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.DesignerSystem.Hosting;

/// <summary>
/// Узкая граница между Designer и конкретным приложением-host. Контракт не зависит
/// от Avalonia, Visual Studio или глобального service locator.
/// </summary>
public interface IDesignerHostServices
{
    IDesignerClipboard Clipboard { get; }
    IDesignerDialogService Dialogs { get; }
    IDesignerFilePickerService FilePicker { get; }
    IDesignerNotificationService Notifications { get; }
    IDesignerPathService Paths { get; }
    IDesignerFileSystem FileSystem { get; }
    IDesignerScheduler Scheduler { get; }
    IDesignerExternalLauncher ExternalLauncher { get; }
    IDesignerHostCommandService Commands { get; }
}

public interface IDesignerClipboard
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

public enum DesignerDialogKind
{
    Confirmation,
    Information,
    Warning,
    Error,
    UnsavedChanges
}

public enum DesignerDialogResult
{
    Accepted,
    Rejected,
    Cancelled,
    Save,
    Discard
}

public sealed record DesignerDialogRequest(
    DesignerDialogKind Kind,
    string Title,
    string Message,
    string Details = "",
    string AcceptText = "OK",
    string RejectText = "Cancel");

public interface IDesignerDialogService
{
    Task<DesignerDialogResult> ShowAsync(DesignerDialogRequest request, CancellationToken cancellationToken = default);
}

public sealed record DesignerFileTypeFilter(string Name, IReadOnlyList<string> Extensions)
{
    public static DesignerFileTypeFilter Create(string name, params string[] extensions) =>
        new(name, extensions ?? Array.Empty<string>());
}

public sealed record DesignerOpenFilePickerOptions(
    string Title,
    IReadOnlyList<DesignerFileTypeFilter> FileTypes,
    bool AllowMultiple = false,
    string? InitialDirectory = null);

public sealed record DesignerSaveFilePickerOptions(
    string Title,
    string SuggestedFileName,
    string DefaultExtension,
    IReadOnlyList<DesignerFileTypeFilter> FileTypes,
    bool ShowOverwritePrompt = true,
    string? InitialDirectory = null);

/// <summary>
/// Host-owned file handle. Designer code uses paths when they are available and
/// can still work with non-local storage through the stream methods.
/// </summary>
public interface IDesignerHostFile
{
    string Name { get; }
    string? LocalPath { get; }
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
    Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default);
}

public interface IDesignerFilePickerService
{
    Task<IReadOnlyList<IDesignerHostFile>> OpenFilesAsync(DesignerOpenFilePickerOptions options, CancellationToken cancellationToken = default);
    Task<IDesignerHostFile?> SaveFileAsync(DesignerSaveFilePickerOptions options, CancellationToken cancellationToken = default);
    Task<string?> SelectFolderAsync(string title, string? initialDirectory = null, CancellationToken cancellationToken = default);
}

public enum DesignerNotificationSeverity
{
    Information,
    Success,
    Warning,
    Error
}

public sealed record DesignerNotification(
    DesignerNotificationSeverity Severity,
    string Title,
    string Message = "",
    string Details = "",
    bool IsPersistent = false);

public interface IDesignerNotificationService
{
    void Publish(DesignerNotification notification);
}

public interface IDesignerPathService
{
    string ApplicationBaseDirectory { get; }
    string UserDataDirectory { get; }
    string LogsDirectory { get; }
    string TempDirectory { get; }
    string PluginDirectory { get; }
    string RecoveryDirectory { get; }
    string ArtifactsDirectory { get; }
}

public interface IDesignerFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);
    Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default);
    Task WriteAllTextAtomicallyAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default);
    void DeleteFile(string path);
}

public enum DesignerSchedulerPriority
{
    Normal,
    Background
}

public interface IDesignerScheduler
{
    bool CheckAccess();
    void Post(Action action, DesignerSchedulerPriority priority = DesignerSchedulerPriority.Normal);
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}

public interface IDesignerExternalLauncher
{
    Task OpenFileAsync(string path, CancellationToken cancellationToken = default);
    Task OpenFolderAsync(string path, CancellationToken cancellationToken = default);
    Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default);
}

public enum DesignerHostCommand
{
    OpenSettings,
    OpenHelp,
    OpenPreview,
    OpenExport
}

public interface IDesignerHostCommandService
{
    Task ExecuteAsync(DesignerHostCommand command, CancellationToken cancellationToken = default);
}
