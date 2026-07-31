using Avalonia.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FormDesigner.PluginContracts;

public enum PropertyEditorKind
{
    Text,
    Bool,
    Number,
    Enum,
    Color,
    Binding,
    Collection
}

public enum DesignerPreviewMode
{
    Designer,
    RuntimePreview
}

public sealed class PropertyOption
{
    public string Value { get; init; } = "";
    public string Title { get; init; } = "";
}

public sealed class DesignPropertyDescriptor
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "General";
    public PropertyEditorKind Editor { get; init; }
    public string? BuiltInPropertyName { get; init; }
    public string? DefaultValueJson { get; init; }
    public bool IsBindable { get; init; }
    public bool IsCollection { get; init; }
    public IReadOnlyList<PropertyOption> Options { get; init; } = Array.Empty<PropertyOption>();
}

public sealed class DesignerControlDefinition
{
    public string TypeKey { get; set; } = "";
    public string DescriptorId { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string PluginVersion { get; set; } = "";

    public Dictionary<string, object?> BuiltInProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CustomProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BindingImportRequest
{
    public string AssemblyPath { get; init; } = "";
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BindingImportDiagnostics
{
    public string ProviderId { get; init; } = "";
    public string AssemblyPath { get; init; } = "";
    public int ScannedTypeCount { get; init; }
    public int IgnoredTypeCount { get; init; }
    public int InfrastructureTypeCount { get; init; }
    public int CandidateTypeCount { get; init; }
    public int ImportedSourceCount { get; init; }
    public int FailedCandidateTypeCount { get; init; }
    public int TableAttributedTypeCount { get; init; }
    public int ColumnAttributedTypeCount { get; init; }
    public int LoaderExceptionCount { get; init; }
    public string FailureMessage { get; init; } = "";
    public string ExceptionType { get; init; } = "";
    public string ExceptionMessage { get; init; } = "";
    public string ExceptionDetails { get; init; } = "";
    public IReadOnlyList<string> LoaderExceptionMessages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateTypeNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InfrastructureTypeNames { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Optional provider metadata for a descriptor. Existing descriptors do not
/// need to implement this contract: the host supplies sensible defaults for
/// built-in and user-installed controls.
/// </summary>
public interface IDesignerControlProviderMetadata
{
    string ProviderId { get; }
    string ProviderTitle { get; }
    string ToolboxGroup { get; }
    string ToolboxBadge { get; }
    int ToolboxGroupOrder => 100;
}

public sealed record DesignerPackageReference(
    string PackageId,
    string Version,
    string Reason);

public sealed record DesignerApplicationStyleContribution(
    string XmlNamespacePrefix,
    string XmlNamespace,
    string StyleAxaml,
    string Description);

public sealed class DesignerExportContributionContext
{
    public IReadOnlyCollection<string> UsedControlTypeKeys { get; init; } = Array.Empty<string>();

    public bool UsesControl(string typeKey)
    {
        return !string.IsNullOrWhiteSpace(typeKey)
            && UsedControlTypeKeys.Contains(typeKey, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class DesignerExportContributions
{
    public IReadOnlyList<DesignerPackageReference> Packages { get; init; } = Array.Empty<DesignerPackageReference>();
    public IReadOnlyList<DesignerApplicationStyleContribution> ApplicationStyles { get; init; } = Array.Empty<DesignerApplicationStyleContribution>();
}

/// <summary>
/// Optional plugin capability for declaring dependencies and application-level
/// styles required by controls that are actually used in an exported project.
/// </summary>
public interface IDesignerExportContributionProvider
{
    string ProviderId { get; }
    DesignerExportContributions GetExportContributions(DesignerExportContributionContext context);
}

public sealed class DesignerRuntimePreviewContributionContext
{
    public IReadOnlyCollection<string> UsedControlTypeKeys { get; init; } = Array.Empty<string>();

    public bool UsesControl(string typeKey)
    {
        return !string.IsNullOrWhiteSpace(typeKey)
            && UsedControlTypeKeys.Contains(typeKey, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// In-memory dependencies required by the Runtime AXAML Preview. The contribution deliberately
/// carries Assembly objects instead of paths so plugin load-context identity stays intact.
/// </summary>
public sealed class DesignerRuntimePreviewContribution
{
    public string ProviderId { get; init; } = "";
    public IReadOnlyList<Assembly> Assemblies { get; init; } = Array.Empty<Assembly>();
    public Action? Initialize { get; init; }

    /// <summary>
    /// Applies plugin resources to the isolated Runtime AXAML preview subtree after it has
    /// been loaded. Plugin themes must not mutate Application.Current.Styles because that
    /// would restyle the designer chrome itself.
    /// </summary>
    public Action<Control>? ApplyToPreviewRoot { get; init; }
}

public interface IDesignerRuntimePreviewContributionProvider
{
    string ProviderId { get; }
    DesignerRuntimePreviewContribution? GetRuntimePreviewContribution(DesignerRuntimePreviewContributionContext context);
}

public sealed class BindingImportResult
{
    public IReadOnlyList<BindingSourceMetadata> Sources { get; init; } = Array.Empty<BindingSourceMetadata>();
    public BindingImportDiagnostics Diagnostics { get; init; } = new();
}

public sealed class BindingSourceMetadata
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "Source";
    public string Path { get; init; } = "Items";
    public string ItemTypeName { get; init; } = "ItemRow";
    public string Description { get; init; } = "";
    public string SourceKind { get; init; } = "Manual";
    public string SourceAssemblyPath { get; init; } = "";
    public string SourceTypeFullName { get; init; } = "";
    public string SourceTableName { get; init; } = "";
    public string SourceConnectionString { get; init; } = "";
    public string SourceSchemaName { get; init; } = "dbo";
    public string SourceQuery { get; init; } = "";
    public IReadOnlyList<BindingFieldMetadata> Fields { get; init; } = Array.Empty<BindingFieldMetadata>();
}

public sealed class BindingFieldMetadata
{
    public string Header { get; init; } = "Column";
    public string Path { get; init; } = "Property";
    public string SampleValue { get; init; } = "Value";
    public string Width { get; init; } = "*";
    public string TypeName { get; init; } = "string";
    public string DbType { get; init; } = "";
    public bool IsPrimaryKey { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool CanRead { get; init; } = true;
    public bool CanWrite { get; init; } = true;
    public bool IsVisible { get; init; } = true;
    public bool IsSortable { get; init; } = true;
    public string SortDirection { get; init; } = "None";
    public int SortOrder { get; init; } = -1;
    public int GroupOrder { get; init; } = -1;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FormDesignerPluginAttribute : Attribute
{
    public FormDesignerPluginAttribute(Type pluginType)
    {
        PluginType = pluginType;
    }

    public Type PluginType { get; }
}

public interface IFormDesignerPlugin
{
    string Id { get; }
    string Title { get; }
    Version ApiVersion { get; }
    void Register(IDesignerRegistry registry);
}

public interface IDesignerRegistry
{
    void RegisterControl(IControlDescriptor descriptor);
    void RegisterBindingProvider(IBindingMetadataProvider provider);
    void RegisterExportContributionProvider(IDesignerExportContributionProvider provider);
    void RegisterRuntimePreviewContributionProvider(IDesignerRuntimePreviewContributionProvider provider);
    bool TryGetControl(string typeKey, out IControlDescriptor descriptor);
    IControlDescriptor GetRequiredControl(string typeKey);
    IReadOnlyList<IControlDescriptor> GetControls();
    IReadOnlyList<IBindingMetadataProvider> GetBindingProviders();
    IReadOnlyList<IDesignerExportContributionProvider> GetExportContributionProviders();
    IReadOnlyList<IDesignerRuntimePreviewContributionProvider> GetRuntimePreviewContributionProviders();
}

public interface IControlDescriptor
{
    string TypeKey { get; }
    string Title { get; }
    string Category { get; }
    string Description { get; }
    bool IsContainer { get; }
    bool CanHostChildren { get; }
    string ChildLayoutMode { get; }
    IReadOnlyList<DesignPropertyDescriptor> Properties { get; }
    DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context);
    Control BuildPreview(IDesignControlNode control, IPreviewContext context);
    void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context);
}

public interface IBindingMetadataProvider
{
    string Id { get; }
    bool CanHandle(BindingImportRequest request);
    BindingImportResult DiscoverSources(BindingImportRequest request);
}

public interface IDescriptorContext
{
    string ActiveTheme { get; }
    IReadOnlyList<BindingSourceMetadata> BindingSources { get; }
    IServiceProvider Services { get; }
}

public interface IPreviewContext
{
    DesignerPreviewMode Mode { get; }
    IServiceProvider Services { get; }
    IReadOnlyList<IDesignControlNode> GetChildren(string parentId);
    BindingSourceMetadata? GetBindingSource(string bindingSourceId);
}

public interface IPreviewBindingItemsProvider
{
    IEnumerable? GetItems(string bindingSourceId);
}

/// <summary>
/// Resolves the collection property generated for a BindingSource during export.
/// Plugin descriptors use this instead of duplicating the host ViewModel naming rules.
/// </summary>
public interface IRuntimeBindingExportPathProvider
{
    string GetItemsSourcePath(BindingSourceMetadata? bindingSource);
}

public interface IXamlExportContext
{
    IServiceProvider Services { get; }
    IReadOnlyList<IDesignControlNode> GetChildren(string parentId);
    BindingSourceMetadata? GetBindingSource(string bindingSourceId);
    void AppendChildren(IXamlWriter writer, string parentId, int indentLevel);
    void RegisterXmlNamespace(string prefix, string namespaceUri);
}

public interface IXamlWriter
{
    void WriteLine(int indentLevel, string line);
}

public interface IDesignControlNode
{
    string Id { get; }
    string TypeKey { get; }
    string Name { get; }
    string ParentId { get; }
    string DescriptorId { get; }
    string PluginId { get; }
    string PluginVersion { get; }
    IReadOnlyDictionary<string, object?> BuiltInProperties { get; }
    IReadOnlyDictionary<string, string> CustomProperties { get; }
}

public static class DesignControlNodeExtensions
{
    public static string GetString(this IDesignControlNode control, string key, string fallback = "")
    {
        if (control.BuiltInProperties.TryGetValue(key, out var value))
            return value?.ToString() ?? fallback;

        return fallback;
    }

    public static double GetDouble(this IDesignControlNode control, string key, double fallback = 0)
    {
        if (!control.BuiltInProperties.TryGetValue(key, out var value) || value is null)
            return fallback;

        if (value is double doubleValue)
            return doubleValue;

        if (value is float floatValue)
            return floatValue;

        if (value is int intValue)
            return intValue;

        return double.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static int GetInt(this IDesignControlNode control, string key, int fallback = 0)
    {
        if (!control.BuiltInProperties.TryGetValue(key, out var value) || value is null)
            return fallback;

        if (value is int intValue)
            return intValue;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static bool GetBool(this IDesignControlNode control, string key, bool fallback = false)
    {
        if (!control.BuiltInProperties.TryGetValue(key, out var value) || value is null)
            return fallback;

        if (value is bool boolValue)
            return boolValue;

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static string GetCustomPropertyJson(this IDesignControlNode control, string key, string fallbackJson = "null")
    {
        return control.CustomProperties.TryGetValue(key, out var valueJson)
            ? valueJson
            : fallbackJson;
    }

    public static T GetCustomValue<T>(this IDesignControlNode control, string key, T fallback)
    {
        var json = control.GetCustomPropertyJson(key);

        try
        {
            var value = JsonSerializer.Deserialize<T>(json);
            return value is null ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
