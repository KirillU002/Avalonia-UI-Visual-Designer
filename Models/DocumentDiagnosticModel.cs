namespace FormDesigner.Models;

public enum DocumentDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed class DocumentDiagnosticModel
{
    public DocumentDiagnosticSeverity Severity { get; init; } = DocumentDiagnosticSeverity.Info;

    public string Source { get; init; } = "Document";

    public string Category { get; init; } = "";

    public string Message { get; init; } = "";

    public string Recommendation { get; init; } = "";

    public string RelatedControlId { get; init; } = "";

    public string RelatedControlName { get; init; } = "";

    public string RelatedBindingSourceId { get; init; } = "";

    public string RelatedBindingSourceName { get; init; } = "";

    public bool HasRecommendation => !string.IsNullOrWhiteSpace(Recommendation);

    public bool HasNavigationTarget => !string.IsNullOrWhiteSpace(RelatedControlId) || !string.IsNullOrWhiteSpace(RelatedBindingSourceId);

    public string SeverityTitle => Severity switch
    {
        DocumentDiagnosticSeverity.Error => "Ошибка",
        DocumentDiagnosticSeverity.Warning => "Предупреждение",
        _ => "Информация"
    };

    public string SeverityBadgeBackground => Severity switch
    {
        DocumentDiagnosticSeverity.Error => "#DC2626",
        DocumentDiagnosticSeverity.Warning => "#D97706",
        _ => "#2563EB"
    };

    public string SeverityForeground => "#FFFFFF";

    public string SeverityPanelBackground => Severity switch
    {
        DocumentDiagnosticSeverity.Error => "#FEF2F2",
        DocumentDiagnosticSeverity.Warning => "#FFFBEB",
        _ => "#EFF6FF"
    };

    public string SeverityBackground => SeverityPanelBackground;

    public string SeverityPanelBorder => Severity switch
    {
        DocumentDiagnosticSeverity.Error => "#FECACA",
        DocumentDiagnosticSeverity.Warning => "#FDE68A",
        _ => "#BFDBFE"
    };

    public string SeverityBorderBrush => SeverityPanelBorder;

    public string NavigationSummary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(RelatedControlName))
                return $"Связанный элемент: {RelatedControlName}";

            if (!string.IsNullOrWhiteSpace(RelatedBindingSourceName))
                return $"Связанный источник: {RelatedBindingSourceName}";

            return "Общая диагностика документа";
        }
    }

    public string NavigationCaption => NavigationSummary;
}
