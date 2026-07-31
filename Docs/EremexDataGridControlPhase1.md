# Eremex DataGridControl: Phase 1 Compatibility Decision

## Decision

Keep the current in-process stack for the first DataGridControl slice:

| Component | Version |
| --- | --- |
| Target framework | net6.0 |
| Designer / PluginContracts Avalonia | 11.1.5 |
| Eremex.Avalonia.Controls | 1.0.98 |
| Eremex.Avalonia.Themes.DeltaDesign | 1.0.98 |

No Avalonia 12 migration is required for this phase. The installed Eremex 1.0.98 package targets net6.0 and declares the Avalonia 11.1.x dependency line. The existing TextEditor visual-template probe and the DataGridControl vertical smoke both create the real controls, apply the DeltaDesign template, measure, arrange, and run dispatcher jobs against the host Avalonia 11.1.5 assemblies.

Eremex 1.4.x is intentionally not used here. It belongs to the separate net8/Avalonia 12 migration path and must not be loaded into the Avalonia 11 host process.

## Verified DataGridControl API

The Phase 1 descriptor uses only public API from `Eremex.Avalonia.Controls 1.0.98`:

- CLR type: `Eremex.AvaloniaUI.Controls.DataGrid.DataGridControl`
- AXAML namespace: `https://schemas.eremexcontrols.net/avalonia/datagrid`
- `ItemsSource`
- `AutoGenerateColumns`
- `ShowColumnHeaders`
- `ShowAutoFilterRow`
- `ShowGroupPanel`
- `AllowSorting`
- `AllowEditing`
- `AllowColumnResizing`
- `AllowColumnMoving`
- `ShowHorizontalLines`
- `ShowVerticalLines`
- `IsSearchPanelVisible`
- `RowMinHeight`
- `NavigationMode`, `SelectionMode`, and `SearchPanelDisplayMode`

The first adapter deliberately uses `AutoGenerateColumns="True"`. Explicit Eremex column collections, bands, summaries, templates, server mode, master-detail, and advanced grouping are later phases.

## Theme scope

`DeltaDesignTheme` is not added to `Application.Current.Styles` in the Designer. The plugin adds its theme resource only to:

1. the actual Eremex control returned for the Designer Canvas and Legacy Preview;
2. the isolated root used by Runtime AXAML Preview; and
3. the exported application's `App.axaml` when at least one Eremex control is present.

This prevents Eremex styles from changing the main Designer toolbar, Toolbox, Property Inspector, Settings, or standard Avalonia controls.

## Data binding behavior

The descriptor reuses the Designer's existing `BindingSource` and runtime data-mode pipeline:

- no source: an empty `DataView`, no synthetic rows;
- demo: rows only when demo data was explicitly enabled;
- SQL and DLL: existing preview/runtime loaders supply real rows when available;
- export: the descriptor asks the host for the generated ViewModel collection path instead of building a second loader.

Runtime AXAML Preview assigns plugin `ItemsSource` properties through a generic reflection-based contract. It is not tied to Eremex types in the PreviewWindow.

## Evidence

The focused smoke coverage includes:

- `EremexThemeDoesNotChangeDesignerChrome`;
- `EremexTextEditorVerticalSlice`;
- `EremexDataGridControlVerticalSlice`;
- `RuntimePreviewCanLoadDataGrid`;
- `DataGridWithoutSourceExportsEmptyCollection`;
- the six `MultiFormSqlExport*` scenarios.

