using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FormDesigner.DesignerSystem.Infrastructure;

internal sealed class DescriptorContext : IDescriptorContext
{
    public DescriptorContext(string activeTheme, IReadOnlyList<BindingSourceMetadata> bindingSources, IServiceProvider services)
    {
        ActiveTheme = activeTheme;
        BindingSources = bindingSources;
        Services = services;
    }

    public string ActiveTheme { get; }
    public IReadOnlyList<BindingSourceMetadata> BindingSources { get; }
    public IServiceProvider Services { get; }
}

internal sealed class DesignerPreviewContext : IPreviewContext
{
    private readonly Func<string, IReadOnlyList<IDesignControlNode>> _childrenResolver;
    private readonly IReadOnlyDictionary<string, BindingSourceMetadata> _bindingSources;

    public DesignerPreviewContext(
        DesignerPreviewMode mode,
        IServiceProvider services,
        Func<string, IReadOnlyList<IDesignControlNode>> childrenResolver,
        IReadOnlyDictionary<string, BindingSourceMetadata> bindingSources)
    {
        Mode = mode;
        Services = services;
        _childrenResolver = childrenResolver;
        _bindingSources = bindingSources;
    }

    public DesignerPreviewMode Mode { get; }
    public IServiceProvider Services { get; }

    public IReadOnlyList<IDesignControlNode> GetChildren(string parentId)
    {
        return _childrenResolver(parentId);
    }

    public BindingSourceMetadata? GetBindingSource(string bindingSourceId)
    {
        return string.IsNullOrWhiteSpace(bindingSourceId) || !_bindingSources.TryGetValue(bindingSourceId, out var source)
            ? null
            : source;
    }
}

internal sealed class XamlExportContext : IXamlExportContext
{
    private readonly Func<string, IReadOnlyList<IDesignControlNode>> _childrenResolver;
    private readonly Func<IDesignControlNode, int, IXamlWriter, IXamlExportContext, bool> _childAppender;
    private readonly IReadOnlyDictionary<string, BindingSourceMetadata> _bindingSources;
    private readonly Dictionary<string, string> _registeredNamespaces = new(StringComparer.Ordinal);

    public XamlExportContext(
        IServiceProvider services,
        Func<string, IReadOnlyList<IDesignControlNode>> childrenResolver,
        Func<IDesignControlNode, int, IXamlWriter, IXamlExportContext, bool> childAppender,
        IReadOnlyDictionary<string, BindingSourceMetadata> bindingSources)
    {
        Services = services;
        _childrenResolver = childrenResolver;
        _childAppender = childAppender;
        _bindingSources = bindingSources;
    }

    public IServiceProvider Services { get; }

    public IReadOnlyList<IDesignControlNode> GetChildren(string parentId)
    {
        return _childrenResolver(parentId);
    }

    public BindingSourceMetadata? GetBindingSource(string bindingSourceId)
    {
        return string.IsNullOrWhiteSpace(bindingSourceId) || !_bindingSources.TryGetValue(bindingSourceId, out var source)
            ? null
            : source;
    }

    public void AppendChildren(IXamlWriter writer, string parentId, int indentLevel)
    {
        foreach (var child in _childrenResolver(parentId))
            _childAppender(child, indentLevel, writer, this);
    }

    public void RegisterXmlNamespace(string prefix, string namespaceUri)
    {
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(namespaceUri))
            return;

        _registeredNamespaces[prefix] = namespaceUri;
    }

    public IReadOnlyDictionary<string, string> RegisteredNamespaces => _registeredNamespaces;
}

internal sealed class StringBuilderXamlWriter : IXamlWriter
{
    private readonly StringBuilder _builder;

    public StringBuilderXamlWriter(StringBuilder builder)
    {
        _builder = builder;
    }

    public void WriteLine(int indentLevel, string line)
    {
        _builder.Append(' ', Math.Max(0, indentLevel) * 2);
        _builder.AppendLine(line);
    }
}

internal static class BindingMetadataMapper
{
    public static IReadOnlyList<BindingSourceMetadata> ToMetadata(IEnumerable<BindingSourceModel> sources)
    {
        return sources.Select(source => new BindingSourceMetadata
        {
            Id = source.Id,
            Name = source.Name,
            Path = source.Path,
            ItemTypeName = source.ItemTypeName,
            Description = source.Description,
            SourceKind = source.SourceKind,
            SourceAssemblyPath = source.SourceAssemblyPath,
            SourceTypeFullName = source.SourceTypeFullName,
            SourceTableName = source.SourceTableName,
            SourceConnectionString = source.SourceConnectionString,
            SourceSchemaName = source.SourceSchemaName,
            SourceQuery = source.SourceQuery,
            Fields = source.Fields.Select(field => new BindingFieldMetadata
            {
                Header = field.Header,
                Path = field.Path,
                SampleValue = field.SampleValue,
                Width = field.Width,
                TypeName = field.TypeName,
                DbType = field.DbType,
                IsPrimaryKey = field.IsPrimaryKey,
                IsNullable = field.IsNullable,
                CanRead = field.CanRead,
                CanWrite = field.CanWrite,
                IsVisible = field.IsVisible,
                IsSortable = field.IsSortable,
                SortDirection = field.SortDirection,
                SortOrder = field.SortOrder,
                GroupOrder = field.GroupOrder,
                MinWidth = field.MinWidth,
                MaxWidth = field.MaxWidth,
                AllowResize = field.AllowResize,
                AllowSort = field.AllowSort,
                AllowFilter = field.AllowFilter,
                VisibleIndex = field.VisibleIndex
            }).ToList()
        }).ToList();
    }

    public static IReadOnlyDictionary<string, BindingSourceMetadata> ToMetadataMap(IEnumerable<BindingSourceModel> sources)
    {
        return ToMetadata(sources)
            .ToDictionary(source => source.Id, source => source, StringComparer.OrdinalIgnoreCase);
    }

    public static BindingSourceModel ToRuntimeModel(BindingSourceMetadata source)
    {
        var model = new BindingSourceModel
        {
            Id = source.Id,
            Name = source.Name,
            Path = source.Path,
            ItemTypeName = source.ItemTypeName,
            Description = source.Description,
            SourceKind = source.SourceKind,
            SourceAssemblyPath = source.SourceAssemblyPath,
            SourceTypeFullName = source.SourceTypeFullName,
            SourceTableName = source.SourceTableName,
            SourceConnectionString = source.SourceConnectionString,
            SourceSchemaName = source.SourceSchemaName,
            SourceQuery = source.SourceQuery
        };

        foreach (var field in source.Fields)
        {
            model.Fields.Add(new BindingFieldModel
            {
                Header = field.Header,
                Path = field.Path,
                SampleValue = field.SampleValue,
                Width = field.Width,
                TypeName = field.TypeName,
                DbType = field.DbType,
                IsPrimaryKey = field.IsPrimaryKey,
                IsNullable = field.IsNullable,
                CanRead = field.CanRead,
                CanWrite = field.CanWrite,
                IsVisible = field.IsVisible,
                IsSortable = field.IsSortable,
                SortDirection = field.SortDirection,
                SortOrder = field.SortOrder,
                GroupOrder = field.GroupOrder,
                MinWidth = field.MinWidth,
                MaxWidth = field.MaxWidth,
                AllowResize = field.AllowResize,
                AllowSort = field.AllowSort,
                AllowFilter = field.AllowFilter,
                VisibleIndex = field.VisibleIndex
            });
        }

        return model;
    }
}
