using Avalonia;
using Avalonia.Controls;
using FormDesigner.DesignerSystem;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Infrastructure;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private AxamlRoundTripDocument? _axamlBufferSource;
    private bool _isAxamlFragmentExport;
    private bool _axamlSnapshotIsCurrent;
    private string? _axamlProjectionSnapshot;
    public bool IsAxamlStructureBusy { get; private set; }
    public AxamlStructuralCapability GetAxamlStructuralCapability(string? id)=>_activeAxamlRoundTripDocument is {} source
        ?AxamlStructuralPolicy.Inspect(source,id):new(false,false,false,false,false,false,false,false,"Не AXAML document.");
    private string AxamlSourceType(DesignControlModel control)=>_activeAxamlRoundTripDocument?.SourceMap.TryGet(control.Id,out var r)==true?r.Element.LocalName
        :(_axamlBufferSource??_activeAxamlRoundTripDocument)?.PendingTypes.GetValueOrDefault(control.Id)??control.Type;
    private string CreateAxamlDescriptorFragment(DesignerControlFileModel file,string type)
    {
        var model=FromControlFileModel(file);model.Type=type;
        var sb=new StringBuilder();var writer=new StringBuilderXamlWriter(sb);
        if(type is "ComboBoxItem" or "ListBoxItem")
            CollectionControlFactory.AppendItems(sb,file.ParentId,new[]{new StandardMenuItem{Id=file.Id,Kind=type,Name=file.Name,Text=file.Text}},0);
        else
        {
            var services=new DesignerServiceProvider().Add<IBuiltInXamlBridge>(new BuiltInXamlBridge(this));
            var context=new XamlExportContext(services,_=>Array.Empty<IDesignControlNode>(),(_,_,_,_)=>false,
                new Dictionary<string,BindingSourceMetadata>());
            var previous=_isAxamlFragmentExport;_isAxamlFragmentExport=true;
            try{_registry.GetRequiredControl(type=="Canvas"?"Group":type).AppendXaml(writer,new DesignControlNodeAdapter(model),0,context);}
            finally{_isAxamlFragmentExport=previous;}
        }
        return sb.ToString().TrimEnd();
    }
    private AxamlRoundTripDocument ImportAxamlProjection(AxamlRoundTripDocument baseline,DesignerDocumentFileModel model,AxamlPatchResult patch)
    {
        using var stage=FormDesigner.Services.LayoutDropProfile.Stage("AXAML.StructuralSourceMapUpdate");
        var syntax=patch.ParsedSyntax??AxamlSyntaxDocument.Parse(patch.PatchedText);
        var elements=AxamlImportStructureReport.Descendants(syntax.Root).ToDictionary(e=>e.OpeningTagSpan.Start);
        var identities=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var c in model.Controls.Where(c=>!string.IsNullOrWhiteSpace(c.Name)))identities[c.Name]=c.Id;
        if(patch.ControlElementStarts.Count>0)
        {
            foreach(var pair in patch.ControlElementStarts)
                if(elements.TryGetValue(pair.Value,out var element))identities["@path:"+AxamlImportStructureReport.PathOf(element)]=pair.Key;
        }
        else foreach(var r in baseline.SourceMap.Controls.Where(r=>model.Controls.Any(c=>c.Id==r.ControlId)))
        {
            var start=r.Element.OpeningTagSpan.Start;
            var moved=start+patch.Edits.Where(e=>e.Start<start&&e.End<=start).Sum(e=>e.NewText.Length-e.Length);
            if(elements.TryGetValue(moved,out var element))identities["@path:"+AxamlImportStructureReport.PathOf(element)]=r.ControlId;
        }
        var imported=_axamlImportService.ImportParsed(syntax,baseline.SourcePath,identities);
        if(!imported.CapabilityReport.CanOpen||imported.Document.Controls.Count!=model.Controls.Count
            ||!imported.Document.Controls.Select(c=>c.Id).ToHashSet().SetEquals(model.Controls.Select(c=>c.Id)))
            throw new InvalidOperationException("Структурная операция не сохранила однозначные source identities.");
        var expected=model.Controls.ToDictionary(c=>c.Id);
        if(imported.Document.Controls.Any(c=>(c.ParentId??"")!=(expected[c.Id].ParentId??"")))throw new InvalidOperationException("Source ParentId не соответствует структурной операции.");
        imported.RoundTripDocument.SetTextEncoding(baseline.TextEncoding);
        return imported.RoundTripDocument;
    }
    private bool CommitAxamlStructure(DesignerDocumentFileModel candidate,string? selectedId)
    {
        if(IsAxamlStructureBusy){StatusText="Предыдущее изменение структуры ещё выполняется.";return false;}
        IsAxamlStructureBusy=true;
        var ownsBusy=!IsBusy;
        if(ownsBusy)BeginBusy("Изменение AXAML","Проверяем структуру и обновляем source map.");
        try{return CommitAxamlStructureCore(candidate,selectedId);}
        finally{IsAxamlStructureBusy=false;if(ownsBusy)EndBusy();}
    }
    private bool CommitAxamlStructureCore(DesignerDocumentFileModel candidate,string? selectedId)
    {
        if(_activeAxamlRoundTripDocument is null)return false;
        var baseline=_axamlBufferSource??_activeAxamlRoundTripDocument;
        var patch=_axamlPatchWriter.CreatePatch(baseline,candidate,fragmentFactory:CreateAxamlDescriptorFragment);
        if(!patch.CanApply){StatusText=string.Join("; ",patch.Diagnostics.Select(d=>d.Details));return false;}
        AxamlRoundTripDocument projection;
        try {projection=ImportAxamlProjection(baseline,candidate,patch);}
        catch(InvalidOperationException error){StatusText=error.Message;return false;}
        // Descriptor attributes are explicit local values. Seed their existing
        // metadata storage on new models so the next patch cannot mistake them
        // for Reset merely because legacy DTO fields had no CustomProperties.
        var existingIds=Controls.Select(c=>c.Id).ToHashSet();
        foreach(var file in candidate.Controls.Where(c=>!existingIds.Contains(c.Id)))
        {
            var reference=projection.SourceMap.ByControlId[file.Id];var runtime=FromControlFileModel(file);
            foreach(var property in AxamlRoundTripPropertyMap.PropertiesFor(reference.Element.LocalName)
                .Where(p=>p.Metadata is not null&&(p.ResolveExistingAttribute(reference.Element)is not null||reference.PropertyElements.ContainsKey(p.Key))))
                if(!property.TryWrite(runtime,reference.SnapshotValues[property.Key])){StatusText="Невозможно восстановить literal нового source узла: "+property.Key;return false;}
            file.CustomProperties=ToControlFileModel(runtime).CustomProperties;
        }
        _axamlBufferSource=baseline;
        // Pending fragments are also undo evidence before the first VS acknowledgement.
        foreach(var r in projection.SourceMap.Controls.Where(r=>!baseline.SourceMap.ByControlId.ContainsKey(r.ControlId)))
            baseline.RemovedSources.TryAdd(r.ControlId,new(projection.OriginalText,r,projection.SourceMap.ByControlId));
        BeginGridEditorUndoBatch();
        var suspended=_isHistorySuspended;_isHistorySuspended=true;_isStructureTreeRefreshSuspended=true;
        try
        {
            _activeAxamlRoundTripDocument=projection;
            var ids=candidate.Controls.Select(c=>c.Id).ToHashSet();
            foreach(var old in Controls.Where(c=>!ids.Contains(c.Id)).ToArray())Controls.Remove(old);
            foreach(var file in candidate.Controls)
            {
                if(GetControl(file.Id)is not {} model){model=FromControlFileModel(file);model.UsesSourceLayout=true;TrackControlDocument(model,ActiveDocumentId);Controls.Add(model);}
                else
                {
                    model.ParentId=file.ParentId;model.StackOrder=file.StackOrder;model.X=file.X;model.Y=file.Y;
                    model.GridRow=file.GridRow;model.GridColumn=file.GridColumn;model.GridRowSpan=file.GridRowSpan;model.GridColumnSpan=file.GridColumnSpan;
                    model.Name=file.Name;model.Text=file.Text;
                    if(JsonSerializer.Serialize(model.CustomProperties)!=JsonSerializer.Serialize(file.CustomProperties))
                    {model.CustomProperties.Clear();foreach(var value in file.CustomProperties)model.CustomProperties.Add(new(){Key=value.Key,ValueJson=value.ValueJson});}
                }
            }
            if(selectedId is not null&&GetControl(selectedId)is {} selected)SelectSingleControl(selected);else if(SelectedControl is {} old&&!ids.Contains(old.Id))ClearSelection();
        }
        finally{_isHistorySuspended=suspended;_isStructureTreeRefreshSuspended=false;}
        RebuildStructureTree();NotifyDesignerStateChanged();CommitGridEditorUndoBatch();
        _axamlProjectionSnapshot=_currentSnapshot;
        OnPropertyChanged(nameof(ActiveAxamlCapabilityReport));return true;
    }
    private DesignerDocumentFileModel AxamlCandidate()=>CreateDocumentFileModel();
    private string GetUniqueAxamlControlName(string type)
    {
        var name=GetUniqueControlName(type);
        var used=AxamlImportStructureReport.Descendants(_activeAxamlRoundTripDocument!.Syntax.Root)
            .SelectMany(e=>e.Attributes.Where(a=>AxamlSyntaxDocument.GetLocalName(a.Name)=="Name").Select(a=>a.Value)).ToHashSet();
        var stem=name;var suffix=2;while(used.Contains(name))name=stem+suffix++;
        return name;
    }
    public DesignControlModel? InsertAxamlControl(string type,string? parentId,double x,double y,int? index=null)
    {
        if(type=="Group")type="Canvas";
        if(_activeAxamlRoundTripDocument is null||!AxamlStructuralPolicy.CanCreate(type))return null;
        var cap=GetAxamlStructuralCapability(parentId);
        if(!cap.CanAddChild){StatusText=cap.Reason;return null;}
        var parent=GetControl(parentId);var parentType=parent is null?"Canvas":AxamlSourceType(parent);
        if(AxamlStructuralPolicy.IsCollection(parentType)&&type!=(parentType=="ComboBox"?"ComboBoxItem":"ListBoxItem"))
        {StatusText="Коллекция принимает только поддерживаемый Item type.";return null;}
        var model=CreateDefaultControl(type is "Canvas" or "ComboBoxItem" or "ListBoxItem"?"Group":type);
        model.Name=GetUniqueAxamlControlName(type);model.ParentId=parentId??"";
        if(type is "Border" or "StackPanel" or "Grid" or "Canvas")model.Text="";
        if(type=="Canvas")model.Type="Group";
        model.UsesSourceLayout=true;model.ChildLayoutMode=AxamlStructuralPolicy.IsPanel(type)?SourceLayoutMode(type):type is "Border" or "ListBoxItem"?DesignerLayoutModes.Absolute:"";
        var candidate=AxamlCandidate();var file=ToControlFileModel(model);
        PlaceAxamlCandidate(file,parentId,x,y,candidate,index);
        candidate.Controls.Add(file);
        var baseline=_axamlBufferSource??_activeAxamlRoundTripDocument;baseline.PendingTypes[model.Id]=type;
        if(!CommitAxamlStructure(candidate,model.Id)){baseline.PendingTypes.Remove(model.Id);return null;}
        return GetControl(model.Id);
    }
    public bool RemoveAxamlControls(IEnumerable<string> ids)
    {
        var remove=new HashSet<string>(ids);var candidate=AxamlCandidate();
        if(remove.Any(id=>!GetAxamlStructuralCapability(id).CanRemove)){StatusText="Удаление этого source узла не поддерживается.";return false;}
        bool changed;do{changed=false;foreach(var c in candidate.Controls.Where(c=>remove.Contains(c.ParentId??"")))changed|=remove.Add(c.Id);}while(changed);
        candidate.Controls.RemoveAll(c=>remove.Contains(c.Id));return CommitAxamlStructure(candidate,null);
    }
    public bool ReorderAxamlControl(string id,int index)
    {
        if(!GetAxamlStructuralCapability(id).CanReorder)return false;
        var candidate=AxamlCandidate();var model=candidate.Controls.Single(c=>c.Id==id);
        var siblings=candidate.Controls.Where(c=>c.ParentId==model.ParentId).OrderBy(c=>c.StackOrder).ToList();
        if(siblings.IndexOf(model)==Math.Clamp(index,0,siblings.Count-1))return false;
        siblings.Remove(model);siblings.Insert(Math.Clamp(index,0,siblings.Count),model);
        for(var i=0;i<siblings.Count;i++)siblings[i].StackOrder=i;
        return CommitAxamlStructure(candidate,id);
    }
    public bool ReparentAxamlControl(string id,string? parentId,double x,double y,int? index=null)
    {
        if(!GetAxamlStructuralCapability(id).CanReparent||!GetAxamlStructuralCapability(parentId).CanAddChild)return false;
        if(GetControl(parentId)is {} target&&AxamlStructuralPolicy.IsCollection(AxamlSourceType(target)))
        {StatusText="Для source Items используйте Collection Editor; перенос между панелью и Items не поддерживается.";return false;}
        var candidate=AxamlCandidate();var model=candidate.Controls.Single(c=>c.Id==id);
        PlaceAxamlCandidate(model,parentId,x,y,candidate,index);
        return CommitAxamlStructure(candidate,id);
    }
    private void PlaceAxamlCandidate(DesignerControlFileModel model,string? parentId,double x,double y,DesignerDocumentFileModel candidate,int? index)
    {
        var parent=GetControl(parentId);var type=parent is null?"Canvas":AxamlSourceType(parent);
        if(parent is not null&&_activeAxamlRoundTripDocument is {} layoutSource&&!AxamlLayoutBounds.ContainsKey(parent.Id))
            AxamlLayoutBounds=AxamlLayoutProjection.Arrange(layoutSource,Controls,DesignWidth,DesignHeight);
        var sameParent=(model.ParentId??"")==(parentId??"");
        var oldDock=model.CustomProperties.FirstOrDefault(p=>p.Key==LayoutPlacementProperties.DockKey)?.ValueJson;
        var rowSpan=model.GridRowSpan;var columnSpan=model.GridColumnSpan;
        model.ParentId=parentId??"";
        if(!sameParent)
        {
            model.X=model.Y=0;model.GridRow=model.GridColumn=0;model.GridRowSpan=model.GridColumnSpan=1;
            model.CustomProperties.RemoveAll(p=>p.Key is "DockPanel.Dock" or "Canvas.Left" or "Canvas.Top" or "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan");
            if(type=="DockPanel"&&oldDock is not null)model.CustomProperties.Add(new(){Key=LayoutPlacementProperties.DockKey,ValueJson=oldDock});
        }
        var local=ToLocalPosition(parentId,x,y);
        if(type=="Canvas"){model.X=Math.Max(0,local.X);model.Y=Math.Max(0,local.Y);}
        if(type=="Grid"&&parent is not null)
        {
            if(_activeAxamlRoundTripDocument is {} source&&!source.MeasuredGridTracks.ContainsKey(parent.Id))
                _=AxamlLayoutProjection.Arrange(source,Controls,DesignWidth,DesignHeight);
            if(_activeAxamlRoundTripDocument?.MeasuredGridTracks.TryGetValue(parent.Id,out var tracks)==true)
            {model.GridRow=HitSourceTrack(tracks.Rows,local.Y);model.GridColumn=HitSourceTrack(tracks.Columns,local.X);
                model.GridRowSpan=Math.Clamp(rowSpan,1,Math.Max(1,tracks.Rows.Count-model.GridRow));model.GridColumnSpan=Math.Clamp(columnSpan,1,Math.Max(1,tracks.Columns.Count-model.GridColumn));}
        }
        var siblings=candidate.Controls.Where(c=>c.Id!=model.Id&&c.ParentId==model.ParentId).OrderBy(c=>c.StackOrder).ToList();
        var at=index??siblings.Count;
        if(index is null&&type=="StackPanel")
        {
            var horizontal=(parent?.LayoutOrientation??"Vertical")=="Horizontal";
            at=siblings.FindIndex(c=>{var live=GetControl(c.Id);if(live is null)return false;var bounds=GetAbsolutePosition(live);var size=AxamlLayoutBounds.TryGetValue(c.Id,out var measured)?measured.Size:new Size(live.Width,live.Height);return horizontal?x<bounds.X+size.Width/2:y<bounds.Y+size.Height/2;});
            if(at<0)at=siblings.Count;
        }
        siblings.Insert(Math.Clamp(at,0,siblings.Count),model);for(var i=0;i<siblings.Count;i++)siblings[i].StackOrder=i;
    }
    private static int HitSourceTrack(IReadOnlyList<double> sizes,double value){var edge=0d;for(var i=0;i<sizes.Count;i++){edge+=sizes[i];if(value<edge)return i;}return Math.Max(0,sizes.Count-1);}
    private static string SourceLayoutMode(string type)=>type switch{"Grid"=>DesignerLayoutModes.Grid,"StackPanel"=>DesignerLayoutModes.Stack,"WrapPanel"=>DesignerLayoutModes.Flex,"DockPanel"=>DesignerLayoutModes.Dock,_=>DesignerLayoutModes.Absolute};
    private void RefreshAxamlStructureAfterHistory(DesignerDocumentFileModel model)
    {
        if(_axamlBufferSource is null)return;
        var patch=_axamlPatchWriter.CreatePatch(_axamlBufferSource,model,fragmentFactory:CreateAxamlDescriptorFragment);
        if(!patch.CanApply)throw new InvalidOperationException("Undo AXAML отклонён: "+string.Join("; ",patch.Diagnostics.Select(d=>d.Details)));
        _activeAxamlRoundTripDocument=ImportAxamlProjection(_axamlBufferSource,model,patch);
    }
    private void AcknowledgeAxamlStructure(string path,string text,string? snapshot)
    {
        using var stage=FormDesigner.Services.LayoutDropProfile.Stage("AXAML.AcknowledgeSynchronization");
        var baseline=_axamlBufferSource!;
        var current=_activeAxamlRoundTripDocument!;
        AxamlRoundTripDocument updated;
        var exactCurrent=_axamlProjectionSnapshot==_currentSnapshot
            &&(snapshot is null||snapshot==_currentSnapshot)&&text==current.OriginalText;
        if(exactCurrent)
        {
            using var reused=FormDesigner.Services.LayoutDropProfile.Stage("AXAML.AcknowledgeReuse");
            updated=current;
        }
        else
        {
            var model=snapshot is null?CreateDocumentFileModel():JsonSerializer.Deserialize<DesignerDocumentFileModel>(snapshot,JsonOptions)!;
            var patch=_axamlPatchWriter.CreatePatch(baseline,model,fragmentFactory:CreateAxamlDescriptorFragment);
            if(!patch.CanApply||patch.PatchedText!=text)throw new InvalidOperationException("ACK не соответствует структурному patch.");
            updated=ImportAxamlProjection(baseline,model,patch);
        }
        // Keep only evidence absent from the acknowledged map. Present entries are
        // always rendered from that map, so retaining older whole maps is redundant.
        foreach(var r in baseline.SourceMap.Controls.Where(r=>!updated.SourceMap.ByControlId.ContainsKey(r.ControlId)))
            updated.RemovedSources[r.ControlId]=new(baseline.OriginalText,r,baseline.SourceMap.ByControlId);
        foreach(var pair in baseline.RemovedSources)
            if(!updated.SourceMap.ByControlId.ContainsKey(pair.Key))updated.RemovedSources[pair.Key]=pair.Value;
        foreach(var pair in baseline.PendingTypes)
            if(!updated.SourceMap.ByControlId.ContainsKey(pair.Key))updated.PendingTypes[pair.Key]=pair.Value;
        AxamlRoundTripDocument working=updated;
        if(!exactCurrent)
        {
            var live=CreateDocumentFileModel();var pending=_axamlPatchWriter.CreatePatch(updated,live,fragmentFactory:CreateAxamlDescriptorFragment);
            if(!pending.CanApply)throw new InvalidOperationException("Изменения после ACK требуют перезагрузки документа.");
            working=ImportAxamlProjection(updated,live,pending);
        }
        if(updated.SourcePath!=path)updated.ReplaceSource(path,updated.Syntax,updated.SourceMap,updated.CapabilityReport);
        if(working.SourcePath!=path)working.ReplaceSource(path,working.Syntax,working.SourceMap,working.CapabilityReport);
        _axamlBufferSource=updated;_activeAxamlRoundTripDocument=working;
        _axamlProjectionSnapshot=_currentSnapshot;
        if(!exactCurrent)RebuildPropertyGrid("AxamlStructureAcknowledged");
        MarkDocumentSaved(path,snapshot);OnPropertyChanged(nameof(ActiveAxamlCapabilityReport));
    }
}
