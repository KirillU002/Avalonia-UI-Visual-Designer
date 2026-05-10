using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.Infrastructure;

internal static class DesignControlPropertyBag
{
    public static void SetCustomProperty(ICollection<DesignPropertyValueModel> values, string key, string valueJson)
    {
        var existing = values.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.ValueJson = valueJson;
            return;
        }

        values.Add(new DesignPropertyValueModel
        {
            Key = key,
            ValueJson = valueJson
        });
    }

    public static Dictionary<string, string> ToDictionary(IEnumerable<DesignPropertyValueModel> values)
    {
        return values.ToDictionary(value => value.Key, value => value.ValueJson, StringComparer.OrdinalIgnoreCase);
    }
}

internal static class BuiltInPropertyMap
{
    public static IReadOnlyDictionary<string, object?> Create(
        string text,
        string placeholderText,
        string imageSource,
        string background,
        string foreground,
        string borderBrush,
        double borderThickness,
        double cornerRadius,
        string fontFamily,
        double fontSize,
        string fontWeight,
        double opacity,
        double padding,
        string layoutOrientation,
        double layoutSpacing,
        bool isVisible,
        string stretch,
        double x,
        double y,
        double width,
        double height,
        bool anchorLeft,
        bool anchorTop,
        bool anchorRight,
        bool anchorBottom,
        int columns,
        int rows,
        bool showGridLines,
        bool autoGenerateColumns,
        string bindingSourceId,
        string textBindingPath,
        string generatedButtonActionKey,
        string dataGridRowBackground,
        string dataGridAlternateRowBackground,
        bool showFilterRow,
        string filterMode,
        bool showGroupPanel,
        bool allowGrouping)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(DesignControlModel.Text)] = text,
            [nameof(DesignControlModel.PlaceholderText)] = placeholderText,
            [nameof(DesignControlModel.ImageSource)] = imageSource,
            [nameof(DesignControlModel.Background)] = background,
            [nameof(DesignControlModel.Foreground)] = foreground,
            [nameof(DesignControlModel.BorderBrush)] = borderBrush,
            [nameof(DesignControlModel.BorderThickness)] = borderThickness,
            [nameof(DesignControlModel.CornerRadius)] = cornerRadius,
            [nameof(DesignControlModel.FontFamily)] = fontFamily,
            [nameof(DesignControlModel.FontSize)] = fontSize,
            [nameof(DesignControlModel.FontWeight)] = fontWeight,
            [nameof(DesignControlModel.Opacity)] = opacity,
            [nameof(DesignControlModel.Padding)] = padding,
            [nameof(DesignControlModel.LayoutOrientation)] = layoutOrientation,
            [nameof(DesignControlModel.LayoutSpacing)] = layoutSpacing,
            [nameof(DesignControlModel.IsVisible)] = isVisible,
            [nameof(DesignControlModel.Stretch)] = stretch,
            [nameof(DesignControlModel.X)] = x,
            [nameof(DesignControlModel.Y)] = y,
            [nameof(DesignControlModel.Width)] = width,
            [nameof(DesignControlModel.Height)] = height,
            [nameof(DesignControlModel.AnchorLeft)] = anchorLeft,
            [nameof(DesignControlModel.AnchorTop)] = anchorTop,
            [nameof(DesignControlModel.AnchorRight)] = anchorRight,
            [nameof(DesignControlModel.AnchorBottom)] = anchorBottom,
            [nameof(DesignControlModel.Columns)] = columns,
            [nameof(DesignControlModel.Rows)] = rows,
            [nameof(DesignControlModel.ShowGridLines)] = showGridLines,
            [nameof(DesignControlModel.AutoGenerateColumns)] = autoGenerateColumns,
            [nameof(DesignControlModel.BindingSourceId)] = bindingSourceId,
            [nameof(DesignControlModel.TextBindingPath)] = textBindingPath,
            [nameof(DesignControlModel.GeneratedButtonActionKey)] = generatedButtonActionKey,
            [nameof(DesignControlModel.DataGridRowBackground)] = dataGridRowBackground,
            [nameof(DesignControlModel.DataGridAlternateRowBackground)] = dataGridAlternateRowBackground,
            [nameof(DesignControlModel.ShowFilterRow)] = showFilterRow,
            [nameof(DesignControlModel.FilterMode)] = filterMode,
            [nameof(DesignControlModel.ShowGroupPanel)] = showGroupPanel,
            [nameof(DesignControlModel.AllowGrouping)] = allowGrouping
        };
    }
}

internal sealed class DesignControlNodeAdapter : IDesignControlNode
{
    public DesignControlNodeAdapter(DesignControlModel model)
    {
        Model = model;
    }

    public DesignControlModel Model { get; }
    public string Id => Model.Id;
    public string TypeKey => Model.Type;
    public string Name => Model.Name;
    public string ParentId => Model.ParentId;
    public string DescriptorId => Model.DescriptorId;
    public string PluginId => Model.PluginId;
    public string PluginVersion => Model.PluginVersion;

    public IReadOnlyDictionary<string, object?> BuiltInProperties => BuiltInPropertyMap.Create(
        Model.Text,
        Model.PlaceholderText,
        Model.ImageSource,
        Model.Background,
        Model.Foreground,
        Model.BorderBrush,
        Model.BorderThickness,
        Model.CornerRadius,
        Model.FontFamily,
        Model.FontSize,
        Model.FontWeight,
        Model.Opacity,
        Model.Padding,
        Model.LayoutOrientation,
        Model.LayoutSpacing,
        Model.IsVisible,
        Model.Stretch,
        Model.X,
        Model.Y,
        Model.Width,
        Model.Height,
        Model.AnchorLeft,
        Model.AnchorTop,
        Model.AnchorRight,
        Model.AnchorBottom,
        Model.Columns,
        Model.Rows,
        Model.ShowGridLines,
        Model.AutoGenerateColumns,
        Model.BindingSourceId,
        Model.TextBindingPath,
        Model.GeneratedButtonActionKey,
        Model.DataGridRowBackground,
        Model.DataGridAlternateRowBackground,
        Model.ShowFilterRow,
        Model.FilterMode,
        Model.ShowGroupPanel,
        Model.AllowGrouping);

    public IReadOnlyDictionary<string, string> CustomProperties => DesignControlPropertyBag.ToDictionary(Model.CustomProperties);
}

internal sealed class DesignControlFileNodeAdapter : IDesignControlNode
{
    public DesignControlFileNodeAdapter(DesignerControlFileModel model)
    {
        Model = model;
    }

    public DesignerControlFileModel Model { get; }
    public string Id => Model.Id;
    public string TypeKey => Model.Type;
    public string Name => Model.Name;
    public string ParentId => Model.ParentId;
    public string DescriptorId => Model.DescriptorId;
    public string PluginId => Model.PluginId;
    public string PluginVersion => Model.PluginVersion;

    public IReadOnlyDictionary<string, object?> BuiltInProperties => BuiltInPropertyMap.Create(
        Model.Text,
        Model.PlaceholderText,
        Model.ImageSource,
        Model.Background,
        Model.Foreground,
        Model.BorderBrush,
        Model.BorderThickness,
        Model.CornerRadius,
        Model.FontFamily,
        Model.FontSize,
        Model.FontWeight,
        Model.Opacity,
        Model.Padding,
        Model.LayoutOrientation,
        Model.LayoutSpacing,
        Model.IsVisible,
        Model.Stretch,
        Model.X,
        Model.Y,
        Model.Width,
        Model.Height,
        Model.AnchorLeft,
        Model.AnchorTop,
        Model.AnchorRight,
        Model.AnchorBottom,
        Model.Columns,
        Model.Rows,
        Model.ShowGridLines,
        Model.AutoGenerateColumns,
        Model.BindingSourceId,
        Model.TextBindingPath,
        Model.GeneratedButtonActionKey,
        Model.DataGridRowBackground,
        Model.DataGridAlternateRowBackground,
        Model.ShowFilterRow,
        Model.FilterMode,
        Model.ShowGroupPanel,
        Model.AllowGrouping);

    public IReadOnlyDictionary<string, string> CustomProperties => Model.CustomProperties.ToDictionary(value => value.Key, value => value.ValueJson, StringComparer.OrdinalIgnoreCase);
}

internal static class DesignControlDefinitionMapper
{
    public static DesignControlModel ToRuntimeModel(DesignerControlDefinition definition)
    {
        var model = new DesignControlModel
        {
            Type = definition.TypeKey,
            DescriptorId = definition.DescriptorId,
            PluginId = definition.PluginId,
            PluginVersion = definition.PluginVersion
        };

        foreach (var property in definition.BuiltInProperties)
            ApplyBuiltInProperty(model, property.Key, property.Value);

        foreach (var property in definition.CustomProperties)
            DesignControlPropertyBag.SetCustomProperty(model.CustomProperties, property.Key, property.Value);

        return model;
    }

    private static void ApplyBuiltInProperty(DesignControlModel model, string key, object? value)
    {
        switch (key)
        {
            case nameof(DesignControlModel.Name): model.Name = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.Text): model.Text = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.PlaceholderText): model.PlaceholderText = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.ImageSource): model.ImageSource = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.Background): model.Background = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.Foreground): model.Foreground = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.BorderBrush): model.BorderBrush = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.BorderThickness): model.BorderThickness = ConvertToDouble(value, model.BorderThickness); break;
            case nameof(DesignControlModel.CornerRadius): model.CornerRadius = ConvertToDouble(value, model.CornerRadius); break;
            case nameof(DesignControlModel.FontFamily): model.FontFamily = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.FontSize): model.FontSize = ConvertToDouble(value, model.FontSize); break;
            case nameof(DesignControlModel.FontWeight): model.FontWeight = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.Opacity): model.Opacity = ConvertToDouble(value, model.Opacity); break;
            case nameof(DesignControlModel.Padding): model.Padding = ConvertToDouble(value, model.Padding); break;
            case nameof(DesignControlModel.LayoutOrientation): model.LayoutOrientation = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.LayoutSpacing): model.LayoutSpacing = ConvertToDouble(value, model.LayoutSpacing); break;
            case nameof(DesignControlModel.IsVisible): model.IsVisible = ConvertToBool(value, model.IsVisible); break;
            case nameof(DesignControlModel.Stretch): model.Stretch = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.X): model.X = ConvertToDouble(value, model.X); break;
            case nameof(DesignControlModel.Y): model.Y = ConvertToDouble(value, model.Y); break;
            case nameof(DesignControlModel.Width): model.Width = ConvertToDouble(value, model.Width); break;
            case nameof(DesignControlModel.Height): model.Height = ConvertToDouble(value, model.Height); break;
            case nameof(DesignControlModel.AnchorLeft): model.AnchorLeft = ConvertToBool(value, model.AnchorLeft); break;
            case nameof(DesignControlModel.AnchorTop): model.AnchorTop = ConvertToBool(value, model.AnchorTop); break;
            case nameof(DesignControlModel.AnchorRight): model.AnchorRight = ConvertToBool(value, model.AnchorRight); break;
            case nameof(DesignControlModel.AnchorBottom): model.AnchorBottom = ConvertToBool(value, model.AnchorBottom); break;
            case nameof(DesignControlModel.Columns): model.Columns = ConvertToInt(value, model.Columns); break;
            case nameof(DesignControlModel.Rows): model.Rows = ConvertToInt(value, model.Rows); break;
            case nameof(DesignControlModel.ShowGridLines): model.ShowGridLines = ConvertToBool(value, model.ShowGridLines); break;
            case nameof(DesignControlModel.AutoGenerateColumns): model.AutoGenerateColumns = ConvertToBool(value, model.AutoGenerateColumns); break;
            case nameof(DesignControlModel.BindingSourceId): model.BindingSourceId = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.TextBindingPath): model.TextBindingPath = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.GeneratedButtonActionKey): model.GeneratedButtonActionKey = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.DataGridRowBackground): model.DataGridRowBackground = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.DataGridAlternateRowBackground): model.DataGridAlternateRowBackground = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.ShowFilterRow): model.ShowFilterRow = ConvertToBool(value, model.ShowFilterRow); break;
            case nameof(DesignControlModel.FilterMode): model.FilterMode = value?.ToString() ?? ""; break;
            case nameof(DesignControlModel.ShowGroupPanel): model.ShowGroupPanel = ConvertToBool(value, model.ShowGroupPanel); break;
            case nameof(DesignControlModel.AllowGrouping): model.AllowGrouping = ConvertToBool(value, model.AllowGrouping); break;
        }
    }

    private static double ConvertToDouble(object? value, double fallback)
    {
        return value switch
        {
            null => fallback,
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            decimal number => (double)number,
            _ when double.TryParse(value.ToString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static int ConvertToInt(object? value, int fallback)
    {
        return value switch
        {
            null => fallback,
            int number => number,
            long number => (int)number,
            short number => number,
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static bool ConvertToBool(object? value, bool fallback)
    {
        return value switch
        {
            null => fallback,
            bool result => result,
            _ when bool.TryParse(value.ToString(), out var parsed) => parsed,
            _ => fallback
        };
    }
}
