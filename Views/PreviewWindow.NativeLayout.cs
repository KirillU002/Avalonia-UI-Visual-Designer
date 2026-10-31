using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System.Linq;
using System;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FormDesigner.Services;

namespace FormDesigner.Views;

public partial class PreviewWindow
{
    private bool _nativeViewportMeasurePending;
    private void ScheduleNativeViewportMeasure()
    {
        if (_document.LayoutSemanticsVersion != 2 || _databasePreviewClosed || _nativeViewportMeasurePending) return;
        _nativeViewportMeasurePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _nativeViewportMeasurePending = false;
            if (_databasePreviewClosed || PreviewSurfaceBorder.Child is not Control root) return;
            // A resized Grid may initially reuse child DesiredSize from the old viewport.
            // Start one coalesced native measurement from its leaves; retain controls/data.
            foreach (var child in root.GetVisualDescendants().OfType<Control>().Reverse()) child.InvalidateMeasure();
            root.InvalidateMeasure();
        }, DispatcherPriority.Background);
    }
    private void RenderNativeLayoutDocument()
    {
        try
        {
            var files = _document.Controls.ToDictionary(c => c.Id);
            var models = _document.Controls.Select(CreateRuntimeControlModel).ToArray();
            foreach (var model in models)
                model.IsVisible = model.IsVisible && IsRuntimeControlVisible(files[model.Id]);
            var tree = AxamlLayoutProjection.BuildJson(_document, models, model =>
            {
                var view = CreatePreviewControl(files[model.Id]);
                view.IsEnabled = IsRuntimeControlEnabled(files[model.Id]);
                return view;
            });
            PluginPreviewControlBuilder.ApplyNativeLayoutRootStyles(_registry,
                _projectFormsById.Values.SelectMany(f => f.Document.Controls).Select(c => c.Type).Concat(models.Select(c => c.Type)), tree.Root);
            PreviewSurfaceBorder.Child = tree.Root;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            PreviewSurfaceBorder.Child = new TextBlock { Text = "Не удалось построить Grid Layout: " + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(16) };
            PreviewRuntimeStatusText.Text = "Ошибка Grid Layout";
        }
    }
}
