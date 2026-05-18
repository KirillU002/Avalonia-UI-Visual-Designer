using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.Infrastructure;

public sealed class DesignerRegistry : IDesignerRegistry
{
    private readonly Dictionary<string, IControlDescriptor> _controls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IBindingMetadataProvider> _bindingProviders = new();
    private readonly List<PluginLoadReport> _pluginLoadReports = new();

    public void RegisterControl(IControlDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.TypeKey))
            throw new InvalidOperationException("Control descriptor TypeKey cannot be empty.");

        if (_controls.ContainsKey(descriptor.TypeKey))
            throw new InvalidOperationException($"Control descriptor TypeKey '{descriptor.TypeKey}' is already registered.");

        _controls[descriptor.TypeKey] = descriptor;
    }

    public void RegisterBindingProvider(IBindingMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _bindingProviders.Add(provider);
    }

    public bool TryGetControl(string typeKey, out IControlDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            descriptor = new MissingPluginDescriptor("Unknown");
            return false;
        }

        return _controls.TryGetValue(typeKey, out descriptor!);
    }

    public IControlDescriptor GetRequiredControl(string typeKey)
    {
        return TryGetControl(typeKey, out var descriptor)
            ? descriptor
            : new MissingPluginDescriptor(typeKey);
    }

    public IReadOnlyList<IControlDescriptor> GetControls()
    {
        return _controls.Values
            .OrderBy(control => control.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(control => control.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<IBindingMetadataProvider> GetBindingProviders()
    {
        return _bindingProviders.ToList();
    }

    public int LastPluginAssemblyScanCount { get; private set; }

    public DateTime LastPluginScanUtc { get; private set; }

    public string LastPluginScanFolder { get; private set; } = "";

    public IReadOnlyList<PluginLoadReport> GetPluginLoadReports()
    {
        return _pluginLoadReports.ToList();
    }

    public void ClearPluginLoadReports()
    {
        _pluginLoadReports.Clear();
    }

    public void AddPluginLoadReport(PluginLoadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _pluginLoadReports.Add(report);
    }

    public void SetPluginScanMetadata(string folderPath, int assemblyCount)
    {
        LastPluginScanFolder = folderPath;
        LastPluginAssemblyScanCount = Math.Max(0, assemblyCount);
        LastPluginScanUtc = DateTime.UtcNow;
    }

    public void ClearPluginRegistrations()
    {
        var hostAssembly = typeof(DesignerRegistry).Assembly;
        var pluginControlKeys = _controls
            .Where(pair => pair.Value.GetType().Assembly != hostAssembly)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in pluginControlKeys)
            _controls.Remove(key);

        _bindingProviders.RemoveAll(provider => provider.GetType().Assembly != hostAssembly);
    }
}
