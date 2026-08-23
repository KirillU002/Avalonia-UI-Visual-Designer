using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Services;

public sealed class ReusableTemplateStorageService
{
    private const string CustomTemplatesFileName = "custom-templates.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string TemplatesDirectoryPath { get; }

    public string CustomTemplatesFilePath => Path.Combine(TemplatesDirectoryPath, CustomTemplatesFileName);

    public ReusableTemplateStorageService(string? templatesDirectoryPath = null)
    {
        TemplatesDirectoryPath = string.IsNullOrWhiteSpace(templatesDirectoryPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FormDesigner",
                "Templates")
            : templatesDirectoryPath;
    }

    public IReadOnlyList<ReusableTemplateModel> LoadCustomTemplates()
    {
        if (!File.Exists(CustomTemplatesFilePath))
            return Array.Empty<ReusableTemplateModel>();

        try
        {
            var json = File.ReadAllText(CustomTemplatesFilePath);
            var templates = JsonSerializer.Deserialize<List<ReusableTemplateModel>>(json, JsonOptions) ?? new();
            foreach (var template in templates)
                template.IsBuiltIn = false;

            return templates
                .Where(template => template.Controls.Count > 0)
                .ToList();
        }
        catch
        {
            return Array.Empty<ReusableTemplateModel>();
        }
    }

    public void SaveCustomTemplates(IEnumerable<ReusableTemplateModel> templates)
    {
        Directory.CreateDirectory(TemplatesDirectoryPath);

        var normalized = templates
            .Where(template => !template.IsBuiltIn && template.Controls.Count > 0)
            .ToList();

        var tempPath = CustomTemplatesFilePath + ".tmp";
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Copy(tempPath, CustomTemplatesFilePath, overwrite: true);
        File.Delete(tempPath);
    }
}
