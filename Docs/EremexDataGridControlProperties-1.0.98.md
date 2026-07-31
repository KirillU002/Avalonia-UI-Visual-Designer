# Eremex DataGridControl properties: 1.0.98

## Evidence

The inventory was taken by reflection from the locally restored `net6.0`
assembly `Eremex.Avalonia.Controls` version **1.0.98**, using the real CLR
type `Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl`.

The probe installed `Eremex.Avalonia.Themes.DeltaDesign` 1.0.98 in the
control's local style scope, assigned a two-row `ItemsSource`, then performed
attach, measure, arrange and `ApplyTemplate`. The control produced 83 visuals,
including `ColumnHeaderControl`, `AutoFilterRowControl`, `DataGridRowControl`,
`CellControl`, and real Eremex `TextEditor` cells.

This is a curated Designer inventory. Public runtime APIs are not automatically
made editable unless their value, lifecycle and AXAML form are suitable.

## DataGridControl

| Category | Property | CLR type | RW | Default | Designer / AXAML |
| --- | --- | --- | --- | --- | --- |
| Data | `ItemsSource` | `IEnumerable` | RW | null | BindingSource selector / `{Binding ...}` |
| Data | `AutoGenerateColumns` | `bool` | RW | `false` | Yes / attribute |
| Columns | `Columns` | `GridColumnCollection` | R | empty | shared column editor / property element |
| Columns | `AllowColumnMoving` | `bool` | RW | `true` | Yes / attribute |
| Columns | `AllowColumnResizing` | `bool` | RW | `true` | Yes / attribute |
| Columns | `ShowColumnHeaders` | `bool` | RW | `true` | Yes / attribute |
| Columns | `ShowGroupedColumns` | `bool` | RW | `false` | Yes / attribute |
| Columns | `HeaderPanelMinHeight` | `double` | RW | `29` | Yes / attribute |
| Columns | `HeaderDropIndicatorWidth` | `double` | RW | `2` | advanced numeric / attribute |
| Sorting/filtering | `AllowSorting` | `bool` | RW | `true` | Yes / attribute |
| Sorting/filtering | `ShowAutoFilterRow` | `bool` | RW | `false` | Yes / attribute |
| Grouping | `ShowGroupPanel` | `bool` | RW | `true` | Yes / attribute |
| Grouping | `AutoExpandAllGroups` | `bool` | RW | `false` | Yes / attribute |
| Navigation | `NavigationMode` | Eremex enum | RW | `Cell` | inline ComboBox / attribute |
| Navigation | `FocusedItem` | `object` | RW | null | runtime state; hidden |
| Navigation | `FocusedColumn` | `GridColumn` | RW | null | runtime state; hidden |
| Navigation | `FocusedRowIndex` | `int` | RW | `Int32.MinValue` | runtime state; hidden |
| Appearance | `ShowHorizontalLines` | `bool` | RW | `true` | Yes / attribute |
| Appearance | `ShowVerticalLines` | `bool` | RW | `true` | Yes / attribute |
| Appearance | `CellTemplate` | `IDataTemplate` | RW | null | complex template; unsupported |
| Menus | `ColumnMenu` / `RowCellMenu` | `PopupMenu` | RW | null | complex object; unsupported |
| Diagnostics | `GroupCount`, `VisibleRowCount`, `Commands`, `SerializationInfo` | runtime types | R | runtime | hidden |

## DataControlBase inherited by DataGridControl

| Category | Property | CLR type | RW | Default | Designer / AXAML |
| --- | --- | --- | --- | --- | --- |
| Editing | `AllowEditing` | `bool` | RW | `true` | Yes / attribute |
| Editing | `AllowImmediateEditorValuePosting` | `bool?` | RW | null | Yes; unset inherits Eremex behavior |
| Editing | `EditorShowMode` | Eremex enum | RW | `PointerPressed` | inline ComboBox / attribute |
| Editing | `EditorButtonShowMode` | Eremex enum | RW | `ShowOnlyInEditor` | inline ComboBox / attribute |
| Editing | `ValidateCellValuesOnShowAndUpdate` | `bool` | RW | `false` | Yes / attribute |
| Navigation | `AutoScrollToFocusedRow` | `bool` | RW | `true` | Yes / attribute |
| Search | `IsSearchPanelVisible` | `bool` | RW | `false` | Yes / attribute |
| Search | `SearchPanelDisplayMode` | Eremex enum | RW | `Never` | inline ComboBox / attribute |
| Search | `ShowSearchPanelCloseButton` | `bool` | RW | `true` | Yes / attribute |
| Search | `SearchPanelHighlightResults` | `bool` | RW | `true` | Yes / attribute |
| Search | `SearchText` | `string` | RW | empty | runtime state; hidden |
| Diagnostics | `ShowItemsSourceErrors` | `bool` | RW | `true` | Yes / attribute |
| Columns | `IsColumnChooserVisible` | `bool` | RW | `false` | Yes; opens chooser / attribute |
| Layout | `RowMinHeight` | `double` | RW | `29` | Yes / attribute |
| Layout | `RowLevelIndent` | `double` | RW | `0` | Yes / attribute |
| Runtime | `ActiveEditor`, `HeaderDropIndex` | runtime types | RW | runtime | hidden |

`SelectionMode` is inherited from Avalonia selection infrastructure. The
descriptor exposes it with an inline ComboBox only when reflection resolves the
enum in the installed runtime.

## Shared schema and provider adapter

The provider-neutral column model is `BindingFieldModel`, projected to plugins
as `BindingFieldMetadata`. It carries header, binding path, width, minimum
width, visibility, write permission, sorting/filtering permissions, and visible
order. `EremexDataGridControlDescriptor` maps it to the real `GridColumn` API:

```text
Header -> Header                 Path -> FieldName
Width -> Width                   MinWidth -> MinWidth
CanWrite -> ReadOnly (inverted) AllowResize -> AllowResizing
AllowSort + IsSortable -> AllowSorting
VisibleIndex -> VisibleIndex
```

`AutoGenerateColumns=true` customizes actual Eremex columns through
`AutoGeneratingColumn`. `AutoGenerateColumns=false` uses the confirmed 1.0.98
AXAML property-element form:

```xml
<mxdg:DataGridControl.Columns>
  <mxdg:GridColumn FieldName="Name" Header="Full name" />
</mxdg:DataGridControl.Columns>
```

Bands, summaries, custom templates, unbound columns, popup menus, server mode,
and an advanced grouping designer remain explicitly out of scope for this
vertical slice.

## Avalonia inherited properties

`Background`, `Foreground`, `BorderBrush`, `BorderThickness`, `CornerRadius`,
`Padding`, `FontFamily`, `FontSize`, `FontStyle`, `FontWeight`, `Opacity`,
`IsVisible`, `Width`, `Height`, and `Margin` use the existing common Inspector
editors and normal AXAML attributes. They are not duplicated as Eremex entries.

## Hidden dependency metadata

The following JSON data remains persisted for package contribution,
compatibility checks and missing-plugin recovery. It is not a visual property
and is therefore hidden from the normal Property Inspector:

```text
Eremex.ClrType
Eremex.PackageId
Eremex.PackageVersion
Eremex.ThemePackageId
```

It may be displayed read-only in a later developer diagnostics view.
