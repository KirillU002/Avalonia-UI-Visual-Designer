using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private void MeasureNativeDesignerLayout(double width, double height)
    {
        try { VM.NativeLayoutBounds = VM.BuildNativeLayout(CreatePreviewControl, includeHidden: !VM.IsUserPreviewMode).Measure(width, height); }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            VM.NativeLayoutBounds = new Dictionary<string, Rect>();
            VM.StatusText = "Не удалось построить Grid Layout: " + ex.Message;
        }
    }

    private Control CreateNativeContainerBackdrop(DesignControlModel model) => new Border
    {
        Width = model.Width, Height = model.Height,
        Background = ParseBrush(model.Background, "Transparent"),
        BorderBrush = ParseBrush(model.BorderBrush, "Transparent"),
        BorderThickness = new Thickness(model.BorderThickness),
        CornerRadius = new CornerRadius(model.CornerRadius), IsHitTestVisible = false
    };

    private void AddNativeSelectionFrame(Canvas root, Border wrapper, bool selected, double width, double height)
    {
        // Decoration is a sibling of content on the editor shell, never layout input.
        root.Children.Add(new Rectangle
        {
            Width = width, Height = height,
            Stroke = wrapper.BorderBrush, StrokeThickness = selected ? 2 : 1,
            Fill = Brushes.Transparent, IsHitTestVisible = false
        });
        wrapper.BorderThickness = new Thickness(0);
    }
}
