using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;

namespace FormDesigner.Views;

public partial class MainWindow : Window
{
    private Border? _draggedBorder;
    private DesignControlModel? _draggedModel;
    private Point _dragOffset;

    private bool _isResizing;
    private DesignControlModel? _resizingModel;
    private Point _resizeStart;
    private double _startWidth;
    private double _startHeight;

    private bool _isDragging;

    private bool _isResizingDesignSurface;
    private Point _designResizeStart;
    private double _designStartWidth;
    private double _designStartHeight;

    private DesignControlModel? _trackedSelectedControl;

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainWindowViewModel();
        DataContext = vm;

        vm.Controls.CollectionChanged += (_, _) => RenderDesigner();
        vm.PropertyChanged += VM_PropertyChanged;

        KeyDown += MainWindow_KeyDown;
    }

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private void VM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedControl))
        {
            TrackSelectedControl();
            RenderDesigner();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.DesignWidth) ||
            e.PropertyName == nameof(MainWindowViewModel.DesignHeight))
        {
            RenderDesigner();
        }
    }

    private void TrackSelectedControl()
    {
        if (_trackedSelectedControl is not null)
            _trackedSelectedControl.PropertyChanged -= SelectedControl_PropertyChanged;

        _trackedSelectedControl = VM.SelectedControl;

        if (_trackedSelectedControl is not null)
            _trackedSelectedControl.PropertyChanged += SelectedControl_PropertyChanged;
    }

    private void SelectedControl_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RenderDesigner();
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            VM.DeleteSelectedCommand.Execute(null);
            RenderDesigner();
            e.Handled = true;
        }
    }

    private async void ToolboxItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not ToolboxItem item)
            return;

        var data = new DataObject();
        data.Set("control-type", item.Type);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private void DesignerCanvas_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("control-type"))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
    }

    private void DesignerCanvas_Drop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("control-type"))
            return;

        var type = e.Data.Get("control-type")?.ToString();
        if (string.IsNullOrWhiteSpace(type))
            return;

        var position = e.GetPosition(DesignerCanvas);
        var model = VM.CreateControl(type, VM.Snap(position.X), VM.Snap(position.Y));

        RenderDesigner();
        VM.SelectedControl = model;
    }

    private void RenderDesigner()
    {
        _isApplyingTextChanges = true;

        try
        {
            DesignerCanvas.Children.Clear();

            foreach (var model in VM.Controls)
            {
                var wrapper = CreateDesignerWrapper(model);

                Canvas.SetLeft(wrapper, model.X);
                Canvas.SetTop(wrapper, model.Y);

                DesignerCanvas.Children.Add(wrapper);
            }

            VM.GenerateXamlCommand.Execute(null);
        }
        finally
        {
            _isApplyingTextChanges = false;
        }
    }

    private Border CreateDesignerWrapper(DesignControlModel model)
    {
        var content = CreateAvaloniaControl(model);

        var root = new Canvas
        {
            Width = model.Width,
            Height = model.Height
        };

        root.Children.Add(content);
        Canvas.SetLeft(content, 0);
        Canvas.SetTop(content, 0);

        var resizeHitArea = new Border
        {
            Width = 18,
            Height = 18,
            Background = Brushes.Transparent,
            Tag = model,
            IsVisible = VM.SelectedControl == model,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
        };

        var resizeVisual = new Border
        {
            Width = 10,
            Height = 10,
            Background = Brushes.DodgerBlue,
            IsHitTestVisible = false,
            IsVisible = VM.SelectedControl == model
        };

        Canvas.SetLeft(resizeHitArea, model.Width - 9);
        Canvas.SetTop(resizeHitArea, model.Height - 9);

        Canvas.SetLeft(resizeVisual, model.Width - 10);
        Canvas.SetTop(resizeVisual, model.Height - 10);

        resizeHitArea.PointerPressed += ResizeHandle_PointerPressed;
        resizeHitArea.PointerMoved += ResizeHandle_PointerMoved;
        resizeHitArea.PointerReleased += ResizeHandle_PointerReleased;

        root.Children.Add(resizeHitArea);
        root.Children.Add(resizeVisual);

        var wrapper = new Border
        {
            Child = root,
            Width = model.Width,
            Height = model.Height,
            BorderThickness = VM.SelectedControl == model ? new Thickness(2) : new Thickness(1),
            BorderBrush = VM.SelectedControl == model ? Brushes.DodgerBlue : Brushes.Gray,
            Background = Brushes.Transparent,
            Tag = model
        };

        wrapper.PointerPressed += Control_PointerPressed;
        wrapper.PointerMoved += Control_PointerMoved;
        wrapper.PointerReleased += Control_PointerReleased;

        return wrapper;
    }

    private void ResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingModel is not null)
        {
            _resizingModel.Width = VM.Snap(_resizingModel.Width);
            _resizingModel.Height = VM.Snap(_resizingModel.Height);
        }

        _isResizing = false;
        _resizingModel = null;
        e.Pointer.Capture(null);

        RenderDesigner();
    }

    private void ResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not DesignControlModel model)
            return;

        _isResizing = true;
        _resizingModel = model;
        _resizeStart = e.GetPosition(DesignerCanvas);
        _startWidth = model.Width;
        _startHeight = model.Height;

        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void ResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing || _resizingModel is null)
            return;

        var pos = e.GetPosition(DesignerCanvas);
        var dx = pos.X - _resizeStart.X;
        var dy = pos.Y - _resizeStart.Y;

        _resizingModel.Width = Math.Max(40, _startWidth + dx);
        _resizingModel.Height = Math.Max(24, _startHeight + dy);

        RenderDesigner();
    }

    private void DesignResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        VM.DesignWidth = VM.Snap(VM.DesignWidth);
        VM.DesignHeight = VM.Snap(VM.DesignHeight);

        _isResizingDesignSurface = false;
        e.Pointer.Capture(null);

        RenderDesigner();
    }

    private Control CreateAvaloniaControl(DesignControlModel model)
    {
        return model.Type switch
        {
            "Button" => new Button
            {
                Content = model.Text,
                Width = model.Width,
                Height = model.Height,
                IsHitTestVisible = false
            },

            "TextBox" => new TextBox
            {
                Text = model.Text,
                Width = model.Width,
                Height = model.Height,
                IsHitTestVisible = false
            },

            "TextBlock" => new TextBlock
            {
                Text = model.Text,
                Width = model.Width,
                Height = model.Height,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false
            },

            "CheckBox" => new CheckBox
            {
                Content = model.Text,
                Width = model.Width,
                Height = model.Height,
                IsHitTestVisible = false
            },

            "Grid" => CreateGridPreview(model),

            _ => new TextBlock
            {
                Text = "Unknown",
                IsHitTestVisible = false
            }
        };
    }

    private Control CreateGridPreview(DesignControlModel model)
    {
        var grid = new Grid
        {
            Width = model.Width,
            Height = model.Height,
            Background = Brushes.White,
            IsHitTestVisible = false
        };

        for (int i = 0; i < Math.Max(1, model.Columns); i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        for (int i = 0; i < Math.Max(1, model.Rows); i++)
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));

        for (int row = 0; row < Math.Max(1, model.Rows); row++)
        {
            for (int col = 0; col < Math.Max(1, model.Columns); col++)
            {
                var cell = new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = model.ShowGridLines ? new Thickness(1) : new Thickness(0),
                    Background = Brushes.White
                };

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            Width = model.Width,
            Height = model.Height,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = grid,
            IsHitTestVisible = false
        };
    }

    private void Control_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not DesignControlModel model)
            return;

        VM.SelectedControl = model;
        _draggedBorder = border;
        _draggedModel = model;

        _dragOffset = e.GetPosition(border);
        _isDragging = true;

        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void Control_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _draggedBorder is null || _draggedModel is null)
            return;

        var pos = e.GetPosition(DesignerCanvas);

        _draggedModel.X = Math.Max(0, VM.Snap(pos.X - _dragOffset.X));
        _draggedModel.Y = Math.Max(0, VM.Snap(pos.Y - _dragOffset.Y));

        Canvas.SetLeft(_draggedBorder, _draggedModel.X);
        Canvas.SetTop(_draggedBorder, _draggedModel.Y);

        VM.GenerateXaml();
    }

    private void Control_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;

        if (_draggedBorder is not null)
            e.Pointer.Capture(null);

        _draggedBorder = null;
        _draggedModel = null;
    }

    private void DesignResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isResizingDesignSurface = true;
        _designResizeStart = e.GetPosition(DesignSurfaceHost);
        _designStartWidth = VM.DesignWidth;
        _designStartHeight = VM.DesignHeight;

        if (sender is InputElement element)
            e.Pointer.Capture(element);

        e.Handled = true;
    }

    private void DesignResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingDesignSurface)
            return;

        var current = e.GetPosition(DesignSurfaceHost);

        var dx = current.X - _designResizeStart.X;
        var dy = current.Y - _designResizeStart.Y;

        VM.DesignWidth = Math.Max(300, _designStartWidth + dx);
        VM.DesignHeight = Math.Max(200, _designStartHeight + dy);

        VM.GenerateXaml();
    }

    private bool _isApplyingTextChanges;

    private void RefreshFromPropertyPanel()
    {
        if (_isApplyingTextChanges)
            return;

        RenderDesigner();
        VM.GenerateXaml();
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        return double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out value)
            || double.TryParse(
                text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.CurrentCulture,
                out value);
    }

    private static bool TryParseInt(string? text, out int value)
    {
        return int.TryParse(
            text,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out value)
            || int.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.CurrentCulture,
                out value);
    }

    private void SelectedNameTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        VM.SelectedControl.Name = tb.Text ?? "";
        RefreshFromPropertyPanel();
    }

    private void SelectedTextTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        VM.SelectedControl.Text = tb.Text ?? "";
        RefreshFromPropertyPanel();
    }

    private void SelectedWidthTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        if (!TryParseDouble(tb.Text, out var value))
            return;

        VM.SelectedControl.Width = Math.Max(40, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedHeightTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        if (!TryParseDouble(tb.Text, out var value))
            return;

        VM.SelectedControl.Height = Math.Max(24, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedXTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        if (!TryParseDouble(tb.Text, out var value))
            return;

        VM.SelectedControl.X = Math.Max(0, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedYTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        if (!TryParseDouble(tb.Text, out var value))
            return;

        VM.SelectedControl.Y = Math.Max(0, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedColumnsTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        if (!TryParseInt(tb.Text, out var value))
            return;

        VM.SelectedControl.Columns = Math.Max(1, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedRowsTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || VM.SelectedControl is null || sender is not TextBox tb)
            return;

        if (!TryParseInt(tb.Text, out var value))
            return;

        VM.SelectedControl.Rows = Math.Max(1, value);
        RefreshFromPropertyPanel();
    }

    private void SelectedShowGridLinesCheckBox_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (VM.SelectedControl is null || sender is not CheckBox cb)
            return;

        VM.SelectedControl.ShowGridLines = cb.IsChecked ?? false;
        RefreshFromPropertyPanel();
    }

    private void DesignWidthTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || sender is not TextBox tb)
            return;

        if (!TryParseDouble(tb.Text, out var value))
            return;

        VM.DesignWidth = Math.Max(300, value);
        RefreshFromPropertyPanel();
    }

    private void DesignHeightTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingTextChanges || sender is not TextBox tb)
            return;

        if (!TryParseDouble(tb.Text, out var value))
            return;

        VM.DesignHeight = Math.Max(200, value);
        RefreshFromPropertyPanel();
    }
}