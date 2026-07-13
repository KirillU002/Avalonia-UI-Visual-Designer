using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FormDesigner.Services;

public enum AxamlGenerationMode
{
    Export,
    RuntimePreview
}

public sealed record GeneratedAxamlDocument(
    string Axaml,
    AxamlGenerationMode Mode,
    string RootElement,
    string RemovedXClass,
    int NormalizedXNameCount,
    IReadOnlyList<string> RemovedRootProperties,
    int KeptRootPropertyCount,
    int NormalizedRootPropertyElementCount);

public static class GeneratedAxamlService
{
    private static readonly HashSet<string> CodeBehindEventNames = new(StringComparer.Ordinal)
    {
        "Click",
        "TextChanged",
        "Checked",
        "Unchecked",
        "SelectionChanged"
    };

    // These attributes belong to Window/TopLevel and cannot be copied to the
    // uncompiled UserControl used by the in-memory Runtime Preview loader.
    private static readonly HashSet<string> WindowOnlyRootAttributeNames = new(StringComparer.Ordinal)
    {
        "Title",
        "RequestedThemeVariant",
        "Icon",
        "WindowState",
        "WindowStartupLocation",
        "CanResize",
        "CanMinimize",
        "CanMaximize",
        "ShowInTaskbar",
        "ShowActivated",
        "Topmost",
        "SystemDecorations",
        "SizeToContent",
        "Position",
        "ClientSize",
        "TransparencyLevelHint",
        "TransparencyBackgroundFallback",
        "OffScreenMargin",
        "ExtendClientAreaToDecorationsHint",
        "ExtendClientAreaChromeHints",
        "ExtendClientAreaTitleBarHeightHint"
    };

    public static GeneratedAxamlDocument Create(string exportAxaml, AxamlGenerationMode mode)
    {
        if (string.IsNullOrWhiteSpace(exportAxaml))
            throw new InvalidOperationException("Generated AXAML is empty.");

        var document = XDocument.Parse(exportAxaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root ?? throw new InvalidOperationException("Generated AXAML has no root element.");
        var rootElement = root.Name.LocalName;
        if (mode == AxamlGenerationMode.Export)
        {
            return new GeneratedAxamlDocument(
                exportAxaml,
                mode,
                rootElement,
                "",
                0,
                Array.Empty<string>(),
                CountRootProperties(root),
                0);
        }

        XNamespace xNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var xClassAttribute = root.Attribute(xNamespace + "Class");
        var removedXClass = xClassAttribute?.Value ?? "";
        xClassAttribute?.Remove();

        var removedRootProperties = new List<string>();
        var normalizedRootPropertyElementCount = 0;
        if (string.Equals(root.Name.LocalName, "Window", StringComparison.Ordinal))
        {
            root.Name = root.GetDefaultNamespace() + "UserControl";
            removedRootProperties.AddRange(FilterRootPropertiesForRuntimePreview(root));
            normalizedRootPropertyElementCount = NormalizeRootPropertyElementsForRuntimePreview(
                root,
                removedRootProperties);

            rootElement = "UserControl";
        }

        var normalizedXNameCount = 0;
        foreach (var element in root.DescendantsAndSelf())
        {
            var xNameAttribute = element.Attribute(xNamespace + "Name");
            if (xNameAttribute is null)
                continue;

            xNameAttribute.Remove();
            normalizedXNameCount++;
        }

        foreach (var attribute in root
                     .DescendantsAndSelf()
                     .Attributes()
                     .Where(attribute => attribute.Name.NamespaceName.Length == 0
                                         && CodeBehindEventNames.Contains(attribute.Name.LocalName))
                     .ToList())
        {
            attribute.Remove();
        }

        return new GeneratedAxamlDocument(
            document.ToString(SaveOptions.DisableFormatting),
            mode,
            rootElement,
            removedXClass,
            normalizedXNameCount,
            removedRootProperties,
            CountRootProperties(root),
            normalizedRootPropertyElementCount);
    }

    private static IReadOnlyList<string> FilterRootPropertiesForRuntimePreview(XElement root)
    {
        var removedProperties = root
            .Attributes()
            .Where(IsUnqualifiedPropertyAttribute)
            .Where(attribute => WindowOnlyRootAttributeNames.Contains(attribute.Name.LocalName))
            .Select(attribute => attribute.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var propertyName in removedProperties)
            root.Attribute(propertyName)?.Remove();

        return removedProperties;
    }

    private static bool IsUnqualifiedPropertyAttribute(XAttribute attribute)
    {
        return !attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.Length == 0;
    }

    private static int NormalizeRootPropertyElementsForRuntimePreview(
        XElement root,
        ICollection<string> removedRootProperties)
    {
        const string sourceOwnerPrefix = "Window.";
        var normalizedCount = 0;
        foreach (var propertyElement in root.Elements().ToList())
        {
            var localName = propertyElement.Name.LocalName;
            if (!localName.StartsWith(sourceOwnerPrefix, StringComparison.Ordinal))
                continue;

            var propertyName = localName[sourceOwnerPrefix.Length..];
            if (WindowOnlyRootAttributeNames.Contains(propertyName))
            {
                propertyElement.Remove();
                if (!removedRootProperties.Contains(propertyName))
                    removedRootProperties.Add(propertyName);
                continue;
            }

            propertyElement.Name = propertyElement.Name.Namespace + $"UserControl.{propertyName}";
            normalizedCount++;
        }

        return normalizedCount;
    }

    private static int CountRootProperties(XElement root)
    {
        return root.Attributes().Count(attribute => !attribute.IsNamespaceDeclaration);
    }

}
