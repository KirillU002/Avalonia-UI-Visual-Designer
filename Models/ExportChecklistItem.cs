namespace FormDesigner.Models;

public enum ExportChecklistSeverity
{
    Ok,
    Warning,
    Error
}

public sealed class ExportChecklistItem
{
    public ExportChecklistSeverity Severity { get; init; } = ExportChecklistSeverity.Ok;

    public string Title { get; init; } = "";

    public string Value { get; init; } = "";

    public string Details { get; init; } = "";

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string SeverityTitle => Severity switch
    {
        ExportChecklistSeverity.Error => "Error",
        ExportChecklistSeverity.Warning => "Warning",
        _ => "OK"
    };

    public string SeverityBadgeBackground => Severity switch
    {
        ExportChecklistSeverity.Error => "#DC2626",
        ExportChecklistSeverity.Warning => "#D97706",
        _ => "#16A34A"
    };

    public string SeverityBackground => Severity switch
    {
        ExportChecklistSeverity.Error => "#FEF2F2",
        ExportChecklistSeverity.Warning => "#FFFBEB",
        _ => "#F0FDF4"
    };

    public string SeverityBorderBrush => Severity switch
    {
        ExportChecklistSeverity.Error => "#FECACA",
        ExportChecklistSeverity.Warning => "#FDE68A",
        _ => "#BBF7D0"
    };

    public string SeverityForeground => Severity switch
    {
        ExportChecklistSeverity.Error => "#7F1D1D",
        ExportChecklistSeverity.Warning => "#78350F",
        _ => "#14532D"
    };
}
