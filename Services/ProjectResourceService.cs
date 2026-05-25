using FormDesigner.Models;
using System;
using System.Linq;

namespace FormDesigner.Services;

public sealed class ProjectResourceService
{
    public DesignerResourceModel AddResourceDictionary(DesignerProjectModel project, string baseName = "Resources.axaml")
    {
        var name = GetUniqueName(project, baseName);
        var resource = new DesignerResourceModel
        {
            Name = name,
            RelativePath = $"Resources/{name}",
            Content = """
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</ResourceDictionary>
"""
        };
        project.Resources.Add(resource);
        return resource;
    }

    private static string GetUniqueName(DesignerProjectModel project, string baseName)
    {
        var normalized = string.IsNullOrWhiteSpace(baseName) ? "Resources.axaml" : baseName.Trim();
        if (project.Resources.All(resource => !string.Equals(resource.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            return normalized;

        var stem = normalized.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^6]
            : normalized;
        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{stem}{index}.axaml";
            if (project.Resources.All(resource => !string.Equals(resource.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{stem}{Guid.NewGuid():N}.axaml";
    }
}
