using FormDesigner.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FormDesigner.Services;

public sealed class ExportWorkspaceService
{
    public GeneratedFileModel BuildProjectReadme(DesignerProjectModel project)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Generated Avalonia Project Export");
        sb.AppendLine();
        sb.AppendLine($"Project: {project.Name}");
        sb.AppendLine($"Namespace: {project.DefaultNamespace}");
        sb.AppendLine($"Target framework: {project.TargetFramework}");
        sb.AppendLine($"Avalonia: {project.AvaloniaVersion}");
        sb.AppendLine();
        sb.AppendLine("## Forms");
        foreach (var form in project.Forms)
            sb.AppendLine($"- {form.DisplayName} ({form.RelativePath})");
        sb.AppendLine();
        sb.AppendLine("## Assets");
        foreach (var asset in project.Assets)
            sb.AppendLine($"- {asset.DisplayPath}");
        sb.AppendLine();
        sb.AppendLine("## Resources");
        foreach (var resource in project.Resources)
            sb.AppendLine($"- {resource.RelativePath}");

        return new GeneratedFileModel
        {
            Path = "README.project.generated.md",
            Content = sb.ToString(),
            Severity = ExportChecklistSeverity.Ok
        };
    }

    public IReadOnlyList<GeneratedFileModel> BuildProjectMetadataFiles(DesignerProjectModel project)
    {
        var files = new List<GeneratedFileModel> { BuildProjectReadme(project) };
        files.AddRange(project.Resources.Select(resource => new GeneratedFileModel
        {
            Path = resource.RelativePath,
            Content = resource.Content,
            Severity = ExportChecklistSeverity.Ok
        }));
        return files;
    }
}
