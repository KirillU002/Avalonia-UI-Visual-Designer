using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace FormDesigner.ViewModels;

public sealed class PropertyGridCategoryViewModel : ObservableObject
{
    private readonly Action<PropertyGridCategoryViewModel, bool>? _expandedChanged;
    private bool _isExpanded;

    public PropertyGridCategoryViewModel(
        string key,
        string title,
        bool isExpanded,
        Action<PropertyGridCategoryViewModel, bool>? expandedChanged = null)
    {
        Key = key;
        Title = title;
        _isExpanded = isExpanded;
        _expandedChanged = expandedChanged;
    }

    public string Key { get; }

    public string Title { get; }

    public ObservableCollection<PropertyGridRowViewModel> Rows { get; } = new();

    public int Count => Rows.Count;

    public string HeaderText => Title;

    public string ExpandGlyph => IsExpanded ? "\u25BE" : "\u25B8";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
                return;

            OnPropertyChanged(nameof(ExpandGlyph));
            _expandedChanged?.Invoke(this, value);
        }
    }

    public void NotifyRowsChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HeaderText));
    }
}
