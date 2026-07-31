using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

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
        var document = new RuntimeXamlLoaderDocument(new Uri(SyntheticBaseUri), axaml);
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
