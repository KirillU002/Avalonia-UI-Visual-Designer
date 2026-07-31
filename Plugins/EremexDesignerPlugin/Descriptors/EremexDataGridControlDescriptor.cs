using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
using System.Runtime.Loader;
using System.Text.Json;

namespace EremexDesignerPlugin.Descriptors;

/// <summary>
/// Eremex grid adapter. It supports documented automatic and explicit
/// <see cref="GridColumn"/> collections without reusing Avalonia DataGrid columns.
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
    private const string ShowSearchPanelCloseButtonProperty = "ShowSearchPanelCloseButton";
    private const string SearchPanelHighlightResultsProperty = "SearchPanelHighlightResults";
    private const string ShowItemsSourceErrorsProperty = "ShowItemsSourceErrors";
    private const string ShowGroupedColumnsProperty = "ShowGroupedColumns";
    private const string AutoExpandAllGroupsProperty = "AutoExpandAllGroups";
    private const string AllowImmediateEditorValuePostingProperty = "AllowImmediateEditorValuePosting";
    private const string AutoScrollToFocusedRowProperty = "AutoScrollToFocusedRow";
    private const string ValidateCellValuesOnShowAndUpdateProperty = "ValidateCellValuesOnShowAndUpdate";
    private const string IsColumnChooserVisibleProperty = "IsColumnChooserVisible";
    private const string RowMinHeightProperty = "RowMinHeight";
    private const string HeaderPanelMinHeightProperty = "HeaderPanelMinHeight";
    private const string HeaderDropIndicatorWidthProperty = "HeaderDropIndicatorWidth";
    private const string RowLevelIndentProperty = "RowLevelIndent";
    private const string NavigationModeProperty = "NavigationMode";
    private const string SelectionModeProperty = "SelectionMode";
    private const string SearchPanelDisplayModeProperty = "SearchPanelDisplayMode";
    private const string EditorShowModeProperty = "EditorShowMode";
    private const string EditorButtonShowModeProperty = "EditorButtonShowMode";

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
        IsSearchPanelVisibleProperty,
        ShowSearchPanelCloseButtonProperty,
        SearchPanelHighlightResultsProperty,
        ShowItemsSourceErrorsProperty,
        ShowGroupedColumnsProperty,
        AutoExpandAllGroupsProperty,
        AllowImmediateEditorValuePostingProperty,
        AutoScrollToFocusedRowProperty,
        ValidateCellValuesOnShowAndUpdateProperty,
        IsColumnChooserVisibleProperty
    };

    private static readonly string[] NumberPropertyNames =
    {
        RowMinHeightProperty,
        HeaderPanelMinHeightProperty,
        HeaderDropIndicatorWidthProperty,
        RowLevelIndentProperty
    };

    private static readonly string[] EnumPropertyNames =
    {
        NavigationModeProperty,
        SelectionModeProperty,
        SearchPanelDisplayModeProperty,
        EditorShowModeProperty,
        EditorButtonShowModeProperty
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
        AddDefaultValue(definition, ShowSearchPanelCloseButtonProperty, true);
        AddDefaultValue(definition, SearchPanelHighlightResultsProperty, true);
        AddDefaultValue(definition, ShowItemsSourceErrorsProperty, true);
        AddDefaultValue(definition, ShowGroupedColumnsProperty, false);
        AddDefaultValue(definition, AutoExpandAllGroupsProperty, false);
        AddDefaultValue(definition, AllowImmediateEditorValuePostingProperty, false);
        AddDefaultValue(definition, AutoScrollToFocusedRowProperty, true);
        AddDefaultValue(definition, ValidateCellValuesOnShowAndUpdateProperty, false);
        AddDefaultValue(definition, IsColumnChooserVisibleProperty, false);
        foreach (var propertyName in NumberPropertyNames)
            definition.CustomProperties[propertyName] = JsonSerializer.Serialize(GetDefaultNumber(propertyName));
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
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_THEME_READY grid={control.Name}; mode={context.Mode}; localStyles={grid.Styles.Count}; " +
                $"themeAssembly={typeof(Eremex.AvaloniaUI.Themes.DeltaDesign.DeltaDesignTheme).Assembly.GetName().Version}");
            ApplyCustomProperties(grid, control);

            var sourceId = control.GetString(BindingSourceIdProperty, string.Empty);
            var source = context.GetBindingSource(sourceId);
            var fields = GetVisibleFields(source);
            var itemsProvider = context.Services.GetService(typeof(IPreviewBindingItemsProvider)) as IPreviewBindingItemsProvider;
            var bindingStopwatch = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Debug.WriteLine($"EREMEX_DATAGRID_BIND_START grid={control.Name}; sourceId={sourceId}");
            var items = BuildDataView(source, fields, itemsProvider?.GetItems(sourceId));
            if (fields.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"EREMEX_DATAGRID_FAKE_COLUMN_GENERATION_BLOCKED grid={control.Name}; reason=source has no visible schema fields");
            }
            else if (grid.AutoGenerateColumns)
            {
                grid.AutoGeneratingColumn += (_, args) => ConfigureColumn(args.Column, fields);
            }
            else
            {
                foreach (var field in fields)
                    grid.Columns.Add(CreateColumn(field));
            }

            grid.ItemsSource = items;
            bindingStopwatch.Stop();
            stopwatch.Stop();
            System.Diagnostics.Debug.WriteLine(
                "EREMEX_DATAGRID_ITEMS_SOURCE_RESOLVED " +
                $"grid={control.Name}; sourceId={sourceId}; sourceConfigured={source is not null}; rows={items.Count}; columns={items.Table?.Columns.Count ?? 0}; autoGenerateColumns={grid.AutoGenerateColumns}");
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_BIND_END grid={control.Name}; elapsedMs={bindingStopwatch.ElapsedMilliseconds}; rows={items.Count}");
            System.Diagnostics.Debug.WriteLine(
                "EREMEX_DATAGRID_COLUMN_SOURCE_RESOLVED " +
                $"grid={control.Name}; sourceId={sourceId}; schemaColumns={fields.Count}; manualColumns={grid.Columns.Count}; autoGeneration={grid.AutoGenerateColumns}");
            grid.AutoGeneratedColumns += (_, _) => LogGridStructure(grid, control, context, "auto-generated-columns");
            grid.TemplateApplied += (_, _) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"EREMEX_DATAGRID_TEMPLATE_APPLIED grid={control.Name}; mode={context.Mode}; columns={grid.Columns.Count}; " +
                    $"visualChildren={grid.GetVisualDescendants().Count()}");
                LogGridStructure(grid, control, context, "template-applied");
            };
            grid.AttachedToVisualTree += (_, _) =>
            {
                var assembly = grid.GetType().Assembly;
                System.Diagnostics.Debug.WriteLine(
                    "EREMEX_DATAGRID_CANVAS_INSTANCE " +
                    $"descriptorId={TypeKey}; clrType={grid.GetType().FullName}; assembly={assembly.FullName}; " +
                    $"assemblyVersion={assembly.GetName().Version}; alc={AssemblyLoadContext.GetLoadContext(assembly)?.Name ?? "default"}; " +
                    $"templateApplied={grid.Template is not null}; visualChildren={grid.GetVisualDescendants().Count()}; " +
                    $"bounds={grid.Bounds.Width}x{grid.Bounds.Height}");
                LogGridStructure(grid, control, context, "attached");
            };
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

        foreach (var propertyName in NumberPropertyNames)
            attributes.Add($"{propertyName}=\"{FormatDouble(Math.Max(0d, control.GetCustomValue(propertyName, GetDefaultNumber(propertyName))))}\"");
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

        var fields = GetVisibleFields(source);
        var useManualColumns = !control.GetBool(AutoGenerateColumnsProperty, true) && fields.Count > 0;
        if (!useManualColumns)
        {
            writer.WriteLine(indentLevel, $"<mxdg:DataGridControl {string.Join(" ", attributes)} />");
        }
        else
        {
            writer.WriteLine(indentLevel, $"<mxdg:DataGridControl {string.Join(" ", attributes)}>");
            writer.WriteLine(indentLevel + 1, "<mxdg:DataGridControl.Columns>");
            foreach (var field in fields)
                writer.WriteLine(indentLevel + 2, BuildColumnAxaml(field));
            writer.WriteLine(indentLevel + 1, "</mxdg:DataGridControl.Columns>");
            writer.WriteLine(indentLevel, "</mxdg:DataGridControl>");
        }
        System.Diagnostics.Debug.WriteLine(
            $"EREMEX_DATAGRID_AXAML_EXPORTED grid={control.Name}; itemsSource={itemsSourcePath}; autoGenerateColumns={control.GetBool(AutoGenerateColumnsProperty, true)}; manualColumns={useManualColumns}; columnCount={fields.Count}");
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
                Category = GetPropertyCategory(RowMinHeightProperty),
                Editor = PropertyEditorKind.Number,
                DefaultValueJson = JsonSerializer.Serialize(GetDefaultNumber(RowMinHeightProperty))
            }
        };

        foreach (var propertyName in BooleanPropertyNames)
        {
            properties.Add(new DesignPropertyDescriptor
            {
                Key = propertyName,
                Title = propertyName,
                Description = GetPropertyDescription(propertyName),
                Category = GetPropertyCategory(propertyName),
                Editor = PropertyEditorKind.Bool,
                DefaultValueJson = JsonSerializer.Serialize(DefaultBoolean(propertyName))
            });
        }

        foreach (var propertyName in NumberPropertyNames.Where(propertyName => !string.Equals(propertyName, RowMinHeightProperty, StringComparison.Ordinal)))
        {
            properties.Add(new DesignPropertyDescriptor
            {
                Key = propertyName,
                Title = propertyName,
                Description = GetPropertyDescription(propertyName),
                Category = GetPropertyCategory(propertyName),
                Editor = PropertyEditorKind.Number,
                DefaultValueJson = JsonSerializer.Serialize(GetDefaultNumber(propertyName))
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
                Description = GetPropertyDescription(propertyName),
                Category = GetPropertyCategory(propertyName),
                Editor = PropertyEditorKind.Enum,
                DefaultValueJson = JsonSerializer.Serialize(GetDefaultEnumValue(propertyName)),
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
        var value = GetDefaultEnumValue(propertyName);
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

        foreach (var propertyName in NumberPropertyNames)
            ApplyProperty(grid, propertyName, Math.Max(0d, control.GetCustomValue(propertyName, GetDefaultNumber(propertyName))));
        foreach (var propertyName in EnumPropertyNames)
            ApplyProperty(grid, propertyName, control.GetCustomValue(propertyName, GetDefaultEnumValue(propertyName)));
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

            if (targetType == typeof(int))
            {
                property.SetValue(grid, Convert.ToInt32(value, CultureInfo.InvariantCulture));
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

    private static IReadOnlyList<BindingFieldMetadata> GetVisibleFields(BindingSourceMetadata? source)
    {
        if (source is null)
            return Array.Empty<BindingFieldMetadata>();

        return source.Fields
            .Select((field, index) => new { Field = field, Index = index })
            .Where(item => item.Field.IsVisible && !string.IsNullOrWhiteSpace(item.Field.Path))
            .OrderBy(item => item.Field.VisibleIndex >= 0 ? item.Field.VisibleIndex : 1_000_000 + item.Index)
            .Select(item => item.Field)
            .ToList();
    }

    private static DataView BuildDataView(
        BindingSourceMetadata? source,
        IReadOnlyList<BindingFieldMetadata> fields,
        IEnumerable? sourceItems)
    {
        var table = new DataTable(string.IsNullOrWhiteSpace(source?.Name) ? "EremexGrid" : source.Name);
        foreach (var field in fields)
        {
            var column = table.Columns.Add(field.Path, ResolveDataColumnType(field.TypeName));
            column.Caption = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header;
            // Preview may contain incomplete SQL/DLL rows while schema is already known.
            // Keeping the temporary DataTable nullable prevents a missing sample value from
            // turning a visual preview into a data conversion failure.
            column.AllowDBNull = true;
        }

        if (sourceItems is null || fields.Count == 0)
            return table.DefaultView;

        foreach (var item in sourceItems)
        {
            var row = table.NewRow();
            foreach (var field in fields)
            {
                var dataColumn = table.Columns[field.Path];
                row[field.Path] = CoerceDataValue(ReadItemValue(item, field.Path), dataColumn?.DataType ?? typeof(string));
            }
            table.Rows.Add(row);
        }

        return table.DefaultView;
    }

    private static GridColumn CreateColumn(BindingFieldMetadata field)
    {
        var column = new GridColumn { FieldName = field.Path };
        ConfigureColumn(column, field);
        return column;
    }

    private static void ConfigureColumn(GridColumn column, IReadOnlyList<BindingFieldMetadata> fields)
    {
        var field = fields.FirstOrDefault(candidate => string.Equals(candidate.Path, column.FieldName, StringComparison.OrdinalIgnoreCase));
        if (field is not null)
            ConfigureColumn(column, field);
    }

    private static void ConfigureColumn(GridColumn column, BindingFieldMetadata field)
    {
        column.FieldName = field.Path;
        column.Header = string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header;
        column.IsVisible = field.IsVisible;
        column.ReadOnly = !field.CanWrite;
        column.AllowResizing = field.AllowResize;
        column.AllowSorting = field.AllowSort && field.IsSortable;
        column.MinWidth = Math.Max(0d, field.MinWidth);
        column.Width = ParseGridLength(field.Width);
        column.VisibleIndex = field.VisibleIndex >= 0 ? field.VisibleIndex : column.VisibleIndex;
    }

    private static string BuildColumnAxaml(BindingFieldMetadata field)
    {
        var attributes = new List<string>
        {
            $"FieldName=\"{EscapeXml(field.Path)}\"",
            $"Header=\"{EscapeXml(string.IsNullOrWhiteSpace(field.Header) ? field.Path : field.Header)}\"",
            $"IsVisible=\"{ToXamlBoolean(field.IsVisible)}\"",
            $"ReadOnly=\"{ToXamlBoolean(!field.CanWrite)}\"",
            $"AllowResizing=\"{ToXamlBoolean(field.AllowResize)}\"",
            $"AllowSorting=\"{ToXamlBoolean(field.AllowSort && field.IsSortable)}\"",
            $"MinWidth=\"{FormatDouble(Math.Max(0d, field.MinWidth))}\""
        };

        if (!string.IsNullOrWhiteSpace(field.Width))
            attributes.Add($"Width=\"{EscapeXml(field.Width)}\"");
        if (field.VisibleIndex >= 0)
            attributes.Add($"VisibleIndex=\"{field.VisibleIndex.ToString(CultureInfo.InvariantCulture)}\"");

        return $"<mxdg:GridColumn {string.Join(" ", attributes)} />";
    }

    private static GridLength ParseGridLength(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "Auto", StringComparison.OrdinalIgnoreCase))
            return GridLength.Auto;

        if (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            var factorText = normalized[..^1];
            var factor = string.IsNullOrWhiteSpace(factorText)
                ? 1d
                : double.TryParse(factorText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1d;
            return new GridLength(Math.Max(0.1d, factor), GridUnitType.Star);
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
            ? new GridLength(Math.Max(0d, pixels), GridUnitType.Pixel)
            : GridLength.Auto;
    }

    private static Type ResolveDataColumnType(string? typeName)
    {
        var normalized = (typeName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("bool", StringComparison.Ordinal))
            return typeof(bool);
        if (normalized.Contains("date", StringComparison.Ordinal) || normalized.Contains("time", StringComparison.Ordinal))
            return typeof(DateTime);
        if (normalized.Contains("decimal", StringComparison.Ordinal) || normalized.Contains("double", StringComparison.Ordinal) || normalized.Contains("float", StringComparison.Ordinal))
            return typeof(decimal);
        if (normalized.Contains("int", StringComparison.Ordinal) || normalized.Contains("long", StringComparison.Ordinal) || normalized.Contains("short", StringComparison.Ordinal))
            return typeof(long);
        return typeof(string);
    }

    private static object CoerceDataValue(object? value, Type targetType)
    {
        if (value is null || value is DBNull)
            return DBNull.Value;

        try
        {
            if (targetType == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (targetType == typeof(bool))
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(DateTime))
                return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(decimal))
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(long))
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return DBNull.Value;
        }

        return value;
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

    private static double GetDefaultNumber(string propertyName)
    {
        var property = typeof(DataGridControl).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanRead)
            return 0d;

        try
        {
            var value = property.GetValue(new DataGridControl());
            return value is null ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return propertyName == RowMinHeightProperty ? 29d : 0d;
        }
    }

    private static string GetDefaultEnumValue(string propertyName)
    {
        var property = typeof(DataGridControl).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.PropertyType.IsEnum)
            return GetEnumOptions(propertyName).FirstOrDefault()?.Value ?? string.Empty;

        try
        {
            return Convert.ToString(property.GetValue(new DataGridControl()), CultureInfo.InvariantCulture)
                   ?? GetEnumOptions(propertyName).FirstOrDefault()?.Value
                   ?? string.Empty;
        }
        catch (Exception)
        {
            return GetEnumOptions(propertyName).FirstOrDefault()?.Value ?? string.Empty;
        }
    }

    private static string GetPropertyDescription(string propertyName)
    {
        return propertyName switch
        {
            ShowColumnHeadersProperty => "Show the Eremex column header panel.",
            ShowAutoFilterRowProperty => "Show Eremex Auto Filter Row below the column headers.",
            ShowGroupPanelProperty => "Show the Eremex group panel above the rows.",
            ShowGroupedColumnsProperty => "Keep grouped columns visible in the header panel.",
            AutoExpandAllGroupsProperty => "Expand all Eremex groups after grouping is applied.",
            AllowSortingProperty => "Allow users to sort data through Eremex column headers.",
            AllowEditingProperty => "Allow editing cells when their column is not read-only.",
            AllowImmediateEditorValuePostingProperty => "Post editor values to the source immediately.",
            AllowColumnResizingProperty => "Allow users to resize Eremex columns.",
            AllowColumnMovingProperty => "Allow users to move Eremex columns.",
            IsColumnChooserVisibleProperty => "Show the built-in Eremex column chooser.",
            IsSearchPanelVisibleProperty => "Show the Eremex search panel.",
            SearchPanelDisplayModeProperty => "Control when the Eremex search panel is displayed.",
            ShowSearchPanelCloseButtonProperty => "Show the close button in the Eremex search panel.",
            SearchPanelHighlightResultsProperty => "Highlight matching values in Eremex search results.",
            ShowItemsSourceErrorsProperty => "Show data-source errors reported by Eremex.",
            ValidateCellValuesOnShowAndUpdateProperty => "Validate cell values when they are shown or updated.",
            NavigationModeProperty => "Choose the Eremex keyboard navigation mode.",
            SelectionModeProperty => "Choose the Eremex selection mode.",
            EditorShowModeProperty => "Choose when Eremex cell editors are activated.",
            EditorButtonShowModeProperty => "Choose when editor buttons are visible.",
            RowMinHeightProperty => "Minimum Eremex data-row height.",
            HeaderPanelMinHeightProperty => "Minimum height of the Eremex header panel.",
            HeaderDropIndicatorWidthProperty => "Width of the Eremex header drag-drop indicator.",
            RowLevelIndentProperty => "Indent applied to hierarchical/grouped Eremex rows.",
            _ => $"Eremex DataGrid {propertyName} setting."
        };
    }

    private static string GetPropertyCategory(string propertyName)
    {
        return propertyName switch
        {
            ShowColumnHeadersProperty or
            AllowColumnResizingProperty or
            AllowColumnMovingProperty or
            IsColumnChooserVisibleProperty or
            HeaderPanelMinHeightProperty or
            HeaderDropIndicatorWidthProperty => "Behavior",

            ShowAutoFilterRowProperty or
            AllowSortingProperty or
            ShowGroupPanelProperty or
            ShowGroupedColumnsProperty or
            AutoExpandAllGroupsProperty or
            IsSearchPanelVisibleProperty or
            SearchPanelDisplayModeProperty or
            ShowSearchPanelCloseButtonProperty or
            SearchPanelHighlightResultsProperty or
            ShowItemsSourceErrorsProperty or
            AllowEditingProperty or
            AllowImmediateEditorValuePostingProperty or
            ValidateCellValuesOnShowAndUpdateProperty or
            EditorShowModeProperty or
            EditorButtonShowModeProperty => "Behavior",

            NavigationModeProperty or
            SelectionModeProperty or
            AutoScrollToFocusedRowProperty => "Interaction",

            RowMinHeightProperty or
            RowLevelIndentProperty or
            ShowHorizontalLinesProperty or
            ShowVerticalLinesProperty => "Appearance",

            _ => "Eremex DataGrid"
        };
    }

    private static void LogGridStructure(
        DataGridControl grid,
        IDesignControlNode control,
        IPreviewContext context,
        string trigger)
    {
        try
        {
            var visualChildren = grid.GetVisualDescendants().Count();
            System.Diagnostics.Debug.WriteLine(
                "EREMEX_DATAGRID_VISUAL_TREE_CREATED " +
                $"grid={control.Name}; mode={context.Mode}; trigger={trigger}; templateApplied={grid.Template is not null}; " +
                $"visualChildren={visualChildren}; columns={grid.Columns.Count}; rows={GetEnumerableCount(grid.ItemsSource)}");
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_RUNTIME_RENDER_SUCCESS grid={control.Name}; mode={context.Mode}; columns={grid.Columns.Count}; actualWidth={grid.Bounds.Width}; actualHeight={grid.Bounds.Height}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"EREMEX_DATAGRID_THEME_OR_TEMPLATE_FAILED grid={control.Name}; mode={context.Mode}; trigger={trigger}; exception={ex.GetType().Name}; reason={ex.Message}; stackTrace={ex}");
        }
    }

    private static int GetEnumerableCount(IEnumerable? items)
    {
        if (items is ICollection collection)
            return collection.Count;

        if (items is null)
            return 0;

        var count = 0;
        foreach (var _ in items)
            count++;
        return count;
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
            ShowSearchPanelCloseButtonProperty => true,
            SearchPanelHighlightResultsProperty => true,
            ShowItemsSourceErrorsProperty => true,
            AutoScrollToFocusedRowProperty => true,
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
