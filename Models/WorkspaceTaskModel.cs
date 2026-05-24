using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FormDesigner.Models;

public enum WorkspaceTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed partial class WorkspaceTaskModel : ObservableObject
{
    [ObservableProperty]
    private WorkspaceTaskStatus status = WorkspaceTaskStatus.Pending;

    [ObservableProperty]
    private double? progress;

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private string errorMessage = "";

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public bool IsRunning => Status is WorkspaceTaskStatus.Pending or WorkspaceTaskStatus.Running;

    public bool HasProgress => Progress.HasValue;

    public string ProgressText => Progress.HasValue ? $"{Math.Clamp(Progress.Value, 0, 100):0}%" : "";

    public string DisplayStatus => Status switch
    {
        WorkspaceTaskStatus.Pending => "Pending",
        WorkspaceTaskStatus.Running => string.IsNullOrWhiteSpace(StatusMessage) ? "Running" : StatusMessage,
        WorkspaceTaskStatus.Completed => "Completed",
        WorkspaceTaskStatus.Failed => string.IsNullOrWhiteSpace(ErrorMessage) ? "Failed" : ErrorMessage,
        WorkspaceTaskStatus.Cancelled => "Cancelled",
        _ => Status.ToString()
    };

    public string StatusBrush => Status switch
    {
        WorkspaceTaskStatus.Completed => "#16A34A",
        WorkspaceTaskStatus.Failed => "#DC2626",
        WorkspaceTaskStatus.Cancelled => "#64748B",
        WorkspaceTaskStatus.Running => "#2563EB",
        _ => "#64748B"
    };

    partial void OnStatusChanged(WorkspaceTaskStatus value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(DisplayStatus));
        OnPropertyChanged(nameof(StatusBrush));
    }

    partial void OnProgressChanged(double? value)
    {
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayStatus));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayStatus));
    }
}
