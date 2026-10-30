using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FormDesigner.ViewModels;

public sealed partial class SettingsWindowViewModel
{
    private CancellationTokenSource? _postgreSqlTest;
    [ObservableProperty] private PostgreSqlSettingsModel postgres = new();
    [ObservableProperty] private string postgresStatus = "Параметры PostgreSQL не проверены.";
    public bool IsPostgresSelected => IsSelected("postgres");
    public string[] PostgreSqlSslModes { get; } = { "Disable", "Allow", "Prefer", "Require", "VerifyCA", "VerifyFull" };

    partial void OnPostgresChanged(PostgreSqlSettingsModel? oldValue, PostgreSqlSettingsModel newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= PostgresSettingsChanged;
        newValue.PropertyChanged += PostgresSettingsChanged;
    }
    private void PostgresSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_trackDirty) IsDirty = true;
    }

    [RelayCommand]
    private async Task TestPostgreSqlConnectionAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _postgreSqlTest = cancellation;
        PostgresStatus = "Подключение к PostgreSQL…";
        try
        {
            var result = await new PostgreSqlConnectionService().TestConnectionAsync(Postgres.Clone(), cancellation.Token);
            PostgresStatus = result.Message;
        }
        finally { if (ReferenceEquals(_postgreSqlTest, cancellation)) _postgreSqlTest = null; }
    }
    [RelayCommand] private void CancelPostgreSqlTest() => _postgreSqlTest?.Cancel();
    [RelayCommand] private void ResetPostgreSqlSettings()
    {
        Postgres = new();
        IsDirty = true;
        PostgresStatus = "Настройки PostgreSQL сброшены. Нажмите «Применить» или «Сохранить».";
    }
}
