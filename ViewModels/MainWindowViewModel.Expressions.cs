using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;

namespace FormDesigner.ViewModels;

public sealed record PropertyExpressionEditorContext(PropertyGridRowViewModel Row,AvaloniaPropertyInfo Property,
    PropertyExpressionValue InitialValue,IReadOnlyList<string> Paths,IReadOnlyList<string> ResourceKeys,string Notice);

public partial class MainWindowViewModel
{
    private void ConfigureExpressionRow(PropertyGridRowViewModel row,AvaloniaPropertyInfo property)
    {
        var initialKind=row.IsReadOnly?property.Editor==MetadataEditor.Collection?PropertyValueKind.Collection:PropertyValueKind.ComplexObject:PropertyValueKind.Literal;
        if(SelectedControl is {} selected&&_activeAxamlRoundTripDocument?.SourceMap.TryGet(selected.Id,out var selectedSource)==true
            &&selectedSource.Element.FindAttribute(property.Key)is {} sourceAttribute)
            initialKind=PropertyExpressions.Classify(WebUtility.HtmlDecode(sourceAttribute.Value));
        if (SelectedControl is not { } target || ActiveCollectionEditor is not null || !PropertyExpressions.CanTarget(property)
            || property.Key == "Content" && Controls.Any(c => c.ParentId == target.Id))
        {
            row.SetExpressionCapability(false, initialKind, "Для этого значения нет безопасного expression adapter.", null);
            return;
        }
        var nativeKey = property.Key;
        var allowed=true;var reason="";
        AxamlSourceReference? source=null;
        if(_activeAxamlRoundTripDocument is {} doc)
            allowed=doc.CapabilityReport.CanSafelyPatch&&doc.SourceMap.TryGet(target.Id,out source)
                &&AxamlPatchWriter.CanEditExpression(source,nativeKey,out reason);
        var entry = target.CustomProperties.Count == 0 ? null :
            target.CustomProperties.FirstOrDefault(p => p.Key == PropertyExpressions.Prefix + nativeKey);
        var stored = PropertyExpressions.TryReadJson(entry?.ValueJson, out var value);
        if (entry is not null && (!stored || !PropertyExpressions.Validate(property, value)))
        {
            row.SetExpressionCapability(false, PropertyValueKind.Opaque, "Некорректное выражение доступно только для чтения.", null);
            row.ProtectValue("Неизвестная версия/повреждённый expression payload сохранён без изменений.");
            return;
        }
        var attribute=source?.Element.Attributes.FirstOrDefault(a=>a.Name==nativeKey);
        var sourceText=attribute is null?null:WebUtility.HtmlDecode(attribute.Value);
        if(stored)
        {
            var kind=value.Kind;
            row.Refresh(kind==PropertyValueKind.Unset?property.DefaultText:value.Text,false,row.IsFavorite);
            row.SetPropertyMetadata(row.PropertyMetadata!,kind switch
            {
                PropertyValueKind.Binding=>PropertyGridValueSource.Binding,
                PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource=>PropertyGridValueSource.Resource,
                PropertyValueKind.Unset=>PropertyGridValueSource.RegistrationDefault,_=>PropertyGridValueSource.LocalValue
            },kind!=PropertyValueKind.Unset,
            "Явное редактирование через fx. Binding/resource сохранён; безопасная проекция не исполняет DataContext/converter и не вычисляет ресурс.");
            row.ProtectValue("Expression mode: изменяйте через fx; scalar editor не заменяет выражение автоматически.");
        }
        row.SetExpressionCapability(allowed,stored?value.Kind:PropertyExpressions.Classify(sourceText??row.Value),
            allowed?"Выберите строку и нажмите fx — Literal / Binding / Resource. Без исполнения пользовательского кода.":reason,
            stored&&value.Kind!=PropertyValueKind.Unset?()=>ResetPropertyExpression(row):null);
    }
    public PropertyExpressionEditorContext? OpenPropertyExpressionEditor(PropertyGridRowViewModel row)
    {
        if(!row.CanEditExpression||!CanApplyPropertyGridRow(row,"<open-expression>")||SelectedControl is not {} target
            ||row.PropertyMetadata?.Native is not {} property)return null;
        var initial=PropertyExpressions.TryRead(target.CustomProperties,property.Key,out var stored)?stored:new PropertyExpressionValue(PropertyValueKind.Literal,row.Value);
        var keys=new HashSet<string>(StringComparer.Ordinal);
        if(_activeAxamlRoundTripDocument?.SourceMap.TryGet(target.Id,out var source)==true)
        {
            for(var scope=source.Element;scope is not null;scope=scope.Parent)
            foreach(var resources in scope.Children.Where(c=>c.LocalName.EndsWith(".Resources",StringComparison.Ordinal)))
            foreach(var item in resources.Children.SelectMany(c=>c.LocalName=="ResourceDictionary"?c.Children.AsEnumerable():new[]{c}))
                if(PropertyExpressionProjection.XamlAttribute(item,"Key") is {} key&&PropertyExpressions.IsResourceKey(key))keys.Add(key);
        }
        else foreach(var f in typeof(ThemeResourceKeys).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.IsLiteral&&f.FieldType==typeof(string)))
                keys.Add((string)f.GetRawConstantValue()!);
        if (!HasImportedStyleScope && _formAppearance is {} appearance)
            foreach (var entry in appearance.Resources) keys.Add(entry.Key);
        // Existing schema only. No SQL, assembly loading, connection strings or credentials.
        var paths=BindingSources.SelectMany(s=>new[]{s.Path}.Concat(s.Fields.Select(f=>s.Path+"[0]."+f.Path)))
            .Where(p=>PropertyExpressions.TryBinding(new(p),out _,out _)).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        return new(row,property,initial,paths,keys.OrderBy(k=>k,StringComparer.Ordinal).ToArray(),
            "Binding не исполняется редактором. Неизвестный DataContext требует настройки в приложении; ICommand — ссылка на существующую команду, не Click handler. Ключи ресурсов — metadata, тип/значение не вычисляются.");
    }
    public bool CommitPropertyExpression(PropertyGridRowViewModel row,PropertyExpressionValue value,out string error)
    {
        error="Устаревшая строка или неподдержанный тип выражения.";
        if(!row.CanEditExpression||!CanApplyPropertyGridRow(row,value.Text)||IsBusy||IsAxamlStructureBusy
            ||SelectedControl is not {} target||row.PropertyMetadata?.Native is not {} property||!PropertyExpressions.Validate(property,value))return false;
        if(property.Key=="Content"&&Controls.Any(c=>c.ParentId==target.Id))return false;
        if(_activeAxamlRoundTripDocument is {} source)
        {
            if(!source.SourceMap.TryGet(target.Id,out var reference)||!AxamlPatchWriter.CanEditExpression(reference,property.Key,out error))return false;
            for(var scope=reference.Element;scope is not null;scope=scope.Parent)
                if(value.Kind==PropertyValueKind.Binding&&string.Equals(PropertyExpressionProjection.XamlAttribute(scope,"CompileBindings"),"True",StringComparison.OrdinalIgnoreCase))
                {error="Compiled context: нельзя создавать Binding без проверенного x:DataType.";return false;}
            if(value.Text.Contains("{x:Null}",StringComparison.Ordinal))
            {
                var scope=reference.Element;while(scope is not null&&scope.FindAttribute("xmlns:x")is null)scope=scope.Parent;
                if(scope?.GetAttributeValue("xmlns:x")!="http://schemas.microsoft.com/winfx/2006/xaml")
                {error="Для x:Null нужен существующий namespace x.";return false;}
            }
        }
        if(PropertyExpressions.TryRead(target.CustomProperties,property.Key,out var previous)&&previous==value){error="";return true;}
        if(_isEditingPropertyGrid)EndPropertyGridTextEdit();
        _lastHistoryMutationUtc=DateTime.MinValue;BeginUndoBatch();
        try
        {
            PropertyExpressions.Write(target,property.Key,value);
            // Explicit replacement of the legacy field binding, not a second binding
            // attached to the same target. The existing undo snapshot preserves it.
            if(property.Key is "Text" or "Content")
            {
                target.TextBindingPath="";
                if(value.Kind==PropertyValueKind.Binding&&PropertyExpressions.TryParseBinding(value.Text,out var binding)
                    &&binding.ElementName.Length==0&&binding.RelativeSource.Length==0)
                {
                    var sources=BindingSources.Where(s=>binding.Path==s.Path||binding.Path.StartsWith(s.Path+"[",StringComparison.Ordinal)
                        ||binding.Path.StartsWith(s.Path+".",StringComparison.Ordinal)).ToArray();
                    if(sources.Length==1)target.BindingSourceId=sources[0].Id;
                }
            }
            NotifyDesignerStateChanged();RebuildPropertyGrid("PropertyExpression");
        }
        finally{CommitUndoBatch();}
        error="";return true;
    }
    public bool ResetPropertyExpression(PropertyGridRowViewModel row)
    {
        if(_activeAxamlRoundTripDocument is not null)return CommitPropertyExpression(row,new(PropertyValueKind.Unset,""),out _);
        if(!row.CanEditExpression||!CanApplyPropertyGridRow(row,"<reset-expression>")||SelectedControl is not {} target||row.PropertyMetadata is not {} p)return false;
        if(_isEditingPropertyGrid)EndPropertyGridTextEdit();_lastHistoryMutationUtc=DateTime.MinValue;BeginUndoBatch();
        try
        {
            foreach(var value in target.CustomProperties.Where(v=>v.Key==PropertyExpressions.Prefix+p.Native.Key).ToArray())target.CustomProperties.Remove(value);
            NotifyDesignerStateChanged();RebuildPropertyGrid("ResetExpression");
        }
        finally{CommitUndoBatch();}
        return true;
    }
}
