using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FormDesigner.DesignerSystem.AxamlRoundTrip;

namespace FormDesigner.DesignerSystem.Binding;

public sealed partial class ReflectionBindingMetadataProvider
{
    // Recognition never uses the runtime fallback of DiscoverSources.
    internal static IReadOnlyList<AxamlTypeIdentity> ReadAxamlTypes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        var name = reader.GetString(assembly.Name);
        var aliases = new List<(string Clr, string Xml)>();
        foreach (var handle in assembly.GetCustomAttributes())
        {
            if (GetPortableAttributeTypeFullName(reader,handle) != "Avalonia.Metadata.XmlnsDefinitionAttribute") continue;
            var blob = reader.GetBlobReader(reader.GetCustomAttribute(handle).Value);
            if (blob.ReadUInt16() == 1 && blob.ReadSerializedString() is {} xml && blob.ReadSerializedString() is {} clr)
                aliases.Add((clr,xml));
        }
        var result = new List<AxamlTypeIdentity>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public || type.GetGenericParameters().Count != 0) continue;
            var ns = reader.GetString(type.Namespace);
            var props = new List<string>();
            foreach (var property in type.GetProperties())
            {
                var getter = reader.GetPropertyDefinition(property).GetAccessors().Getter;
                if (getter.IsNil || (reader.GetMethodDefinition(getter).Attributes & (MethodAttributes.MemberAccessMask | MethodAttributes.Static)) != MethodAttributes.Public) continue;
                var info = ReadPortableMetadataProperty(reader,property);
                if (info.IsPublicReadable) props.Add(info.Name + " · " + info.TypeName);
            }
            result.Add(new(reader.GetString(type.Name), ns, name, GetEntityFullName(reader,type.BaseType),
                aliases.Where(a=>a.Clr==ns).Select(a=>a.Xml).ToArray(),null,props));
        }
        return result;
    }
}
