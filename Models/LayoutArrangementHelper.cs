using System;
using System.Collections.Generic;

namespace FormDesigner.Models;

/// <summary>
/// Чистая геометрия auto-layout без привязки к Avalonia-контролам.
/// Один и тот же helper используется дизайнером и окном preview,
/// чтобы форма раскладывалась одинаково в обоих режимах.
/// </summary>
public static class LayoutArrangementHelper
{
    public readonly record struct ChildSnapshot(string Id, double Width, double Height);
    public readonly record struct ChildFrame(string Id, double X, double Y, double Width, double Height);

    public static IReadOnlyList<ChildFrame> ArrangeChildren(
        string layoutMode,
        string orientation,
        double spacing,
        int columns,
        int rows,
        double padding,
        double availableWidth,
        double availableHeight,
        IReadOnlyList<ChildSnapshot> children)
    {
        var normalizedMode = DesignerLayoutModes.NormalizeMode(layoutMode);
        var normalizedOrientation = DesignerLayoutModes.NormalizeOrientation(orientation);
        var normalizedSpacing = Math.Max(0, spacing);
        var normalizedPadding = Math.Max(0, padding);
        var innerWidth = Math.Max(0, availableWidth - (normalizedPadding * 2));
        var innerHeight = Math.Max(0, availableHeight - (normalizedPadding * 2));

        return normalizedMode switch
        {
            DesignerLayoutModes.Stack => ArrangeStack(normalizedOrientation, normalizedSpacing, normalizedPadding, innerWidth, innerHeight, children),
            DesignerLayoutModes.Grid => ArrangeGrid(normalizedSpacing, normalizedPadding, innerWidth, innerHeight, columns, rows, children),
            DesignerLayoutModes.Flex => ArrangeFlex(normalizedOrientation, normalizedSpacing, normalizedPadding, innerWidth, innerHeight, children),
            _ => Array.Empty<ChildFrame>()
        };
    }

    private static IReadOnlyList<ChildFrame> ArrangeStack(
        string orientation,
        double spacing,
        double padding,
        double innerWidth,
        double innerHeight,
        IReadOnlyList<ChildSnapshot> children)
    {
        var frames = new List<ChildFrame>(children.Count);
        var x = padding;
        var y = padding;

        foreach (var child in children)
        {
            var width = ClampLength(child.Width, innerWidth);
            var height = ClampLength(child.Height, innerHeight);
            frames.Add(new ChildFrame(child.Id, x, y, width, height));

            if (orientation == DesignerLayoutModes.Horizontal)
                x += width + spacing;
            else
                y += height + spacing;
        }

        return frames;
    }

    private static IReadOnlyList<ChildFrame> ArrangeGrid(
        double spacing,
        double padding,
        double innerWidth,
        double innerHeight,
        int columns,
        int rows,
        IReadOnlyList<ChildSnapshot> children)
    {
        var frames = new List<ChildFrame>(children.Count);
        var normalizedColumns = Math.Max(1, columns);
        var normalizedRows = Math.Max(Math.Max(1, rows), (int)Math.Ceiling(children.Count / (double)normalizedColumns));
        var cellWidth = Math.Max(40, (innerWidth - (Math.Max(0, normalizedColumns - 1) * spacing)) / normalizedColumns);
        var cellHeight = Math.Max(24, (innerHeight - (Math.Max(0, normalizedRows - 1) * spacing)) / normalizedRows);

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var column = index % normalizedColumns;
            var row = index / normalizedColumns;
            var x = padding + (column * (cellWidth + spacing));
            var y = padding + (row * (cellHeight + spacing));
            var width = Math.Min(Math.Max(40, child.Width), cellWidth);
            var height = Math.Min(Math.Max(24, child.Height), cellHeight);
            frames.Add(new ChildFrame(child.Id, x, y, width, height));
        }

        return frames;
    }

    private static IReadOnlyList<ChildFrame> ArrangeFlex(
        string orientation,
        double spacing,
        double padding,
        double innerWidth,
        double innerHeight,
        IReadOnlyList<ChildSnapshot> children)
    {
        var frames = new List<ChildFrame>(children.Count);
        var x = padding;
        var y = padding;
        var lineExtent = 0d;

        foreach (var child in children)
        {
            var width = ClampLength(child.Width, innerWidth);
            var height = ClampLength(child.Height, innerHeight);

            if (orientation == DesignerLayoutModes.Horizontal)
            {
                if (x > padding && x + width > padding + innerWidth)
                {
                    x = padding;
                    y += lineExtent + spacing;
                    lineExtent = 0;
                }

                frames.Add(new ChildFrame(child.Id, x, y, width, height));
                x += width + spacing;
                lineExtent = Math.Max(lineExtent, height);
            }
            else
            {
                if (y > padding && y + height > padding + innerHeight)
                {
                    y = padding;
                    x += lineExtent + spacing;
                    lineExtent = 0;
                }

                frames.Add(new ChildFrame(child.Id, x, y, width, height));
                y += height + spacing;
                lineExtent = Math.Max(lineExtent, width);
            }
        }

        return frames;
    }

    private static double ClampLength(double value, double max)
    {
        var normalized = Math.Max(0, value);
        if (max <= 0)
            return normalized;

        return Math.Min(normalized, max);
    }
}
