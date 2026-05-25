using FormDesigner.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Services;

public sealed class ProjectWorkspaceService
{
    public WorkspaceModel CreateWorkspace(string projectName = "Avalonia UI Project")
    {
        var workspace = new WorkspaceModel
        {
            Project = new DesignerProjectModel
            {
                Name = projectName,
                DefaultNamespace = SanitizeNamespace(projectName)
            }
        };

        workspace.Project.Forms.Add(CreateForm("Form1"));
        workspace.Project.ExportProfiles.Add(new DesignerExportProfileModel
        {
            Name = "Debug",
            Namespace = workspace.Project.DefaultNamespace
        });
        workspace.Project.ExportProfiles.Add(new DesignerExportProfileModel
        {
            Name = "Production",
            Namespace = workspace.Project.DefaultNamespace
        });

        workspace.Session.ActiveDocumentId = workspace.Project.Forms[0].Id;
        workspace.Session.OpenDocumentIds.Add(workspace.Project.Forms[0].Id);
        return workspace;
    }

    public WorkspaceModel WrapSingleDocument(DesignerDocumentFileModel document, string? sourcePath)
    {
        var fileName = string.IsNullOrWhiteSpace(sourcePath)
            ? document.FormTitle
            : Path.GetFileNameWithoutExtension(sourcePath);
        var projectName = string.IsNullOrWhiteSpace(fileName) ? "Avalonia UI Project" : fileName;
        var workspace = CreateWorkspace(projectName);
        var form = workspace.Project.Forms[0];
        form.Name = string.IsNullOrWhiteSpace(document.FormTitle) ? "MainWindow" : document.FormTitle;
        form.RelativePath = $"Forms/{form.Name}.formdesigner.json";
        form.Document = document;
        return workspace;
    }

    public bool TryDeserializeWorkspace(string json, out WorkspaceModel workspace)
    {
        workspace = new WorkspaceModel();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Project", out _))
                return false;

            workspace = JsonSerializer.Deserialize<WorkspaceModel>(json, JsonOptions) ?? new WorkspaceModel();
            EnsureWorkspaceDefaults(workspace);
            return true;
        }
        catch
        {
            workspace = new WorkspaceModel();
            return false;
        }
    }

    public string SerializeWorkspace(WorkspaceModel workspace)
    {
        EnsureWorkspaceDefaults(workspace);
        return JsonSerializer.Serialize(workspace, JsonOptions);
    }

    public void EnsureWorkspaceDefaults(WorkspaceModel workspace)
    {
        workspace.Project ??= new DesignerProjectModel();
        if (workspace.Project.Forms.Count == 0)
            workspace.Project.Forms.Add(CreateForm("MainWindow"));
        if (workspace.Project.ExportProfiles.Count == 0)
            workspace.Project.ExportProfiles.Add(new DesignerExportProfileModel
            {
                Name = "Debug",
                Namespace = workspace.Project.DefaultNamespace
            });
        if (string.IsNullOrWhiteSpace(workspace.Session.ActiveDocumentId)
            || workspace.Project.Forms.All(form => form.Id != workspace.Session.ActiveDocumentId))
        {
            workspace.Session.ActiveDocumentId = workspace.Project.Forms[0].Id;
        }
        if (workspace.Session.OpenDocumentIds.Count == 0)
            workspace.Session.OpenDocumentIds.Add(workspace.Session.ActiveDocumentId);
    }

    public DesignerFormDocument CreateForm(string name)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "Form" : name.Trim();
        return new DesignerFormDocument
        {
            Name = safeName,
            RelativePath = $"Forms/{safeName}.formdesigner.json",
            Document = new DesignerDocumentFileModel
            {
                FormTitle = safeName
            }
        };
    }

    private static string SanitizeNamespace(string value)
    {
        var chars = new string((value ?? "AvaloniaApplication1")
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '.')
            .ToArray());
        chars = string.Join('.', chars.Split('.', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(chars) ? "AvaloniaApplication1" : chars;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
