using System;

namespace FormDesigner.Models;

public class RecoveryDraftFileModel
{
    public string Version { get; set; } = "1.0";

    public string SessionId { get; set; } = "";

    public string DocumentPath { get; set; } = "";

    public string DocumentDisplayName { get; set; } = "Без имени.formdesigner.json";

    public DateTime LastAutosaveUtc { get; set; } = DateTime.UtcNow;

    public bool HasUnsavedChanges { get; set; } = true;

    public string DocumentJson { get; set; } = "";
}
