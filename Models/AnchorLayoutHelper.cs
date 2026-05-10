namespace FormDesigner.Models;

/// <summary>
/// Рассчитывает фактическое положение и размер контрола по WinForms-подобным Anchor-флагам.
/// Базовые X/Y/Width/Height трактуются как дизайн-координаты внутри контейнера исходного размера.
/// </summary>
public static class AnchorLayoutHelper
{
    public static AnchoredFrame ResolveFrame(
        double x,
        double y,
        double width,
        double height,
        double baseParentWidth,
        double baseParentHeight,
        double actualParentWidth,
        double actualParentHeight,
        bool anchorLeft,
        bool anchorTop,
        bool anchorRight,
        bool anchorBottom)
    {
        var left = x;
        var top = y;
        var right = baseParentWidth - (x + width);
        var bottom = baseParentHeight - (y + height);

        double resolvedX;
        double resolvedWidth;
        if (anchorLeft && anchorRight)
        {
            resolvedX = left;
            resolvedWidth = actualParentWidth - left - right;
        }
        else if (!anchorLeft && anchorRight)
        {
            resolvedWidth = width;
            resolvedX = actualParentWidth - right - resolvedWidth;
        }
        else
        {
            resolvedX = left;
            resolvedWidth = width;
        }

        double resolvedY;
        double resolvedHeight;
        if (anchorTop && anchorBottom)
        {
            resolvedY = top;
            resolvedHeight = actualParentHeight - top - bottom;
        }
        else if (!anchorTop && anchorBottom)
        {
            resolvedHeight = height;
            resolvedY = actualParentHeight - bottom - resolvedHeight;
        }
        else
        {
            resolvedY = top;
            resolvedHeight = height;
        }

        return new AnchoredFrame(
            resolvedX,
            resolvedY,
            resolvedWidth < 0 ? 0 : resolvedWidth,
            resolvedHeight < 0 ? 0 : resolvedHeight);
    }
}

public readonly record struct AnchoredFrame(double X, double Y, double Width, double Height);
