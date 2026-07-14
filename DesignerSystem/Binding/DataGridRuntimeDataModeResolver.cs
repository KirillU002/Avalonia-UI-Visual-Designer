using FormDesigner.Models;
using System;
using System.Linq;

namespace FormDesigner.DesignerSystem.Binding;

public enum DataGridRuntimeDataMode
{
    Empty,
    Demo,
    Sql,
    Dll
}

public sealed record DataGridRuntimeDataModeResolution(
    DataGridRuntimeDataMode Mode,
    string Reason,
    bool SourceConfigured,
    bool ExplicitDemoEnabled);

public static class DataGridRuntimeDataModeResolver
{
    public static DataGridRuntimeDataModeResolution Resolve(
        BindingSourceModel? source,
        bool includeDemoData = false)
    {
        if (source is null)
            return Empty("source-not-selected");

        var hasVisibleFields = source.Fields.Any(field => field.IsVisible && !string.IsNullOrWhiteSpace(field.Path));
        if (!hasVisibleFields)
            return Empty("visible-columns-not-configured");

        var explicitDemo = IsExplicitDemoEnabled(source, includeDemoData);
        if (explicitDemo)
            return new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Demo, "explicit-demo-enabled", false, true);

        if (BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) == BindingSourceModel.PreviewRowModeSchemaOnly)
            return Empty("schema-only-mode");

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
        {
            var configured = SqlPreviewDataLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable;
            return configured
                ? new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Sql, "sql-source-configured", true, false)
                : Empty("sql-source-not-configured");
        }

        if (DataSourceIdentity.IsAssembly(source.SourceKind))
        {
            var configured = PreviewRowsLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable;
            return configured
                ? new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Dll, "dll-provider-available", true, false)
                : Empty("dll-metadata-only");
        }

        return Empty("manual-source-without-demo");
    }

    public static DataGridRuntimeDataModeResolution Resolve(
        BindingSourceFileModel? source,
        bool includeDemoData = false)
    {
        if (source is null)
            return Empty("source-not-selected");

        var hasVisibleFields = source.Fields.Any(field => field.IsVisible && !string.IsNullOrWhiteSpace(field.Path));
        if (!hasVisibleFields)
            return Empty("visible-columns-not-configured");

        var explicitDemo = IsExplicitDemoEnabled(source, includeDemoData);
        if (explicitDemo)
            return new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Demo, "explicit-demo-enabled", false, true);

        if (BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) == BindingSourceModel.PreviewRowModeSchemaOnly)
            return Empty("schema-only-mode");

        if (DataSourceIdentity.IsSqlServer(source.SourceKind))
        {
            var configured = SqlPreviewDataLoader.CanLoad(source) && source.UseRealPreviewRowsIfAvailable;
            return configured
                ? new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Sql, "sql-source-configured", true, false)
                : Empty("sql-source-not-configured");
        }

        if (DataSourceIdentity.IsAssembly(source.SourceKind))
        {
            var configured = !string.IsNullOrWhiteSpace(source.SourceAssemblyPath)
                             && !string.IsNullOrWhiteSpace(source.SourceTypeFullName)
                             && source.UseRealPreviewRowsIfAvailable;
            return configured
                ? new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Dll, "dll-provider-available", true, false)
                : Empty("dll-metadata-only");
        }

        return Empty("manual-source-without-demo");
    }

    public static bool IsExplicitDemoEnabled(BindingSourceModel source, bool includeDemoData = false)
    {
        return includeDemoData
               || source.UseDemoData
               || BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) == BindingSourceModel.PreviewRowModeSampleRows;
    }

    public static bool IsExplicitDemoEnabled(BindingSourceFileModel source, bool includeDemoData = false)
    {
        return includeDemoData
               || source.UseDemoData
               || BindingSourceModel.NormalizePreviewRowMode(source.PreviewRowMode) == BindingSourceModel.PreviewRowModeSampleRows;
    }

    private static DataGridRuntimeDataModeResolution Empty(string reason)
    {
        return new DataGridRuntimeDataModeResolution(DataGridRuntimeDataMode.Empty, reason, false, false);
    }
}
