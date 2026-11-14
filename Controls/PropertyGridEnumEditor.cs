using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FormDesigner.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace FormDesigner.Controls;

// Native selection reconciliation is not a user edit. Avoid a recycled ComboBox's
// old SelectedItem writing through a new immutable Row context during ItemsSource reset.
public sealed class PropertyGridEnumEditor : ComboBox
{
    public static readonly StyledProperty<PropertyGridRowViewModel?> RowProperty =
        AvaloniaProperty.Register<PropertyGridEnumEditor, PropertyGridRowViewModel?>(nameof(Row));
    public PropertyGridRowViewModel? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    protected override Type StyleKeyOverride => typeof(ComboBox);
    private PropertyGridRowViewModel? _attached, _inputRow;
    private bool _synchronizing;
    public PropertyGridEnumEditor()
    {
        SelectionChanged += (_, _) =>
        {
            if (_synchronizing || _inputRow is null || !ReferenceEquals(_inputRow, Row)) return;
            var row = _inputRow; _inputRow = null;
            if (SelectedItem is PropertyGridOptionViewModel option && row.Options.Contains(option))
                Owner()?.CommitPropertyGridTextEdit(row, option.Value);
        };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == RowProperty) BindRow(); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { _inputRow = Row; base.OnPointerPressed(e); }
    protected override void OnKeyDown(KeyEventArgs e) { _inputRow = Row; base.OnKeyDown(e); if (!IsDropDownOpen) _inputRow = null; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_attached is not null) _attached.PropertyChanged -= RowChanged;
        _attached = null; _inputRow = null; ItemsSource = null; base.OnDetachedFromVisualTree(e);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); BindRow(); }
    private void BindRow()
    {
        if (_attached is not null) _attached.PropertyChanged -= RowChanged;
        _attached = Row; _inputRow = null;
        if (_attached is not null) _attached.PropertyChanged += RowChanged;
        Synchronize();
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(PropertyGridRowViewModel.Value) or nameof(PropertyGridRowViewModel.SelectedOption)) Synchronize(); }
    private void Synchronize()
    {
        _synchronizing = true;
        try { ItemsSource = Row?.Options; SelectedItem = Row?.Options.FirstOrDefault(o => o.Value == Row.Value); }
        finally { _synchronizing = false; }
    }
    public void CommitSelection(string value)
    {
        if (Row is { } row && row.Options.Any(o => o.Value == value)) Owner()?.CommitPropertyGridTextEdit(row, value);
    }
    private MainWindowViewModel? Owner() => (this.GetVisualRoot() as Window)?.DataContext as MainWindowViewModel;
}
