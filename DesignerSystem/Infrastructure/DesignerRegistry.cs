using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.Infrastructure;

public sealed class DesignerRegistry : IDesignerRegistry
{
    private readonly Dictionary<string, IControlDescriptor> _controls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IBindingMetadataProvider> _bindingProviders = new();

    public void RegisterControl(IControlDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
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
}
