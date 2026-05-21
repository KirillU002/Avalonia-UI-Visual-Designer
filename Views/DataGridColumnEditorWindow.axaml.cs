using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace FormDesigner.Views;

public partial class DataGridColumnEditorWindow : Window
{
    private DataGridColumnEditorViewModel? _editor;

    public DataGridColumnEditorWindow()
    {
        InitializeComponent();
    }

    public DataGridColumnEditorWindow(
        MainWindowViewModel owner,
        DesignControlModel dataGrid,
        BindingSourceModel? bindingSource)
        : this()
    {
        _editor = new DataGridColumnEditorViewModel(owner, dataGrid, bindingSource);
        DataContext = _editor;
        Title = dataGrid.Type == "Demo.TreeList"
            ? "Редактор колонок TreeList"
            : "Редактор колонок DataGrid";
        Closed += (_, _) => _editor.Dispose();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AddColumnButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.AddColumn();
    }

    private void DeleteColumnButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.DeleteSelectedColumn();
    }

    private void MoveColumnUpButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.MoveSelectedColumn(-1);
    }

    private void MoveColumnDownButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.MoveSelectedColumn(1);
    }

    private void ShowAllColumnsButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.SetAllColumnsVisible(true);
    }

    private void HideAllColumnsButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.SetAllColumnsVisible(false);
    }

    private void ResetColumnWidthsButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.ResetColumnWidths();
    }

    private void ClearGroupingButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.ClearGrouping();
    }

    private void RemoveSelectedGroupingButton_Click(object? sender, RoutedEventArgs e)
    {
        _editor?.RemoveSelectedGrouping();
    }

    private void SetColumnWidthPresetButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BindingFieldModel field, Tag: string preset }
            && !string.IsNullOrWhiteSpace(preset))
        {
            _editor?.SetColumnWidthPreset(field, preset);
        }
    }
}

public sealed partial class DataGridColumnEditorViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _owner;
    private readonly DesignControlModel _dataGrid;
    private readonly BindingSourceModel? _bindingSource;
    private readonly Dictionary<BindingFieldModel, DataGridColumnEditorFieldItem> _itemsByField = new();
    private bool _isDisposed;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private DataGridColumnEditorFieldItem? selectedItem;

    public DataGridColumnEditorViewModel(
        MainWindowViewModel owner,
        DesignControlModel dataGrid,
        BindingSourceModel? bindingSource)
    {
        _owner = owner;
        _dataGrid = dataGrid;
        _bindingSource = bindingSource;

        if (_bindingSource is not null)
        {
            _bindingSource.Fields.CollectionChanged += BindingFields_CollectionChanged;
            foreach (var field in _bindingSource.Fields)
                AttachField(field);
        }

        RebuildFilteredFields();
        SelectedItem = FilteredFields.FirstOrDefault();
    }

    public ObservableCollection<DataGridColumnEditorFieldItem> FilteredFields { get; } = new();

    public IReadOnlyList<string> AvailableColumnAlignments => _owner.AvailableColumnAlignments;

    public IReadOnlyList<string> AvailableColumnTextTrimmings => _owner.AvailableColumnTextTrimmings;

    public IReadOnlyList<string> AvailableColumnTextWrappings => _owner.AvailableColumnTextWrappings;

    public IReadOnlyList<string> AvailableFieldSortDirections => _owner.AvailableFieldSortDirections;

    public IReadOnlyList<string> AvailableFieldSummaryTypes => _owner.AvailableFieldSummaryTypes;

    public IReadOnlyList<InteractionOptionModel> AvailableColumnAlignmentOptions { get; } =
    new[]
    {
        new InteractionOptionModel(BindingFieldModel.AlignmentLeft, "Слева", "Текст прижимается к левому краю."),
        new InteractionOptionModel(BindingFieldModel.AlignmentCenter, "По центру", "Текст выравнивается по центру колонки."),
        new InteractionOptionModel(BindingFieldModel.AlignmentRight, "Справа", "Удобно для чисел, сумм и идентификаторов.")
    };

    public IReadOnlyList<InteractionOptionModel> AvailableColumnTextTrimmingOptions { get; } =
    new[]
    {
        new InteractionOptionModel(BindingFieldModel.TextTrimmingNone, "Не обрезать", "Показывать текст полностью, если хватает места."),
        new InteractionOptionModel(BindingFieldModel.TextTrimmingCharacterEllipsis, "Обрезать символами", "Длинный текст завершается многоточием."),
        new InteractionOptionModel(BindingFieldModel.TextTrimmingWordEllipsis, "Обрезать словами", "Текст обрезается по словам с многоточием.")
    };

    public IReadOnlyList<InteractionOptionModel> AvailableColumnTextWrappingOptions { get; } =
    new[]
    {
        new InteractionOptionModel(BindingFieldModel.TextWrappingNoWrap, "В одну строку", "Текст не переносится."),
        new InteractionOptionModel(BindingFieldModel.TextWrappingWrap, "Переносить", "Текст может занимать несколько строк.")
    };

    public IReadOnlyList<InteractionOptionModel> AvailableFieldSortDirectionOptions { get; } =
    new[]
    {
        new InteractionOptionModel(BindingFieldModel.SortDirectionNone, "Нет", "Колонка не сортируется по умолчанию."),
        new InteractionOptionModel(BindingFieldModel.SortDirectionAscending, "По возрастанию", "Сортировка от меньшего к большему."),
        new InteractionOptionModel(BindingFieldModel.SortDirectionDescending, "По убыванию", "Сортировка от большего к меньшему.")
    };

    public IReadOnlyList<InteractionOptionModel> AvailableFieldSummaryTypeOptions { get; } =
    new[]
    {
        new InteractionOptionModel(BindingFieldModel.SummaryTypeNone, "Нет", "Итог для колонки не показывается."),
        new InteractionOptionModel(BindingFieldModel.SummaryTypeCount, "Количество", "Подсчитать количество строк."),
        new InteractionOptionModel(BindingFieldModel.SummaryTypeSum, "Сумма", "Сложить числовые значения."),
        new InteractionOptionModel(BindingFieldModel.SummaryTypeAvg, "Среднее", "Показать среднее значение."),
        new InteractionOptionModel(BindingFieldModel.SummaryTypeMin, "Минимум", "Показать минимальное значение."),
        new InteractionOptionModel(BindingFieldModel.SummaryTypeMax, "Максимум", "Показать максимальное значение.")
    };

    public bool HasBindingSource => _bindingSource is not null;

    public bool HasNoBindingSource => _bindingSource is null;

    public bool HasSelectedField => SelectedItem is not null;

    public bool HasNoSelectedField => SelectedItem is null;

    public bool HasGroupedFields => _bindingSource?.Fields.Any(field => field.GroupOrder >= 0) == true;

    public bool CanRemoveSelectedGrouping => SelectedField?.GroupOrder >= 0;

    public BindingFieldModel? SelectedField => SelectedItem?.Field;

    public string HeaderText
    {
        get
        {
            var controlName = string.IsNullOrWhiteSpace(_dataGrid.Name) ? "DataGrid" : _dataGrid.Name;
            return _bindingSource is null
                ? $"{controlName}: источник данных не выбран"
                : $"{controlName}: колонки источника {_bindingSource.Name}";
        }
    }

    public string SummaryText
    {
        get
        {
            if (_bindingSource is null)
                return "Редактор колонок станет доступен после выбора BindingSource.";

            var totalCount = _bindingSource.Fields.Count;
            var visibleCount = _bindingSource.Fields.Count(field => field.IsVisible);
            var hiddenCount = totalCount - visibleCount;
            var sortableCount = _bindingSource.Fields.Count(field => field.AllowSort && field.IsSortable);
            var sortedCount = _bindingSource.Fields.Count(field => !string.Equals(field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase));
            var groupedCount = _bindingSource.Fields.Count(field => field.GroupOrder >= 0);
            var summaryCount = _bindingSource.Fields.Count(field => BindingFieldModel.NormalizeSummaryType(field.SummaryType) != BindingFieldModel.SummaryTypeNone);

            return $"Всего: {totalCount}, видимых: {visibleCount}, скрытых: {hiddenCount}, сортируемых: {sortableCount}, сортировок: {sortedCount}, группировок: {groupedCount}, итогов: {summaryCount}. Изменения сразу отражаются в preview.";
        }
    }

    public void AddColumn()
    {
        if (_bindingSource is null)
            return;

        var index = _bindingSource.Fields.Count + 1;
        var path = CreateUniquePath($"Field{index}");
        var field = new BindingFieldModel
        {
            Header = $"Колонка {index}",
            Path = path,
            SampleValue = $"Значение {index}",
            Width = "*",
            TypeName = "string",
            VisibleIndex = _bindingSource.Fields.Count
        };

        _owner.BeginUndoBatch();
        try
        {
            _bindingSource.Fields.Add(field);
            SearchText = string.Empty;
            SelectField(field);
            _owner.StatusText = $"Добавлена колонка «{field.Header}»";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void DeleteSelectedColumn()
    {
        if (_bindingSource is null || SelectedItem is null)
            return;

        var field = SelectedItem.Field;
        var orderedFields = OrderedFields().ToList();
        var removedIndex = orderedFields.IndexOf(field);

        _owner.BeginUndoBatch();
        try
        {
            _bindingSource.Fields.Remove(field);
            NormalizeVisibleIndexes(OrderedFields());

            var nextField = OrderedFields().ElementAtOrDefault(Math.Max(0, removedIndex - 1));
            SelectField(nextField);
            _owner.StatusText = $"Удалена колонка «{field.Header}»";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void MoveSelectedColumn(int direction)
    {
        if (_bindingSource is null || SelectedItem is null || direction == 0)
            return;

        var orderedFields = OrderedFields().ToList();
        var currentIndex = orderedFields.IndexOf(SelectedItem.Field);
        var targetIndex = Math.Clamp(currentIndex + direction, 0, orderedFields.Count - 1);
        if (currentIndex < 0 || currentIndex == targetIndex)
            return;

        _owner.BeginUndoBatch();
        try
        {
            (orderedFields[currentIndex], orderedFields[targetIndex]) = (orderedFields[targetIndex], orderedFields[currentIndex]);
            NormalizeVisibleIndexes(orderedFields);
            RebuildFilteredFields(SelectedItem.Field);
            _owner.StatusText = $"Порядок колонки «{SelectedItem.Field.Header}» изменен";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void SetAllColumnsVisible(bool isVisible)
    {
        if (_bindingSource is null)
            return;

        _owner.BeginUndoBatch();
        try
        {
            foreach (var field in _bindingSource.Fields)
                field.IsVisible = isVisible;

            OnPropertyChanged(nameof(SummaryText));
            _owner.StatusText = isVisible
                ? "Все колонки DataGrid показаны"
                : "Все колонки DataGrid скрыты";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void ResetColumnWidths()
    {
        if (_bindingSource is null)
            return;

        _owner.BeginUndoBatch();
        try
        {
            foreach (var field in _bindingSource.Fields)
                field.Width = "*";

            _owner.StatusText = "Ширины колонок DataGrid сброшены";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void ClearGrouping()
    {
        if (_bindingSource is null || !HasGroupedFields)
            return;

        _owner.BeginUndoBatch();
        try
        {
            foreach (var field in _bindingSource.Fields.Where(field => field.GroupOrder >= 0))
                field.GroupOrder = -1;

            RaiseGroupingProperties();
            _owner.StatusText = "Группировка DataGrid очищена";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void RemoveSelectedGrouping()
    {
        if (_bindingSource is null || SelectedField is not { GroupOrder: >= 0 } field)
            return;

        _owner.BeginUndoBatch();
        try
        {
            field.GroupOrder = -1;
            NormalizeGroupOrders();
            RaiseGroupingProperties();
            _owner.StatusText = $"Группировка колонки «{field.Header}» снята";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void SetColumnWidthPreset(BindingFieldModel field, string preset)
    {
        if (_bindingSource is null || !_bindingSource.Fields.Contains(field) || string.IsNullOrWhiteSpace(preset))
            return;

        _owner.BeginUndoBatch();
        try
        {
            field.Width = preset;
            _owner.StatusText = $"Ширина колонки «{field.Header}» установлена: {preset}";
        }
        finally
        {
            _owner.CommitUndoBatch();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (_bindingSource is not null)
            _bindingSource.Fields.CollectionChanged -= BindingFields_CollectionChanged;

        foreach (var field in _itemsByField.Keys.ToList())
            DetachField(field);
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildFilteredFields(SelectedItem?.Field);
    }

    partial void OnSelectedItemChanged(DataGridColumnEditorFieldItem? value)
    {
        OnPropertyChanged(nameof(SelectedField));
        OnPropertyChanged(nameof(HasSelectedField));
        OnPropertyChanged(nameof(HasNoSelectedField));
        OnPropertyChanged(nameof(CanRemoveSelectedGrouping));
    }

    private void BindingFields_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (BindingFieldModel field in e.OldItems)
                DetachField(field);
        }

        if (e.NewItems is not null)
        {
            foreach (BindingFieldModel field in e.NewItems)
                AttachField(field);
        }

        RebuildFilteredFields(SelectedItem?.Field);
        OnPropertyChanged(nameof(SummaryText));
        RaiseGroupingProperties();
    }

    private void Field_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BindingFieldModel.Header)
            or nameof(BindingFieldModel.Path)
            or nameof(BindingFieldModel.SampleValue)
            or nameof(BindingFieldModel.VisibleIndex))
        {
            RebuildFilteredFields(SelectedItem?.Field);
        }

        OnPropertyChanged(nameof(SummaryText));
        if (e.PropertyName is nameof(BindingFieldModel.GroupOrder))
            RaiseGroupingProperties();
    }

    private void AttachField(BindingFieldModel field)
    {
        if (_itemsByField.ContainsKey(field))
            return;

        field.PropertyChanged += Field_PropertyChanged;
        _itemsByField[field] = new DataGridColumnEditorFieldItem(field);
    }

    private void DetachField(BindingFieldModel field)
    {
        field.PropertyChanged -= Field_PropertyChanged;

        if (_itemsByField.Remove(field, out var item))
            item.Dispose();
    }

    private void RebuildFilteredFields(BindingFieldModel? preferredField = null)
    {
        FilteredFields.Clear();

        if (_bindingSource is null)
            return;

        var search = SearchText?.Trim();
        foreach (var field in OrderedFields())
        {
            if (!IsFieldMatch(field, search))
                continue;

            if (!_itemsByField.TryGetValue(field, out var item))
            {
                AttachField(field);
                item = _itemsByField[field];
            }

            FilteredFields.Add(item);
        }

        if (preferredField is not null)
            SelectField(preferredField);
        else if (SelectedItem is null || !FilteredFields.Contains(SelectedItem))
            SelectedItem = FilteredFields.FirstOrDefault();
    }

    private void SelectField(BindingFieldModel? field)
    {
        if (field is null)
        {
            SelectedItem = FilteredFields.FirstOrDefault();
            return;
        }

        if (_itemsByField.TryGetValue(field, out var item) && FilteredFields.Contains(item))
            SelectedItem = item;
        else
            SelectedItem = FilteredFields.FirstOrDefault();
    }

    private IEnumerable<BindingFieldModel> OrderedFields()
    {
        return _bindingSource?.Fields
            .Select((field, index) => new { Field = field, Index = index })
            .OrderBy(item => item.Field.VisibleIndex < 0 ? int.MaxValue : item.Field.VisibleIndex)
            .ThenBy(item => item.Index)
            .Select(item => item.Field)
            ?? Enumerable.Empty<BindingFieldModel>();
    }

    private void NormalizeVisibleIndexes(IEnumerable<BindingFieldModel> orderedFields)
    {
        var index = 0;
        foreach (var field in orderedFields)
            field.VisibleIndex = index++;
    }

    private void NormalizeGroupOrders()
    {
        if (_bindingSource is null)
            return;

        var groupedFields = _bindingSource.Fields
            .Where(field => field.GroupOrder >= 0)
            .OrderBy(field => field.GroupOrder)
            .ThenBy(field => field.Header)
            .ToList();

        for (var index = 0; index < groupedFields.Count; index++)
            groupedFields[index].GroupOrder = index;
    }

    private void RaiseGroupingProperties()
    {
        OnPropertyChanged(nameof(HasGroupedFields));
        OnPropertyChanged(nameof(CanRemoveSelectedGrouping));
        OnPropertyChanged(nameof(SummaryText));
    }

    private bool IsFieldMatch(BindingFieldModel field, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return Contains(field.Header, search)
            || Contains(field.Path, search)
            || Contains(field.SampleValue, search);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private string CreateUniquePath(string candidate)
    {
        if (_bindingSource is null)
            return candidate;

        var usedPaths = _bindingSource.Fields
            .Select(field => field.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!usedPaths.Contains(candidate))
            return candidate;

        var suffix = 2;
        while (usedPaths.Contains($"{candidate}{suffix}"))
            suffix++;

        return $"{candidate}{suffix}";
    }
}

public sealed partial class DataGridColumnEditorFieldItem : ObservableObject, IDisposable
{
    public DataGridColumnEditorFieldItem(BindingFieldModel field)
    {
        Field = field;
        Field.PropertyChanged += Field_PropertyChanged;
    }

    public BindingFieldModel Field { get; }

    public string Title => string.IsNullOrWhiteSpace(Field.Header) ? Field.Path : Field.Header;

    public string Subtitle => string.IsNullOrWhiteSpace(Field.Path) ? "Path не задан" : Field.Path;

    public bool IsHidden => !Field.IsVisible;

    public bool IsSorted => !string.Equals(Field.SortDirection, BindingFieldModel.SortDirectionNone, StringComparison.OrdinalIgnoreCase);

    public bool IsGrouped => Field.GroupOrder >= 0;

    public bool HasSummary => BindingFieldModel.NormalizeSummaryType(Field.SummaryType) != BindingFieldModel.SummaryTypeNone;

    public string VisibleIndexText => Field.VisibleIndex >= 0 ? $"#{Field.VisibleIndex + 1}" : string.Empty;

    public void Dispose()
    {
        Field.PropertyChanged -= Field_PropertyChanged;
    }

    private void Field_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsHidden));
        OnPropertyChanged(nameof(IsSorted));
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(VisibleIndexText));
    }
}
