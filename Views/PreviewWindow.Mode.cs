using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace FormDesigner.Views;

public partial class PreviewWindow
{
    private readonly List<PreviewModeDiagnostic> _modeDiagnostics = new();
    public IReadOnlyList<PreviewModeDiagnostic> ModeDiagnostics => _modeDiagnostics;
    public event EventHandler<PreviewModeDiagnostic>? ModeDiagnosticEmitted;
    private void TracePreviewMode(string code, string reason, string mode = "AXAML")
    {
        var record = new PreviewModeDiagnostic(code, _exportedAxamlPreview?.ProjectId ?? "", _currentFormId, mode,
            _exportedAxamlPreview?.SourceKind ?? "ProjectJson", reason);
        _modeDiagnostics.Add(record);
        Trace.WriteLine(code + " " + JsonSerializer.Serialize(record));
        ModeDiagnosticEmitted?.Invoke(this, record);
    }
}
