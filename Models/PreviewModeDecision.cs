namespace FormDesigner.Models;

public static class PreviewModePolicy
{
    // JSON-only default, enabled after the Preview Phase 2 native/runtime gate.
    // Explicit user choices and imported-source sessions are resolved separately.
    public static bool DefaultForJson => true;
}

public sealed record PreviewModeDecision(string ProjectId, string FormId, string PreviewMode, string SourceKind, string Reason);
public sealed record PreviewModeDiagnostic(string Event, string ProjectId, string FormId, string PreviewMode, string SourceKind, string Reason);
