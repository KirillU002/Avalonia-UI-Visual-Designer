using FormDesigner.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FormDesigner.Services;

public sealed class WorkspaceTaskService
{
    public ObservableCollection<WorkspaceTaskModel> Tasks { get; } = new();

    public WorkspaceTaskModel? ActiveTask => Tasks.FirstOrDefault(task => task.IsRunning);

    public event EventHandler? TasksChanged;

    public WorkspaceTaskModel Start(string title, string description = "", double? progress = null)
    {
        var task = new WorkspaceTaskModel
        {
            Title = title,
            Description = description,
            Status = WorkspaceTaskStatus.Running,
            Progress = progress,
            StatusMessage = "Running"
        };

        Tasks.Insert(0, task);
        TrimCompletedTasks();
        TasksChanged?.Invoke(this, EventArgs.Empty);
        return task;
    }

    public void Report(WorkspaceTaskModel? task, double? progress = null, string statusMessage = "")
    {
        if (task is null)
            return;

        if (progress.HasValue)
            task.Progress = Math.Clamp(progress.Value, 0, 100);

        if (!string.IsNullOrWhiteSpace(statusMessage))
            task.StatusMessage = statusMessage;

        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Complete(WorkspaceTaskModel? task, string statusMessage = "Completed")
    {
        if (task is null)
            return;

        task.Status = WorkspaceTaskStatus.Completed;
        task.Progress = 100;
        task.StatusMessage = statusMessage;
        task.CompletedAtUtc = DateTime.UtcNow;
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Fail(WorkspaceTaskModel? task, string errorMessage)
    {
        if (task is null)
            return;

        task.Status = WorkspaceTaskStatus.Failed;
        task.ErrorMessage = errorMessage;
        task.CompletedAtUtc = DateTime.UtcNow;
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel(WorkspaceTaskModel? task, string statusMessage = "Cancelled")
    {
        if (task is null)
            return;

        task.Status = WorkspaceTaskStatus.Cancelled;
        task.StatusMessage = statusMessage;
        task.CompletedAtUtc = DateTime.UtcNow;
        TasksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TrimCompletedTasks()
    {
        while (Tasks.Count > 30)
            Tasks.RemoveAt(Tasks.Count - 1);
    }
}
