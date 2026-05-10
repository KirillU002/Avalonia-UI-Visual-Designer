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
}
