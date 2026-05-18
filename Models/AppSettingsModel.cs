using System;
using System.Collections.Generic;
using System.IO;

namespace FormDesigner.Models;

public class AppSettingsModel
{
    public string Version { get; set; } = "1.0";

    public List<RecentFileModel> RecentFiles { get; set; } = new();

    public SessionStateModel Session { get; set; } = new();

    public ExportCacheModel ExportCache { get; set; } = new();

    public AutosaveMetadataModel Autosave { get; set; } = new();
}

public class RecentFileModel
{
    public string FilePath { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;

    public string LastOpenedText => LastOpenedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public string PathText => string.IsNullOrWhiteSpace(FilePath) ? "Путь не задан" : FilePath;

    public string Title => string.IsNullOrWhiteSpace(DisplayName)
        ? string.IsNullOrWhiteSpace(FilePath) ? "Без имени" : Path.GetFileName(FilePath)
        : DisplayName;
}

public class SessionStateModel
{
    public string LastDocumentPath { get; set; } = "";

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public int WindowX { get; set; }

    public int WindowY { get; set; }

    public string WindowState { get; set; } = "";

    public double SurfaceZoom { get; set; } = 1.0;

    public double ViewportOffsetX { get; set; }

    public double ViewportOffsetY { get; set; }

    public string WorkspaceMode { get; set; } = "Дизайн";

    public string SelectedControlId { get; set; } = "";
}

public class ExportCacheModel
{
    public string ExportTarget { get; set; } = "Замена MainWindow";

    public string ExportProjectNamespace { get; set; } = "AvaloniaApplication1";

    public string DataGridExportMode { get; set; } = "Visual table без NuGet";

    public string LayoutExportMode { get; set; } = "Canvas layout";

    public string XamlVerbosity { get; set; } = "Компактный";

    public bool IncludeExportComments { get; set; }

    public bool IncludeSampleData { get; set; }

    public bool IncludeCrudSkeleton { get; set; }

    public bool IncludeCommunityToolkitAttributes { get; set; }

    public bool IncludePluginRuntimeReferences { get; set; }

    public string GeneratedXaml { get; set; } = "";

    public string GeneratedCSharp { get; set; } = "";

    public string GeneratedBindingGuide { get; set; } = "";

    public string DocumentSnapshotHash { get; set; } = "";

    public string SettingsSignature { get; set; } = "";

    public DateTime GeneratedUtc { get; set; }
}

public class AutosaveMetadataModel
{
    public DateTime? LastAutosaveUtc { get; set; }

    public string LastDraftPath { get; set; } = "";
}

public class BackupFileModel
{
    public string FilePath { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public long SizeBytes { get; set; }

    public string CreatedText => CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public string SizeText => SizeBytes <= 0
        ? "размер неизвестен"
        : $"{Math.Max(1, SizeBytes / 1024)} KB";
}
