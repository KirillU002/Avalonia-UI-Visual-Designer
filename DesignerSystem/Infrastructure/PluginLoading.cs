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
    private static readonly Version SupportedApiVersion = new(1, 0, 0);

    private readonly IDesignerLogger _logger;
    private readonly List<PluginLoadContext> _loadedContexts = new();

    public PluginLoader(IDesignerLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<PluginLoadReport> LoadFromFolder(
        string folderPath,
        IDesignerRegistry registry,
        bool replaceDiagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (registry is DesignerRegistry designerRegistry && replaceDiagnostics)
            designerRegistry.ClearPluginLoadReports();

        if (!Directory.Exists(folderPath))
        {
            _logger.Info($"Plugin folder '{folderPath}' was not found. Skipping plugin discovery.");
            if (registry is DesignerRegistry missingFolderRegistry)
                missingFolderRegistry.SetPluginScanMetadata(folderPath, 0);
            return Array.Empty<PluginLoadReport>();
        }

        var assemblyPaths = Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (registry is DesignerRegistry metadataRegistry)
            metadataRegistry.SetPluginScanMetadata(folderPath, assemblyPaths.Count);

        var reports = new List<PluginLoadReport>();
        foreach (var assemblyPath in assemblyPaths)
        {
            foreach (var report in LoadPluginAssembly(assemblyPath, registry))
            {
                reports.Add(report);
                if (registry is DesignerRegistry reportRegistry)
                    reportRegistry.AddPluginLoadReport(report);
            }
        }

        return reports;
    }

    private IReadOnlyList<PluginLoadReport> LoadPluginAssembly(string assemblyPath, IDesignerRegistry registry)
    {
        var loadContext = new PluginLoadContext(assemblyPath);
        _loadedContexts.Add(loadContext);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var discovery = DiscoverPluginTypes(assembly);
            if (discovery.PluginTypes.Count == 0)
            {
                if (discovery.LoaderErrors.Count > 0)
                {
                    return new[]
                    {
                        BuildAssemblyErrorReport(
                            assemblyPath,
                            "Assembly loaded, but some exported types could not be inspected.",
                            discovery.LoaderErrors)
                    };
                }

                _logger.Info($"Assembly '{Path.GetFileName(assemblyPath)}' does not contain designer plugins.");
                return Array.Empty<PluginLoadReport>();
            }

            return discovery.PluginTypes
                .Select(pluginType => LoadPluginType(assemblyPath, pluginType, registry, discovery.LoaderErrors))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load plugin assembly '{assemblyPath}'.", ex);
            return new[]
            {
                BuildAssemblyErrorReport(
                    assemblyPath,
                    "Failed to load plugin assembly.",
                    new[] { FormatExceptionMessage(ex) })
            };
        }
    }

    private PluginLoadReport LoadPluginType(
        string assemblyPath,
        Type pluginType,
        IDesignerRegistry registry,
        IReadOnlyList<string> discoveryWarnings)
    {
        var errors = new List<string>();
        var warnings = new List<string>(discoveryWarnings);
        IFormDesignerPlugin? plugin = null;

        try
        {
            if (Activator.CreateInstance(pluginType) is not IFormDesignerPlugin createdPlugin)
            {
                errors.Add($"Type '{pluginType.FullName}' does not implement IFormDesignerPlugin.");
                return BuildPluginReport(assemblyPath, pluginType, null, errors, warnings, Array.Empty<IControlDescriptor>());
            }

            plugin = createdPlugin;

            if (!IsApiCompatible(plugin.ApiVersion))
            {
                errors.Add($"Plugin API version {plugin.ApiVersion} is not compatible with supported SDK {SupportedApiVersion}.");
                return BuildPluginReport(assemblyPath, pluginType, plugin, errors, warnings, Array.Empty<IControlDescriptor>());
            }

            var registrationRegistry = new PluginRegistrationRegistry(registry);
            try
            {
                plugin.Register(registrationRegistry);
            }
            catch (Exception ex)
            {
                errors.Add($"Register(...) failed: {FormatExceptionMessage(ex)}");
                _logger.Error($"Plugin '{plugin.Id}' Register(...) failed.", ex);
                return BuildPluginReport(assemblyPath, pluginType, plugin, errors, warnings, Array.Empty<IControlDescriptor>());
            }

            warnings.AddRange(registrationRegistry.Warnings);
            errors.AddRange(registrationRegistry.Errors);

            var registeredDescriptors = CommitRegistration(registry, registrationRegistry.Descriptors, errors);
            foreach (var bindingProvider in registrationRegistry.BindingProviders)
            {
                try
                {
                    registry.RegisterBindingProvider(bindingProvider);
                }
                catch (Exception ex)
                {
                    errors.Add($"Binding provider '{bindingProvider.Id}' was not registered: {FormatExceptionMessage(ex)}");
                }
            }

            if (registeredDescriptors.Count == 0 && errors.Count == 0)
                warnings.Add("Plugin loaded, but it did not register any controls.");

            _logger.Info($"Loaded designer plugin '{plugin.Id}' from '{Path.GetFileName(assemblyPath)}'.");
            return BuildPluginReport(assemblyPath, pluginType, plugin, errors, warnings, registeredDescriptors);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to activate plugin type '{pluginType.FullName}': {FormatExceptionMessage(ex)}");
            _logger.Error($"Failed to activate plugin type '{pluginType.FullName}' from '{assemblyPath}'.", ex);
            return BuildPluginReport(assemblyPath, pluginType, plugin, errors, warnings, Array.Empty<IControlDescriptor>());
        }
    }

    private static IReadOnlyList<IControlDescriptor> CommitRegistration(
        IDesignerRegistry registry,
        IReadOnlyList<IControlDescriptor> descriptors,
        ICollection<string> errors)
    {
        var registered = new List<IControlDescriptor>();
        foreach (var descriptor in descriptors)
        {
            try
            {
                registry.RegisterControl(descriptor);
                registered.Add(descriptor);
            }
            catch (Exception ex)
            {
                errors.Add($"Control '{descriptor.TypeKey}' was not registered: {FormatExceptionMessage(ex)}");
            }
        }

        return registered;
    }

    private static bool IsApiCompatible(Version version)
    {
        return version.Major == SupportedApiVersion.Major;
    }

    private static PluginLoadReport BuildPluginReport(
        string assemblyPath,
        Type pluginType,
        IFormDesignerPlugin? plugin,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<IControlDescriptor> registeredDescriptors)
    {
        var status = errors.Count > 0
            ? registeredDescriptors.Count > 0 ? PluginLoadStatus.Warning : PluginLoadStatus.Error
            : warnings.Count > 0
                ? PluginLoadStatus.Warning
                : PluginLoadStatus.Ok;

        return new PluginLoadReport
        {
            AssemblyPath = assemblyPath,
            PluginId = plugin?.Id ?? pluginType.FullName ?? "",
            PluginTitle = plugin?.Title ?? pluginType.Name,
            PluginVersion = plugin?.GetType().Assembly.GetName().Version?.ToString() ?? "",
            ApiVersion = plugin?.ApiVersion.ToString() ?? "",
            Status = status,
            Message = status switch
            {
                PluginLoadStatus.Ok => "Plugin loaded successfully.",
                PluginLoadStatus.Warning => "Plugin loaded with warnings.",
                _ => "Plugin failed to load."
            },
            RegisteredControls = registeredDescriptors
                .Select(descriptor => $"{descriptor.Title} ({descriptor.TypeKey})")
                .ToList(),
            Warnings = warnings.ToList(),
            Errors = errors.ToList()
        };
    }

    private static PluginLoadReport BuildAssemblyErrorReport(
        string assemblyPath,
        string message,
        IReadOnlyList<string> errors)
    {
        return new PluginLoadReport
        {
            AssemblyPath = assemblyPath,
            Status = PluginLoadStatus.Error,
            Message = message,
            Errors = errors.ToList()
        };
    }

    private static PluginTypeDiscovery DiscoverPluginTypes(Assembly assembly)
    {
        var loaderErrors = new List<string>();
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
            loaderErrors.AddRange(ex.LoaderExceptions
                .Where(exception => exception is not null)
                .Select(exception => FormatExceptionMessage(exception!)));
        }

        var fromInterfaces = exportedTypes
            .Where(type => !type.IsAbstract && typeof(IFormDesignerPlugin).IsAssignableFrom(type));

        return new PluginTypeDiscovery(
            fromAttributes.Concat(fromInterfaces).Distinct().ToList(),
            loaderErrors);
    }

    private static string FormatExceptionMessage(Exception exception)
    {
        return exception is ReflectionTypeLoadException reflectionException
            ? string.Join("; ", reflectionException.LoaderExceptions
                .Where(loaderException => loaderException is not null)
                .Select(loaderException => loaderException!.Message))
            : exception.Message;
    }

    private sealed class PluginRegistrationRegistry : IDesignerRegistry
    {
        private readonly IDesignerRegistry _innerRegistry;
        private readonly HashSet<string> _localTypeKeys = new(StringComparer.OrdinalIgnoreCase);

        public PluginRegistrationRegistry(IDesignerRegistry innerRegistry)
        {
            _innerRegistry = innerRegistry;
        }

        public List<IControlDescriptor> Descriptors { get; } = new();
        public List<IBindingMetadataProvider> BindingProviders { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();

        public void RegisterControl(IControlDescriptor descriptor)
        {
            if (descriptor is null)
            {
                Errors.Add("Plugin tried to register a null control descriptor.");
                return;
            }

            if (string.IsNullOrWhiteSpace(descriptor.TypeKey))
            {
                Errors.Add($"Descriptor '{descriptor.GetType().FullName}' has an empty TypeKey.");
                return;
            }

            if (_localTypeKeys.Contains(descriptor.TypeKey))
            {
                Errors.Add($"Duplicate TypeKey '{descriptor.TypeKey}' inside the same plugin package.");
                return;
            }

            if (_innerRegistry.TryGetControl(descriptor.TypeKey, out var existingDescriptor))
            {
                Warnings.Add(
                    $"TypeKey '{descriptor.TypeKey}' is already registered by '{existingDescriptor.GetType().Assembly.GetName().Name}'. The duplicate descriptor was skipped.");
                return;
            }

            _localTypeKeys.Add(descriptor.TypeKey);
            Descriptors.Add(descriptor);
        }

        public void RegisterBindingProvider(IBindingMetadataProvider provider)
        {
            if (provider is null)
            {
                Errors.Add("Plugin tried to register a null binding metadata provider.");
                return;
            }

            BindingProviders.Add(provider);
        }

        public bool TryGetControl(string typeKey, out IControlDescriptor descriptor)
        {
            var localDescriptor = Descriptors.FirstOrDefault(candidate =>
                candidate.TypeKey.Equals(typeKey, StringComparison.OrdinalIgnoreCase));
            if (localDescriptor is not null)
            {
                descriptor = localDescriptor;
                return true;
            }

            return _innerRegistry.TryGetControl(typeKey, out descriptor);
        }

        public IControlDescriptor GetRequiredControl(string typeKey)
        {
            return TryGetControl(typeKey, out var descriptor)
                ? descriptor
                : _innerRegistry.GetRequiredControl(typeKey);
        }

        public IReadOnlyList<IControlDescriptor> GetControls()
        {
            return _innerRegistry.GetControls().Concat(Descriptors).ToList();
        }

        public IReadOnlyList<IBindingMetadataProvider> GetBindingProviders()
        {
            return _innerRegistry.GetBindingProviders().Concat(BindingProviders).ToList();
        }
    }

    private sealed record PluginTypeDiscovery(
        IReadOnlyList<Type> PluginTypes,
        IReadOnlyList<string> LoaderErrors);
}

