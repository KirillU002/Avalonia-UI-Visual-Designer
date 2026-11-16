using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.ViewModels;
public partial class MainWindowViewModel
{
    internal string ImportedCollectionKind(DesignControlModel control,string property)=>IsAxamlRoundTripDocument&&property=="Items"
        ?AxamlSourceType(control) switch{"ComboBox"=>"ComboBoxItem","ListBox"=>"ListBoxItem",_=>""}:"";
    internal CollectionItemsContract ReadImportedCollection(DesignControlModel owner,string kind)=>new(){Items=GetChildControls(owner.Id).OrderBy(c=>c.StackOrder)
        .Select(c=>new StandardMenuItem{Id=c.Id,Kind=kind,Name=c.Name,Text=AxamlControlMetadata.Find(kind)!.LiteralProperties.First(p=>p.Key=="Content").Read(c),
            IsEnabled=!c.CustomProperties.Any(p=>p.Key=="IsEnabled"&&p.ValueJson=="false")}).ToList()};
    private bool WriteImportedCollection(CollectionEditorSession session,CollectionItemsContract data)
    {
        if(!session.IsCurrent||GetControl(session.OwnerId)is not {} owner||!GetAxamlStructuralCapability(owner.Id).CanEditCollection)return false;
        if(data.Items.Any(i=>i.Kind!=session.ItemKind||i.Children.Count>0))throw new FormatException("Импортированная коллекция поддерживает только простые Items.");
        var candidate=AxamlCandidate();var desired=data.Items.Select(i=>i.Id).ToHashSet();
        candidate.Controls.RemoveAll(c=>c.ParentId==owner.Id&&!desired.Contains(c.Id));
        var baseline=_axamlBufferSource??_activeAxamlRoundTripDocument!;
        var newIds=new List<string>();
        for(var i=0;i<data.Items.Count;i++)
        {
            var item=data.Items[i];var file=candidate.Controls.FirstOrDefault(c=>c.Id==item.Id);
            if(file is null)
            {
                var model=CreateDefaultControl("Group");model.Id=item.Id;model.ParentId=owner.Id;model.ChildLayoutMode=session.ItemKind=="ListBoxItem"?DesignerLayoutModes.Absolute:"";
                model.Name=GetUniqueAxamlControlName(session.ItemKind);model.Text=item.Text;
                file=ToControlFileModel(model);candidate.Controls.Add(file);
                baseline.PendingTypes[item.Id]=session.ItemKind;newIds.Add(item.Id);
            }
            else
            {
                if(item.Name.Length>0)file.Name=item.Name;
                var runtime=FromControlFileModel(file);var content=AxamlRoundTripPropertyMap.PropertiesFor(session.ItemKind).First(p=>p.Key=="Content");
                if(content.Read(runtime)!=item.Text)
                {
                    if(!content.TryWrite(runtime,item.Text))throw new FormatException("Content не является безопасным literal.");
                    var updated=ToControlFileModel(runtime);
                    file.Text=updated.Text;file.CustomProperties=updated.CustomProperties;
                }
            }
            file.StackOrder=i;
            if(file.CustomProperties.FirstOrDefault(p=>p.Key=="IsEnabled")is {} enabled)enabled.ValueJson=item.IsEnabled?"true":"false";
            else if(!item.IsEnabled)file.CustomProperties.Add(new(){Key="IsEnabled",ValueJson="false"});
        }
        var committed=CommitAxamlStructure(candidate,owner.Id);
        if(!committed){foreach(var id in newIds)baseline.PendingTypes.Remove(id);throw new FormatException(StatusText);}
        return true;
    }
    private IEnumerable<PropertyGridRowViewModel> ImportedCollectionRows(DesignControlModel control)
    {
        if(ImportedCollectionKind(control,"Items").Length==0)yield break;
        var reason=CollectionReadOnlyReason(control,"Items");
        yield return reason.Length==0?CreateActionRow(PropertyGridCategoryData,"Items","Items","",
            "Source-preserving Items: Add/Remove/Reorder для простых ComboBoxItem/ListBoxItem.","Изменить коллекцию…")
            :CreateReadOnlyRow(PropertyGridCategoryData,"Items","Items","Source collection",reason);
    }
}
