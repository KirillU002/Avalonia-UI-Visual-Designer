using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.Models;
using FormDesigner.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.Services;

/// <summary>
/// Standalone composition for host-neutral Designer contracts. Только этот слой
/// знает об Avalonia TopLevel, StorageProvider, custom windows и Process.Start.
/// </summary>
public sealed class StandaloneDesignerHostServices : IDesignerHostServices
{
    private readonly Func<TopLevel?> _topLevelProvider;
    private TopLevel? _attachedTopLevel;

    public StandaloneDesignerHostServices(Func<TopLevel?>? topLevelProvider = null)
    {
        _topLevelProvider = topLevelProvider ?? (() => _attachedTopLevel);
        Paths = new StandaloneDesignerPathService();
        FileSystem = new PhysicalDesignerFileSystem();
        Scheduler = new AvaloniaDesignerScheduler();
        Clipboard = new AvaloniaDesignerClipboard(_topLevelProvider);
        FilePicker = new AvaloniaDesignerFilePickerService(_topLevelProvider);
        Dialogs = new StandaloneDesignerDialogService(_topLevelProvider);
        Notifications = new StandaloneDesignerNotificationService();
        ExternalLauncher = new StandaloneDesignerExternalLauncher();
        Commands = new StandaloneDesignerHostCommandService();
    }

    public IDesignerClipboard Clipboard { get; }
    public IDesignerDialogService Dialogs { get; }
    public IDesignerFilePickerService FilePicker { get; }
    public IDesignerNotificationService Notifications { get; }
    public IDesignerPathService Paths { get; }
    public IDesignerFileSystem FileSystem { get; }
    public IDesignerScheduler Scheduler { get; }
    public IDesignerExternalLauncher ExternalLauncher { get; }
    public IDesignerHostCommandService Commands { get; }

    public void AttachTopLevel(TopLevel topLevel) => _attachedTopLevel = topLevel;
}

public sealed class AvaloniaDesignerClipboard : IDesignerClipboard
{
    private readonly Func<TopLevel?> _topLevelProvider;

    public AvaloniaDesignerClipboard(Func<TopLevel?> topLevelProvider) => _topLevelProvider = topLevelProvider;

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _topLevelProvider()?.Clipboard
            ?? throw new InvalidOperationException("Clipboard is unavailable for the current host.");
        return await clipboard.GetTextAsync();
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _topLevelProvider()?.Clipboard
            ?? throw new InvalidOperationException("Clipboard is unavailable for the current host.");
        await clipboard.SetTextAsync(text ?? string.Empty);
    }
}

public sealed class AvaloniaDesignerFilePickerService : IDesignerFilePickerService
{
    private readonly Func<TopLevel?> _topLevelProvider;

    public AvaloniaDesignerFilePickerService(Func<TopLevel?> topLevelProvider) => _topLevelProvider = topLevelProvider;

    public async Task<IReadOnlyList<IDesignerHostFile>> OpenFilesAsync(DesignerOpenFilePickerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storage = _topLevelProvider()?.StorageProvider;
        if (storage is null || !storage.CanOpen)
            throw new InvalidOperationException("File picker is unavailable for the current host.");

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = options.Title,
            AllowMultiple = options.AllowMultiple,
            FileTypeFilter = ToAvaloniaTypes(options.FileTypes),
            SuggestedStartLocation = await TryGetFolderAsync(storage, options.InitialDirectory)
        });
        return files.Select(file => (IDesignerHostFile)new AvaloniaDesignerHostFile(file)).ToList();
    }

    public async Task<IDesignerHostFile?> SaveFileAsync(DesignerSaveFilePickerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storage = _topLevelProvider()?.StorageProvider;
        if (storage is null || !storage.CanSave)
            throw new InvalidOperationException("Save file picker is unavailable for the current host.");

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = options.Title,
            SuggestedFileName = options.SuggestedFileName,
            DefaultExtension = options.DefaultExtension,
            ShowOverwritePrompt = options.ShowOverwritePrompt,
            FileTypeChoices = ToAvaloniaTypes(options.FileTypes),
            SuggestedStartLocation = await TryGetFolderAsync(storage, options.InitialDirectory)
        });
        return file is null ? null : new AvaloniaDesignerHostFile(file);
    }

    public async Task<string?> SelectFolderAsync(string title, string? initialDirectory = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storage = _topLevelProvider()?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
            throw new InvalidOperationException("Folder picker is unavailable for the current host.");

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDirectory)
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static IReadOnlyList<FilePickerFileType> ToAvaloniaTypes(IReadOnlyList<DesignerFileTypeFilter> filters)
    {
        return filters
            .Where(filter => filter.Extensions.Count > 0)
            .Select(filter => new FilePickerFileType(filter.Name) { Patterns = filter.Extensions.ToArray() })
            .ToList();
    }

    private static async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider storage, string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : await storage.TryGetFolderFromPathAsync(path);
    }
}

public sealed class AvaloniaDesignerHostFile : IDesignerHostFile
{
    private readonly IStorageFile _file;

    public AvaloniaDesignerHostFile(IStorageFile file) => _file = file;

    public string Name => _file.Name;
    public string? LocalPath => _file.TryGetLocalPath();
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => _file.OpenReadAsync();
    public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) => _file.OpenWriteAsync();
}

public sealed class StandaloneDesignerDialogService : IDesignerDialogService
{
    private readonly Func<TopLevel?> _topLevelProvider;

    public StandaloneDesignerDialogService(Func<TopLevel?> topLevelProvider) => _topLevelProvider = topLevelProvider;

    public async Task<DesignerDialogResult> ShowAsync(DesignerDialogRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = _topLevelProvider() as Window
            ?? throw new InvalidOperationException("Dialog owner is unavailable for the current host.");

        if (request.Kind == DesignerDialogKind.UnsavedChanges)
        {
            var unsavedChangesDialog = new UnsavedChangesWindow(request.Message);
            var result = await unsavedChangesDialog.ShowDialog<UnsavedChangesDialogResult>(owner);
            return result switch
            {
                UnsavedChangesDialogResult.Save => DesignerDialogResult.Save,
                UnsavedChangesDialogResult.Discard => DesignerDialogResult.Discard,
                _ => DesignerDialogResult.Cancelled
            };
        }

        var dialog = new Window
        {
            Title = request.Title,
            Width = 460,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock { Text = request.Message, Margin = new Avalonia.Thickness(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
        };
        dialog.KeyDown += (_, _) => dialog.Close(DesignerDialogResult.Accepted);
        return await dialog.ShowDialog<DesignerDialogResult>(owner);
    }
}

public sealed class StandaloneDesignerNotificationService : IDesignerNotificationService
{
    public event EventHandler<DesignerNotification>? Published;
    public void Publish(DesignerNotification notification) => Published?.Invoke(this, notification);
}

public sealed class StandaloneDesignerPathService : IDesignerPathService
{
    public string ApplicationBaseDirectory => AppContext.BaseDirectory;
    public string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FormDesigner");
    public string LogsDirectory => Path.Combine(UserDataDirectory, "logs");
    public string TempDirectory => Path.Combine(Path.GetTempPath(), "FormDesigner");
    public string PluginDirectory => Path.Combine(ApplicationBaseDirectory, "Plugins");
    public string RecoveryDirectory => Path.Combine(UserDataDirectory, "Recovery");
    public string ArtifactsDirectory => Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
}

public sealed class PhysicalDesignerFileSystem : IDesignerFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => File.ReadAllTextAsync(path, cancellationToken);
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) => File.WriteAllTextAsync(path, contents, cancellationToken);

    public async Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        await WriteAllTextAtomicallyAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    public async Task WriteAllTextAtomicallyAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, contents, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

public sealed class AvaloniaDesignerScheduler : IDesignerScheduler
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action, DesignerSchedulerPriority priority = DesignerSchedulerPriority.Normal)
    {
        Dispatcher.UIThread.Post(action, priority == DesignerSchedulerPriority.Background ? DispatcherPriority.Background : DispatcherPriority.Normal);
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        return Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).GetTask();
    }
}

public sealed class StandaloneDesignerExternalLauncher : IDesignerExternalLauncher
{
    public Task OpenFileAsync(string path, CancellationToken cancellationToken = default) => LaunchAsync(path, cancellationToken);
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) => LaunchAsync(path, cancellationToken);
    public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default) => LaunchAsync(uri.AbsoluteUri, cancellationToken);

    private static Task LaunchAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        return Task.CompletedTask;
    }
}

public sealed class StandaloneDesignerHostCommandService : IDesignerHostCommandService
{
    public event EventHandler<DesignerHostCommand>? CommandRequested;

    public Task ExecuteAsync(DesignerHostCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommandRequested?.Invoke(this, command);
        return Task.CompletedTask;
    }
}
