using FormDesigner.PluginContracts;
using MinimalDesignerPlugin.Descriptors;
using System;

[assembly: FormDesignerPlugin(typeof(MinimalDesignerPlugin.MinimalPluginPackage))]

namespace MinimalDesignerPlugin;

public sealed class MinimalPluginPackage : IFormDesignerPlugin
{
    public const string PluginIdValue = "Samples.MinimalDesignerPlugin";
    public const string PluginVersionValue = "1.0.0";

    public string Id => PluginIdValue;
    public string Title => "Minimal Designer Plugin";
    public Version ApiVersion => new(1, 0, 0);

    public void Register(IDesignerRegistry registry)
    {
        registry.RegisterControl(new HelloCardDescriptor(PluginIdValue, PluginVersionValue));
    }
}

