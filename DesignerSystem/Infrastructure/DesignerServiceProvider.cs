using System;
using System.Collections.Generic;

namespace FormDesigner.DesignerSystem.Infrastructure;

internal sealed class DesignerServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public DesignerServiceProvider Add<TService>(TService instance)
        where TService : class
    {
        _services[typeof(TService)] = instance;
        return this;
    }

    public object? GetService(Type serviceType)
    {
        return _services.TryGetValue(serviceType, out var service) ? service : null;
    }
}
