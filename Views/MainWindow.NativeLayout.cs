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
using Avalonia.VisualTree;
using FormDesigner.Services;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private bool _buildingLegacyCanvasProjection;
    private readonly Dictionary<string,(Avalonia.Layout.HorizontalAlignment Horizontal,Avalonia.Layout.VerticalAlignment Vertical)> _legacyNativeAlignments=new();
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
        _legacyNativeAlignments.Clear();
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
                foreach(var item in tree.Controls)_legacyNativeAlignments[item.Key]=(item.Value.HorizontalAlignment,item.Value.VerticalAlignment);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                VM.StatusText = "Не удалось измерить вложенный Canvas layout: " + ex.Message;
            }
            finally { _buildingLegacyCanvasProjection = false; }
        }
        VM.AxamlLayoutBounds = measured;
    }

    // Native containers own their child shell layout too. Reuse the existing
    // selection/resize wrappers inside real Child/Content/Children; no Canvas root
    // is inserted into the authored container or its exported hierarchy.
    private void PopulateAdvancedDesignerContainer(DesignControlModel model, Control host)
    {
        var children=new List<Control>();
        var previousNativeFactory=_buildingLegacyCanvasProjection;
        _buildingLegacyCanvasProjection=true;
        try {
        foreach(var child in VM.GetChildControls(model.Id).OrderBy(c=>c.StackOrder))
        {
            var frames=VM.UsesNativeLayout?VM.NativeLayoutBounds:VM.AxamlLayoutBounds;
            var size=frames.TryGetValue(child.Id,out var frame)&&frame.Width>0&&frame.Height>0?frame.Size:new Size(child.Width,child.Height);
            var wrapper=CreateDesignerWrapper(child,size.Width,size.Height);_wrapperByControlId[child.Id]=wrapper;
            FormDesigner.DesignerSystem.AxamlRoundTrip.AxamlLayoutProjection.ApplyJsonLayout(child,wrapper,includeHidden:!VM.IsUserPreviewMode);
            if(!VM.UsesNativeLayout&&!VM.IsAxamlRoundTripDocument&&_legacyNativeAlignments.TryGetValue(child.Id,out var alignment))
            {wrapper.HorizontalAlignment=alignment.Horizontal;wrapper.VerticalAlignment=alignment.Vertical;}
            wrapper.Opacity=1; // The native view already owns authored opacity.
            children.Add(wrapper);
        }
        } finally { _buildingLegacyCanvasProjection=previousNativeFactory; }
        FormDesigner.DesignerSystem.Metadata.StandardControlProperties.AttachChildren(host,children,designerEmptyTarget:true);
        // The shell remains selectable, while native header/scroll gestures do not
        // mutate the model behind PropertyGrid/Undo in the editing surface.
        host.IsHitTestVisible=true;
        if(host is Avalonia.Controls.Primitives.TemplatedControl templated)
            templated.TemplateApplied+=(_,_)=>{
                foreach(var control in host.GetVisualDescendants().OfType<Control>().Where(c=>c.TemplatedParent==host))
                    if(control is Avalonia.Controls.Primitives.ToggleButton or Avalonia.Controls.Primitives.ScrollBar || host is ButtonSpinner && control is Button)control.IsHitTestVisible=false;
            };
    }

    private Control CreateNativeContainerBackdrop(DesignControlModel model)
    {
        var view=new Border
        {
        Width = model.Width, Height = model.Height,
        Background = ParseBrush(model.Background, "Transparent"),
        BorderBrush = ParseBrush(model.BorderBrush, "Transparent"),
        BorderThickness = new Thickness(model.BorderThickness),
        CornerRadius = new CornerRadius(model.CornerRadius), IsHitTestVisible = false
        };
        FormDesigner.DesignerSystem.Metadata.StandardControlProperties.ApplyAdditional(view,new FormDesigner.DesignerSystem.Infrastructure.DesignControlNodeAdapter(model));
        view.IsHitTestVisible=false;
        return view;
    }

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
