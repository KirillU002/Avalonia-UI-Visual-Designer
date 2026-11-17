using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FormDesigner.DesignerSystem.Metadata;

// Only matrix literals and relative origins. Custom Transform graphs, expressions
// and object property elements remain opaque; their ToString is never serialized.
internal static class TransformLiteralCodec
{
    public static bool Supports(AvaloniaPropertyInfo property) =>
        property.Key is "RenderTransform" or "LayoutTransform" && property.PropertyType == typeof(ITransform)
        || property.Key == "RenderTransformOrigin" && property.PropertyType == typeof(RelativePoint);

    public static bool TryParse(AvaloniaPropertyInfo property, string text, out object? value)
    {
        value = null;
        if (text is MetadataValueCodec.NullText or "{x:Null}") return property.Key != "RenderTransformOrigin";
        if (text.Length > 2048 || text.TrimStart().StartsWith('{')) return false;
        try
        {
            var transform = property.Key != "RenderTransformOrigin";
            // The AXAML ITransform converter uses TransformOperations, whose matrix
            // syntax differs from the legacy Transform.Parse six-number format.
            var literal = text.Trim();
            if (transform)
            {
                if (literal != "none")
                {
                    var operations = Regex.Matches(literal, @"(matrix|translate|translateX|translateY|rotate|scale|scaleX|scaleY|skew|skewX|skewY)\(([^()]*)\)", RegexOptions.CultureInvariant);
                    if (operations.Count is 0 or > 16) return false;
                    var offset=0;
                    foreach(Match operation in operations)
                    {
                        if (!string.IsNullOrWhiteSpace(literal[offset..operation.Index])) return false;
                        var args=operation.Groups[2].Value.Split(new[]{',',' '},StringSplitOptions.RemoveEmptyEntries);
                        if(args.Length is 0 or > 6)return false;
                        foreach(var arg in args)
                        {
                            var number=Regex.Match(arg,@"^([-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?)(px|deg|rad|grad|turn)?$",RegexOptions.CultureInvariant);
                            if(!number.Success||!double.TryParse(number.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||Math.Abs(n)>1e6)return false;
                        }
                        offset=operation.Index+operation.Length;
                    }
                    if(!string.IsNullOrWhiteSpace(literal[offset..]))return false;
                }
                var parsed=TransformOperations.Parse(literal);
                var m=((ITransform)parsed).Value;
                if(new[]{m.M11,m.M12,m.M21,m.M22,m.M31,m.M32}.Any(n=>!double.IsFinite(n)||Math.Abs(n)>1e9))return false;
                value=parsed;return true;
            }
            var parts = literal.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;
            // A relative origin must use the same unit on both axes.
            if (parts[0].EndsWith('%') != parts[1].EndsWith('%')) return false;
            if (parts.Any(p => !double.TryParse(p.TrimEnd('%'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || Math.Abs(n) > 1e6)) return false;
            value = RelativePoint.Parse(string.Join(",", parts));
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { return false; }
    }
}
