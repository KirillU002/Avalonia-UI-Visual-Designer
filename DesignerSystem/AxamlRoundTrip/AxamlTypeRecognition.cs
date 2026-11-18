using Avalonia;
using Avalonia.Controls;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public sealed record AxamlTypeIdentity(string Name, string Namespace, string AssemblyName, string BaseType,
    IReadOnlyList<string> XmlNamespaces, Type? FrameworkType, IReadOnlyList<string> DeclaredProperties)
{
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;
    public IEnumerable<string> Properties => FrameworkType is not {} type ? DeclaredProperties :
        typeof(Control).IsAssignableFrom(type) ? AvaloniaMetadataCatalog.Get(type).Properties.Select(p => p.Key + " · " + p.PropertyType.Name) :
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name + " · " + p.PropertyType.Name);
}

public sealed record AxamlRecognitionCapabilities(AxamlTypeIdentity? Identity, bool Inspectable, bool Editable,
    bool StructuralEditable, bool Previewable, bool DescriptorExportable, bool Opaque)
{
    public bool Recognized => Identity != null;
}

// XML identity is separate from the smaller set of reviewed native projection and patch adapters.
public sealed class AxamlTypeRecognition
{
    internal const string FrameworkUri = "https://github.com/avaloniaui";
    private const string XamlUri = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Lazy<ILookup<string, AxamlTypeIdentity>> Framework = new(BuildFrameworkIndex);
    private readonly Dictionary<string, ILookup<string,AxamlTypeIdentity>> _libraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string,string),AxamlTypeIdentity?> _resolved = new();
    internal IEnumerable<string> PluginFiles { get; set; } = Array.Empty<string>();
    private bool _pluginsRead;
    public List<string> Diagnostics { get; } = new();

    private static ILookup<string,AxamlTypeIdentity> BuildFrameworkIndex()
    {
        var entries = new List<AxamlTypeIdentity>();
        foreach (var assembly in AvaloniaMetadataCatalog.FrameworkAssemblies)
        {
            var xmlns = assembly.GetCustomAttributesData().Where(a => a.AttributeType.FullName == "Avalonia.Metadata.XmlnsDefinitionAttribute")
                .ToLookup(a => a.ConstructorArguments[1].Value as string, a => (string)a.ConstructorArguments[0].Value!);
            foreach (var type in assembly.GetExportedTypes().Where(t => !t.IsNested && !t.ContainsGenericParameters))
                entries.Add(new(type.Name, type.Namespace ?? "", assembly.GetName().Name!, type.BaseType?.FullName ?? "",
                    xmlns[type.Namespace].ToArray(), type, Array.Empty<string>()));
        }
        foreach(var type in new[]{typeof(string),typeof(double),typeof(int),typeof(bool)})
            entries.Add(new(type.Name,"System",type.Assembly.GetName().Name!,type.BaseType!.FullName!,
                new[]{XamlUri},type,Array.Empty<string>()));
        return entries.ToLookup(t => t.Name, StringComparer.Ordinal);
    }

    internal static Type? FrameworkType(string uri, string name)
    {
        var matches = Framework.Value[name].Where(t => Matches(t, uri)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0].FrameworkType : null;
    }

    public void InspectAssembly(string file)
    {
        var path = Path.GetFullPath(file);
        var entries = ReflectionBindingMetadataProvider.ReadAxamlTypes(path).ToLookup(t => t.Name, StringComparer.Ordinal);
        _libraries[path] = entries;
        _resolved.Clear();
    }

    public AxamlTypeIdentity? Resolve(string uri, string name)
    {
        if (_resolved.TryGetValue((uri,name),out var cached)) return cached;
        var standard = Framework.Value[name].Where(t => Matches(t,uri)).ToArray();
        AxamlTypeIdentity? result = standard.Length == 1 ? standard[0] : null;
        if (uri is not (FrameworkUri or XamlUri or "") && (standard.Length == 0 || _libraries.Count > 0))
        {
            if (!_pluginsRead)
            {
                _pluginsRead = true;
                foreach (var file in PluginFiles.Concat(PluginRuntimeAssemblyBridge.GetDeclaredRuntimeMetadataPaths()).Distinct(StringComparer.OrdinalIgnoreCase))
                    try { if (!_libraries.ContainsKey(file)) InspectAssembly(file); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
                    { Diagnostics.Add(Path.GetFileName(file) + " · " + ex.Message); }
            }
            var matches = standard.Concat(_libraries.Values.SelectMany(lib => lib[name]).Where(t => Matches(t,uri)))
                .DistinctBy(t => (t.FullName, t.AssemblyName)).Take(2).ToArray();
            result = matches.Length == 1 ? matches[0] : null;
        }
        return _resolved[(uri,name)] = result;
    }

    private static bool Matches(AxamlTypeIdentity t, string uri) =>
        t.XmlNamespaces.Contains(uri == "" ? FrameworkUri : uri, StringComparer.Ordinal) || uri == "using:" + t.Namespace ||
        uri == "clr-namespace:" + t.Namespace + ";assembly=" + t.AssemblyName;

    public AxamlRecognitionCapabilities Inspect(AxamlRoundTripDocument document, AxamlElementSyntax node, IDesignerRegistry registry)
    {
        var identity = Resolve(node);
        var reference = document.SourceMap.Controls.FirstOrDefault(r => r.Element == node);
        var editable = reference != null && document.CapabilityReport.CanSafelyPatch && reference.EditableProperties.Any();
        var isRootCanvas = node == document.SourceMap.CanvasElement;
        var structural = reference != null || isRootCanvas
            ? AxamlStructuralPolicy.Inspect(document,reference?.ControlId) : null;
        var exporter = identity != null && !node.LocalName.Contains('.') && registry.GetControls().Any(d =>
        {
            var native = DesignerRegistry.NativeIdentity(d);
            return native.FullName == identity.FullName && native.Assembly == identity.AssemblyName &&
                DesignerRegistry.CanExportNativeType(registry, d);
        });
        return new(identity, true, editable, structural is { CanAddChild:true } or { CanRemove:true } or { CanReorder:true } or { CanReparent:true },
            isRootCanvas || reference != null && identity?.FrameworkType != null && typeof(Control).IsAssignableFrom(identity.FrameworkType),
            exporter, reference == null && !isRootCanvas);
    }

    public AxamlTypeIdentity? Resolve(AxamlElementSyntax node)
    {
        if (node.NamespaceUri.Length == 0 && !node.LegacyUnqualified) return null;
        var dot = node.LocalName.IndexOf('.');
        var owner = Resolve(node.NamespaceUri, dot < 0 ? node.LocalName : node.LocalName[..dot]);
        if (dot < 0 || owner == null) return owner;
        var member = node.LocalName[(dot+1)..];
        if (owner.FrameworkType is {} type)
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.Name == member) ||
                type.GetField(member + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) is {} field
                && typeof(AvaloniaProperty).IsAssignableFrom(field.FieldType) ? owner : null;
        return owner.DeclaredProperties.Any(p => p.StartsWith(member+" · ",StringComparison.Ordinal)) ? owner : null;
    }
}
