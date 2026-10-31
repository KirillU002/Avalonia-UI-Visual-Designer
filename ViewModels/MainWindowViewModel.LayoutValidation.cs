using Avalonia;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private void AppendNativeLayoutDiagnostics(ICollection<DocumentDiagnosticModel> diagnostics)
    {
        if (!UsesNativeLayout) return;
        void Add(DesignControlModel model, string message, string recommendation, DocumentDiagnosticSeverity severity = DocumentDiagnosticSeverity.Warning)
            => diagnostics.Add(new DocumentDiagnosticModel { Category = "Layout", Severity = severity, Source = model.Name,
                RelatedControlId = model.Id, RelatedControlName = model.Name, Message = message, Recommendation = recommendation });
        foreach (var model in Controls)
        {
            foreach (var pair in new[] { ("MinWidth", "MaxWidth"), ("MinHeight", "MaxHeight") })
                if (LayoutPlacementProperties.Minimum(model, pair.Item1) > LayoutPlacementProperties.Maximum(model, pair.Item2))
                    Add(model, $"{pair.Item1} больше {pair.Item2}. Ограничения противоречат друг другу.", "Измените ограничения явно. Авторские размеры не исправляются автоматически.");
        }
        // Geometry diagnostics consume the current native snapshot; they never create
        // another tree, perform I/O or rewrite authored constraints.
        if (NativeGridMeasurementKey != GridDropLayoutKey) return;
        var geometryWarnings = 0;
        foreach (var model in Controls.Where(c => c.IsVisible && c.Opacity > .001))
        {
            if (geometryWarnings >= 50) break;
            if (!NativeContainerMeasurements.TryGetValue(model.ParentId, out var parent)) continue;
            var child = parent.Children.FirstOrDefault(c => c.Id == model.Id);
            if (child is null) continue;
            var slot = parent.Bounds;
            if (parent.Mode == DesignerLayoutModes.Grid && NativeGridMeasurements.TryGetValue(model.ParentId, out var grid))
                slot = new Rect(grid.Bounds.X + grid.Columns.Take(model.GridColumn).Sum(c => c.Size), grid.Bounds.Y + grid.Rows.Take(model.GridRow).Sum(r => r.Size),
                    grid.Columns.Skip(model.GridColumn).Take(model.GridColumnSpan).Sum(c => c.Size), grid.Rows.Skip(model.GridRow).Take(model.GridRowSpan).Sum(r => r.Size));
            if (model.WidthMode == "Fixed" && model.Width + child.Margin.Left + child.Margin.Right > slot.Width + 1)
            {
                Add(model, "Фиксированный Width превышает доступную ширину размещения.", "Проверьте Width, Margin, alignment и размеры контейнера. Можно явно выбрать Auto."); geometryWarnings++;
            }
            if (child.Bounds.Left < parent.Clip.Left - 1 || child.Bounds.Top < parent.Clip.Top - 1 || child.Bounds.Right > parent.Clip.Right + 1 || child.Bounds.Bottom > parent.Clip.Bottom + 1)
            {
                Add(model, "Элемент выходит за видимую область контейнера.", "Проверьте фиксированные размеры и Min/Max. Переполнение может быть намеренным; Layout не изменён."); geometryWarnings++;
            }
        }
        foreach (var parent in NativeContainerMeasurements.Values.Where(p => p.Mode is DesignerLayoutModes.Grid or DesignerLayoutModes.Absolute))
        {
            var visible = parent.Children.Where(c => c.Visible).ToArray();
            for (var i = 0; i < visible.Length && geometryWarnings < 50; i++)
                for (var j = i+1; j < visible.Length && geometryWarnings < 50; j++)
                {
                    var overlap = visible[i].Bounds.Intersect(visible[j].Bounds).Intersect(parent.Clip);
                    if (overlap.Width <= 1 || overlap.Height <= 1 || GetControl(visible[j].Id) is not { } model) continue;
                    Add(model, "Дочерние элементы перекрываются в Layout.", "В Grid и Canvas это допустимо. Проверьте, соответствует ли перекрытие вашему замыслу.", DocumentDiagnosticSeverity.Info); geometryWarnings++;
                }
        }
    }
}
