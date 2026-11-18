using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace FormDesigner.DesignerSystem.Metadata;

public static class PropertyExpressionProjection
{
    // One explicit overlay on the existing generator's output, using its existing
    // syntax/span machinery. Never serializes a complete imported document.
    public static string ApplyGenerated(string text,IEnumerable<DesignControlModel> models,Func<DesignControlModel,string> name, bool safePreview=false)
    {
        var targets=models.Where(c=>c.CustomProperties.Any(p=>p.Key.StartsWith(PropertyExpressions.Prefix,StringComparison.Ordinal))).ToArray();
        if(targets.Length==0)return text;
        var syntax=AxamlSyntaxDocument.Parse(text);var nodes=Descendants(syntax.Root).ToArray();var edits=new List<AxamlTextEdit>();
        foreach(var model in targets)
        {
            var matching=nodes.Where(e=>(e.GetAttributeValue("x:Name")??e.GetAttributeValue("Name"))==name(model)).ToArray();
            if(matching.Length!=1)throw new InvalidOperationException("Expression export target is ambiguous: "+model.Name);
            var node=matching[0];var additions=new List<string>();
            foreach(var entry in model.CustomProperties.Where(p=>p.Key.StartsWith(PropertyExpressions.Prefix,StringComparison.Ordinal)))
            {
                var key=entry.Key[PropertyExpressions.Prefix.Length..];var p=PropertyExpressions.Find(model.Type,key);
                if(p is null||!PropertyExpressions.TryRead(model.CustomProperties,key,out var value)||!PropertyExpressions.Validate(p,value))
                    throw new InvalidOperationException("Invalid property expression: "+model.Name+"."+key);
                if(node.Children.Any(c=>c.LocalName.EndsWith("."+key,StringComparison.Ordinal))
                    ||key=="Content"&&node.Children.Any(c=>!c.LocalName.Contains('.')))throw new InvalidOperationException("Expression conflicts with nested Content: "+model.Name+"."+key);
                var attribute=node.FindAttribute(key);
                if(value.Kind==PropertyValueKind.Unset||safePreview&&value.Kind!=PropertyValueKind.Literal)
                { if(attribute is not null)edits.Add(new(attribute.FullSpan.Start,attribute.FullSpan.Length,""));continue; }
                if(!safePreview)
                {
                    var resourceKey=value.Kind is PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource
                        && PropertyExpressions.TryResource(value.Text,value.Kind,out var keyText)?keyText:null;
                    var converterKey=value.Kind==PropertyValueKind.Binding&&PropertyExpressions.TryParseBinding(value.Text,out var b)?b.ConverterKey:"";
                    if(resourceKey is not null||converterKey.Length>0)
                    {
                        var referencedKey=resourceKey??converterKey;
                        var resource=nodes.FirstOrDefault(e=>e.GetAttributeValue("x:Key")==referencedKey);
                        if(resource is null&&(value.Kind==PropertyValueKind.StaticResource||converterKey.Length>0))
                            throw new InvalidOperationException("StaticResource/Converter key не объявлен в generated scope: "+referencedKey);
                        if(resource is not null&&converterKey.Length>0)
                            throw new InvalidOperationException("Converter требует reviewed object/resource adapter: "+referencedKey);
                        if(resource?.LocalName=="SolidColorBrush"&&!p.PropertyType.IsAssignableFrom(typeof(Avalonia.Media.SolidColorBrush)))
                            throw new InvalidOperationException("Несовместимый тип Brush resource: "+model.Name+"."+p.Key);
                    }
                }
                var escaped=Escape(value.Text,attribute?.Quote??'"');
                if(attribute is not null)edits.Add(new(attribute.ValueSpan.Start,attribute.ValueSpan.Length,escaped));
                else additions.Add(" "+key+"=\""+escaped+"\"");
            }
            if(additions.Count>0)edits.Add(new(node.OpeningTagSpan.End-(node.IsSelfClosing?2:1),0,string.Concat(additions)));
        }
        var result=AxamlPatchWriter.ApplyEdits(text,edits);_ = AxamlSyntaxDocument.Parse(result);_ = System.Xml.Linq.XDocument.Parse(result,System.Xml.Linq.LoadOptions.PreserveWhitespace);return result;
    }
    public static string? XamlAttribute(AxamlElementSyntax element,string localName)
    {
        foreach(var attribute in element.Attributes)
        {
            var colon=attribute.Name.IndexOf(':');if(colon<1||attribute.Name[(colon+1)..]!=localName)continue;
            var declaration="xmlns:"+attribute.Name[..colon];
            for(var scope=element;scope is not null;scope=scope.Parent)
                if(scope.GetAttributeValue(declaration)is {} uri)
                {if(uri=="http://schemas.microsoft.com/winfx/2006/xaml")return WebUtility.HtmlDecode(attribute.Value);break;}
        }
        return null;
    }
    public static IEnumerable<AxamlElementSyntax> Descendants(AxamlElementSyntax root)
    { yield return root;foreach(var child in root.Children)foreach(var node in Descendants(child))yield return node; }
    private static string Escape(string value,char quote)=>value.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;")
        .Replace(quote=='"'?"\"":"'",quote=='"'?"&quot;":"&apos;");
    public static IReadOnlyList<string> Warnings(IEnumerable<DesignerControlFileModel> controls) => controls.SelectMany(c=>c.CustomProperties
        .Where(p=>p.Key.StartsWith(PropertyExpressions.Prefix,StringComparison.Ordinal))
        .Select(p=>c.Name+"."+p.Key[PropertyExpressions.Prefix.Length..]+": expression сохранён; DataContext/resources/converter не исполняются безопасной проекцией."))
        .Distinct().Take(6).ToArray();
}
