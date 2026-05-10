using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace FormDesigner.Models;

/// <summary>
/// View-model узла панели Structure/Layers.
/// Сам документ по-прежнему хранится в DesignControlModel, а этот класс
/// дает удобное дерево, badges и быстрые действия для UI.
/// </summary>
public partial class StructureTreeItemModel : ObservableObject
{
    private readonly DesignControlModel? _control;

    public StructureTreeItemModel(
        DesignControlModel? control,
        string id,
        string name,
        string type,
        string text,
        bool isContainer,
        bool isGroup,
        bool isHidden,
        bool isLocked)
    {
        _control = control;
        this.id = id;
        this.name = name;
        this.type = type;
        this.text = text;
        this.isContainer = isContainer;
        this.isGroup = isGroup;
        this.isHidden = isHidden;
        this.isLocked = isLocked;
    }

    public DesignControlModel? Control => _control;
    public ObservableCollection<StructureTreeItemModel> Children { get; } = new();

    [ObservableProperty]
    private string id = "";

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string type = "";

    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private bool isContainer;

    [ObservableProperty]
    private bool isGroup;

    [ObservableProperty]
    private bool isHidden;

    [ObservableProperty]
    private bool isLocked;

    [ObservableProperty]
    private bool isSelected;

    public bool IsRoot => _control is null;
    public bool CanRename => _control is not null;
    public bool CanActOnControl => _control is not null;
    public bool HasChildren => Children.Count > 0;
    public bool HasTextPreview => !string.IsNullOrWhiteSpace(Text);
    public bool ShowSelectedBadge => IsSelected && !IsRoot;
    public bool ShowHiddenBadge => IsHidden && !IsRoot;
    public bool ShowLockedBadge => IsLocked && !IsRoot;
    public bool ShowContainerBadge => IsContainer && !IsRoot;
    public bool ShowGroupBadge => IsGroup && !IsRoot;

    public string KindLabel => IsRoot
        ? "Form"
        : IsGroup
            ? "Group"
            : IsContainer
                ? "Container"
                : "Control";

    public string Subtitle
    {
        get
        {
            if (IsRoot)
                return $"{Children.Count} элементов на форме";

            var textPart = string.IsNullOrWhiteSpace(Text) ? "" : $"  Text: {Text}";
            return $"{Type}{textPart}";
        }
    }

    public string VisibilityActionText => IsHidden ? "Показать" : "Скрыть";
    public string LockActionText => IsLocked ? "Разблокировать" : "Заблокировать";
    public double ItemOpacity => IsHidden ? 0.58 : 1.0;
    public string CardBackground => IsSelected ? "#EFF6FF" : IsRoot ? "#F8FAFC" : "#FFFFFF";
    public string CardBorderBrush => IsSelected ? "#2563EB" : IsLocked ? "#F59E0B" : "#D7E2EE";
    public string NameForeground => IsHidden ? "#64748B" : "#0F172A";

    partial void OnNameChanged(string value)
    {
        if (_control is null || string.Equals(_control.Name, value, System.StringComparison.Ordinal))
            return;

        _control.Name = value?.Trim() ?? "";
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSelectedBadge));
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasTextPreview));
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnIsHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHiddenBadge));
        OnPropertyChanged(nameof(VisibilityActionText));
        OnPropertyChanged(nameof(ItemOpacity));
        OnPropertyChanged(nameof(NameForeground));
    }

    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLockedBadge));
        OnPropertyChanged(nameof(LockActionText));
        OnPropertyChanged(nameof(CardBorderBrush));
    }
}
