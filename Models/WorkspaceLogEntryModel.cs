using System;

namespace FormDesigner.Models;

public enum WorkspaceLogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class WorkspaceLogEntryModel
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public WorkspaceLogLevel Level { get; init; } = WorkspaceLogLevel.Info;

    public string Category { get; init; } = "General";

    public string Message { get; init; } = "";

    public string Details { get; init; } = "";

    public string RelatedDocumentPath { get; init; } = "";

    public string RelatedControlId { get; init; } = "";

    public string TimestampText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

    public string LevelText => Level.ToString();

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string LevelBrush => Level switch
    {
        WorkspaceLogLevel.Success => "#16A34A",
        WorkspaceLogLevel.Warning => "#D97706",
        WorkspaceLogLevel.Error => "#DC2626",
        _ => "#2563EB"
    };

    public string LevelBackground => Level switch
    {
        WorkspaceLogLevel.Success => "#DCFCE7",
        WorkspaceLogLevel.Warning => "#FEF3C7",
        WorkspaceLogLevel.Error => "#FEE2E2",
        _ => "#DBEAFE"
    };
}
