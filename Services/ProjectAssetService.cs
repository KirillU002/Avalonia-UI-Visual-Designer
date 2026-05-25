using FormDesigner.Models;
using System;
using System.IO;
using System.Linq;

namespace FormDesigner.Services;

public sealed class ProjectAssetService
{
    public DesignerAssetModel RegisterAsset(DesignerProjectModel project, string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var relativePath = $"Assets/{GetUniqueAssetName(project, fileName)}";
        var asset = new DesignerAssetModel
        {
            Name = Path.GetFileName(relativePath),
            SourcePath = sourcePath,
            RelativePath = relativePath,
            Kind = IsImageFile(fileName) ? "Image" : "File",
            ImportedUtc = DateTime.UtcNow
        };
        project.Assets.Add(asset);
        return asset;
    }

    private static bool IsImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueAssetName(DesignerProjectModel project, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "asset" : fileName;
        if (project.Assets.All(asset => !asset.RelativePath.EndsWith($"/{name}", StringComparison.OrdinalIgnoreCase)))
            return name;

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{stem}{index}{extension}";
            if (project.Assets.All(asset => !asset.RelativePath.EndsWith($"/{candidate}", StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{stem}{Guid.NewGuid():N}{extension}";
    }
}
