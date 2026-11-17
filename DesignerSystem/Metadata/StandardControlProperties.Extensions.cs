using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace FormDesigner.DesignerSystem.Metadata;

public static partial class StandardControlProperties
{
    // Reviewed property adapters for existing descriptors; never another registration list.
    public static IReadOnlyDictionary<string,Type> ExtensionTypes { get; } = new Dictionary<string,Type>(StringComparer.Ordinal)
    {
        ["Button"]=typeof(Button),["TextBox"]=typeof(TextBox),["TextBlock"]=typeof(TextBlock),["CheckBox"]=typeof(CheckBox),
        ["Border"]=typeof(Border),["Image"]=typeof(Image),["Grid"]=typeof(Grid),["StackPanel"]=typeof(StackPanel),["WrapPanel"]=typeof(WrapPanel),
        ["Group"]=typeof(Canvas),["Canvas"]=typeof(Canvas),["DockPanel"]=typeof(DockPanel),["DropDownButton"]=typeof(DropDownButton),["SplitButton"]=typeof(SplitButton),
        ["ListBox"]=typeof(ListBox),["Menu"]=typeof(Menu),["ComboBox"]=typeof(ComboBox),["TabControl"]=typeof(TabControl),["TreeView"]=typeof(TreeView),
        ["ItemsControl"]=typeof(ItemsControl),["Carousel"]=typeof(Carousel),["TabStrip"]=typeof(Avalonia.Controls.Primitives.TabStrip)
    };
    private static readonly string[] AdditionalKeys={"IsEnabled","IsHitTestVisible","Focusable","IsTabStop","TabIndex","UseLayoutRounding","ClipToBounds","FontStyle","FontStretch","ToolTip.Tip","RenderTransform","RenderTransformOrigin","Clip","FlowDirection","OpacityMask","TextDecorations","SelectionBrush","SelectionForegroundBrush"};
    private static IEnumerable<string> AdditionalKeysFor(string type) => AdditionalKeys.Concat(type switch
    {
        "TextBox" => new[]{"IsReadOnly","MaxLength","AcceptsReturn","AcceptsTab","TextWrapping","TextAlignment"},
        "TextBlock" => new[]{"TextAlignment","TextWrapping","TextTrimming","LineHeight","LetterSpacing","MaxLines"},
        "Border" => new[]{"BoxShadow"},
        _ => Array.Empty<string>()
    });
    public static Type? PropertyTypeFor(string type) => SupportedTypes.TryGetValue(type,out var native)?native:ExtensionTypes.TryGetValue(type,out native)?native:null;
    public static Control NativePropertyTarget(Control view,string type)
    {
        var native=PropertyTypeFor(type);
        return native is null||view.GetType()==native?view:view.GetVisualDescendants().OfType<Control>().FirstOrDefault(c=>c.GetType()==native)??view;
    }
    public static void ApplyAdditional(Control target,IDesignControlNode node)
    {
        target=NativePropertyTarget(target,node.TypeKey);
        if(!ExtensionTypes.ContainsKey(node.TypeKey))return;
        foreach(var p in PropertiesFor(node.TypeKey))
        {
            if(!node.CustomProperties.TryGetValue(p.Key,out var json))continue;
            var text=Read(p,json);
            if(!TryEncode(p,text,out _)||!TryParseValue(p,text,out var value))throw new FormatException("Invalid Avalonia property: "+node.TypeKey+"."+p.Key);
            target.SetValue(p.Property!,value);
        }
    }
    public static string ApplyAdditionalGeneratedAttributes(string line,string type,IDesignControlNode node,Func<string,string> escape)
    {
        foreach(var p in PropertiesFor(type))
        {
            if(!node.CustomProperties.TryGetValue(p.Key,out var json))continue;
            var text=Read(p,json);if(!TryEncode(p,text,out _))throw new FormatException("Invalid Avalonia property: "+p.Key);
            var attribute=p.Key+"=\""+(text==MetadataValueCodec.NullText?"{x:Null}":escape(AttributeText(p,text)))+"\"";
            var pattern="(?<![\\w.])"+Regex.Escape(p.Key)+"\\s*=\\s*(['\"])(.*?)\\1";
            if(Regex.IsMatch(line,pattern))line=Regex.Replace(line,pattern,_=>attribute);
            else {var end=line.LastIndexOf("/>",StringComparison.Ordinal);if(end<0)end=line.LastIndexOf('>');if(end<0)throw new FormatException("Generated opening tag missing");line=line.Insert(end," "+attribute);}
        }
        return line;
    }
}
