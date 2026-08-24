using Avalonia.Controls;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.Services;
using System;
using System.IO;

namespace AvaloniaDesigner.VsHost;

/// <summary>
/// Separate host services keep VsHost recovery/settings/log files out of the standalone profile.
/// The implementation is Avalonia-specific, but the shared Designer only sees IDesignerHostServices.
/// </summary>
internal sealed class VsHostDesignerHostServices : IDesignerHostServices
{
    private TopLevel? _topLevel;

    public VsHostDesignerHostServices()
    {
        Paths = new VsHostPathService();
        FileSystem = new PhysicalDesignerFileSystem();
        Scheduler = new AvaloniaDesignerScheduler();
        Clipboard = new AvaloniaDesignerClipboard(() => _topLevel);
        FilePicker = new AvaloniaDesignerFilePickerService(() => _topLevel);
        Dialogs = new StandaloneDesignerDialogService(() => _topLevel);
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

    public void AttachTopLevel(TopLevel topLevel) => _topLevel = topLevel;
}

internal sealed class VsHostPathService : IDesignerPathService
{
    public string ApplicationBaseDirectory => AppContext.BaseDirectory;
    public string UserDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FormDesigner", "VsHost");
    public string LogsDirectory => Path.Combine(UserDataDirectory, "logs");
    public string TempDirectory => Path.Combine(Path.GetTempPath(), "FormDesigner", "VsHost");
    public string PluginDirectory => Path.Combine(ApplicationBaseDirectory, "Plugins");
    public string RecoveryDirectory => Path.Combine(UserDataDirectory, "Recovery");
    public string ArtifactsDirectory => Path.Combine(UserDataDirectory, "artifacts");
}
