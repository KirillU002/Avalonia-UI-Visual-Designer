using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using FormDesigner.Models;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System;
using System.Linq;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private MainWindowViewModel.NativeDropRenderChange? _nativeDropVisualChange;
    private string _nativeRenderedSelection = "";
    private int _nativeRenderedSelectionCount;
    private IPointer? _contextMenuPointer;
    private void CancelPendingContextMenu()
    {
        _pendingContextMenuControlId = "";
        var pointer = _contextMenuPointer; _contextMenuPointer = null;
        if (ReferenceEquals(pointer?.Captured, DesignerCanvas)) pointer.Capture(null);
    }
    private string NativeSelectionStamp() => VM.DocumentSessionId + ":" + VM.SelectedControl?.Id + ":" + string.Join(",", VM.SelectedControlIds.OrderBy(id => id));

    private bool TryUpdateNativeDropVisuals(MainWindowViewModel.NativeDropRenderChange change)
    {
        using var stage = LayoutDropProfile.Stage("UpdateExistingWrappers");
        // Data/plugin/item previews can refresh independently of placement (for
        // example an async row cache). Preserve their existing full refresh path.
        if (VM.Controls.Any(c => !VM.CanHostChildren(c)
            && (c.Type is not (DesignerControlTypes.Button or DesignerControlTypes.TextBox or DesignerControlTypes.TextBlock)
                || !string.IsNullOrEmpty(c.BindingSourceId) || !string.IsNullOrEmpty(c.TextBindingPath)))) return false;
        // Narrow reuse: same hierarchy, selection and dimensions. All other edits,
        // reparenting, zero-size changes and new controls use the existing full renderer.
        foreach (var model in VM.Controls)
        {
            var positive = VM.NativeLayoutBounds.TryGetValue(model.Id, out var bounds) && bounds.Width > 0 && bounds.Height > 0;
            if (model.Id == change.AddedControlId) { if (!positive || _wrapperByControlId.ContainsKey(model.Id)) return false; continue; }
            if (!positive) { if (_wrapperByControlId.ContainsKey(model.Id)) return false; continue; }
            if (!_wrapperByControlId.TryGetValue(model.Id, out var wrapper) || !ReferenceEquals(wrapper.Tag, model)
                || wrapper.Width != bounds.Width || wrapper.Height != bounds.Height) return false;
            var parent = wrapper.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Tag is DesignControlModel);
            if ((parent?.Tag as DesignControlModel)?.Id != (model.ParentId.Length == 0 ? null : model.ParentId)) return false;
        }
        if (change.AddedControlId.Length > 0)
        {
            var added = VM.GetControl(change.AddedControlId)!;
            Canvas? dropHost = DesignerCanvas;
            if (added.ParentId.Length > 0)
            {
                if (!_wrapperByControlId.TryGetValue(added.ParentId,out var parent) || parent.Child is not Canvas root) return false;
                dropHost = root.Children.OfType<Canvas>().FirstOrDefault()?.Children.OfType<Canvas>().LastOrDefault();
                if (dropHost is null) return false;
            }
            if (_wrapperByControlId.Count == 0) DesignerCanvas.Children.Clear(); // first control replaces the empty-state card
            AddRenderedControl(dropHost, added, VM.NativeLayoutBounds[added.Id], false);
        }
        foreach (var pair in _wrapperByControlId)
        {
            if (!VM.NativeLayoutBounds.TryGetValue(pair.Key, out var bounds)) return false;
            Canvas.SetLeft(pair.Value, bounds.X); Canvas.SetTop(pair.Value, bounds.Y);
            if (change.AddedControlId.Length > 0 && pair.Value.Tag is DesignControlModel model)
                RefreshNativeWrapperSelection(pair.Value,model);
            LayoutDropProfile.Count("DesignerWrappersUpdated");
        }
        var siblings = GetActiveChildControls(change.Parent).Where(c => _wrapperByControlId.ContainsKey(c.Id)).ToArray();
        if (siblings.FirstOrDefault() is { } first && _wrapperByControlId[first.Id].Parent is Canvas host)
            for (var i = 0; i < siblings.Length; i++)
            {
                var wrapper = _wrapperByControlId[siblings[i].Id];
                var index = host.Children.IndexOf(wrapper);
                if (index != i) host.Children.Move(index, i);
            }
        return true;
    }

    private void RefreshNativeWrapperSelection(Border wrapper, DesignControlModel model)
    {
        if (wrapper.Child is not Canvas root) return;
        var selected = VM.IsControlSelected(model); var primary = VM.SelectedControl?.Id == model.Id;
        wrapper.BorderBrush = new SolidColorBrush(Color.Parse(selected ? (model.IsLocked ? "#0F766E" : "#2563EB") : (model.IsLocked ? "#64748B" : "#66CBD5E1")));
        if (root.Children.OfType<Avalonia.Controls.Shapes.Rectangle>().LastOrDefault() is { } frame)
        { frame.Stroke = wrapper.BorderBrush; frame.StrokeThickness = selected ? 2 : 1; }
        foreach (var outline in root.Children.OfType<Avalonia.Controls.Shapes.Rectangle>().Where(r=>r.StrokeDashArray?.Count > 0)) outline.StrokeThickness=selected?2.5:1.5;
        foreach (var grip in root.Children.OfType<Border>().Where(b => (b.Width == 18 && b.Height == 18) || (b.Width == 11 && b.Height == 11)))
            grip.IsVisible = primary && CanResizeControl(model);
    }

    private void RenderControlNameBadge()
    {
        if (VM.IsUserPreviewMode || VM.SelectedControl is not { } model || !_wrapperByControlId.TryGetValue(model.Id, out var wrapper)) return;
        // New wrappers have not been arranged yet. Use the measured document bounds,
        // not TranslatePoint on an unarranged (0,0) visual.
        var absolute = VM.GetAbsolutePosition(model);
        var origin = new Point(absolute.X, absolute.Y);
        if (!new Rect(0,0,VM.PreviewFormWidth,VM.PreviewFormHeight).Intersects(new Rect(origin,new Size(wrapper.Width,wrapper.Height)))) return;
        // One primary-selection label, outside its frame. Names of other controls
        // remain available on hover and in the Inspector/structure tree.
        var label = new Border { Name = "DesignerControlNameBadge", IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.Parse("#0F172A")), CornerRadius = new CornerRadius(6), Padding = new Thickness(6,2),
            Child = new TextBlock { Text = model.Name, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis } };
        // Measure inside its actual style/font scope, so even short names do not
        // get ellipsized because of detached font metrics.
        SelectionOverlayCanvas.Children.Add(label);
        label.Measure(new Size(Math.Min(220, Math.Max(24, VM.PreviewFormWidth)), 24));
        var width = Math.Min(label.DesiredSize.Width, Math.Min(220, VM.PreviewFormWidth));
        var height = label.DesiredSize.Height;
        var x = Math.Clamp(origin.X, 0, Math.Max(0, VM.PreviewFormWidth-width));
        var y = origin.Y >= height+4 ? origin.Y-height-4 : origin.Y+wrapper.Height+12;
        // If there is no spare space below, use the adjacent side, never the resize grip.
        if (y+height > VM.PreviewFormHeight)
        {
            x = origin.X+wrapper.Width+12;
            if (x+width > VM.PreviewFormWidth) { x=origin.X-width-12; if(x<0) { SelectionOverlayCanvas.Children.Remove(label); return; } }
            y = Math.Clamp(origin.Y,0,Math.Max(0,VM.PreviewFormHeight-height));
        }
        label.Width=width; Canvas.SetLeft(label,x); Canvas.SetTop(label,y);
    }
}
