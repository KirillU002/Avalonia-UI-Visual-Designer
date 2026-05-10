using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace FormDesigner.DesignerSystem.Infrastructure;

public interface IDesignerLogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class TraceDesignerLogger : IDesignerLogger
{
    public void Info(string message)
    {
        Trace.WriteLine("[Designer] " + message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Trace.WriteLine("[Designer][Error] " + message);
        if (exception is not null)
            Trace.WriteLine(exception);
    }
}

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Dictionary<string, Assembly> _sharedAssemblies;

    public PluginLoadContext(string pluginAssemblyPath)
        : base($"DesignerPlugin:{Path.GetFileNameWithoutExtension(pluginAssemblyPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _sharedAssemblies = AssemblyLoadContext.Default.Assemblies
            .Where(assembly => !string.IsNullOrWhiteSpace(assembly.GetName().Name))
            .GroupBy(assembly => assembly.GetName().Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName.Name)
            && _sharedAssemblies.TryGetValue(assemblyName.Name, out var sharedAssembly))
        {
            return sharedAssembly;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}

public sealed class PluginLoader
{
    private readonly IDesignerLogger _logger;
    private readonly List<PluginLoadContext> _loadedContexts = new();

    public PluginLoader(IDesignerLogger logger)
    {
        _logger = logger;
    }

    public void LoadFromFolder(string folderPath, IDesignerRegistry registry)
    {
        if (!Directory.Exists(folderPath))
        {
            _logger.Info($"Plugin folder '{folderPath}' was not found. Skipping plugin discovery.");
            return;
        }

        foreach (var assemblyPath in Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories))
            LoadPluginAssembly(assemblyPath, registry);
    }

    private void LoadPluginAssembly(string assemblyPath, IDesignerRegistry registry)
    {
        var loadContext = new PluginLoadContext(assemblyPath);
        _loadedContexts.Add(loadContext);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var pluginTypes = DiscoverPluginTypes(assembly).ToList();
            if (pluginTypes.Count == 0)
            {
                _logger.Info($"Assembly '{Path.GetFileName(assemblyPath)}' does not contain designer plugins.");
                return;
            }

            foreach (var pluginType in pluginTypes)
            {
                try
                {
                    if (Activator.CreateInstance(pluginType) is not IFormDesignerPlugin plugin)
                    {
                        _logger.Info($"Type '{pluginType.FullName}' does not implement IFormDesignerPlugin.");
                        continue;
                    }

                    plugin.Register(registry);
                    _logger.Info($"Loaded designer plugin '{plugin.Id}' from '{Path.GetFileName(assemblyPath)}'.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to activate plugin type '{pluginType.FullName}' from '{assemblyPath}'.", ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load plugin assembly '{assemblyPath}'.", ex);
        }
    }

    private static IEnumerable<Type> DiscoverPluginTypes(Assembly assembly)
    {
        var fromAttributes = assembly.GetCustomAttributes<FormDesignerPluginAttribute>()
            .Select(attribute => attribute.PluginType);

        IEnumerable<Type> exportedTypes;

        try
        {
            exportedTypes = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            exportedTypes = ex.Types.Where(type => type is not null).Cast<Type>();
        }

        var fromInterfaces = exportedTypes
            .Where(type => !type.IsAbstract && typeof(IFormDesignerPlugin).IsAssignableFrom(type));

        return fromAttributes.Concat(fromInterfaces).Distinct();
    }
}
