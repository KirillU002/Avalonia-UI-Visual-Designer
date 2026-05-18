using CommunityToolkit.Mvvm.ComponentModel;

namespace FormDesigner.Models;

/// <summary>
/// Краткая информация об установленном plugin-пакете для вкладки Plugins.
/// Показывает, откуда загружен plugin и какие контролы он добавил в toolbox.
/// </summary>
public partial class InstalledPluginInfoModel : ObservableObject
{
    [ObservableProperty]
    private string pluginName = "";

    [ObservableProperty]
    private string version = "";

    [ObservableProperty]
    private string assemblyPath = "";

    [ObservableProperty]
    private int controlCount;

    [ObservableProperty]
    private string controlsSummary = "";

    [ObservableProperty]
    private string summary = "";

    [ObservableProperty]
    private string status = "OK";

    [ObservableProperty]
    private string apiVersion = "";

    [ObservableProperty]
    private string pluginId = "";

    [ObservableProperty]
    private string errorDetails = "";

    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);

    public string StatusBadgeBackground => Status switch
    {
        "Error" => "#FEE2E2",
        "Warning" => "#FEF3C7",
        _ => "#DCFCE7"
    };

    public string StatusBadgeBorder => Status switch
    {
        "Error" => "#FCA5A5",
        "Warning" => "#FCD34D",
        _ => "#86EFAC"
    };

    public string StatusBadgeForeground => Status switch
    {
        "Error" => "#B91C1C",
        "Warning" => "#92400E",
        _ => "#166534"
    };
}
