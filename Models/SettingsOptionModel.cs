namespace FormDesigner.Models;

public sealed class SettingsOptionModel
{
    public SettingsOptionModel(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }

    public string DisplayName { get; }

    public override string ToString() => DisplayName;
}
