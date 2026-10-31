namespace FormDesigner.Models;

public sealed record PreviewLifecycleDiagnostic(string Event, string PreviewId, string Mode,
    int ActiveWindows, int PendingActivities, string Reason = "");
