using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public sealed partial class AxamlPatchWriter
{
    internal static void ImportExpressions(AxamlSourceReference reference,DesignControlModel model)
    {
        foreach(var attribute in reference.Element.Attributes.Where(a=>a.Value.Contains('{')||a.Value.Contains("&#",StringComparison.Ordinal)))
        {
            var mapped=AxamlRoundTripPropertyMap.PropertiesFor(reference.Element.LocalName).FirstOrDefault(p=>p.AttributeNames.Contains(attribute.Name));
            if(mapped is null)continue;
            var key=mapped.PreferredAttributeName;var native=PropertyExpressions.Find(reference.Element.LocalName,key);
            var raw=WebUtility.HtmlDecode(attribute.Value);var value=new PropertyExpressionValue(PropertyExpressions.Classify(raw),raw);
            if(value.Kind is not (PropertyValueKind.Binding or PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource)
                ||native is null||!PropertyExpressions.Validate(native,value)||!CanEditExpression(reference,key,out _))continue;
            PropertyExpressions.Write(model,key,value);reference.ExpressionValues[key]=value;
        }
    }
    private static bool ExpressionManaged(AxamlSourceReference reference,DesignControlModel current,AxamlRoundTripProperty property)=>
        reference.ExpressionValues.ContainsKey(property.PreferredAttributeName)
        ||current.CustomProperties.Any(p=>p.Key==PropertyExpressions.Prefix+property.PreferredAttributeName);
    public static void RebaseExpressionSnapshots(AxamlRoundTripDocument source,DesignerDocumentFileModel acknowledged)
    {
        if(!source.SourceMap.Controls.Any(r=>r.ExpressionValues.Count>0))return;
        var controls=acknowledged.Controls.ToDictionary(c=>c.Id,StringComparer.Ordinal);
        foreach(var reference in source.SourceMap.Controls.Where(r=>r.ExpressionValues.Count>0))
        {
            if(!controls.TryGetValue(reference.ControlId,out var file))continue;
            var model=ToRuntimeModel(file);
            foreach(var key in reference.ExpressionValues.Keys)
                if(PropertyExpressions.TryRead(model.CustomProperties,key,out var expression)&&expression.Text==reference.ExpressionValues[key].Text
                    &&ExpressionProperty(reference,key)is {} property)
                    reference.SnapshotValues[property.Key]=property.Read(model);
        }
    }
    public static bool CanEditExpression(AxamlSourceReference reference, string nativeKey, out string reason)
    {
        reason = "Неоднозначный source mapping или сложное property element.";
        if (reference.Capability.Mode == AxamlElementCapabilityMode.Opaque) return false;
        var p = ExpressionProperty(reference, nativeKey);
        if (p is null || p.HasAmbiguousAttribute(reference.Element)
            || reference.Element.Children.Any(c => c.LocalName.EndsWith("." + nativeKey, StringComparison.Ordinal))) return false;
        for (var scope = reference.Element; scope is not null; scope = scope.Parent)
            if (scope.LocalName is "ControlTemplate" or "DataTemplate" || scope.LocalName.EndsWith(".Resources",StringComparison.Ordinal)
                || scope.LocalName.EndsWith(".Styles",StringComparison.Ordinal)) return false;
        var raw = WebUtility.HtmlDecode(p.ResolveExistingAttribute(reference.Element)?.Value ?? "");
        var kind = PropertyExpressions.Classify(raw);
        if (kind == PropertyValueKind.Binding && !PropertyExpressions.TryParseBinding(raw, out _)
            || kind is PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource && !PropertyExpressions.TryResource(raw,kind,out _)
            || kind is PropertyValueKind.Opaque or PropertyValueKind.TemplateBinding)
        { reason = "Compiled/Multi/TemplateBinding или неподдержанный expression сохраняется read-only."; return false; }
        if(kind==PropertyValueKind.Binding)
            for(var scope=reference.Element;scope is not null;scope=scope.Parent)
                if(string.Equals(PropertyExpressionProjection.XamlAttribute(scope,"CompileBindings"),"True",StringComparison.OrdinalIgnoreCase))
                {reason="Binding в compiled context требует типизированного контекста; сохраняется read-only.";return false;}
        reason = ""; return true;
    }
    private static AxamlRoundTripProperty? ExpressionProperty(AxamlSourceReference reference, string nativeKey) =>
        AxamlRoundTripPropertyMap.PropertiesFor(reference.Element.LocalName).FirstOrDefault(p => p.PreferredAttributeName == nativeKey);
    private static void ValidateExpressionChanges(AxamlSourceReference reference, DesignControlModel current)
    {
        foreach (var entry in current.CustomProperties.Where(p => p.Key.StartsWith(PropertyExpressions.Prefix,StringComparison.Ordinal)))
        {
            var key = entry.Key[PropertyExpressions.Prefix.Length..];
            var p = PropertyExpressions.Find(reference.Element.LocalName,key);
            if (p is null || !PropertyExpressions.TryRead(current.CustomProperties,key,out var value) || !PropertyExpressions.Validate(p,value)
                || !CanEditExpression(reference,key,out _)) throw new InvalidOperationException("Небезопасное выражение свойства: " + key);
            if (current.CustomProperties.Count(v=>v.Key==entry.Key)!=1) throw new InvalidOperationException("Duplicate expression property: " + key);
            var mapped=ExpressionProperty(reference,key)!;
            if(reference.ExpressionValues.ContainsKey(key)&&reference.SnapshotValues.TryGetValue(mapped.Key,out var backing)
                &&mapped.Read(current)!=backing)
                throw new InvalidOperationException("Scalar mutation conflicts with active expression: "+key);
            for (var scope = reference.Element; scope is not null; scope = scope.Parent)
            {
                if (value.Kind == PropertyValueKind.Binding && string.Equals(PropertyExpressionProjection.XamlAttribute(scope,"CompileBindings"),"True",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Binding в compiled context требует x:DataType; implicit conversion запрещена.");
            }
            if (value.Text.Contains("{x:Null}",StringComparison.Ordinal))
            {
                var scope = reference.Element;
                while(scope is not null && scope.FindAttribute("xmlns:x") is null) scope=scope.Parent;
                if(scope?.GetAttributeValue("xmlns:x")!="http://schemas.microsoft.com/winfx/2006/xaml")throw new InvalidOperationException("Namespace x для null недоступен.");
            }
        }
    }
    private static void AppendExpressionEdits(AxamlSourceReference reference, DesignControlModel current,
        ICollection<AxamlTextEdit> edits, ICollection<(string Name,string Value)> additions)
    {
        ValidateExpressionChanges(reference,current);
        var keys=current.CustomProperties.Where(p=>p.Key.StartsWith(PropertyExpressions.Prefix,StringComparison.Ordinal))
            .Select(p=>p.Key[PropertyExpressions.Prefix.Length..]).Concat(reference.ExpressionValues.Keys).Distinct();
        foreach(var key in keys)
        {
            var p=ExpressionProperty(reference,key)!;var attribute=p.ResolveExistingAttribute(reference.Element);
            if(!PropertyExpressions.TryRead(current.CustomProperties,key,out var value))
            {
                // Undo a literal -> expression conversion after a VS ACK. Existing
                // literal model fields are the preserved backing value, not defaults
                // guessed from a reimported expression.
                value=new(PropertyValueKind.Literal,p.Read(current));
                var native=PropertyExpressions.Find(reference.Element.LocalName,key);
                if(native is null||!PropertyExpressions.Validate(native,value)||!CanEditExpression(reference,key,out _))
                    throw new InvalidOperationException("Невозможно безопасно восстановить literal: "+key);
            }
            if(value.Kind==PropertyValueKind.Unset)
            { if(attribute is not null)edits.Add(new(attribute.FullSpan.Start,attribute.FullSpan.Length,"")); }
            else if(attribute is null)additions.Add((p.PreferredAttributeName,value.Text));
            else if(WebUtility.HtmlDecode(attribute.Value)!=value.Text)
                edits.Add(new(attribute.ValueSpan.Start,attribute.ValueSpan.Length,EscapeAttributeValue(value.Text,attribute.Quote)));
        }
    }
}
