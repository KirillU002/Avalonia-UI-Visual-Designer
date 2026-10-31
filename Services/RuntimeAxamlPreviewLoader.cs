using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Avalonia.Metadata;

namespace FormDesigner.Services;

public static class RuntimeAxamlPreviewLoader
{
    public const string LoaderName = nameof(AvaloniaRuntimeXamlLoader);
    public const string SyntheticBaseUri = "avares://FormDesigner/RuntimePreview.axaml";

    public static object Load(string axaml, IEnumerable<Assembly>? pluginAssemblies = null)
    {
        if (string.IsNullOrWhiteSpace(axaml))
            throw new InvalidOperationException("Runtime Preview AXAML is empty.");

        var runtimeAssemblies = pluginAssemblies?
            .Where(assembly => assembly is not null)
            .Distinct()
            .ToList()
            ?? new List<Assembly>();
        EnsureRuntimeCompilerAssembliesLoaded(axaml, runtimeAssemblies);
        var document = new RuntimeXamlLoaderDocument(new Uri(SyntheticBaseUri), QualifyPluginNamespaces(axaml, runtimeAssemblies));
        var configuration = new RuntimeXamlLoaderConfiguration
        {
            DesignMode = false,
            LocalAssembly = runtimeAssemblies.FirstOrDefault(),
            UseCompiledBindingsByDefault = false
        };

        var pluginContext = runtimeAssemblies
            .Select(AssemblyLoadContext.GetLoadContext)
            .FirstOrDefault(context => context is not null && context != AssemblyLoadContext.Default);
        if (pluginContext is null)
            return AvaloniaRuntimeXamlLoader.Load(document, configuration);

        using (pluginContext.EnterContextualReflection())
            return AvaloniaRuntimeXamlLoader.Load(document, configuration);
    }

    private static string QualifyPluginNamespaces(string axaml, IReadOnlyList<Assembly> assemblies)
    {
        if (assemblies.Count == 0) return axaml;
        // The runtime compiler's schema URL discovery can miss assemblies in a plugin
        // load context. Preserve aliases and qualify only unambiguous contributed maps.
        var maps = assemblies.SelectMany(a => a.GetCustomAttributes<XmlnsDefinitionAttribute>()
                .Select(d => new { d.XmlNamespace, d.ClrNamespace, Assembly = a.GetName().Name }))
            .GroupBy(d => d.XmlNamespace).Where(g => g.Key is not ("https://github.com/avaloniaui" or "http://schemas.microsoft.com/winfx/2006/xaml")
                && g.Select(d => (d.ClrNamespace, d.Assembly)).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => "clr-namespace:" + g.First().ClrNamespace + ";assembly=" + g.First().Assembly);
        if (maps.Count == 0) return axaml;
        var document = XDocument.Parse(axaml, LoadOptions.PreserveWhitespace);
        foreach (var element in document.Descendants())
        {
            if (maps.TryGetValue(element.Name.NamespaceName, out var target)) element.Name = XName.Get(element.Name.LocalName, target);
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.IsNamespaceDeclaration && maps.TryGetValue(attribute.Value, out var uri)) attribute.Value = uri;
                else if (!attribute.IsNamespaceDeclaration && maps.TryGetValue(attribute.Name.NamespaceName, out var ns))
                { element.SetAttributeValue(XName.Get(attribute.Name.LocalName, ns), attribute.Value); attribute.Remove(); }
            }
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void EnsureRuntimeCompilerAssembliesLoaded(string axaml, IEnumerable<Assembly>? pluginAssemblies)
    {
        GC.KeepAlive(typeof(Binding));
        GC.KeepAlive(typeof(ItemsRepeater).Assembly);
        if (axaml.Contains("DataGrid", StringComparison.Ordinal))
            GC.KeepAlive(typeof(DataGrid).Assembly);

        foreach (var assembly in pluginAssemblies?.Where(assembly => assembly is not null).Distinct() ?? Enumerable.Empty<Assembly>())
            GC.KeepAlive(assembly);
    }
}
