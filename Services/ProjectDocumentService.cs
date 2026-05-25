using FormDesigner.Models;
using System;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Services;

public sealed class ProjectDocumentService
{
    public DesignerFormDocument AddForm(DesignerProjectModel project, string baseName = "Form")
    {
        var name = GetUniqueName(project, baseName);
        var form = new DesignerFormDocument
        {
            Name = name,
            Document = new DesignerDocumentFileModel()
        };
        form.Document.FormTitle = form.Name;
        form.RelativePath = $"Forms/{form.Name}.formdesigner.json";
        project.Forms.Add(form);
        return form;
    }

    public DesignerFormDocument DuplicateForm(DesignerProjectModel project, DesignerFormDocument source)
    {
        var json = JsonSerializer.Serialize(source.Document, JsonOptions);
        var document = JsonSerializer.Deserialize<DesignerDocumentFileModel>(json, JsonOptions) ?? new DesignerDocumentFileModel();
        var form = new DesignerFormDocument
        {
            Name = GetUniqueName(project, $"{source.DisplayName}Copy"),
            Document = document
        };
        form.Document.FormTitle = form.Name;
        form.RelativePath = $"Forms/{form.Name}.formdesigner.json";
        project.Forms.Add(form);
        return form;
    }

    public bool DeleteForm(DesignerProjectModel project, string formId)
    {
        if (project.Forms.Count <= 1)
            return false;

        var form = project.Forms.FirstOrDefault(item => item.Id == formId);
        return form is not null && project.Forms.Remove(form);
    }

    public string GetUniqueName(DesignerProjectModel project, string baseName)
    {
        var normalized = string.IsNullOrWhiteSpace(baseName) ? "Form" : baseName.Trim();
        if (string.Equals(normalized, "Form", StringComparison.OrdinalIgnoreCase))
            return GetNextNumberedFormName(project);

        if (project.Forms.All(form => !string.Equals(form.DisplayName, normalized, StringComparison.OrdinalIgnoreCase)))
            return normalized;

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{normalized}{index}";
            if (project.Forms.All(form => !string.Equals(form.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{normalized}{Guid.NewGuid():N}";
    }

    private static string GetNextNumberedFormName(DesignerProjectModel project)
    {
        for (var index = 1; index < 1000; index++)
        {
            var candidate = $"Form{index}";
            if (project.Forms.All(form => !string.Equals(form.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"Form{Guid.NewGuid():N}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
