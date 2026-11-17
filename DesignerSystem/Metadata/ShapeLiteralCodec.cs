using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FormDesigner.DesignerSystem.Metadata;

// Property-specific literals, not an IEnumerable/object editor. Keep validated
// text in the existing JSON bag: Geometry.ToString is not a reversible contract.
internal static class ShapeLiteralCodec
{
    public static bool Supports(AvaloniaPropertyInfo property) => typeof(Shape).IsAssignableFrom(property.OwnerType)
        && property.Key is "Points" or "Data" or "StrokeDashArray"
        || property.OwnerType == typeof(Avalonia.Controls.PathIcon) && property.Key == "Data"
        || property.Key == "Clip" && property.PropertyType == typeof(Geometry);

    public static bool TryParse(AvaloniaPropertyInfo property, string text, out object? value)
    {
        value = null;
        if (text is MetadataValueCodec.NullText or "{x:Null}") return true;
        if (text.Length > 16384 || text.TrimStart().StartsWith('{')) return false;
        try
        {
            if (property.Key is "Data" or "Clip")
            {
                // Only the standard path mini-language, with bounded finite coordinates.
                if (string.IsNullOrWhiteSpace(text) || text.Any(c => !(char.IsAsciiDigit(c) || char.IsWhiteSpace(c) || ",.+-eEMmLlHhVvCcSsQqTtAaZzFf".Contains(c)))) return false;
                var numbers = Regex.Matches(text, @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant);
                if (numbers.Count > 2048 || numbers.Any(m => !double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || Math.Abs(n) > 1e6)) return false;
                value = StreamGeometry.Parse(text); return true;
            }
            var parts = text.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2048) return false;
            var values = parts.Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e6)) return false;
            if (property.Key == "StrokeDashArray")
            {
                if (values.Any(n => n < 0)) return false;
                value = new AvaloniaList<double>(values); return true;
            }
            if (values.Length % 2 != 0) return false;
            value = new AvaloniaList<Point>(Enumerable.Range(0, values.Length / 2).Select(i => new Point(values[i * 2], values[i * 2 + 1])));
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException or System.IO.InvalidDataException) { return false; }
    }
}
