using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FormDesigner.Models;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.AxamlRoundTrip;

public static partial class AxamlLayoutProjection
{
    public sealed record LayoutChildMeasurement(string Id, Rect Bounds, Thickness Margin, bool Visible);
    public sealed record ContainerMeasurement(string Mode, Rect Bounds, Rect Clip,
        bool Horizontal, bool LastChildFill, IReadOnlyList<LayoutChildMeasurement> Children);
    public sealed record LayoutVisualHit(string Id, Rect Bounds, Rect Clip);

    public sealed partial record JsonLayoutTree
    {
        public IReadOnlyDictionary<string, Panel> ContainerPanels { get; init; } = new Dictionary<string, Panel>();
        internal IReadOnlyDictionary<string, bool> ModelVisibility { get; init; } = new Dictionary<string, bool>();
        public IReadOnlyDictionary<string, ContainerMeasurement> MeasuredContainers { get; private set; } = new Dictionary<string, ContainerMeasurement>();
        public IReadOnlyList<LayoutVisualHit> VisualHits { get; private set; } = new List<LayoutVisualHit>();

        private void CaptureDropGeometry()
        {
            var ids = Controls.ToDictionary(p => p.Value, p => p.Key);
            var clips = new Dictionary<Visual, Rect>(); var hits = new List<LayoutVisualHit>();
            Rect Bounds(Visual visual) => new(visual.TranslatePoint(default, Root) ?? default, visual.Bounds.Size);
            void Visit(Visual visual, Rect clip, bool visible)
            {
                var bounds = Bounds(visual);
                visible &= visual.IsVisible && visual.Opacity > 0.001;
                if (ids.TryGetValue(visual as Control ?? Root, out var id))
                {
                    visible &= ModelVisibility[id];
                    // The editor's existing child-host Canvas clips to its model wrapper.
                    if (ContainerPanels.ContainsKey(id)) clip = clip.Intersect(bounds);
                    if (visible) hits.Add(new(id, bounds, clip));
                }
                if (visual.ClipToBounds) clip = clip.Intersect(bounds);
                if (visual.Clip is { } geometry)
                    clip = clip.Intersect(geometry.Bounds.Translate(new Vector(bounds.X, bounds.Y)));
                clips[visual] = visible ? clip : default;
                // Capture actual native paint hierarchy, not a flat deepest-rectangle guess.
                foreach (var child in visual.GetVisualChildren().Select((view, index) => (view, index))
                    .OrderBy(p => p.view.ZIndex).ThenBy(p => p.index)) Visit(child.view, clip, visible);
            }
            Visit(Root, new Rect(Root.Bounds.Size), true);
            VisualHits = hits;
            MeasuredContainers = ContainerPanels.ToDictionary(pair => pair.Key, pair =>
            {
                var panel = pair.Value;
                var mode = panel switch { Grid => DesignerLayoutModes.Grid, StackPanel => DesignerLayoutModes.Stack,
                    WrapPanel => DesignerLayoutModes.Flex, DockPanel => DesignerLayoutModes.Dock, _ => DesignerLayoutModes.Absolute };
                var children = panel.Children.Where(ids.ContainsKey).Select(c => new LayoutChildMeasurement(ids[c], Bounds(c), c.Margin, ModelVisibility[ids[c]])).ToArray();
                var horizontal = panel is StackPanel stack ? stack.Orientation == Avalonia.Layout.Orientation.Horizontal
                    : panel is WrapPanel wrap && wrap.Orientation == Avalonia.Layout.Orientation.Horizontal;
                return new ContainerMeasurement(mode, Bounds(panel), clips[panel], horizontal, panel is DockPanel { LastChildFill: true }, children);
            });
        }
    }
}
