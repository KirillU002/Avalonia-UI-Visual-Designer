using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FormDesigner.Services;

namespace FormDesigner.Controls;

// Owned by one Inspector instance. Contexts stay on immutable RowVMs; only views recycle.
internal sealed class PropertyGridRecyclingTemplate(IDataTemplate original) : IRecyclingDataTemplate
{
    public bool Match(object? data) => original.Match(data);
    public Control? Build(object? data)
    {
        using var stage = LayoutDropProfile.Stage("InspectorEditorControlCreationAndBindings");
        var control = original.Build(data);
        if (control is not null) control.DataContext = data;
        LayoutDropProfile.Count("InspectorViewsCreated");
        return control;
    }
    public Control? Build(object? data, Control? existing)
    {
        if (existing is null) return Build(data);
        using var stage = LayoutDropProfile.Stage("InspectorRecycledBindings");
        existing.DataContext = data;
        LayoutDropProfile.Count("InspectorViewsReused");
        return existing;
    }
}
