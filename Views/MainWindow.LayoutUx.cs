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
using System.Text.Json;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private MainWindowViewModel.NativeDropRenderChange? _nativeDropVisualChange;
    private string _nativeRenderedSelection = "";
    private int _nativeRenderedSelectionCount;
    private string _renderedPrimaryControlId="";
    private bool _hasPendingAuthoredDesignerChange;
    private IPointer? _contextMenuPointer;
    private readonly System.Collections.Generic.Dictionary<string,string> _nativeContentKeys=new();
    private int _previewContentRevision;
    private static bool HasReusableDataPreview(DesignControlModel model)=>model.Type is "DataGrid" or "Eremex.DataGridControl" or "Eremex.TextEditor";
    private string? NativeContentKey(DesignControlModel model)
    {
        try
        {
            var copy=model.Clone();copy.Id=model.Id;copy.ParentId="";copy.X=copy.Y=0;copy.GridRow=copy.GridColumn=copy.StackOrder=0;copy.GridRowSpan=copy.GridColumnSpan=1;
            // Private, in-memory comparison only: never log or persist these values.
            return JsonSerializer.Serialize(new{Model=copy,Source=VM.GetBindingSource(model.BindingSourceId)?.Clone(),Revision=_previewContentRevision,
                Selected= model.Type=="DataGrid"&&VM.IsControlSelected(model)});
        }
        catch(Exception ex) when(ex is JsonException or NotSupportedException or ArgumentException){return null;}
    }
    private void InvalidateNativePreviewContent(){_previewContentRevision++;_nativeDropVisualChange=null;}
    private void CancelPendingContextMenu()
    {
        _pendingContextMenuControlId = "";
        var pointer = _contextMenuPointer; _contextMenuPointer = null;
        if (ReferenceEquals(pointer?.Captured, DesignerCanvas)) pointer.Capture(null);
    }
    private string NativeSelectionStamp() => VM.DocumentSessionId + ":" + VM.SelectedControl?.Id + ":" + string.Join(",", VM.SelectedControlIds.OrderBy(id => id));

    private bool TryAppendLegacyCanvasControl(DesignControlModel added)
    {
        using var stage=LayoutDropProfile.Stage("AppendCanvasWrapper");
        bool Plain(DesignControlModel c)=>c.Type is "Button" or "TextBox" or "TextBlock" && string.IsNullOrEmpty(c.BindingSourceId)&&string.IsNullOrEmpty(c.TextBindingPath);
        if(VM.UsesNativeLayout||VM.IsAxamlRoundTripDocument||VM.IsUserPreviewMode||!DesignerLayoutModes.IsAbsolute(VM.SurfaceLayoutMode)
            ||_nativeRenderedSelectionCount>1||added.ParentId.Length!=0||VM.Controls.Any(c=>c.ParentId.Length!=0||!Plain(c))
            ||_wrapperByControlId.Count!=VM.Controls.Count-1)return false;
        foreach(var model in VM.Controls.Where(c=>c.Id!=added.Id))
            if(!_wrapperByControlId.TryGetValue(model.Id,out var wrapper)||!ReferenceEquals(wrapper.Tag,model))return false;
        _designerRenderTimer.Stop();_isDesignerRenderScheduled=false;_hasPendingAuthoredDesignerChange=false;_nativeDropVisualChange=null;
        if(_wrapperByControlId.Count==0)DesignerCanvas.Children.Clear();
        // Exactly the same Rect as the existing Absolute branch of AddControlsToCanvas.
        AddRenderedControl(DesignerCanvas,added,new Rect(added.X,added.Y,added.Width,added.Height),false);
        if(_wrapperByControlId.TryGetValue(_renderedPrimaryControlId,out var previous)&&previous.Tag is DesignControlModel old)
        {RefreshNativeWrapperSelection(previous,old);previous.ZIndex=VM.GetCanvasVisualZIndex(old);LayoutDropProfile.Count("DesignerWrappersUpdated");}
        ClearGuideOverlay();RenderIdleSelectionOverlay();RenderControlNameBadge();RenderMiniMap();
        _nativeRenderedSelection=NativeSelectionStamp();_nativeRenderedSelectionCount=VM.SelectedControlIds.Count;_renderedPrimaryControlId=added.Id;
        VM.MarkCanvasRenderedDocument("CanvasDropAppend",_wrapperByControlId.Count,_previewRowsBySourceKey.Values.Sum(p=>p.Rows.Count));return true;
    }

    private bool TryUpdateNativeDropVisuals(MainWindowViewModel.NativeDropRenderChange change)
    {
        using var stage = LayoutDropProfile.Stage("UpdateExistingWrappers");
        bool Reject(string reason){LayoutDropProfile.Count("ReuseRejected_"+reason);return false;}
        // Data/plugin/item previews can refresh independently of placement (for
        // example an async row cache). Preserve their existing full refresh path.
        if (VM.Controls.Any(c => !VM.CanHostChildren(c)&&!HasReusableDataPreview(c)
            && (c.Type is not (DesignerControlTypes.Button or DesignerControlTypes.TextBox or DesignerControlTypes.TextBlock)
                || !string.IsNullOrEmpty(c.BindingSourceId) || !string.IsNullOrEmpty(c.TextBindingPath)))) return Reject("ContentKind");
        var refreshData=VM.Controls.Where(HasReusableDataPreview).Where(c=>!_nativeContentKeys.TryGetValue(c.Id,out var key)||key!=NativeContentKey(c)).Select(c=>c.Id).ToHashSet();
        Canvas? Host(string parentId)
        {
            if(parentId.Length==0)return DesignerCanvas;
            return _wrapperByControlId.TryGetValue(parentId,out var parent)&&parent.Child is Canvas root
                ?root.Children.OfType<Canvas>().FirstOrDefault()?.Children.OfType<Canvas>().LastOrDefault():null;
        }
        var create=new System.Collections.Generic.List<DesignControlModel>();
        var remove=new System.Collections.Generic.List<string>();
        Border? reparent=null;
        // Only leaf wrappers may be replaced/moved. Changing a container's own
        // geometry or an authored edit still requires the existing full renderer.
        var ids=VM.Controls.Select(c=>c.Id).ToHashSet();
        if(_wrapperByControlId.Keys.Any(id=>!ids.Contains(id)))return false;
        foreach (var model in VM.Controls)
        {
            var positive = VM.NativeLayoutBounds.TryGetValue(model.Id, out var bounds) && bounds.Width > 0 && bounds.Height > 0;
            var exists=_wrapperByControlId.TryGetValue(model.Id,out var wrapper);
            if(model.Id==change.AddedControlId&&exists)return false;
            if(!positive)
            {if(exists){if(VM.CanHostChildren(model))return false;remove.Add(model.Id);}continue;}
            if(!exists)
            {if(model.Id!=change.AddedControlId&&VM.CanHostChildren(model))return false;create.Add(model);continue;}
            if(!ReferenceEquals(wrapper!.Tag,model))return false;
            if(wrapper.Width!=bounds.Width||wrapper.Height!=bounds.Height||refreshData.Contains(model.Id))
            {if(VM.CanHostChildren(model))return Reject("ContainerSize");remove.Add(model.Id);create.Add(model);continue;}
            var parent = wrapper.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Tag is DesignControlModel);
            if ((parent?.Tag as DesignControlModel)?.Id != (model.ParentId.Length == 0 ? null : model.ParentId))
            {if(model.Id!=change.MovedControlId||VM.CanHostChildren(model)||Host(model.ParentId) is null)return false;reparent=wrapper;}
        }
        if(create.Any(m=>Host(m.ParentId) is null))return false;
        foreach(var id in remove)
        {
            var wrapper=_wrapperByControlId[id];(wrapper.Parent as Canvas)?.Children.Remove(wrapper);_wrapperByControlId.Remove(id);
            _nativeContentKeys.Remove(id);
            LayoutDropProfile.Count("DesignerWrappersReplaced");
        }
        if(reparent is not null){(reparent.Parent as Canvas)!.Children.Remove(reparent);Host(((DesignControlModel)reparent.Tag!).ParentId)!.Children.Add(reparent);}
        if(_wrapperByControlId.Count==0)DesignerCanvas.Children.Clear();
        foreach(var model in create)AddRenderedControl(Host(model.ParentId)!,model,VM.NativeLayoutBounds[model.Id],false);
        foreach (var pair in _wrapperByControlId)
        {
            if (!VM.NativeLayoutBounds.TryGetValue(pair.Key, out var bounds)) return false;
            Canvas.SetLeft(pair.Value, bounds.X); Canvas.SetTop(pair.Value, bounds.Y);
            if ((change.AddedControlId.Length > 0||pair.Key==change.MovedControlId) && pair.Value.Tag is DesignControlModel model)
                RefreshNativeWrapperSelection(pair.Value,model);
            LayoutDropProfile.Count("DesignerWrappersUpdated");
            LayoutDropProfile.Affect(pair.Key);
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
        if(!VM.UsesNativeLayout)wrapper.BorderThickness=new Thickness(selected?(primary?2:1.5):1);
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
