using Avalonia.Media;
using System;
using System.Linq;

namespace FormDesigner.DesignerSystem.Metadata;

// Only the framework's standard decoration names. Custom pens, offsets, bindings
// and object collections keep their opaque representation.
internal static class TextDecorationsLiteralCodec
{
    public static bool Supports(AvaloniaPropertyInfo p) => p.Key == "TextDecorations" && p.PropertyType == typeof(TextDecorationCollection);
    public static bool TryParse(string text,out object? value)
    {
        value=null;
        if(text is MetadataValueCodec.NullText or "{x:Null}")return true;
        if(text.Length>128)return false;
        var names=text.Split(new[]{' ',','},StringSplitOptions.RemoveEmptyEntries);
        if(names.Length is 0 or > 4||names.Distinct(StringComparer.Ordinal).Count()!=names.Length
            ||names.Any(n=>n is not ("Underline" or "Strikethrough" or "Overline" or "Baseline")))return false;
        try{value=TextDecorationCollection.Parse(string.Join(",",names));return true;}
        catch(ArgumentException){return false;}
    }
}
