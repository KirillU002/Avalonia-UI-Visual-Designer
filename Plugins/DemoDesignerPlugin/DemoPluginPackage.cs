using DemoDesignerPlugin.Descriptors;
using FormDesigner.PluginContracts;
using System;

[assembly: FormDesignerPlugin(typeof(DemoDesignerPlugin.DemoPluginPackage))]

namespace DemoDesignerPlugin;

public sealed class DemoPluginPackage : IFormDesignerPlugin
{
    public const string PluginId = "Demo.DesignerPlugin";
    public const string PluginVersion = "1.0.0";

    public string Id => PluginId;
    public string Title => "Demo Designer Plugin";
    public Version ApiVersion => new(1, 0, 0);

    public void Register(IDesignerRegistry registry)
    {
        registry.RegisterControl(new DemoDevButtonDescriptor(PluginId, PluginVersion));
        registry.RegisterControl(new DemoGridControlDescriptor(PluginId, PluginVersion));
        registry.RegisterControl(new DemoTreeListDescriptor(PluginId, PluginVersion));
    }
}
