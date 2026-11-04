using Avalonia;
using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.Services;

// Adapt the existing v1 export placement contract, without changing the document
// or implementing layout arithmetic. Avalonia still measures the resulting tree.
public static class LegacyCanvasLayoutProjection
{
    public static AxamlLayoutProjection.JsonLayoutTree Build(IReadOnlyList<DesignControlModel> controls,
        string rootId, double width, double height, Func<DesignControlModel, Control> factory, bool includeHidden = false)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { rootId };
        bool changed;
        do
        {
            changed = false;
            foreach (var control in controls)
                if (ids.Contains(control.ParentId) && ids.Add(control.Id)) changed = true;
        } while (changed);
        var models = controls.Where(c => ids.Contains(c.Id)).Select(c => { var copy = c.Clone();copy.Id = c.Id;return copy; }).ToArray();
        var byId = models.ToDictionary(c => c.Id);
        foreach (var model in models)
        {
            model.WidthMode = model.HeightMode = "Fixed";
            if (string.IsNullOrWhiteSpace(model.Margin)) model.Margin = "0";
        }
        var root = byId[rootId];root.ParentId = "";root.X = root.Y = 0;root.Width = width;root.Height = height;
        var document = new DesignerDocumentFileModel { LayoutSemanticsVersion = 2,
            SurfaceLayoutMode = DesignerLayoutModes.Absolute, DesignWidth = width, DesignHeight = height };
        return AxamlLayoutProjection.BuildJson(document, models, factory, includeHidden, (model, view) =>
        {
            // v1 export omits default Stretch/Top, so Avalonia/theme defaults apply.
            if (model.HorizontalAlignment == DesignerLayoutModes.AlignStretch) view.ClearValue(Control.HorizontalAlignmentProperty);
            if (model.VerticalAlignment == DesignerLayoutModes.AlignTop) view.ClearValue(Control.VerticalAlignmentProperty);
            if (model.Id == rootId) { view.Margin = default; return; }
            var parent = byId[model.ParentId];
            var mode = string.IsNullOrEmpty(parent.ChildLayoutMode) ? DesignerLayoutModes.GetModeForControlType(parent.Type) : DesignerLayoutModes.NormalizeMode(parent.ChildLayoutMode);
            if (DesignerLayoutModes.IsAbsolute(mode)) { view.Margin = default; return; }
            if (!string.IsNullOrWhiteSpace(model.Margin) && model.Margin.Trim() != "0") return;
            var spacing = Math.Max(0, parent.LayoutSpacing);
            view.Margin = mode is DesignerLayoutModes.Grid or DesignerLayoutModes.Flex ? new Thickness(0,0,spacing,spacing)
                : parent.LayoutOrientation == DesignerLayoutModes.Horizontal ? new Thickness(0,0,spacing,0) : new Thickness(0,0,0,spacing);
        });
    }
}
