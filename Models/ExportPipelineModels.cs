using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.Models;

public enum ExportBuildValidationStatus
{
    NotValidated,
    Building,
    Passed,
    Failed
}

public sealed class ExportProfile
{
    public string Name { get; init; } = "Clean UI";

    public string TargetMode { get; init; } = "";

    public string ProjectNamespace { get; init; } = "AvaloniaApplication1";

    public string DataGridExportMode { get; init; } = "";

    public string LayoutExportMode { get; init; } = "";

    public string XamlVerbosity { get; init; } = "";

    public bool IncludeComments { get; init; }

    public bool IncludeDemoData { get; init; }

    public bool IncludePluginRuntime { get; init; }

    public string OutputFolder { get; init; } = "";
}

public sealed class GeneratedFileModel
{
    public string Path { get; init; } = "";

    public string Content { get; init; } = "";

    public ExportChecklistSeverity Severity { get; init; } = ExportChecklistSeverity.Ok;

    public string StatusText => Severity switch
    {
        ExportChecklistSeverity.Error => "Error",
        ExportChecklistSeverity.Warning => "Warning",
        _ => "OK"
    };

    public string FileName => string.IsNullOrWhiteSpace(Path) ? "generated" : System.IO.Path.GetFileName(Path);

    public string DirectoryName => string.IsNullOrWhiteSpace(Path)
        ? ""
        : System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";

    public bool HasDirectory => !string.IsNullOrWhiteSpace(DirectoryName);

    public int LineCount => string.IsNullOrEmpty(Content) ? 0 : Content.Split('\n').Length;

    public string Summary => $"{StatusText} · {LineCount} lines";

    public string SeverityForeground => Severity switch
    {
        ExportChecklistSeverity.Error => "#DC2626",
        ExportChecklistSeverity.Warning => "#D97706",
        _ => "#16A34A"
    };
}

public sealed class GeneratedFileTreeNodeModel
{
    public string Name { get; init; } = "";

    public string Path { get; init; } = "";

    public bool IsFolder { get; init; }

    public GeneratedFileModel? File { get; init; }

    public ObservableCollection<GeneratedFileTreeNodeModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    public string Icon => IsFolder ? "Folder" : "File";

    public string Summary => File?.Summary ?? $"{Children.Count} files";

    public string StatusText => File?.StatusText ?? "";

    public string SeverityForeground => File?.SeverityForeground ?? "#64748B";
}

public sealed class RequiredPackageModel
{
    public string Id { get; init; } = "";

    public string Version { get; init; } = "";

    public string Reason { get; init; } = "";

    public ExportChecklistSeverity Severity { get; init; } = ExportChecklistSeverity.Warning;

    public string InstallCommand => string.IsNullOrWhiteSpace(Version)
        ? $"dotnet add package {Id}"
        : $"dotnet add package {Id} --version {Version}";

    public string Summary => string.IsNullOrWhiteSpace(Version) ? Id : $"{Id} {Version}";
}

public sealed class ExportDiagnosticModel
{
    public ExportChecklistSeverity Severity { get; init; } = ExportChecklistSeverity.Ok;

    public string Source { get; init; } = "Export";

    public string Message { get; init; } = "";

    public string Details { get; init; } = "";

    public string SeverityText => Severity switch
    {
        ExportChecklistSeverity.Error => "Error",
        ExportChecklistSeverity.Warning => "Warning",
        _ => "OK"
    };

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string SeverityForeground => Severity switch
    {
        ExportChecklistSeverity.Error => "#DC2626",
        ExportChecklistSeverity.Warning => "#D97706",
        _ => "#16A34A"
    };
}

public sealed class ExportBuildValidationResult
{
    public ExportBuildValidationStatus Status { get; init; } = ExportBuildValidationStatus.NotValidated;

    public string ProjectPath { get; init; } = "";

    public int ExitCode { get; init; }

    public string Output { get; init; } = "";

    public string DetailedLogPath { get; init; } = "";

    public string StepSummary { get; init; } = "";

    public DateTime CompletedUtc { get; init; } = DateTime.UtcNow;

    public string StatusText => Status switch
    {
        ExportBuildValidationStatus.Building => "Building...",
        ExportBuildValidationStatus.Passed => "Build passed",
        ExportBuildValidationStatus.Failed => "Build failed",
        _ => "Not validated"
    };

    public string StatusForeground => Status switch
    {
        ExportBuildValidationStatus.Passed => "#16A34A",
        ExportBuildValidationStatus.Failed => "#DC2626",
        ExportBuildValidationStatus.Building => "#2563EB",
        _ => "#64748B"
    };
}

public sealed class ExportResult
{
    public ExportProfile Profile { get; init; } = new();

    public IReadOnlyList<GeneratedFileModel> GeneratedFiles { get; init; } = Array.Empty<GeneratedFileModel>();

    public IReadOnlyList<RequiredPackageModel> RequiredPackages { get; init; } = Array.Empty<RequiredPackageModel>();

    public IReadOnlyList<ExportDiagnosticModel> Diagnostics { get; init; } = Array.Empty<ExportDiagnosticModel>();

    public ExportBuildValidationResult BuildValidation { get; init; } = new();

    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;

    public string TargetMode => Profile.TargetMode;

    public string DataGridExportMode => Profile.DataGridExportMode;

    public string LayoutMode => Profile.LayoutExportMode;

    public string PluginRequirements => RequiredPackages.Any(package => package.Id.Contains("Plugin", StringComparison.OrdinalIgnoreCase))
        ? "Plugin runtime references"
        : "Plugins: none";

    public string Summary =>
        $"{GeneratedFiles.Count} files · {RequiredPackages.Count} packages · {Diagnostics.Count(d => d.Severity != ExportChecklistSeverity.Ok)} diagnostics";
}
