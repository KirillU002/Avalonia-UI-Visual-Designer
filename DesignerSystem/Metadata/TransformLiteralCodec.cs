using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using System;
using System.Globalization;
using System.Linq;

namespace FormDesigner.DesignerSystem.Metadata;

// Only matrix literals and relative origins. Custom Transform graphs, expressions
// and object property elements remain opaque; their ToString is never serialized.
internal static class TransformLiteralCodec
{
    public static bool Supports(AvaloniaPropertyInfo property) =>
        property.Key == "RenderTransform" && property.PropertyType == typeof(ITransform)
        || property.Key == "RenderTransformOrigin" && property.PropertyType == typeof(RelativePoint);

    public static bool TryParse(AvaloniaPropertyInfo property, string text, out object? value)
    {
        value = null;
        if (text is MetadataValueCodec.NullText or "{x:Null}") return property.Key == "RenderTransform";
        if (text.Length > 256 || text.TrimStart().StartsWith('{')) return false;
        try
        {
            var matrix = property.Key == "RenderTransform";
            // The AXAML ITransform converter uses TransformOperations, whose matrix
            // syntax differs from the legacy Transform.Parse six-number format.
            var literal = text.Trim();
            if (matrix)
            {
                if (!literal.StartsWith("matrix(", StringComparison.Ordinal) || !literal.EndsWith(')')) return false;
                literal = literal[7..^1];
            }
            var parts = literal.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != (matrix ? 6 : 2)) return false;
            // A relative origin must use the same unit on both axes.
            if (!matrix && parts[0].EndsWith('%') != parts[1].EndsWith('%')) return false;
            if (parts.Any(p => !double.TryParse(matrix ? p : p.TrimEnd('%'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || Math.Abs(n) > 1e6)) return false;
            value = matrix ? TransformOperations.Parse(text) : RelativePoint.Parse(string.Join(",", parts));
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { return false; }
    }
}
