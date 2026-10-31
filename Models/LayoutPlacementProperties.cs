using Avalonia.Controls;
using System;
using System.Linq;
using System.Text.Json;

namespace FormDesigner.Models;

// Typed accessors over the existing serialized property bag; no second document model.
public static class LayoutPlacementProperties
{
    public const string DockKey = "DockPanel.Dock";
    public const string LastChildFillKey = "DockPanel.LastChildFill";
    public const string MinWidthKey = "MinWidth";
    public const string MinHeightKey = "MinHeight";
    public const string MaxWidthKey = "MaxWidth";
    public const string MaxHeightKey = "MaxHeight";
    public static readonly string[] SizeConstraintKeys = { MinWidthKey, MinHeightKey, MaxWidthKey, MaxHeightKey };

    public static double Minimum(DesignControlModel model, string key)
        => double.TryParse(model.CustomProperties.FirstOrDefault(p => p.Key == key)?.ValueJson,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : 0;

    public static double Maximum(DesignControlModel model, string key)
        => double.TryParse(model.CustomProperties.FirstOrDefault(p => p.Key == key)?.ValueJson,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : double.PositiveInfinity;

    public static Dock GetDock(DesignControlModel model) => Enum.TryParse<Dock>(ReadString(model, DockKey), out var dock) && Enum.IsDefined(dock) ? dock : Dock.Left;
    public static bool LastChildFill(DesignControlModel model)
    {
        var property = model.CustomProperties.FirstOrDefault(p => p.Key == LastChildFillKey);
        return property is null || property.ValueJson != "false";
    }

    public static string? ReadString(DesignControlModel model, string key)
    {
        try { return JsonSerializer.Deserialize<string>(model.CustomProperties.FirstOrDefault(p => p.Key == key)?.ValueJson ?? "null"); }
        catch (JsonException) { return null; }
    }

    public static void SetLiteral(DesignControlModel model, string key, object value)
    {
        var property = model.CustomProperties.FirstOrDefault(p => p.Key == key);
        if (property is null) model.CustomProperties.Add(new DesignPropertyValueModel { Key = key, ValueJson = JsonSerializer.Serialize(value) });
        else property.ValueJson = JsonSerializer.Serialize(value);
    }

    public static string? ProtectedPlacement(DesignControlModel model)
    {
        foreach (var property in model.CustomProperties)
        {
            if (property.Key is not ("Canvas.Left" or "Canvas.Top" or "X" or "Y" or "Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan" or "StackOrder" or DockKey)) continue;
            // Dock literals are owned by this adapter. Other custom placement values have
            // no unambiguous mapping to the built-in numeric fields; preserve them by refusing.
            if (property.Key == DockKey && Enum.TryParse<Dock>(ReadString(model, DockKey), out var dock) && Enum.IsDefined(dock)) continue;
            return property.Key;
        }
        return null;
    }
}
