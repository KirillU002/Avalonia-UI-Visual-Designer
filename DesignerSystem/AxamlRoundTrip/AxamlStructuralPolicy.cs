using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

// Capabilities over the existing syntax/source map, never a second parser.
public static class AxamlStructuralPolicy
{
    public static bool IsPanel(string type) => type is "Grid" or "Canvas" or "StackPanel" or "DockPanel" or "WrapPanel";
    public static bool IsCollection(string type) => type is "ComboBox" or "ListBox";
    public static bool CanInsertToolboxItem(string type) => CanCreate(type == "Group" ? "Canvas" : type);

    public static bool CanCreate(string type) => type is "Button" or "TextBox" or "TextBlock" or "CheckBox" or "Border" or "StackPanel" or "Grid" or "Canvas" or "ComboBoxItem" or "ListBoxItem";
    public static AxamlElementSyntax? Host(AxamlElementSyntax parent)
    {
        var metadata=AxamlControlMetadata.Find(parent.LocalName);
        if (metadata is null) return null;
        var wrappers=parent.Children.Where(metadata.IsChildProperty).ToArray();
        return wrappers.Length==0 ? parent : wrappers.Length==1 && !parent.Children.Any(c=>!AxamlImportStructureReport.IsPropertyElement(c)) ? wrappers[0] : null;
    }
    public static AxamlStructuralCapability Inspect(AxamlRoundTripDocument document, string? id)
    {
        var element=string.IsNullOrEmpty(id) ? document.SourceMap.CanvasElement
            : document.SourceMap.TryGet(id,out var reference) ? reference.Element : null;
        if(element is null || !document.CapabilityReport.CanSafelyPatch) return Denied("Нет однозначного source mapping поддерживаемого контейнера.");
        var parent=element.Parent;
        if(parent is not null && AxamlImportStructureReport.IsPropertyElement(parent)) parent=parent.Parent;
        var host=Host(element);
        var collection=IsCollection(element.LocalName);
        var supported=IsPanel(element.LocalName)||collection;
        var conflicting=collection && new[]{"ItemsSource","Items","ItemTemplate","SelectedItem","SelectedValue"}.Any(k=>AxamlControlMetadata.HasSourceValue(element,k)
            && !(k=="Items" && element.Children.Any(c=>AxamlControlMetadata.Find(element.LocalName)!.IsChildProperty(c))));
        var mapped=document.SourceMap.Controls.Select(r=>r.Element).ToHashSet();
        var unknown=host is not null && host.Children.Any(c=>!AxamlImportStructureReport.IsPropertyElement(c)&&!mapped.Contains(c));
        var simpleItems=!collection||host is not null&&host.Children.Where(c=>!AxamlImportStructureReport.IsPropertyElement(c))
            .All(c=>c.LocalName==element.LocalName+"Item"&&c.Children.Count==0);
        var text=host is null||host.IsSelfClosing?"":document.OriginalText.Substring(host.ContentStart,host.EndTagSpan.Start-host.ContentStart);
        // Text/CDATA and processing instructions have ambiguous ownership; comments stay at their original slots.
        var residual=host is null?"":text;
        if(host is not null && !host.IsSelfClosing)
            foreach(var child in host.Children.OrderByDescending(c=>c.ElementSpan.Start)) residual=residual.Remove(child.ElementSpan.Start-host.ContentStart,child.ElementSpan.Length);
        residual=System.Text.RegularExpressions.Regex.Replace(residual,@"<!--[\s\S]*?-->","");
        var add=supported&&host is not null&&!conflicting&&!unknown&&simpleItems&&string.IsNullOrWhiteSpace(residual)
            && !element.Attributes.Any(a=>a.Name is "Children" or "Content");
        var parentCollection=parent is not null&&IsCollection(parent.LocalName);
        var parentReference=parent is null?null:document.SourceMap.Controls.FirstOrDefault(r=>ReferenceEquals(r.Element,parent));
        var parentEditable=parentReference is not null?Inspect(document,parentReference.ControlId).CanAddChild
            :parent is not null&&ReferenceEquals(parent,document.SourceMap.CanvasElement)&&Inspect(document,null).CanAddChild;
        var removable=!string.IsNullOrEmpty(id)&&parent is not null&&(IsPanel(parent.LocalName)||parentCollection)&&(!parentCollection||parentEditable);
        var reason=add?"":conflicting?"ItemsSource/Selection/Template управляет коллекцией; локальное структурное редактирование запрещено.":"Неподдерживаемое или неоднозначное содержимое; исходный XML сохранён.";
        return new(add,removable,removable&&parentEditable,removable&&!parentCollection,add,add&&collection,removable&&parentEditable,removable,reason);
    }
    private static AxamlStructuralCapability Denied(string reason)=>new(false,false,false,false,false,false,false,false,reason);
    internal static Dictionary<string,string> Namespaces(AxamlElementSyntax element)
    {
        var scope=new Dictionary<string,string>(StringComparer.Ordinal);
        for(var e=element;e is not null;e=e.Parent)
            foreach(var a in e.Attributes.Where(a=>a.Name=="xmlns"||a.Name.StartsWith("xmlns:",StringComparison.Ordinal)))scope.TryAdd(a.Name,a.Value);
        return scope;
    }
    internal static bool CompatibleNamespaceScope(AxamlElementSyntax element,AxamlElementSyntax target)
    {
        var destination=Namespaces(target);
        string? Inherited(AxamlElementSyntax? e,string key){for(;e is not null;e=e.Parent)if(e.GetAttributeValue(key)is {} v)return v;return null;}
        foreach(var key in new[]{"xml:space"})
            if(element.FindAttribute(key)is null&&Inherited(element.Parent,key)!=Inherited(target,key))return false;
        string? Directive(AxamlElementSyntax e,string key)=>e.Attributes.FirstOrDefault(a=>
        {
            var colon=a.Name.IndexOf(':');
            return colon>0&&a.Name[(colon+1)..]==key&&Namespaces(e).GetValueOrDefault("xmlns:"+a.Name[..colon])=="http://schemas.microsoft.com/winfx/2006/xaml";
        })?.Value;
        string? InheritedDirective(AxamlElementSyntax? e,string key){for(;e is not null;e=e.Parent)if(Directive(e,key)is {} value)return value;return null;}
        foreach(var key in new[]{"DataType","CompileBindings"})
            if(Directive(element,key)is null&&InheritedDirective(element.Parent,key)!=InheritedDirective(target,key))return false;
        // A declaration on the moved element travels with it; ancestor aliases must retain meaning.
        return element.Parent is null || Namespaces(element.Parent).All(p=>element.FindAttribute(p.Key)is not null
            || destination.TryGetValue(p.Key,out var value)&&value==p.Value);
    }
}
