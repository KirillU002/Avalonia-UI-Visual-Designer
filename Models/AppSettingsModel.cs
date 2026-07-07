using System;
using System.Collections.Generic;
using System.IO;

namespace FormDesigner.Models;

public class AppSettingsModel
{
    public string Version { get; set; } = "1.0";

    public List<RecentFileModel> RecentFiles { get; set; } = new();

    public List<string> PropertyGridFavorites { get; set; } = new();

    public List<string> PropertyGridCollapsedCategories { get; set; } = new();

    public PropertyGridUserSettings PropertyGrid { get; set; } = new();

    public CanvasEditorSettingsModel CanvasEditor { get; set; } = new();

    public UiDensitySettingsModel UiDensity { get; set; } = new();

    public SessionStateModel Session { get; set; } = new();

    public ExportCacheModel ExportCache { get; set; } = new();

    public PreviewSettingsModel Preview { get; set; } = new();

    public BuildAndLogsSettingsModel BuildAndLogs { get; set; } = new();

    public GeneralSettingsModel General { get; set; } = new();

    public InterfaceSettingsModel Interface { get; set; } = new();

    public SqlServerSettingsModel SqlServer { get; set; } = new();

    public AdvancedSettingsModel Advanced { get; set; } = new();

    public AutosaveMetadataModel Autosave { get; set; } = new();
}

public sealed class GeneralSettingsModel
{
    public string Language { get; set; } = "Русский";

    public string Theme { get; set; } = "Светлая";

    public bool ConfirmNewProjectWithUnsavedChanges { get; set; } = true;

    public bool EnableRecoveryAutosave { get; set; } = true;
}

public sealed class CanvasEditorSettingsModel
{
    public bool IsCanvasSnappingEnabled { get; set; } = true;

    public bool IsDesignerGridVisible { get; set; } = true;

    public bool IsSmartGuidesEnabled { get; set; } = true;

    public bool IsDistanceHintsEnabled { get; set; } = true;

    public bool IgnoreLockedDuringSelection { get; set; } = true;

    public bool IsSelectionToolbarEnabled { get; set; } = true;
}

public sealed class UiDensitySettingsModel
{
    public string DensityMode { get; set; } = "Compact";
}

public sealed class InterfaceSettingsModel
{
    public bool ShowPropertyTooltips { get; set; } = true;

    public bool CompactPropertyInspector { get; set; } = true;

    public bool ShowAdvancedProperties { get; set; }

    public int GridStep { get; set; } = 10;
}

public sealed class PreviewSettingsModel
{
    public bool ShowRuntimeBadge { get; set; }

    public bool CompactRuntimeBadge { get; set; } = true;

    public bool AutoHideRuntimeBadge { get; set; } = true;

    public bool PreviewTopmost { get; set; }

    public int PreviewDefaultZoomPercent { get; set; } = 100;

    public bool EnableExperimentalLayoutTab { get; set; }
}

public sealed class BuildAndLogsSettingsModel
{
    public bool ValidateBuildAfterExport { get; set; }

    public bool VerboseBuildLogs { get; set; } = true;

    public bool KeepSuccessfulBuildArtifacts { get; set; } = true;

    public bool CleanOldArtifactsAutomatically { get; set; } = true;

    public bool SaveLogsToFile { get; set; } = true;

    public bool UseCustomNuGetSource { get; set; }

    public string CustomNuGetSource { get; set; } = "";

    public bool AllowInsecureNuGetSource { get; set; }

    public bool IncludeNuGetOrgFallback { get; set; } = true;

    public bool GenerateNuGetConfigInExportedProject { get; set; } = true;

    public bool ExportSqlConnectionString { get; set; }

    public int BuildTimeoutSeconds { get; set; } = 120;

    public string LogLevel { get; set; } = "Info";

    public int MaxLogFilesCount { get; set; } = 10;

    public int MaxLogFileSizeMb { get; set; } = 20;
}

public sealed class SqlServerSettingsModel
{
    public const string AuthWindows = "Windows Authentication";
    public const string AuthSqlLogin = "SQL Login";

    public string ServerName { get; set; } = "";

    public string DatabaseName { get; set; } = "";

    public string AuthenticationMode { get; set; } = AuthWindows;

    public string UserName { get; set; } = "";

    public string Password { get; set; } = "";

    public bool SavePassword { get; set; }

    public bool TrustServerCertificate { get; set; } = true;

    public bool EncryptConnection { get; set; }

    public int ConnectionTimeoutSeconds { get; set; } = 15;

    public string DefaultSchema { get; set; } = "dbo";

    public int DefaultPreviewTopN { get; set; } = 100;

    public bool UseGlobalSettingsForSqlSources { get; set; } = true;

    public bool ExportConnectionStringInGeneratedCode { get; set; }
}

public sealed class AdvancedSettingsModel
{
    public bool EnableTraceDiagnostics { get; set; }

    public bool EnableDeveloperWarnings { get; set; } = true;

    public bool ResetLayoutOnNextStart { get; set; }
}

public sealed class PropertyGridUserSettings
{
    public Dictionary<string, HashSet<string>> FavoritePropertiesByTypeKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> UserCustomizedTypeKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, HashSet<string>> ExpandedCategoriesByTypeKey { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class RecentFileModel
{
    public string FilePath { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;

    public bool IsPinned { get; set; }

    public string LastOpenedText => LastOpenedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public string PathText => string.IsNullOrWhiteSpace(FilePath) ? "Путь не задан" : FilePath;

    public string Title => string.IsNullOrWhiteSpace(DisplayName)
        ? string.IsNullOrWhiteSpace(FilePath) ? "Без имени" : Path.GetFileName(FilePath)
        : DisplayName;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    public string AvailabilityText => IsAvailable ? "Available" : "Missing";

    public string PinText => IsPinned ? "Pinned" : "Pin";
}

public class SessionStateModel
{
    public string LastDocumentPath { get; set; } = "";

    public bool ReopenLastWorkspaceOnStartup { get; set; } = true;

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

    public List<string> OpenDocumentIds { get; set; } = new();

    public string ActiveDocumentId { get; set; } = "";

    public string LastProjectPath { get; set; } = "";

    public EditorShellLayoutState EditorShell { get; set; } = new();
}

public sealed class EditorShellLayoutState
{
    public bool IsLeftPanelVisible { get; set; } = true;

    public bool IsRightPanelVisible { get; set; } = true;

    public bool IsBottomPanelVisible { get; set; }

    public double LeftPanelWidth { get; set; } = 260;

    public double RightPanelWidth { get; set; } = 340;

    public double BottomPanelHeight { get; set; } = 180;

    public string ActiveLeftTab { get; set; } = "Components";

    public string ActiveRightTab { get; set; } = "Properties";

    public string ActiveBottomTab { get; set; } = "Diagnostics";
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
