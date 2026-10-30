namespace FormDesigner.Models;

public sealed record DataGridPreviewDiagnostic(
    string Event, string FormId, string ControlId, string BindingSourceId,
    string Provider, string QueryType, int LoadedRowsCount, int ItemsSourceCount,
    string DataContextType, string PreviewMode, long Duration, string Details = "");
