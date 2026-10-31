using FormDesigner.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private static void ValidateWorkspaceDocuments(WorkspaceModel workspace)
    {
        if (workspace.Project.Forms.Any(f => f is null || string.IsNullOrWhiteSpace(f.Id))
            || workspace.Project.Forms.GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Некорректные идентификаторы форм. Текущий проект не изменён.");
        foreach (var form in workspace.Project.Forms)
        {
            form.Document ??= new DesignerDocumentFileModel { FormTitle = form.Name };
            ValidateDocumentForLoad(form.Document);
            // Form switching restores CurrentSnapshot, so preflight that representation too.
            if (!string.IsNullOrWhiteSpace(form.CurrentSnapshot))
                ValidateDocumentForLoad(JsonSerializer.Deserialize<DesignerDocumentFileModel>(form.CurrentSnapshot, JsonOptions)
                    ?? throw new JsonException("Некорректный snapshot формы."));
            if (form.UndoSnapshots is null || form.RedoSnapshots is null)
                throw new InvalidOperationException("Некорректная история формы. Текущий проект не изменён.");
        }
    }

    private static void ValidateDocumentForLoad(DesignerDocumentFileModel document)
    {
        if (document.Controls is null || document.BindingSources is null || document.Interactions is null
            || document.Controls.Any(c => c is null || c.CustomProperties is null || c.CustomProperties.Any(p => p is null))
            || document.BindingSources.Any(s => s is null || s.Fields is null || s.Fields.Any(f => f is null)) || document.Interactions.Any(i => i is null))
            throw new InvalidOperationException("Некорректная структура формы. Текущий проект не изменён.");
        ValidateNativeDocument(document);
    }

    /// <summary>Acknowledge precisely the workspace revision written by standalone Save.</summary>
    public void MarkWorkspaceSaved(string path, string savedWorkspaceJson)
    {
        var saved = JsonSerializer.Deserialize<WorkspaceModel>(savedWorkspaceJson, JsonOptions)
            ?? throw new JsonException("Отсутствует сохранённый snapshot workspace.");
        if (saved.WorkspaceId != Workspace.WorkspaceId)
            throw new InvalidOperationException("Сохранение относится к другому проекту.");
        PersistActiveFormDocumentState(refreshProjectViews: false);
        foreach (var form in CurrentProject.Forms)
        {
            var written = saved.Project.Forms.FirstOrDefault(f => f.Id == form.Id);
            if (written is null) continue; // Forms created while writing remain unsaved.
            var snapshot = string.IsNullOrEmpty(written.CurrentSnapshot)
                ? JsonSerializer.Serialize(written.Document, JsonOptions) : written.CurrentSnapshot;
            form.SavedSnapshot = snapshot;
            if (_documentSessions.TryGetValue(form.Id, out var session) && !session.IsDisposed)
                session.SetSavedSnapshot(snapshot);
            form.IsDirty = !string.Equals(form.CurrentSnapshot, snapshot, StringComparison.Ordinal);
        }
        CurrentDocumentPath = CurrentProjectPath = path;
        RefreshDocumentTabs(); RebuildProjectExplorer(); RaiseDocumentStateProperties(); RaiseProjectWorkspaceProperties();
        StatusText = $"Сохранен документ: {Path.GetFileName(path)}";
    }
}
