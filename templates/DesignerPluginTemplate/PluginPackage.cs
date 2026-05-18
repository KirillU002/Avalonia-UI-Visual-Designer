using FormDesigner.PluginContracts;
using System;

[assembly: FormDesignerPlugin(typeof(DesignerPluginTemplate.PluginPackage))]

namespace DesignerPluginTemplate;

public sealed class PluginPackage : IFormDesignerPlugin
{
    public const string PluginIdValue = "Company.DesignerPluginTemplate";
    public const string PluginVersionValue = "1.0.0";

    public string Id => PluginIdValue;
    public string Title => "Designer Plugin Template";
    public Version ApiVersion => new(1, 0, 0);

    public void Register(IDesignerRegistry registry)
    {
        registry.RegisterControl(new MyControlDescriptor(PluginIdValue, PluginVersionValue));
    }
}

