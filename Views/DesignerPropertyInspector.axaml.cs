using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace FormDesigner.Views;

/// <summary>Общая таблица Property Inspector для standalone и будущих host.</summary>
public partial class DesignerPropertyInspector : UserControl
{
    public static readonly StyledProperty<object?> ContextProperty =
        AvaloniaProperty.Register<DesignerPropertyInspector, object?>(nameof(Context));

    public DesignerPropertyInspector()
    {
        InitializeComponent();
    }

    public object? Context
    {
        get => GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public ItemsControl CategoriesItemsControl => PropertyGridCategoriesItemsControl;

    public event EventHandler<RoutedEventArgs>? ColorRequested;
    public event EventHandler<RoutedEventArgs>? ResetRequested;
    public event EventHandler<RoutedEventArgs>? ActionRequested;

    private void PropertyGridColorButton_Click(object? sender, RoutedEventArgs e) => ColorRequested?.Invoke(sender, e);
    private void PropertyGridResetButton_Click(object? sender, RoutedEventArgs e) => ResetRequested?.Invoke(sender, e);
    private void PropertyGridActionButton_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(sender, e);
}
