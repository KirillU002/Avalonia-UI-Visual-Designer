# Generated Form Design System

## Goal

Generated Avalonia forms use a small, semantic design system rather than a
collection of unrelated literal colours. It is embedded in each generated
window, so the exported project and the AXAML Runtime Preview load the same
resources and styles.

## Design references

- [Fluent 2](https://fluent2.microsoft.design/) informed the restrained
  surface hierarchy, clear focus treatment, and subtle interaction layers.
- [WinUI control guidance](https://learn.microsoft.com/windows/apps/develop/ui/controls/)
  informed the expectation that controls share reusable, accessible XAML
  styles.
- [Avalonia styles and resources](https://docs.avaloniaui.net/docs/styling/resources)
  informed the use of resource keys rather than hard-coded values in emitted
  markup.

## Semantic tokens

`GeneratedFormDesignSystem` derives a complete token set from the form theme's
surface, text, and `AccentBrush`. Derived colours include hover, pressed,
focus, subtle selection layers, disabled content, DataGrid chrome, and group
chips. Transparent accent layers are expressed as ARGB brushes, so selection
and hover remain calm over light and dark surfaces.

The existing `Theme*Brush` keys remain stable. New values are available through
keys such as `ThemeAccentHoverBrush`, `ThemeAccentSubtleBrush`,
`ThemeDataGridHoverRowBackgroundBrush`, and
`ThemeDataGridSelectedRowBackgroundBrush`.

## Control coverage

Shared styles cover Button, TextBox, ComboBox, ComboBoxItem, CheckBox,
RadioButton, Border, ListBox, ListBoxItem, TreeView, TreeViewItem, TabControl,
TabItem, Menu, MenuItem, and DataGrid when the DataGrid package is required.

Each interactive family receives normal, hover, focus, disabled, pressed, or
selected treatment as applicable. Per-control values in the document retain
Avalonia local-value priority, so explicit user overrides are never replaced.

## DataGrid

The common DataGrid style supplies shared header, cell, hover, selected, and
grid-line tokens. The generated per-grid style keeps layout-specific choices
such as column lines, alignment, padding, font metrics, and custom colours.
Default per-grid values resolve to design-system resources; custom values stay
literal in the exported AXAML.

## Preview parity

The AXAML Runtime Preview uses the export AXAML transformation, including the
window's resources and styles. The legacy Preview also resolves default
Button, TextBox, CheckBox, Border, DataGrid, and grouping-panel colours from
the same token generator. This preserves its interactive behavior while
bringing its baseline palette in line with Export.

## Compact export

New documents default to `FullStyled` export. The existing `Compact` option is
retained as an intentional minimal-markup mode for users who need it; it does
not inject the design-system resources.
