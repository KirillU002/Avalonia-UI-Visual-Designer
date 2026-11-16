using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public sealed partial class AxamlPatchWriter
{
    internal static bool NeedsStructuralPatch(AxamlRoundTripDocument source, DesignerDocumentFileModel document)
    {
        var current=document.Controls.ToDictionary(c=>c.Id);
        if(source.SourceMap.Controls.Any(r=>!current.TryGetValue(r.ControlId,out var c)||(c.ParentId??"")!=(r.ParentId??""))
            ||current.Keys.Any(id=>!source.SourceMap.ByControlId.ContainsKey(id)))return true;
        return source.SourceMap.Controls.GroupBy(r=>r.ParentId??"").Any(g=>!g.OrderBy(r=>r.Element.ElementSpan.Start).Select(r=>r.ControlId)
            .SequenceEqual(g.OrderBy(r=>current[r.ControlId].StackOrder).Select(r=>r.ControlId)));
    }
    private sealed record StructuralEdit(int Start,int Length,Fragment Value) { public int End=>Start+Length; }
    private sealed record Fragment(string Text, Dictionary<string,int> Starts);
    private sealed record SourceEntry(string Text,AxamlSourceReference Reference,IReadOnlyDictionary<string,AxamlSourceReference> References);
    private static bool Placement(string key)=>key is "X" or "Y" or "Canvas.Left" or "Canvas.Top" or "GridRow" or "GridColumn" or "GridRowSpan" or "GridColumnSpan" or "DockPanel.Dock";

    private AxamlPatchResult CreateStructuralPatch(AxamlRoundTripDocument source,DesignerDocumentFileModel document,
        Func<DesignerControlFileModel,string,string>? factory)
    {
        using var stage=FormDesigner.Services.LayoutDropProfile.Stage("AXAML.StructuralPatch");
        try
        {
            var models=document.Controls.ToDictionary(c=>c.Id,StringComparer.Ordinal);
            var entries=source.RemovedSources.ToDictionary(p=>p.Key,p=>new SourceEntry(p.Value.Text,p.Value.Reference,p.Value.References),StringComparer.Ordinal);
            foreach(var r in source.SourceMap.Controls)entries[r.ControlId]=new(source.OriginalText,r,source.SourceMap.ByControlId);
            var children=models.Values.ToLookup(c=>c.ParentId??"");
            var sourceChildren=new Dictionary<IReadOnlyDictionary<string,AxamlSourceReference>,ILookup<string,AxamlSourceReference>>(ReferenceEqualityComparer.Instance);
            string[] Desired(string id)=>children[id].OrderBy(c=>c.StackOrder).Select(c=>c.Id).ToArray();
            var visited=new HashSet<string>();
            foreach(var c in models.Values)
            {
                var chain=new HashSet<string>{c.Id};var parent=c.ParentId??"";
                while(parent.Length>0){if(!models.TryGetValue(parent,out var p)||!chain.Add(parent))throw new InvalidOperationException("Неоднозначный ParentId или цикл.");parent=p.ParentId??"";}
            }
            SourceEntry? ParentEntry(string parent)=>entries.GetValueOrDefault(parent);
            AxamlElementSyntax? Destination(string parent)=>ParentEntry(parent)?.Reference.Element??(parent.Length==0?source.SourceMap.CanvasElement:null);
            Fragment Raw(SourceEntry entry,AxamlTextSpan span)=>new(entry.Text.Substring(span.Start,span.Length),entry.References.Values
                .Where(r=>models.ContainsKey(r.ControlId)&&r.Element.OpeningTagSpan.Start>=span.Start&&r.Element.OpeningTagSpan.Start<span.End)
                .ToDictionary(r=>r.ControlId,r=>r.Element.OpeningTagSpan.Start-span.Start));
            List<StructuralEdit> Plan(string parent,SourceEntry? entry,AxamlElementSyntax element)
            {
                var text=entry?.Text??source.OriginalText;
                var references=entry?.References??source.SourceMap.ByControlId;
                var edits=new List<StructuralEdit>();
                if(entry is not null)
                {
                    var reference=entry.Reference;var file=models[parent];var runtime=ToRuntimeModel(file);
                    var moved=(file.ParentId??"")!=(reference.ParentId??"");
                    if(file.Type!=reference.ControlType)throw new InvalidOperationException("Изменение типа существующего AXAML узла запрещено.");
                    if(reference.SnapshotValues.TryGetValue("@ChildLayoutMode",out var mode)&&mode!=file.ChildLayoutMode)throw new InvalidOperationException("Изменение layout semantics существующего узла запрещено.");
                    foreach(var p in AxamlRoundTripPropertyMap.PropertiesFor(element.LocalName))
                    {
                        if(moved&&Placement(p.Key))continue;
                        if(reference.SnapshotValues.TryGetValue(p.Key,out var before)&&p.Read(runtime)!=before
                            &&(!reference.Capability.CanEditProperty(p.Key)||!p.TryWrite(new DesignControlModel{UsesSourceLayout=true},p.Read(runtime))))
                            throw new InvalidOperationException("Небезопасное значение свойства: "+p.Key);
                    }
                    var scalar=new List<AxamlTextEdit>();AppendPropertyEdits(text,reference,runtime,scalar,moved);
                    edits.AddRange(scalar.Select(e=>new StructuralEdit(e.Start,e.Length,new(e.NewText,new()))));
                    if(moved)AppendPlacement(edits,element,file,Destination(file.ParentId??""));
                }
                var host=AxamlStructuralPolicy.Host(element)??element;
                if(!sourceChildren.TryGetValue(references,out var indexed))sourceChildren[references]=indexed=references.Values.OrderBy(r=>r.Element.ElementSpan.Start).ToLookup(r=>r.ParentId??"");
                var original=indexed[parent].Where(r=>r.Element.ElementSpan.Start>=host.ContentStart
                    &&r.Element.ElementSpan.End<=host.ElementSpan.End).OrderBy(r=>r.Element.ElementSpan.Start).ToArray();
                var desired=Desired(parent);
                var changed=!original.Select(r=>r.ControlId).SequenceEqual(desired);
                if(changed)
                {
                    // Validate against the source owning this fragment, including restored subtrees.
                    var local=new AxamlRoundTripDocument("",AxamlSyntaxDocument.Parse(text),new AxamlSourceMap(parent.Length==0?element:null),source.CapabilityReport);
                    foreach(var r in references.Values)local.SourceMap.Add(r);
                    var cap=AxamlStructuralPolicy.Inspect(local,parent);
                    if(AxamlStructuralPolicy.IsCollection(element.LocalName))
                    {
                        if(!cap.CanEditCollection)throw new InvalidOperationException("Source коллекция управляется выражением или имеет неподдерживаемую структуру.");
                        var itemType=element.LocalName+"Item";
                        if(desired.Any(id=>(entries.TryGetValue(id,out var item)?item.Reference.Element.LocalName:source.PendingTypes.GetValueOrDefault(id)??models[id].Type)!=itemType))
                            throw new InvalidOperationException("Source коллекция принимает только "+itemType+".");
                    }
                    var extraction=desired.SequenceEqual(original.Where(r=>desired.Contains(r.ControlId)).Select(r=>r.ControlId));
                    if(!cap.CanAddChild&&!extraction)throw new InvalidOperationException(element.LocalName+" (parent="+parent+"): "+cap.Reason);
                    foreach(var old in original.Where(r=>!models.ContainsKey(r.ControlId)))
                        if(AxamlImportStructureReport.Descendants(old.Element).Any(e=>source.CapabilityReport.Elements.Any(c=>c.Element==e&&c.Mode==AxamlElementCapabilityMode.Opaque)))
                            throw new InvalidOperationException("Удаление содержит opaque subtree; требуется явное редактирование source.");
                }
                for(var i=0;i<original.Length;i++)
                {
                    var old=original[i];
                    if(i<desired.Length&&old.ControlId==desired[i])edits.AddRange(Plan(desired[i],entries[desired[i]],old.Element));
                    else
                    {
                        var fragment=i<desired.Length?Render(desired[i],element,AxamlSyntaxDocument.GetLineIndent(text,old.Element.ElementSpan.Start)):new Fragment("",new());
                        edits.Add(new(old.Element.ElementSpan.Start,old.Element.ElementSpan.Length,fragment));
                    }
                }
                if(desired.Length>original.Length)
                {
                    var nl=source.Syntax.NewLine;var indent=AxamlSyntaxDocument.GetLineIndent(text,element.OpeningTagSpan.Start);
                    var childIndent=original.Length>0?AxamlSyntaxDocument.GetLineIndent(text,original[0].Element.OpeningTagSpan.Start):indent+source.Syntax.IndentUnit;
                    var value=new StringBuilder();var anchors=new Dictionary<string,int>();
                    foreach(var id in desired.Skip(original.Length))
                    {
                        var f=Render(id,element,childIndent);value.Append(nl).Append(childIndent);
                        foreach(var a in f.Starts)anchors[a.Key]=value.Length+a.Value;
                        value.Append(f.Text);
                    }
                    if(host.IsSelfClosing)
                    {
                        value.Insert(0,">");foreach(var id in anchors.Keys.ToArray())anchors[id]++;
                        value.Append(nl).Append(indent).Append("</").Append(host.Name).Append('>');
                        edits.Add(new(host.OpeningTagSpan.End-2,2,new(value.ToString(),anchors)));
                    }
                    else
                    {
                        var at=host.EndTagSpan.Start;var line=AxamlSyntaxDocument.GetLineStart(text,at);
                        if(string.IsNullOrWhiteSpace(text[line..at])){at=line;value.Remove(0,nl.Length);foreach(var id in anchors.Keys.ToArray())anchors[id]-=nl.Length;}
                        value.Append(nl);edits.Add(new(at,0,new(value.ToString(),anchors)));
                    }
                }
                return edits;
            }
            Fragment Render(string id,AxamlElementSyntax destination,string targetIndent)
            {
                if(!visited.Add(id))throw new InvalidOperationException("Повторная структурная ссылка.");
                if(entries.TryGetValue(id,out var entry))
                {
                    var moved=(models[id].ParentId??"")!=(entry.Reference.ParentId??"");
                    if(moved&&!AxamlStructuralPolicy.CompatibleNamespaceScope(entry.Reference.Element,destination))throw new InvalidOperationException("Namespace scope изменяется при переносе.");
                    var span=entry.Reference.Element.ElementSpan;var planned=Plan(id,entry,entry.Reference.Element);
                    return ApplyFragment(Raw(entry,span),span.Start,planned);
                }
                var file=models[id];var type=source.PendingTypes.GetValueOrDefault(id)??file.Type;
                if(!AxamlStructuralPolicy.CanCreate(type))throw new InvalidOperationException("Тип не поддерживает безопасную вставку: "+type);
                var raw=factory?.Invoke(file,type)??CreateFragment(file,source.Syntax.NewLine,"",source.Syntax.IndentUnit,true);
                // Only new descriptor output is adapted; imported XML is never serialized.
                var wrapper="<Window xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">";
                var parsed=AxamlSyntaxDocument.Parse(wrapper+raw+"</Window>");var node=parsed.Root.Children.Single();
                var generated=new List<StructuralEdit>();AppendPlacement(generated,node,file,destination,rawOffset:wrapper.Length);
                var namespaceText="";
                var scope=AxamlStructuralPolicy.Namespaces(destination);
                if(!scope.TryGetValue("xmlns",out var ns)||ns!="https://github.com/avaloniaui")namespaceText+=" xmlns=\"https://github.com/avaloniaui\"";
                if(!scope.TryGetValue("xmlns:x",out var x)||x!="http://schemas.microsoft.com/winfx/2006/xaml")namespaceText+=" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
                if(namespaceText.Length>0)generated.Add(new(node.OpeningTagSpan.Start+1+node.Name.Length-wrapper.Length,0,new(namespaceText,new())));
                var fragment=ApplyFragment(new(raw,new(){{id,raw.IndexOf('<')}}),0,generated);
                var lines=fragment.Text.Replace("\r\n","\n",StringComparison.Ordinal).Split('\n');
                var indentation=lines.Skip(1).Select(l=>l.TakeWhile(c=>c==' ').Count()).Where(n=>n>0).DefaultIfEmpty(4).Min();
                for(var i=1;i<lines.Length;i++)
                {var spaces=lines[i].TakeWhile(c=>c==' ').Count();lines[i]=targetIndent+string.Concat(Enumerable.Repeat(source.Syntax.IndentUnit,spaces/indentation))+lines[i].TrimStart(' ');}
                fragment=fragment with {Text=string.Join(source.Syntax.NewLine,lines)};
                if(Desired(id).Length>0)throw new InvalidOperationException("Вставьте контейнер, затем добавьте его дочерние элементы отдельным действием.");
                return fragment;
            }
            var root=source.SourceMap.CanvasElement??source.Syntax.Root;
            // The Window content root cannot be duplicated/reparented. Descend through its original slots.
            var edits=Plan("",null,root).GroupBy(e=>e.Start).Select(group=>
            {
                var same=group.OrderBy(e=>e.Length).ToArray();
                if(same.Length==1)return same[0];
                if(same.Count(e=>e.Length>0)>1)throw new InvalidOperationException("Пересекающиеся structural edits.");
                var text=new StringBuilder();var starts=new Dictionary<string,int>();
                foreach(var edit in same){foreach(var a in edit.Value.Starts)starts[a.Key]=text.Length+a.Value;text.Append(edit.Value.Text);}
                return new StructuralEdit(group.Key,same.Max(e=>e.Length),new(text.ToString(),starts));
            }).ToList();
            var full=new Fragment(source.OriginalText,source.SourceMap.Controls.Where(r=>models.ContainsKey(r.ControlId)).ToDictionary(r=>r.ControlId,r=>r.Element.OpeningTagSpan.Start));
            var result=ApplyFragment(full,0,edits);
            if(ApplyEdits(source.OriginalText,edits.Select(e=>new AxamlTextEdit(e.Start,e.Length,e.Value.Text)))!=result.Text)
                throw new InvalidOperationException("Text edit application differs from source projection.");
            DesignerDocumentFormat.Validate(result.Text,DesignerDocumentKind.AxamlRoundTrip,nameof(CreateStructuralPatch));
            _=AxamlSyntaxDocument.Parse(result.Text);
            var applied=AxamlPatchResult.Success(result.Text,edits.OrderByDescending(e=>e.Start).Select(e=>new AxamlTextEdit(e.Start,e.Length,e.Value.Text)).ToArray(),Array.Empty<AxamlRoundTripDiagnostic>());
            applied.ControlElementStarts=result.Starts;return applied;
        }
        catch(Exception error)when(error is InvalidOperationException or ArgumentException or FormatException or XmlException or AxamlSyntaxException)
        {return AxamlPatchResult.Unsafe(source.OriginalText,new[]{new AxamlRoundTripDiagnostic("AXAML_STRUCTURE_BLOCKED",AxamlDiagnosticSeverity.Warning,error.Message)});}
    }
    private static Fragment ApplyFragment(Fragment original,int offset,IEnumerable<StructuralEdit> changes)
    {
        var edits=changes.OrderBy(e=>e.Start).ThenBy(e=>e.Length).ToArray();var output=new StringBuilder();var anchors=new Dictionary<string,int>();var cursor=0;
        void Copy(int end)
        {foreach(var a in original.Starts.Where(a=>a.Value>=cursor&&a.Value<end))anchors[a.Key]=output.Length+a.Value-cursor;output.Append(original.Text,cursor,end-cursor);cursor=end;}
        foreach(var e in edits)
        {
            var at=e.Start-offset;if(at<cursor||at+e.Length>original.Text.Length)throw new InvalidOperationException("Пересекающиеся source spans.");
            Copy(at);foreach(var a in e.Value.Starts)anchors[a.Key]=output.Length+a.Value;
            output.Append(e.Value.Text);cursor=at+e.Length;
        }
        Copy(original.Text.Length);return new(output.ToString(),anchors);
    }
    private static void AppendPlacement(List<StructuralEdit> edits,AxamlElementSyntax element,DesignerControlFileModel model,AxamlElementSyntax? parent,int rawOffset=0)
    {
        if(parent is null)throw new InvalidOperationException("Нет поддерживаемого source parent.");
        var namespaces=AxamlStructuralPolicy.Namespaces(element);
        bool IsLayout(string name)
        {
            if(AxamlSyntaxDocument.GetLocalName(name) is not ("Canvas.Left" or "Canvas.Top" or "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan" or "DockPanel.Dock"))return false;
            var colon=name.IndexOf(':');
            return colon<0||namespaces.TryGetValue("xmlns:"+name[..colon],out var ns)&&ns=="https://github.com/avaloniaui";
        }
        if(element.Children.Any(e=>IsLayout(e.Name)))
            throw new InvalidOperationException("Attached property element при переносе пока read-only.");
        var type=parent.LocalName;
        string Num(double n)=>n.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
        foreach(var a in element.Attributes.Where(a=>IsLayout(a.Name)))
        {
            if(a.Value.TrimStart().StartsWith('{'))throw new InvalidOperationException("Attached layout expression нельзя удалять при переносе.");
            edits.Add(new(a.FullSpan.Start-rawOffset,a.FullSpan.Length,new("",new())));
        }
        var values=type=="Canvas"?" Canvas.Left=\""+Num(model.X)+"\" Canvas.Top=\""+Num(model.Y)+"\""
            :type=="Grid"?$" Grid.Row=\"{model.GridRow}\" Grid.Column=\"{model.GridColumn}\" Grid.RowSpan=\"{model.GridRowSpan}\" Grid.ColumnSpan=\"{model.GridColumnSpan}\""
            :type=="DockPanel"?" DockPanel.Dock=\""+LayoutPlacementProperties.GetDock(ToRuntimeModel(model))+"\"":"";
        if(values.Length>0)edits.Add(new(element.OpeningTagSpan.End-(element.IsSelfClosing?2:1)-rawOffset,0,new(values,new())));
    }
}
