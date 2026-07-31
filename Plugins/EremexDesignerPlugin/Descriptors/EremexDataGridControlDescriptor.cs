using Avalonia;
using Avalonia.Controls;
using Eremex.AvaloniaUI.Controls.DataGrid;
using EremexDesignerPlugin.Services;
using FormDesigner.PluginContracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EremexDesignerPlugin.Descriptors;

/// <summary>
/// First Eremex grid vertical slice. It intentionally uses the documented
/// AutoGenerateColumns path; explicit column collections are a later adapter scope.
/// </summary>
public sealed class EremexDataGridControlDescriptor : IControlDescriptor, IDesignerControlProviderMetadata
{
    public const string TypeKeyValue = "Eremex.DataGridControl";
    public const string XmlNamespace = "https://schemas.eremexcontrols.net/avalonia/datagrid";

    private const string AutoGenerateColumnsProperty = "AutoGenerateColumns";
    private const string BindingSourceIdProperty = "BindingSourceId";
    private const string ShowColumnHeadersProperty = "ShowColumnHeaders";
    private const string ShowAutoFilterRowProperty = "ShowAutoFilterRow";
    private const string ShowGroupPanelProperty = "ShowGroupPanel";
    private const string AllowSortingProperty = "AllowSorting";
    private const string AllowEditingProperty = "AllowEditing";
    private const string AllowColumnResizingProperty = "AllowColumnResizing";
    private const string AllowColumnMovingProperty = "AllowColumnMoving";
    private const string ShowHorizontalLinesProperty = "ShowHorizontalLines";
    private const string ShowVerticalLinesProperty = "ShowVerticalLines";
    private const string IsSearchPanelVisibleProperty = "IsSearchPanelVisible";
    private const string RowMinHeightProperty = "RowMinHeight";
    private const string NavigationModeProperty = "NavigationMode";
    private const string SelectionModeProperty = "SelectionMode";
    private const string SearchPanelDisplayModeProperty = "SearchPanelDisplayMode";

    private static readonly string[] BooleanPropertyNames =
    {
        ShowColumnHeadersProperty,
        ShowAutoFilterRowProperty,
        ShowGroupPanelProperty,
        AllowSortingProperty,
        AllowEditingProperty,
        AllowColumnResizingProperty,
        AllowColumnMovingProperty,
        ShowHorizontalLinesProperty,
        ShowVerticalLinesProperty,
        IsSearchPanelVisibleProperty
    };

    private static readonly string[] EnumPropertyNames =
    {
        NavigationModeProperty,
        SelectionModeProperty,
        SearchPanelDisplayModeProperty
    };

    private readonly string _pluginId;
    private readonly string _pluginVersion;
    private readonly IReadOnlyList<DesignPropertyDescriptor> _properties;

    public EremexDataGridControlDescriptor(string pluginId, string pluginVersion)
    {
        _pluginId = pluginId;
        _pluginVersion = pluginVersion;
        _properties = BuildPropertySchema();
    }

    public string TypeKey => TypeKeyValue;
    public string Title => "DataGridControl";
    public string Category => "Data";
    public string Description => "Eremex data grid. Uses a BindingSource and automatic columns in the first integration stage.";
    public bool IsContainer => false;
    public bool CanHostChildren => false;
    public string ChildLayoutMode => "Absolute";
    public IReadOnlyList<DesignPropertyDescriptor> Properties => _properties;
    public string ProviderId => "Eremex";
    public string ProviderTitle => "Eremex";
    public string ToolboxGroup => "Eremex";
    public string ToolboxBadge => "EMX";
    public int ToolboxGroupOrder => 100;

    public DesignerControlDefinition CreateDefaultDefinition(IDescriptorContext context)
    {
        var definition = new DesignerControlDefinition
        {
            TypeKey = TypeKey,
            DescriptorId = TypeKey,
            PluginId = _pluginId,
            PluginVersion = _pluginVersion
        };

        definition.BuiltInProperties["Width"] = 700d;
        definition.BuiltInProperties["Height"] = 380d;
        definition.BuiltInProperties["Margin"] = "0";
        definition.BuiltInProperties["Opacity"] = 1d;
        definition.BuiltInProperties["IsVisible"] = true;
        definition.BuiltInProperties[AutoGenerateColumnsProperty] = true;
        definition.BuiltInProperties[BindingSourceIdProperty] = string.Empty;

        AddDefaultValue(definition, ShowColumnHeadersProperty, true);
        AddDefaultValue(definition, ShowAutoFilterRowProperty, false);
        AddDefaultValue(definition, ShowGroupPanelProperty, false);
        AddDefaultValue(definition, AllowSortingProperty, true);
        AddDefaultValue(definition, AllowEditingProperty, true);
        AddDefaultValue(definition, AllowColumnResizingProperty, true);
        AddDefaultValue(definition, AllowColumnMovingProperty, true);
        AddDefaultValue(definition, ShowHorizontalLinesProperty, true);
        AddDefaultValue(definition, ShowVerticalLinesProperty, true);
        AddDefaultValue(definition, IsSearchPanelVisibleProperty, false);
        definition.CustomProperties[RowMinHeightProperty] = JsonSerializer.Serialize(29d);
        foreach (var propertyName in EnumPropertyNames)
            AddDefaultEnumValue(definition, propertyName);

        definition.CustomProperties["Eremex.ClrType"] = JsonSerializer.Serialize(typeof(DataGridControl).FullName);
        definition.CustomProperties["Eremex.PackageId"] = JsonSerializer.Serialize(EremexPlugin.ControlsPackageId);
        definition.CustomProperties["Eremex.PackageVersion"] = JsonSerializer.Serialize(EremexPlugin.PackageVersion);
        definition.CustomProperties["Eremex.ThemePackageId"] = JsonSerializer.Serialize(EremexPlugin.ThemePackageId);
        return definition;
    }

    public Control BuildPreview(IDesignControlNode control, IPreviewContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            System.Diagnostics.Debug.WriteLine($"EREMEX_DATAGRID_PREVIEW_CREATE_START grid={control.Name}; mode={context.Mode}");
            var grid = new DataGridControl
            {
                Width = Math.Max(240d, control.GetDouble("Width", 700d)),
                Height = Math.Max(120d, control.GetDouble("Height", 380d)),
                Margin = ParseThickness(control.GetString("Margin", "0")),
                Opacity = Math.Clamp(control.GetDouble("Opacity", 1d), 0d, 1d),
                IsVisible = control.GetBool("IsVisible", true),
                AutoGenerateColumns = control.GetBool(AutoGenerateColumnsProperty, true)
            };

            EremexPreviewTheme.EnsureInstalled(
                grid,
                context.Mode == DesignerPreviewMode.Designer ? "DesignerCanvas" : "LegacyPreview");
            ApplyCustomProperties(grid, control);

            var sourceId = control.GetString(BindingSourceIdProperty, string.Empty);
            var source = context.GetBindingSource(sourceId);
            var itemsProvider = context.Services.GetService(typeof(IPreviewBindingItemsProvider)) as IPreviewBindingItemsProvider;
            var bindingStopwatch = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"EREMEX_DATAGRID_BIND_START grid={control.Name}; sourceId={sourceId}");
            var items = BuildDataView(source, itemsProvider?.GetItems(sourceId));
            grid.ItemsSource = items;
            bindingStopwatch.Stop();
            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine(
                "EREMEX_DATAGRID_ITEMS_SOURCE_RESOLVED " +
                $"grid={control.Name}; sourceId={sourceId}; sourceConfigured={source is not null}; rows={items.Count}; columns={items.Table?.Columns.Count ?? 0}; autoGenerateColumns={grid.AutoGenerateColumns}");
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_BIND_END grid={control.Name}; elapsedMs={bindingStopwatch.ElapsedMilliseconds}; rows={items.Count}");
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_COLUMNS_GENERATED grid={control.Name}; strategy=AutoGenerateColumns; enabled={grid.AutoGenerateColumns}; explicitColumns={grid.Columns.Count}");
            grid.AttachedToVisualTree += (_, _) => System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_RUNTIME_RENDER_SUCCESS grid={control.Name}; mode={context.Mode}; columns={grid.Columns.Count}; actualWidth={grid.Bounds.Width}; actualHeight={grid.Bounds.Height}");
            grid.DetachedFromVisualTree += (_, _) => System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_DISPOSED grid={control.Name}; lifecycle=detached");
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_PREVIEW_CREATE_SUCCESS grid={control.Name}; controlType={typeof(DataGridControl).FullName}; elapsedMs={stopwatch.ElapsedMilliseconds}");
            return grid;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_PREVIEW_CREATE_FAILED grid={control.Name}; exception={ex.GetType().Name}; reason={ex.Message}; stackTrace={ex}; elapsedMs={stopwatch.ElapsedMilliseconds}");
            throw;
        }
    }

    public void AppendXaml(IXamlWriter writer, IDesignControlNode control, int indentLevel, IXamlExportContext context)
    {
        context.RegisterXmlNamespace("mxdg", XmlNamespace);
        var source = context.GetBindingSource(control.GetString(BindingSourceIdProperty, string.Empty));
        var pathResolver = context.Services.GetService(typeof(IRuntimeBindingExportPathProvider)) as IRuntimeBindingExportPathProvider;
        var itemsSourcePath = pathResolver?.GetItemsSourcePath(source) ?? string.Empty;
        var attributes = new List<string>
        {
            $"x:Name=\"{EscapeXml(control.Name)}\"",
            $"Width=\"{FormatDouble(control.GetDouble("Width", 700d))}\"",
            $"Height=\"{FormatDouble(control.GetDouble("Height", 380d))}\"",
            $"Canvas.Left=\"{FormatDouble(control.GetDouble("X", 0d))}\"",
            $"Canvas.Top=\"{FormatDouble(control.GetDouble("Y", 0d))}\"",
            $"AutoGenerateColumns=\"{ToXamlBoolean(control.GetBool(AutoGenerateColumnsProperty, true))}\""
        };

        foreach (var propertyName in BooleanPropertyNames)
            attributes.Add($"{propertyName}=\"{ToXamlBoolean(control.GetCustomValue(propertyName, DefaultBoolean(propertyName)))}\"");

        attributes.Add($"{RowMinHeightProperty}=\"{FormatDouble(Math.Max(1d, control.GetCustomValue(RowMinHeightProperty, 29d)))}\"");
        foreach (var propertyName in EnumPropertyNames)
            AppendOptionalEnumAttribute(attributes, propertyName, control);

        if (!string.IsNullOrWhiteSpace(itemsSourcePath))
            attributes.Add($"ItemsSource=\"{{Binding {EscapeXml(itemsSourcePath)}}}\"");

        var margin = control.GetString("Margin", "0");
        if (!string.IsNullOrWhiteSpace(margin) && !string.Equals(margin.Trim(), "0", StringComparison.Ordinal))
            attributes.Add($"Margin=\"{EscapeXml(margin)}\"");

        var opacity = control.GetDouble("Opacity", 1d);
        if (Math.Abs(opacity - 1d) > 0.0001d)
            attributes.Add($"Opacity=\"{FormatDouble(opacity)}\"");
        if (!control.GetBool("IsVisible", true))
            attributes.Add("IsVisible=\"False\"");

        writer.WriteLine(indentLevel, $"<mxdg:DataGridControl {string.Join(" ", attributes)} />");
        System.Diagnostics.Debug.WriteLine(
            $"EREMEX_DATAGRID_AXAML_EXPORTED grid={control.Name}; itemsSource={itemsSourcePath}; autoGenerateColumns={control.GetBool(AutoGenerateColumnsProperty, true)}");
    }

    private static IReadOnlyList<DesignPropertyDescriptor> BuildPropertySchema()
    {
        var properties = new List<DesignPropertyDescriptor>
        {
            new()
            {
                Key = AutoGenerateColumnsProperty,
                Title = "AutoGenerateColumns",
                Description = "Generate Eremex columns from the selected BindingSource schema.",
                Category = "Data",
                Editor = PropertyEditorKind.Bool,
                BuiltInPropertyName = AutoGenerateColumnsProperty
            },
            new()
            {
                Key = BindingSourceIdProperty,
                Title = "BindingSource",
                Description = "BindingSource used as ItemsSource for this Eremex grid.",
                Category = "Data",
                Editor = PropertyEditorKind.Binding,
                BuiltInPropertyName = BindingSourceIdProperty
            },
            new()
            {
                Key = RowMinHeightProperty,
                Title = "RowMinHeight",
                Description = "Minimum Eremex grid row height.",
                Category = "Eremex DataGrid",
                Editor = PropertyEditorKind.Number,
                DefaultValueJson = JsonSerializer.Serialize(29d)
            }
        };

        foreach (var propertyName in BooleanPropertyNames)
        {
            properties.Add(new DesignPropertyDescriptor
            {
                Key = propertyName,
                Title = propertyName,
                Description = $"Eremex DataGrid {propertyName} setting.",
                Category = "Eremex DataGrid",
                Editor = PropertyEditorKind.Bool,
                DefaultValueJson = JsonSerializer.Serialize(DefaultBoolean(propertyName))
            });
        }

        foreach (var propertyName in EnumPropertyNames)
        {
            var options = GetEnumOptions(propertyName);
            if (options.Count == 0)
                continue;

            properties.Add(new DesignPropertyDescriptor
            {
                Key = propertyName,
                Title = propertyName,
                Description = $"Eremex DataGrid {propertyName} setting.",
                Category = "Eremex DataGrid",
                Editor = PropertyEditorKind.Enum,
                DefaultValueJson = JsonSerializer.Serialize(options[0].Value),
                Options = options
            });
        }

        return properties;
    }

    private static void AddDefaultValue(DesignerControlDefinition definition, string propertyName, bool value)
    {
        definition.CustomProperties[propertyName] = JsonSerializer.Serialize(value);
    }

    private static void AddDefaultEnumValue(DesignerControlDefinition definition, string propertyName)
    {
        var value = GetEnumOptions(propertyName).FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(value))
            definition.CustomProperties[propertyName] = JsonSerializer.Serialize(value);
    }

    private static IReadOnlyList<PropertyOption> GetEnumOptions(string propertyName)
    {
        var enumType = typeof(DataGridControl).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.PropertyType;
        if (enumType is null || !enumType.IsEnum)
            return Array.Empty<PropertyOption>();

        return Enum.GetNames(enumType)
            .Select(value => new PropertyOption { Value = value, Title = value })
            .ToList();
    }

    private static void ApplyCustomProperties(DataGridControl grid, IDesignControlNode control)
    {
        foreach (var propertyName in BooleanPropertyNames)
            ApplyProperty(grid, propertyName, control.GetCustomValue(propertyName, DefaultBoolean(propertyName)));

        ApplyProperty(grid, RowMinHeightProperty, Math.Max(1d, control.GetCustomValue(RowMinHeightProperty, 29d)));
        foreach (var propertyName in EnumPropertyNames)
            ApplyProperty(grid, propertyName, control.GetCustomValue(propertyName, string.Empty));
    }

    private static void ApplyProperty(DataGridControl grid, string propertyName, object? value)
    {
        if (value is null)
            return;

        var property = typeof(DataGridControl).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanWrite)
            return;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        try
        {
            if (targetType == typeof(bool))
            {
                property.SetValue(grid, Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                return;
            }

            if (targetType == typeof(double))
            {
                property.SetValue(grid, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            }

            if (targetType.IsEnum)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(targetType, text, true, out var enumValue))
                    property.SetValue(grid, enumValue);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EREMEX_DATAGRID_PROPERTY_SKIPPED property={propertyName}; reason={ex.Message}");
        }
    }

    private static DataView BuildDataView(BindingSourceMetadata? source, IEnumerable? sourceItems)
    {
        var table = new DataTable(string.IsNullOrWhiteSpace(source?.Name) ? "EremexGrid" : source.Name);
        var fields = source?.Fields.Where(field => field.IsVisible && !string.IsNullOrWhiteSpace(field.Path)).ToList()
                     ?? new List<BindingFieldMetadata>();
        foreach (var field in fields)
            table.Columns.Add(field.Path, typeof(string));

        if (sourceItems is null || fields.Count == 0)
            return table.DefaultView;

        foreach (var item in sourceItems)
        {
            var row = table.NewRow();
            foreach (var field in fields)
                row[field.Path] = ReadItemValue(item, field.Path) ?? string.Empty;
            table.Rows.Add(row);
        }

        return table.DefaultView;
    }

    private static object? ReadItemValue(object? item, string propertyName)
    {
        if (item is null)
            return null;

        if (item is IDictionary dictionary)
        {
            if (dictionary.Contains(propertyName))
                return dictionary[propertyName];

            foreach (DictionaryEntry entry in dictionary)
            {
                if (string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), propertyName, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
        }

        return item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?.GetValue(item);
    }

    private static bool DefaultBoolean(string propertyName)
    {
        return propertyName switch
        {
            ShowColumnHeadersProperty => true,
            AllowSortingProperty => true,
            AllowEditingProperty => true,
            AllowColumnResizingProperty => true,
            AllowColumnMovingProperty => true,
            ShowHorizontalLinesProperty => true,
            ShowVerticalLinesProperty => true,
            _ => false
        };
    }

    private static void AppendOptionalEnumAttribute(List<string> attributes, string key, IDesignControlNode control)
    {
        var value = control.GetCustomValue(key, string.Empty);
        if (!string.IsNullOrWhiteSpace(value))
            attributes.Add($"{key}=\"{EscapeXml(value)}\"");
    }

    private static Thickness ParseThickness(string value)
    {
        try
        {
            return Thickness.Parse(value);
        }
        catch
        {
            return new Thickness(0);
        }
    }

    private static string ToXamlBoolean(bool value) => value ? "True" : "False";

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string FormatDouble(double value) => value.ToString(CultureInfo.InvariantCulture);
}
