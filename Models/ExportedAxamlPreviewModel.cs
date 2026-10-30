using System;
using System.Collections.Generic;
using FormDesigner.PluginContracts;

namespace FormDesigner.Models;

public sealed class ExportedAxamlPreviewModel
{
    public string ExportAxaml { get; init; } = "";

    public string Axaml { get; init; } = "";

    public string RootElement { get; init; } = "";

    public string RemovedXClass { get; init; } = "";

    public string GeneratedCSharp { get; init; } = "";

    public DesignerDocumentFileModel Document { get; init; } = new();

    public IReadOnlyList<DesignerFormDocument> ProjectForms { get; init; } = Array.Empty<DesignerFormDocument>();

    public string ActiveFormId { get; init; } = "";

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PostgreSqlFieldPaths { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public IReadOnlyDictionary<string, string> StandardControlNames { get; init; } = new Dictionary<string, string>();

    public bool IncludeDemoData { get; init; }

    public bool FallbackToLegacyPreviewOnError { get; init; } = true;

    public bool ShowGeneratedAxamlOnError { get; init; } = true;

    public IReadOnlyList<DesignerRuntimePreviewContribution> RuntimePreviewContributions { get; init; } = Array.Empty<DesignerRuntimePreviewContribution>();

}
