namespace FormDesigner.Models;

public sealed class InteractionOptionModel
{
    public InteractionOptionModel(string value, string displayName, string description)
    {
        Value = value;
        DisplayName = displayName;
        Description = description;
    }

    public string Value { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public override string ToString() => DisplayName;
}
