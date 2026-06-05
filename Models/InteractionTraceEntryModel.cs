using System;

namespace FormDesigner.Models;

public sealed class InteractionTraceEntryModel
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string EventName { get; init; } = "";
    public string ActiveForm { get; init; } = "";
    public string SelectedControl { get; init; } = "";
    public string FocusedElement { get; init; } = "";
    public string Flags { get; init; } = "";
    public string Details { get; init; } = "";

    public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string Summary =>
        $"{TimestampText} | {EventName} | active={ActiveForm} | selected={SelectedControl} | focused={FocusedElement} | flags={Flags} | {Details}";
}
