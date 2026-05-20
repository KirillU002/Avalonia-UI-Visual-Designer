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
        bool isLocked,
        bool isMissingPlugin = false)
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
        this.isMissingPlugin = isMissingPlugin;
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

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private bool isSearchMatch;

    [ObservableProperty]
    private bool isMissingPlugin;

    [ObservableProperty]
    private int diagnosticErrorCount;

    [ObservableProperty]
    private int diagnosticWarningCount;

    public bool IsRoot => _control is null;
    public bool CanRename => _control is not null;
    public bool CanActOnControl => _control is not null;
    public bool HasChildren => Children.Count > 0;
    public bool HasTextPreview => !string.IsNullOrWhiteSpace(Text);
    public bool HasDiagnostics => DiagnosticErrorCount > 0 || DiagnosticWarningCount > 0;
    public bool HasDiagnosticErrors => DiagnosticErrorCount > 0;
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
    public string VisibilityGlyph => IsHidden ? "○" : "●";
    public string LockGlyph => IsLocked ? "L" : "U";
    public double ItemOpacity => IsHidden ? 0.58 : 1.0;
    public string RowBackground => IsSelected
        ? "#DBEAFE"
        : IsSearchMatch
            ? "#FFF7ED"
            : IsRoot
                ? "#F8FAFC"
                : "Transparent";
    public string RowBorderBrush => IsSelected
        ? "#2563EB"
        : IsSearchMatch
            ? "#FDBA74"
            : "Transparent";
    public string CardBackground => RowBackground;
    public string CardBorderBrush => RowBorderBrush;
    public string NameForeground => IsHidden ? "#64748B" : "#0F172A";
    public string DiagnosticBadgeText => DiagnosticErrorCount > 0
        ? DiagnosticErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : DiagnosticWarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string DiagnosticBadgeBackground => DiagnosticErrorCount > 0 ? "#DC2626" : "#D97706";
    public string TypeBadgeText => IsRoot ? "F" : IsMissingPlugin ? "?" : Type switch
    {
        "Button" => "B",
        "TextBox" => "TB",
        "TextBlock" => "T",
        "CheckBox" => "CB",
        "DataGrid" => "DG",
        "Border" => "P",
        "StackPanel" => "SP",
        "Grid" => "GR",
        "WrapPanel" => "WP",
        "Group" => "G",
        _ => "PL"
    };
    public string TypeBadgeBackground => IsRoot
        ? "#E0F2FE"
        : IsMissingPlugin
            ? "#FEE2E2"
            : IsGroup
                ? "#EEF2FF"
                : IsContainer
                    ? "#E0F2FE"
                    : "#EAF2FF";
    public string TypeBadgeForeground => IsMissingPlugin ? "#991B1B" : IsGroup ? "#3730A3" : "#075985";
    public string CompactTypeText => IsMissingPlugin ? $"{Type} missing" : Type;

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
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(RowBorderBrush));
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
        OnPropertyChanged(nameof(VisibilityGlyph));
        OnPropertyChanged(nameof(ItemOpacity));
        OnPropertyChanged(nameof(NameForeground));
    }

    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLockedBadge));
        OnPropertyChanged(nameof(LockActionText));
        OnPropertyChanged(nameof(LockGlyph));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(RowBorderBrush));
    }

    partial void OnIsSearchMatchChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(RowBorderBrush));
    }

    partial void OnIsMissingPluginChanged(bool value)
    {
        OnPropertyChanged(nameof(TypeBadgeText));
        OnPropertyChanged(nameof(TypeBadgeBackground));
        OnPropertyChanged(nameof(TypeBadgeForeground));
        OnPropertyChanged(nameof(CompactTypeText));
    }

    partial void OnDiagnosticErrorCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(HasDiagnosticErrors));
        OnPropertyChanged(nameof(DiagnosticBadgeText));
        OnPropertyChanged(nameof(DiagnosticBadgeBackground));
    }

    partial void OnDiagnosticWarningCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(DiagnosticBadgeText));
        OnPropertyChanged(nameof(DiagnosticBadgeBackground));
    }
}
