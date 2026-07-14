using System.Collections.Generic;

namespace FormDesigner.Models;

public class DesignerDocumentFileModel
{
    public string Version { get; set; } = "2.3";

    public double DesignWidth { get; set; } = 1200;

    public double DesignHeight { get; set; } = 800;

    public int SnapStep { get; set; } = 10;

    public bool IsGridSnapEnabled { get; set; } = true;

    public bool IsControlSnapEnabled { get; set; } = true;

    public int SnapThreshold { get; set; } = 6;

    public bool IsCanvasSnappingEnabled { get; set; } = true;

    public bool IsDesignerGridVisible { get; set; } = true;

    public bool IsSmartGuidesEnabled { get; set; } = true;

    public bool IsDistanceHintsEnabled { get; set; } = true;

    public bool IgnoreLockedDuringSelection { get; set; } = true;

    public bool IsSelectionToolbarEnabled { get; set; } = true;

    public string SurfaceBackground { get; set; } = "#FFFFFF";

    public string SurfaceGridMinorColor { get; set; } = "#DCE4EE";

    public string SurfaceGridMajorColor { get; set; } = "#B7C7DA";

    public string SurfaceLayoutMode { get; set; } = DesignerLayoutModes.Absolute;

    public string SurfaceLayoutOrientation { get; set; } = DesignerLayoutModes.Vertical;

    public double SurfaceLayoutSpacing { get; set; } = 12;

    public int SurfaceLayoutColumns { get; set; } = 3;

    public int SurfaceLayoutRows { get; set; } = 3;

    public string SurfaceGridColumnDefinitions { get; set; } = "";

    public string SurfaceGridRowDefinitions { get; set; } = "";

    public string FormTheme { get; set; } = DesignerThemeCatalog.Light;

    public string FormTitle { get; set; } = "Form1";

    public string FormWindowState { get; set; } = "Normal";

    public string FormStartupLocation { get; set; } = "CenterScreen";

    public bool FormCanResize { get; set; } = true;

    public bool FormShowInTaskbar { get; set; } = true;

    public bool FormTopmost { get; set; }

    public bool FormHasSystemDecorations { get; set; } = true;

    public List<DesignerControlFileModel> Controls { get; set; } = new();

    public List<BindingSourceFileModel> BindingSources { get; set; } = new();

    public List<InteractionFileModel> Interactions { get; set; } = new();
}

public class DesignerControlFileModel
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptorId { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string PluginVersion { get; set; } = "";
    public string ParentId { get; set; } = "";
    public string Text { get; set; } = "";
    public string PlaceholderText { get; set; } = "";
    public string ImageSource { get; set; } = "";
    public string Background { get; set; } = "#FFFFFF";
    public string Foreground { get; set; } = "#0F172A";
    public string BorderBrush { get; set; } = "#94A3B8";
    public double BorderThickness { get; set; } = 1;
    public double CornerRadius { get; set; } = 6;
    public string FontFamily { get; set; } = "Inter";
    public double FontSize { get; set; } = 14;
    public string FontWeight { get; set; } = "Normal";
    public double Opacity { get; set; } = 1;
    public double Padding { get; set; } = 8;
    public string ChildLayoutMode { get; set; } = "";
    public string LayoutOrientation { get; set; } = DesignerLayoutModes.Vertical;
    public double LayoutSpacing { get; set; } = 12;
    public string Margin { get; set; } = "0";
    public string HorizontalAlignment { get; set; } = DesignerLayoutModes.AlignStretch;
    public string VerticalAlignment { get; set; } = DesignerLayoutModes.AlignTop;
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public string Stretch { get; set; } = "Uniform";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 140;
    public double Height { get; set; } = 36;
    public bool AnchorLeft { get; set; } = true;
    public bool AnchorTop { get; set; } = true;
    public bool AnchorRight { get; set; }
    public bool AnchorBottom { get; set; }
    public int GridRow { get; set; }
    public int GridColumn { get; set; }
    public int GridRowSpan { get; set; } = 1;
    public int GridColumnSpan { get; set; } = 1;
    public int StackOrder { get; set; }
    public int Columns { get; set; } = 3;
    public int Rows { get; set; } = 3;
    public string GridColumnDefinitions { get; set; } = "";
    public string GridRowDefinitions { get; set; } = "";
    public bool ShowGridLines { get; set; } = true;
    public bool AutoGenerateColumns { get; set; }
    public string BindingSourceId { get; set; } = "";
    public string TextBindingPath { get; set; } = "";
    public string GeneratedButtonActionKey { get; set; } = "";
    public string DataGridRowBackground { get; set; } = "#FFFFFF";
    public string DataGridAlternateRowBackground { get; set; } = "#F8FAFC";
    public string DataGridTextAlignment { get; set; } = DesignControlModel.DataGridTextAlignmentLeft;
    public string DataGridGlowColor { get; set; } = "#60A5FA";
    public string DataGridHeaderBackground { get; set; } = "#E2E8F0";
    public string DataGridHeaderForeground { get; set; } = "#0F172A";
    public string DataGridRowForeground { get; set; } = "#0F172A";
    public string DataGridHoverRowBackground { get; set; } = "#EFF6FF";
    public string DataGridSelectedRowBackground { get; set; } = "#DBEAFE";
    public string DataGridSelectedRowForeground { get; set; } = "#0F172A";
    public string DataGridGridLineBrush { get; set; } = "#D7E2EE";
    public string DataGridOuterBorderBrush { get; set; } = "#60A5FA";
    public double DataGridHeaderFontSize { get; set; } = 13;
    public string DataGridHeaderFontWeight { get; set; } = "SemiBold";
    public double DataGridRowFontSize { get; set; } = 13;
    public string DataGridRowFontWeight { get; set; } = "Normal";
    public double DataGridHeaderHeight { get; set; } = 46;
    public double DataGridRowHeight { get; set; } = 36;
    public double DataGridCellPadding { get; set; } = 14;
    public bool DataGridShowHeader { get; set; } = true;
    public bool DataGridShowRowLines { get; set; } = true;
    public bool DataGridShowColumnLines { get; set; } = true;
    public bool DataGridShowAlternatingRows { get; set; } = true;
    public bool ShowFilterRow { get; set; }
    public string FilterMode { get; set; } = DesignControlModel.DataGridFilterModeContains;
    public bool ShowGroupPanel { get; set; }
    public bool AllowGrouping { get; set; } = true;
    public bool ShowFooter { get; set; } = true;
    public List<DesignPropertyValueFileModel> CustomProperties { get; set; } = new();
}

public class DesignPropertyValueFileModel
{
    public string Key { get; set; } = "";
    public string ValueJson { get; set; } = "null";
}

public class InteractionFileModel
{
    public string Id { get; set; } = "";
    public string SourceControlName { get; set; } = "";
    public string EventName { get; set; } = InteractionModel.EventDataGridSelectionChanged;
    public string ActionType { get; set; } = InteractionModel.ActionSetProperty;
    public string TargetControlName { get; set; } = "";
    public string TargetProperty { get; set; } = InteractionModel.TargetPropertyText;
    public string SourcePath { get; set; } = "";
    public string TextTemplate { get; set; } = "";
    public string MissingValueBehavior { get; set; } = InteractionModel.MissingValueEmpty;
    public string NoSelectionBehavior { get; set; } = InteractionModel.NoSelectionClearTarget;
    public string NoSelectionText { get; set; } = "";
    public string MessageTitle { get; set; } = "";
    public string TargetFormId { get; set; } = "";
    public string TargetFormName { get; set; } = "";
    public string OpenMode { get; set; } = InteractionModel.OpenModeShow;
    public bool CloseCurrentAfterOpen { get; set; }
}

public class BindingSourceFileModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Source";
    public string Path { get; set; } = "Items";
    public string ItemTypeName { get; set; } = "ItemRow";
    public string Description { get; set; } = "";
    public string SourceKind { get; set; } = "Manual";
    public string SourceAssemblyPath { get; set; } = "";
    public string SourceTypeFullName { get; set; } = "";
    public string SourceTableName { get; set; } = "";
    public string SourceConnectionString { get; set; } = "";
    public string SourceSchemaName { get; set; } = "dbo";
    public string SourceQuery { get; set; } = "";
    public string PreviewRowMode { get; set; } = BindingSourceModel.PreviewRowModeTopN;
    public int PreviewTopN { get; set; } = 50;
    public string PreviewSortColumn { get; set; } = "";
    public string PreviewSortDirection { get; set; } = BindingFieldModel.SortDirectionAscending;
    public bool UseRealPreviewRowsIfAvailable { get; set; } = true;
    public bool UseDemoData { get; set; }
    public bool AllowPreviewSampleFallback { get; set; }
    public List<BindingFieldFileModel> Fields { get; set; } = new();
}

public class BindingFieldFileModel
{
    public string Header { get; set; } = "Column";
    public string Path { get; set; } = "Property";
    public string SampleValue { get; set; } = "Value";
    public string Width { get; set; } = "*";
    public string TypeName { get; set; } = "string";
    public string DbType { get; set; } = "";
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool CanRead { get; set; } = true;
    public bool CanWrite { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsSortable { get; set; } = true;
    public string SortDirection { get; set; } = BindingFieldModel.SortDirectionNone;
    public int SortOrder { get; set; } = -1;
    public int GroupOrder { get; set; } = -1;
    public string HeaderAlignment { get; set; } = BindingFieldModel.AlignmentLeft;
    public string CellAlignment { get; set; } = BindingFieldModel.AlignmentLeft;
    public string FormatString { get; set; } = "";
    public string NullText { get; set; } = "";
    public string TextTrimming { get; set; } = BindingFieldModel.TextTrimmingCharacterEllipsis;
    public string TextWrapping { get; set; } = BindingFieldModel.TextWrappingNoWrap;
    public int MaxLines { get; set; } = 1;
    public double MinWidth { get; set; } = 56;
    public double MaxWidth { get; set; }
    public bool AllowResize { get; set; } = true;
    public bool AllowSort { get; set; } = true;
    public bool AllowFilter { get; set; } = true;
    public int VisibleIndex { get; set; } = -1;
    public string SummaryType { get; set; } = BindingFieldModel.SummaryTypeNone;
    public string SummaryFormat { get; set; } = "";
}
