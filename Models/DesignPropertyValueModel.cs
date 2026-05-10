using CommunityToolkit.Mvvm.ComponentModel;

namespace FormDesigner.Models;

public partial class DesignPropertyValueModel : ObservableObject
{
    [ObservableProperty]
    private string key = "";

    [ObservableProperty]
    private string valueJson = "null";

    public DesignPropertyValueModel Clone()
    {
        return new DesignPropertyValueModel
        {
            Key = Key,
            ValueJson = ValueJson
        };
    }
}
