using System;

namespace FormDesigner.ViewModels;

public sealed class PropertyGridOptionViewModel
{
    public PropertyGridOptionViewModel(string value, string title)
    {
        Value = value ?? string.Empty;
        Title = string.IsNullOrWhiteSpace(title) ? Value : title;
    }

    public string Value { get; }

    public string Title { get; }

    public override string ToString()
    {
        return Title;
    }
}
