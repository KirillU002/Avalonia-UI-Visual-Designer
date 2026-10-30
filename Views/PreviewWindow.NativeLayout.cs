using Avalonia.Controls;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.Models;
using System.Linq;
using System;

namespace FormDesigner.Views;

public partial class PreviewWindow
{
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
            PreviewSurfaceBorder.Child = tree.Root;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            PreviewSurfaceBorder.Child = new TextBlock { Text = "Не удалось построить Grid Layout: " + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(16) };
            PreviewRuntimeStatusText.Text = "Ошибка Grid Layout";
        }
    }
}
