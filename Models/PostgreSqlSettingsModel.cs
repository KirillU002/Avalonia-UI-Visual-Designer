using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace FormDesigner.Models;

public sealed partial class PostgreSqlSettingsModel : ObservableObject
{
    [ObservableProperty] private string host = "localhost";
    [ObservableProperty] private int port = 5432;
    [ObservableProperty] private string database = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty]
    [property: JsonIgnore]
    private string password = "";
    [ObservableProperty] private string sslMode = "Prefer";
    [ObservableProperty] private int connectionTimeoutSeconds = 15;
    [ObservableProperty] private int commandTimeoutSeconds = 30;
    [ObservableProperty] private string defaultSchema = "public";
    [ObservableProperty] private int previewRowsLimit = 100;
    [ObservableProperty] private bool rememberPassword = true;

    // DPAPI ciphertext belongs only to local app settings, never to project DTOs.
    public string ProtectedPassword { get; set; } = "";

    public PostgreSqlSettingsModel Clone() => new()
    {
        Host = Host, Port = Port, Database = Database, Username = Username,
        Password = Password, SslMode = SslMode,
        ConnectionTimeoutSeconds = ConnectionTimeoutSeconds, CommandTimeoutSeconds = CommandTimeoutSeconds,
        DefaultSchema = DefaultSchema, PreviewRowsLimit = PreviewRowsLimit,
        RememberPassword = RememberPassword, ProtectedPassword = ProtectedPassword
    };
}
