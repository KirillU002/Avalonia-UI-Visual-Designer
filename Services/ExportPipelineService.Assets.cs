using Avalonia.Platform;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FormDesigner.Services;

public sealed partial class ExportPipelineService
{
    private sealed record DesignerAsset(Uri Source, string RelativePath);

    private static DesignerAsset? DesignerAssetReference(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "avares"
            || !uri.Host.Equals(typeof(ExportPipelineService).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/Assets/", StringComparison.Ordinal) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return null;
        var relative = "Assets/Designer/" + Uri.UnescapeDataString(uri.AbsolutePath[8..]).Replace('\\', '/');
        // Resource names are not trusted filesystem paths.
        if (relative.Split('/').Any(part => part is ".." or "." || part.Contains(':')))
            throw new InvalidOperationException("Небезопасный путь ресурса экспорта.");
        return new DesignerAsset(uri, relative);
    }

    private static IEnumerable<XAttribute> DesignerResourceAttributes(XDocument document) => document.Descendants()
        .Attributes().Where(a => a.Name.NamespaceName.Length == 0 && a.Name.LocalName is "Source" or "Icon");

    private static List<DesignerAsset> FindDesignerAssets(ExportResult result) => result.GeneratedFiles
        .Where(f => f.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
            && f.Content.Contains("avares://FormDesigner/Assets/", StringComparison.OrdinalIgnoreCase))
        .SelectMany(f => DesignerResourceAttributes(XDocument.Parse(f.Content)).Select(a => DesignerAssetReference(a.Value)))
        .Where(a => a is not null).Cast<DesignerAsset>().DistinctBy(a => a.RelativePath).ToList();

    private static string PackageDesignerAssetReferences(string content, string path)
    {
        if (!path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) || !content.Contains("avares://FormDesigner/Assets/", StringComparison.OrdinalIgnoreCase)) return content;
        var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        foreach (var attribute in DesignerResourceAttributes(document))
            if (DesignerAssetReference(attribute.Value) is { } asset) attribute.Value = "/" + asset.RelativePath;
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static async Task CopyDesignerAssetsAsync(ExportResult result, string folder, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        foreach (var asset in FindDesignerAssets(result))
        {
            var destination = Path.GetFullPath(Path.Combine(root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Ресурс находится вне каталога экспорта.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = AssetLoader.Open(asset.Source);
            await using var target = File.Create(destination);
            await source.CopyToAsync(target, cancellation).ConfigureAwait(false);
        }
    }
}
