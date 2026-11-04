using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Styling;
using FormDesigner.Services;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private bool _buildingLegacyCanvasProjection;
    private readonly List<IStyle> _nativeCanvasThemeStyles = new();
    private string _nativeCanvasThemeContext = "";
    private void RefreshNativeCanvasThemeScope()
    {
        var context = VM.UsesNativeLayout ? VM.DocumentSessionId + ":" + string.Join(",", VM.NativeLayoutStyleTypes.OrderBy(t => t)) : "";
        if (context == _nativeCanvasThemeContext) return;
        foreach (var style in _nativeCanvasThemeStyles) DesignerCanvas.Styles.Remove(style);
        _nativeCanvasThemeStyles.Clear(); _nativeCanvasThemeContext = context;
        if (!VM.UsesNativeLayout) return;
        var before = DesignerCanvas.Styles.ToArray();
        PluginPreviewControlBuilder.ApplyNativeLayoutRootStyles(VM.Registry, VM.NativeLayoutStyleTypes, DesignerCanvas);
        _nativeCanvasThemeStyles.AddRange(DesignerCanvas.Styles.Except(before));
    }
    private void MeasureNativeDesignerLayout(double width, double height)
    {
        try
        {
            var tree = VM.BuildNativeLayout(CreatePreviewControl, includeHidden: !VM.IsUserPreviewMode);
            VM.ApplyGridTrackResizePreview(tree);
            VM.UpdateNativeLayoutMeasurements(tree, width, height);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            VM.NativeLayoutBounds = new Dictionary<string, Rect>();
            VM.NativeGridMeasurementKey = "";
            VM.NativeGridMeasurements = new Dictionary<string, FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlLayoutProjection.GridMeasurement>();
            VM.StatusText = "Не удалось построить Grid Layout: " + ex.Message;
        }
    }

    private void MeasureLegacyManagedCanvasLayouts()
    {
        if (VM.UsesNativeLayout || VM.IsAxamlRoundTripDocument) return;
        var measured = new Dictionary<string, Rect>();
        foreach (var root in VM.Controls.Where(c => VM.CanHostChildren(c) && !DesignerLayoutModes.IsAbsolute(VM.GetLayoutModeForControl(c))))
        {
            // An outer managed subtree already measures its descendants.
            var ancestor = VM.GetControl(root.ParentId);var contained = false;var visited = new HashSet<string>();
            while (ancestor is not null && visited.Add(ancestor.Id))
            {
                if (!DesignerLayoutModes.IsAbsolute(VM.GetLayoutModeForControl(ancestor))) { contained = true;break; }
                ancestor = VM.GetControl(ancestor.ParentId);
            }
            if (contained) continue;
            _buildingLegacyCanvasProjection = true;
            try
            {
                var tree = LegacyCanvasLayoutProjection.Build(VM.Controls.ToArray(), root.Id, root.Width, root.Height,
                    CreatePreviewControl, includeHidden: !VM.IsUserPreviewMode);
                foreach (var frame in tree.Measure(root.Width, root.Height))
                    if (frame.Key != root.Id) measured[frame.Key] = frame.Value;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                VM.StatusText = "Не удалось измерить вложенный Canvas layout: " + ex.Message;
            }
            finally { _buildingLegacyCanvasProjection = false; }
        }
        VM.AxamlLayoutBounds = measured;
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
        if (!VM.IsUserPreviewMode && wrapper.Tag is DesignControlModel model
            && model.Type is DesignerControlTypes.DataGrid or "Eremex.DataGridControl")
        {
            void Edge(double x, double y, double w, double h)
            {
                if (w <= 0 || h <= 0) return;
                var edge = new Border { Name = "NativeGridDragFrame", Width = w, Height = h, Background = Brushes.Transparent,
                    Tag = model, Cursor = new Cursor(StandardCursorType.SizeAll) };
                edge.PointerPressed += (_, args) =>
                {
                    if (args.GetCurrentPoint(edge).Properties.IsLeftButtonPressed) Control_PointerPressed(wrapper, args);
                };
                Canvas.SetLeft(edge, x); Canvas.SetTop(edge, y); root.Children.Add(edge);
            }
            // Only the outer 4 DIP are designer interaction chrome. Preserve the bottom-right
            // resize hit area and the grid's native header/cell gestures in its interior.
            Edge(0, 0, width, 4); Edge(0, 4, 4, Math.Max(0, height - 4));
            Edge(Math.Max(0, width - 4), 4, 4, Math.Max(0, height - 22));
            Edge(4, Math.Max(0, height - 4), Math.Max(0, width - 22), 4);
        }
    }
}
