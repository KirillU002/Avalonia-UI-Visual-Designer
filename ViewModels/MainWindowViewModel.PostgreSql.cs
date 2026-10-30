using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private PostgreSqlSettingsModel _postgreSqlSettings = new();
    private CancellationTokenSource? _postgreSqlDiscovery;
    [ObservableProperty] private bool isPostgreSqlLoading;
    public ObservableCollection<string> PostgreSqlSchemas { get; } = new();
    public ObservableCollection<string> PostgreSqlTables { get; } = new();
    public bool IsPostgreSqlSourceSelected => DataSourceIdentity.IsPostgreSql(SelectedBindingSource?.SourceKind);
    public bool IsSqlServerSourceSelected => !IsPostgreSqlSourceSelected;
    public string SelectedDatabaseProviderLabel => IsPostgreSqlSourceSelected ? "PostgreSQL" : "SQL Server";
    public System.Collections.Generic.IEnumerable<string> AvailableSelectedSourcePreviewRowModes =>
        AvailablePreviewRowModes.Where(mode => !IsPostgreSqlSourceSelected || mode != BindingSourceModel.PreviewRowModeSampleRows);
    public System.Collections.Generic.IEnumerable<string> AvailableControlPreviewRowModes =>
        AvailablePreviewRowModes.Where(mode => !DataSourceIdentity.IsPostgreSql(SelectedBindingSourceForControl?.SourceKind) || mode != BindingSourceModel.PreviewRowModeSampleRows);

    public PostgreSqlSettingsModel CapturePostgreSqlSettings() => _postgreSqlSettings.Clone();
    public void ApplyPostgreSqlSettings(PostgreSqlSettingsModel? settings)
    {
        _postgreSqlSettings = settings?.Clone() ?? new();
        _ = new PostgreSqlConnectionService().Build(_postgreSqlSettings); // register in-memory credential, no project state
    }

    public BindingSourceModel AddPostgreSqlBindingSource()
    {
        var source = AddBindingSource();
        source.SourceKind = "PostgreSql";
        source.Name = GetUniqueBindingSourceName("PostgreSqlSource");
        source.Path = source.Name;
        source.ItemTypeName = "PostgreSqlRow";
        source.Description = "Источник PostgreSQL. Реальные данные; пароль хранится только в локальных настройках.";
        source.SourceSchemaName = string.IsNullOrWhiteSpace(_postgreSqlSettings.DefaultSchema) ? "public" : _postgreSqlSettings.DefaultSchema;
        source.PreviewTopN = Math.Clamp(_postgreSqlSettings.PreviewRowsLimit, 1, 10000);
        source.UseDemoData = false;
        source.AllowPreviewSampleFallback = false;
        ApplyGlobalPostgreSqlSettings();
        StatusText = "Создан PostgreSQL BindingSource. Загрузите схемы и таблицы или укажите SELECT-запрос, затем получите колонки.";
        RaiseBindingEditorProperties();
        return source;
    }

    [RelayCommand]
    private void ApplyGlobalPostgreSqlSettings()
    {
        if (SelectedBindingSource is not { } source || !IsPostgreSqlSourceSelected) return;
        var result = new PostgreSqlConnectionService().Build(_postgreSqlSettings);
        source.SourceConnectionString = result.Success ? PostgreSqlConnectionService.WithoutSecrets(result.ConnectionString) : "";
        StatusText = result.Success ? "Применены глобальные параметры PostgreSQL. Пароль не записан в проект." : result.ErrorMessage;
    }

    private string ResolvePostgreSqlConnection(BindingSourceModel source)
    {
        if (!string.IsNullOrWhiteSpace(source.SourceConnectionString)) return source.SourceConnectionString;
        var result = new PostgreSqlConnectionService().Build(_postgreSqlSettings);
        return result.Success ? PostgreSqlConnectionService.WithoutSecrets(result.ConnectionString) : "";
    }

    [RelayCommand]
    private async Task TestPostgreSqlSourceAsync()
    {
        if (SelectedBindingSource is not { } source || !IsPostgreSqlSourceSelected) return;
        await RunPostgreSqlDiscoveryAsync(async token =>
        {
            TraceDocumentDebug("POSTGRES_CONNECTION_START", "provider=Npgsql", toOutput: true);
            var result = await new PostgreSqlConnectionService().TestConnectionStringAsync(ResolvePostgreSqlConnection(source), token);
            StatusText = result.Message;
            TraceDocumentDebug(result.Success ? "POSTGRES_CONNECTION_SUCCESS" : "POSTGRES_CONNECTION_FAILED", result.Message, toOutput: true, warning: !result.Success);
        });
    }

    [RelayCommand]
    private async Task LoadPostgreSqlSchemasAsync()
    {
        if (SelectedBindingSource is not { } source || !IsPostgreSqlSourceSelected) return;
        var session = DocumentSessionId;
        await RunPostgreSqlDiscoveryAsync(async token =>
        {
            var schemas = await new DatabaseReadService(DatabaseProviderAdapters.PostgreSql)
                .GetSchemasAsync(ResolvePostgreSqlConnection(source), token);
            if (SelectedBindingSource != source || session != DocumentSessionId) return;
            PostgreSqlSchemas.Clear();
            foreach (var schema in schemas) PostgreSqlSchemas.Add(schema);
            if (!schemas.Contains(source.SourceSchemaName)) source.SourceSchemaName = schemas.FirstOrDefault() ?? "public";
            StatusText = $"Загружены схемы PostgreSQL: {schemas.Count}.";
            TraceDocumentDebug("POSTGRES_SCHEMA_LOADED", $"count={schemas.Count}", toOutput: true);
        });
    }

    [RelayCommand]
    private async Task LoadPostgreSqlTablesAsync()
    {
        if (SelectedBindingSource is not { } source || !IsPostgreSqlSourceSelected) return;
        var session = DocumentSessionId;
        var schema = source.SourceSchemaName;
        await RunPostgreSqlDiscoveryAsync(async token =>
        {
            var tables = await new DatabaseReadService(DatabaseProviderAdapters.PostgreSql)
                .GetTablesAsync(ResolvePostgreSqlConnection(source), schema, token);
            if (SelectedBindingSource != source || session != DocumentSessionId || schema != source.SourceSchemaName) return;
            PostgreSqlTables.Clear();
            foreach (var table in tables) PostgreSqlTables.Add(table.Name);
            StatusText = $"Загружены таблицы и представления PostgreSQL: {tables.Count}.";
            TraceDocumentDebug("POSTGRES_TABLES_LOADED", $"count={tables.Count}", toOutput: true);
        });
    }

    [RelayCommand] private void CancelPostgreSqlOperation() => _postgreSqlDiscovery?.Cancel();

    private void ResetPostgreSqlDiscovery()
    {
        _postgreSqlDiscovery?.Cancel();
        PostgreSqlSchemas.Clear();
        PostgreSqlTables.Clear();
    }

    private async Task RunPostgreSqlDiscoveryAsync(Func<CancellationToken, Task> operation)
    {
        if (IsPostgreSqlLoading) return;
        using var cancellation = new CancellationTokenSource();
        _postgreSqlDiscovery = cancellation;
        IsPostgreSqlLoading = true;
        try { await operation(cancellation.Token); }
        catch (Exception ex)
        {
            StatusText = ex is ArgumentException ? "Preview PostgreSQL разрешает только один SELECT/ WITH-запрос." : PostgreSqlErrors.Message(ex);
            TraceDocumentDebug("POSTGRES_CONNECTION_FAILED", PostgreSqlErrors.Details(ex), toOutput: true, warning: true);
        }
        finally
        {
            if (ReferenceEquals(_postgreSqlDiscovery, cancellation)) _postgreSqlDiscovery = null;
            IsPostgreSqlLoading = false;
        }
    }

    private async Task<BindingSourceModel> CreatePostgreSqlBindingSourceAsync(BindingSourceModel template, CancellationToken token = default)
    {
        var source = template.Clone();
        source.SourceConnectionString = ResolvePostgreSqlConnection(template);
        source.SourceSchemaName = string.IsNullOrWhiteSpace(source.SourceSchemaName) ? "public" : source.SourceSchemaName;
        if (string.IsNullOrWhiteSpace(source.SourceQuery) && string.IsNullOrWhiteSpace(source.SourceTableName))
            throw new InvalidOperationException("Укажите таблицу или SELECT-запрос PostgreSQL.");
        var fields = await new DatabaseReadService(DatabaseProviderAdapters.PostgreSql).DescribeAsync(source, token);
        source.Fields.Clear();
        foreach (var field in fields) source.Fields.Add(field);
        source.SourceTypeFullName = string.IsNullOrWhiteSpace(source.SourceQuery)
            ? $"{source.SourceSchemaName}.{source.SourceTableName}" : "PostgreSqlQuery";
        source.UseDemoData = false;
        source.AllowPreviewSampleFallback = false;
        TraceDocumentDebug("POSTGRES_COLUMNS_LOADED", $"count={source.Fields.Count}", toOutput: true);
        return source;
    }

    [RelayCommand]
    private async Task LoadPostgreSqlColumnsAsync()
    {
        if (SelectedBindingSource is not { } source || !IsPostgreSqlSourceSelected) return;
        var session = DocumentSessionId;
        var signature = DataSourceIdentity.BuildKey(source);
        await RunPostgreSqlDiscoveryAsync(async token =>
        {
            var imported = await CreatePostgreSqlBindingSourceAsync(source, token);
            if (SelectedBindingSource != source || session != DocumentSessionId || signature != DataSourceIdentity.BuildKey(source)) return;
            BeginUndoBatch();
            try { ApplyDatabaseSourceToSelectedBindingSource(imported); }
            finally { CommitUndoBatch(); }
            StatusText = $"Загружены колонки PostgreSQL: {source.Fields.Count}. Привяжите источник к DataGrid.";
        });
    }
}
