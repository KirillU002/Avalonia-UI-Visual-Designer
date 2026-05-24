using FormDesigner.Models;
using System;
using System.Diagnostics;

namespace FormDesigner.Services;

public sealed class WorkspaceLogService
{
    public event EventHandler<WorkspaceLogEntryModel>? EntryAdded;

    public WorkspaceLogEntryModel Log(
        WorkspaceLogLevel level,
        string category,
        string message,
        string details = "",
        string relatedDocumentPath = "",
        string relatedControlId = "")
    {
        var entry = new WorkspaceLogEntryModel
        {
            TimestampUtc = DateTime.UtcNow,
            Level = level,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            Message = message?.Trim() ?? "",
            Details = details?.Trim() ?? "",
            RelatedDocumentPath = relatedDocumentPath?.Trim() ?? "",
            RelatedControlId = relatedControlId?.Trim() ?? ""
        };

        EntryAdded?.Invoke(this, entry);
        Debug.WriteLine($"[{entry.TimestampText}] [{entry.Level}] [{entry.Category}] {entry.Message} {entry.Details}");
        return entry;
    }

    public WorkspaceLogEntryModel Info(string category, string message, string details = "") =>
        Log(WorkspaceLogLevel.Info, category, message, details);

    public WorkspaceLogEntryModel Success(string category, string message, string details = "") =>
        Log(WorkspaceLogLevel.Success, category, message, details);

    public WorkspaceLogEntryModel Warning(string category, string message, string details = "") =>
        Log(WorkspaceLogLevel.Warning, category, message, details);

    public WorkspaceLogEntryModel Error(string category, string message, string details = "") =>
        Log(WorkspaceLogLevel.Error, category, message, details);
}
