using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace FormDesigner.ExportSmokeTests;

internal static partial class Program
{
    // Read metadata/IL without loading an application or changing its resolution.
    private static int RunPreviewPhase2DependencyReport()
    {
        var rows=new List<object>();
        foreach(var scene in new[]{"EremexEditor","EremexGrid"})
        foreach(var configuration in new[]{"Debug","Release"})
        {
            var folder=Path.Combine(Preview2Folder,"exports",scene);var project=Directory.GetFiles(folder,"*.csproj").Single();
            var file=Path.Combine(folder,"bin",configuration,"net6.0",Path.GetFileNameWithoutExtension(project)+".dll");
            using var stream=File.OpenRead(file);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
            var references=reader.AssemblyReferences.Select(h=>reader.GetString(reader.GetAssemblyReference(h).Name)).ToArray();
            var calls=new List<string>();
            var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>o.Value);
            foreach(var typeHandle in reader.TypeDefinitions)
            {
                var type=reader.GetTypeDefinition(typeHandle);if(reader.GetString(type.Name)!="MainWindow")continue;
                foreach(var handle in type.GetMethods())
                {
                    var definition=reader.GetMethodDefinition(handle);
                    if(reader.GetString(definition.Name)!="InitializeComponent"||definition.RelativeVirtualAddress==0)continue;
                    var il=pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
                    for(var i=0;i<il.Length;)
                    {
                        var first=il[i++];var value=first==0xfe?unchecked((short)(0xfe00|il[i++])):(short)first;var opcode=opcodes[value];
                        if(opcode.OperandType==OperandType.InlineMethod)
                        {
                            var entity=MetadataTokens.EntityHandle(BitConverter.ToInt32(il,i));
                            if(entity.Kind==HandleKind.MemberReference)
                            {
                                var member=reader.GetMemberReference((MemberReferenceHandle)entity);
                                if(member.Parent.Kind==HandleKind.TypeReference)
                                {
                                    var declaring=reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                                    var assembly=declaring.ResolutionScope.Kind==HandleKind.AssemblyReference?reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)declaring.ResolutionScope).Name):"local";
                                    calls.Add(assembly+"::"+reader.GetString(declaring.Namespace)+"."+reader.GetString(declaring.Name)+"."+reader.GetString(member.Name));
                                }
                            }
                        }
                        i+=opcode.OperandType switch
                        {
                            OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,
                            OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,
                            OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,i),_=>4
                        };
                    }
                }
            }
            var row=new{scene,configuration,References=references,InitializeComponentCalls=calls};rows.Add(row);
            Console.WriteLine("DEPENDENCY_EVIDENCE "+JsonSerializer.Serialize(row));
            RequireCapability(references.Contains("Avalonia.Diagnostics")== (configuration=="Debug"),"Unexpected Debug/Release Diagnostics reference.");
            RequireCapability(calls.Any(c=>c.StartsWith("Avalonia.Diagnostics::"))==(configuration=="Debug"),"The expected generated DevTools call was not found in IL.");
        }
        File.WriteAllText(Path.Combine(Preview2Folder,"eremex-dependency-evidence.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }
}
