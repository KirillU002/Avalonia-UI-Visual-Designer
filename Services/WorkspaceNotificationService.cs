using Avalonia.Threading;
using FormDesigner.Models;
using System;
using System.Collections.ObjectModel;

namespace FormDesigner.Services;

public sealed class WorkspaceNotificationService
{
    private readonly DispatcherTimer _cleanupTimer;

    public ObservableCollection<WorkspaceToastModel> Toasts { get; } = new();

    public WorkspaceNotificationService()
    {
        _cleanupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cleanupTimer.Tick += CleanupTimer_Tick;
        _cleanupTimer.Start();
    }

    public WorkspaceToastModel Show(WorkspaceToastLevel level, string title, string message = "", bool isPersistent = false)
    {
        var toast = new WorkspaceToastModel
        {
            Level = level,
            Title = title?.Trim() ?? "",
            Message = message?.Trim() ?? "",
            IsPersistent = isPersistent
        };

        Toasts.Insert(0, toast);
        while (Toasts.Count > 4)
            Toasts.RemoveAt(Toasts.Count - 1);

        return toast;
    }

    public void Dismiss(WorkspaceToastModel? toast)
    {
        if (toast is null)
            return;

        Toasts.Remove(toast);
    }

    public void Clear()
    {
        Toasts.Clear();
    }

    private void CleanupTimer_Tick(object? sender, EventArgs e)
    {
        var threshold = DateTime.UtcNow.AddSeconds(-6);
        for (var i = Toasts.Count - 1; i >= 0; i--)
        {
            if (!Toasts[i].IsPersistent && Toasts[i].CreatedAtUtc < threshold)
                Toasts.RemoveAt(i);
        }
    }
}
