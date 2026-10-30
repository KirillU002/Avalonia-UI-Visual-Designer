using System;
using System.Collections.Generic;
using System.Text;

namespace FormDesigner.Services;

/// <summary>Distinct C# property names; database names stay case-sensitive.</summary>
public static class PostgreSqlExportNames
{
    public static string Identifier(string original)
    {
        var text = new StringBuilder();
        foreach (var character in original)
            text.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        var identifier = text.ToString().TrimStart('_');
        if (identifier.Length == 0 || !char.IsLetter(identifier[0])) identifier = "Field" + identifier;
        identifier = char.ToUpperInvariant(identifier[0]) + identifier[1..];
        return identifier.StartsWith("M_", StringComparison.Ordinal) ? "Column" + identifier : identifier;
    }
    public static Dictionary<string, string> FieldPaths(IEnumerable<string> names, string rowType = "")
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Clone", "CopyFrom", "PropertyChanged", "PropertyChanging", "OnPropertyChanged", "OnPropertyChanging", "SetProperty", rowType
        };
        foreach (var name in names)
        {
            var basis = Identifier(name); var candidate = basis; var suffix = 2;
            while (!used.Add(candidate)) candidate = basis + suffix++;
            result[name] = candidate;
        }
        return result;
    }
}
