using CommunityToolkit.Mvvm.ComponentModel;

namespace FormDesigner.Models;

/// <summary>
/// Краткая карточка импортированной DLL, которую показываем в отдельной вкладке слева.
/// Нужна для быстрого поиска уже загруженных сборок и понимания, какие BindingSource из них созданы.
/// </summary>
public partial class ImportedDllInfoModel : ObservableObject
{
    [ObservableProperty]
    private string fileName = "";

    [ObservableProperty]
    private string assemblyPath = "";

    [ObservableProperty]
    private int sourceCount;

    [ObservableProperty]
    private string sourceNames = "";

    [ObservableProperty]
    private string typeNames = "";

    [ObservableProperty]
    private string summary = "";
}
