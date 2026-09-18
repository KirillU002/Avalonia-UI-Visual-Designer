using System;
using System.IO;

namespace FormDesigner.DesignerSystem;

public enum DesignerDocumentKind
{
    ProjectJson,
    AxamlRoundTrip
}

public static class DesignerDocumentFormat
{
    public static DesignerDocumentKind ForPath(string path) =>
        string.Equals(Path.GetExtension(path), ".axaml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), ".xaml", StringComparison.OrdinalIgnoreCase)
            ? DesignerDocumentKind.AxamlRoundTrip : DesignerDocumentKind.ProjectJson;

    public static void Validate(string text, DesignerDocumentKind expected, string caller)
    {
        var prefix = (text ?? string.Empty).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        var actual = prefix.StartsWith("<", StringComparison.Ordinal) ? "Axaml"
            : prefix.StartsWith("{", StringComparison.Ordinal) || prefix.StartsWith("[", StringComparison.Ordinal) ? "ProjectJson" : "Unknown";
        if ((expected == DesignerDocumentKind.ProjectJson && actual == "Axaml")
            || (expected == DesignerDocumentKind.AxamlRoundTrip && actual == "ProjectJson"))
            throw new InvalidDataException($"DOCUMENT_FORMAT_MISMATCH expected={expected}; actual={actual}; caller={caller}");
    }
}
