using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using FormDesigner.ViewModels;

namespace FormDesigner.Controls;

public sealed class PropertyGridTextEditor : TextBox
{
    public static readonly StyledProperty<PropertyGridRowViewModel?> RowProperty =
        AvaloniaProperty.Register<PropertyGridTextEditor, PropertyGridRowViewModel?>(nameof(Row));

    protected override Type StyleKeyOverride => typeof(TextBox);

    private PropertyGridRowViewModel? _attachedRow;
    private bool _isEditing;
    private bool _isApplyingText;
    private bool _isCancelling;
    private string _initialText = "";

    public PropertyGridTextEditor()
    {
        MinHeight = 22;
        MinWidth = 80;
        Padding = new Thickness(6, 2);
        FontSize = 12;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public PropertyGridRowViewModel? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowProperty)
            AttachCurrentRow();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AttachCurrentRow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachRow();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachCurrentRow();
        SyncTextFromRow();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);

        var row = GetCurrentRow();
        if (row is null)
            return;

        var viewModel = GetViewModel();
        _isEditing = true;
        _isCancelling = false;
        _initialText = Text ?? row.Value;
        viewModel?.BeginPropertyGridTextEdit(row, _initialText);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        if (_isEditing && !_isCancelling)
            CommitEdit();

        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Back or Key.Delete || e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.V or Key.X)
            BeginUserInputAfterCommit();
        if (e.Key == Key.Enter)
        {
            CommitEdit();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelEdit();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
    protected override void OnTextInput(TextInputEventArgs e)
    {
        BeginUserInputAfterCommit();
        base.OnTextInput(e);
    }
    private void BeginUserInputAfterCommit()
    {
        if (_isEditing || _isApplyingText || _isCancelling || GetCurrentRow() is not { IsReadOnly: false } row) return;
        _isEditing = true; _initialText = row.Value;
        GetViewModel()?.BeginPropertyGridTextEdit(row, _initialText);
    }

    private void AttachCurrentRow()
    {
        var row = GetCurrentRow();
        if (ReferenceEquals(row, _attachedRow))
            return;

        // Recycled views must never commit text belonging to the previous immutable context.
        if (_isEditing)
        {
            _isEditing = false;
            if (_attachedRow is { } previous) GetViewModel()?.CancelPropertyGridTextEdit(previous);
        }
        DetachRow();
        _attachedRow = row;
        if (_attachedRow is not null)
            _attachedRow.PropertyChanged += Row_PropertyChanged;

        SyncTextFromRow();
    }

    private void DetachRow()
    {
        if (_attachedRow is not null)
            _attachedRow.PropertyChanged -= Row_PropertyChanged;

        _attachedRow = null;
    }

    private PropertyGridRowViewModel? GetCurrentRow()
    {
        return Row ?? DataContext as PropertyGridRowViewModel;
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PropertyGridRowViewModel.Value))
            SyncTextFromRow();
    }

    private void SyncTextFromRow()
    {
        var row = GetCurrentRow();
        if (row is null || _isEditing || _isApplyingText)
            return;

        _isApplyingText = true;
        try
        {
            Text = row.Value;
        }
        finally
        {
            _isApplyingText = false;
        }
    }

    private void CommitEdit()
    {
        var row = GetCurrentRow();
        if (row is null)
            return;

        var text = Text ?? "";
        var viewModel = GetViewModel();
        if (row.IsStyleDraft) row.Value = text;
        else viewModel?.CommitPropertyGridEdit(row, text);
        _isEditing = false;
        _initialText = row.Value;
        viewModel?.EndPropertyGridTextEdit(row);
    }

    private void CancelEdit()
    {
        var row = GetCurrentRow();
        var viewModel = GetViewModel();
        _isCancelling = true;
        _isEditing = false;

        _isApplyingText = true;
        try
        {
            Text = _initialText;
        }
        finally
        {
            _isApplyingText = false;
        }

        viewModel?.CancelPropertyGridTextEdit(row);
        _isCancelling = false;
    }

    private MainWindowViewModel? GetViewModel()
    {
        return this.GetVisualRoot() is Window { DataContext: MainWindowViewModel viewModel }
            ? viewModel
            : null;
    }
}
