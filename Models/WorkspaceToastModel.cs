using System;

namespace FormDesigner.Models;

public enum WorkspaceToastLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class WorkspaceToastModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public WorkspaceToastLevel Level { get; init; } = WorkspaceToastLevel.Info;

    public string Title { get; init; } = "";

    public string Message { get; init; } = "";

    public bool IsPersistent { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public string LevelBrush => Level switch
    {
        WorkspaceToastLevel.Success => "#16A34A",
        WorkspaceToastLevel.Warning => "#D97706",
        WorkspaceToastLevel.Error => "#DC2626",
        _ => "#2563EB"
    };

    public string BackgroundBrush => Level switch
    {
        WorkspaceToastLevel.Success => "#F0FDF4",
        WorkspaceToastLevel.Warning => "#FFFBEB",
        WorkspaceToastLevel.Error => "#FEF2F2",
        _ => "#EFF6FF"
    };

    public string BorderBrush => Level switch
    {
        WorkspaceToastLevel.Success => "#86EFAC",
        WorkspaceToastLevel.Warning => "#FDE68A",
        WorkspaceToastLevel.Error => "#FECACA",
        _ => "#BFDBFE"
    };
}
