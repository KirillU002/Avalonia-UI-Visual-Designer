using FormDesigner.PluginContracts;
using System.Collections.Generic;

namespace FormDesigner.ViewModels;

// Optional payload over the existing dictionary contract; legacy descriptors remain unchanged.
internal sealed class NativeLayoutExportNode : IDesignControlNode
{
    private readonly IDesignControlNode _source;
    public NativeLayoutExportNode(IDesignControlNode source, string placement)
    {
        _source = source;
        var properties = new Dictionary<string, object?>(source.BuiltInProperties);
        properties["NativeLayout.Placement"] = placement;
        BuiltInProperties = properties;
    }
    public string Id => _source.Id;
    public string TypeKey => _source.TypeKey;
    public string Name => _source.Name;
    public string ParentId => _source.ParentId;
    public string DescriptorId => _source.DescriptorId;
    public string PluginId => _source.PluginId;
    public string PluginVersion => _source.PluginVersion;
    public IReadOnlyDictionary<string, object?> BuiltInProperties { get; }
    public IReadOnlyDictionary<string, string> CustomProperties => _source.CustomProperties;
}
