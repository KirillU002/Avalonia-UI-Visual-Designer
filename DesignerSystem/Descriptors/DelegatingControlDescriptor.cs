using Avalonia.Controls;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;

namespace FormDesigner.DesignerSystem.Descriptors;

internal sealed class DelegatingControlDescriptor : IControlDescriptor
{
    private readonly Func<IDescriptorContext, DesignerControlDefinition> _defaultFactory;
    private readonly Func<IDesignControlNode, IPreviewContext, Control> _previewBuilder;
    private readonly Action<IXamlWriter, IDesignControlNode, int, IXamlExportContext> _xamlBuilder;

    public DelegatingControlDescriptor(
        string typeKey,
        string title,
        string category,
        string description,
        bool isContainer,
        bool canHostChildren,
        string childLayoutMode,
        IReadOnlyList<DesignPropertyDescriptor> properties,
        Func<IDescriptorContext, DesignerControlDefinition> defaultFactory,
        Func<IDesignControlNode, IPreviewContext, Control> previewBuilder,
        Action<IXamlWriter, IDesignControlNode, int, IXamlExportContext> xamlBuilder)
    {
        TypeKey = typeKey;
        Title = title;
        Category = category;
        Description = description;
        IsContainer = isContainer;
        CanHostChildren = canHostChildren;
        ChildLayoutMode = childLayoutMode;
        Properties = properties;
        _defaultFactory = defaultFactory;
        _previewBuilder = previewBuilder;
        _xamlBuilder = xamlBuilder;
    }

    public string TypeKey { get; }
    public string Title { get; }
    public string Category { get; }
    public string Description { get; }
    public bool IsContainer { get; }
    public bool CanHostChildren { get; }
    public string ChildLayoutMode { get; }
    public IReadOnlyList<DesignPropertyDescriptor> Properties { get; }

    public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context) => _defaultFactory(context);
    public Control BuildPreview(IDesignControlNode control, IPreviewContext context) => _previewBuilder(control, context);
    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context) => _xamlBuilder(writer, control, indentLevel, context);
}
