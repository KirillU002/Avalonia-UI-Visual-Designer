using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace FormDesigner.Models;

public sealed partial class ToolboxGroupModel : ObservableObject
{
    public ToolboxGroupModel(string title, string providerId, string badge, int sortOrder)
    {
        Title = title;
        ProviderId = providerId;
        Badge = badge;
        SortOrder = sortOrder;
    }

    public string Title { get; }

    public string ProviderId { get; }

    public string Badge { get; }

    public int SortOrder { get; }

    public ObservableCollection<ToolboxItem> Items { get; } = new();

    [ObservableProperty]
    private bool isExpanded = true;
}
