using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Windows.Input;

namespace FormDesigner.Views;

/// <summary>Общая таблица Property Inspector для standalone и будущих host.</summary>
public partial class DesignerPropertyInspector : UserControl
{
    public static readonly StyledProperty<object?> ContextProperty =
        AvaloniaProperty.Register<DesignerPropertyInspector, object?>(nameof(Context));

    public static readonly StyledProperty<ICommand?> ToggleFavoriteCommandProperty =
        AvaloniaProperty.Register<DesignerPropertyInspector, ICommand?>(nameof(ToggleFavoriteCommand));

    public DesignerPropertyInspector()
    {
        InitializeComponent();
    }

    public object? Context
    {
        get => GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    /// <summary>Команда host для изменения сохраненного состояния избранных свойств.</summary>
    public ICommand? ToggleFavoriteCommand
    {
        get => GetValue(ToggleFavoriteCommandProperty);
        set => SetValue(ToggleFavoriteCommandProperty, value);
    }

    public ItemsControl CategoriesItemsControl => PropertyGridCategoriesItemsControl;

    public event EventHandler<RoutedEventArgs>? ColorRequested;
    public event EventHandler<RoutedEventArgs>? ResetRequested;
    public event EventHandler<RoutedEventArgs>? ActionRequested;
    public event EventHandler<RoutedEventArgs>? FavoriteRequested;

    private void PropertyGridFavoriteButton_Click(object? sender, RoutedEventArgs e) => FavoriteRequested?.Invoke(sender, e);
    private void PropertyGridColorButton_Click(object? sender, RoutedEventArgs e) => ColorRequested?.Invoke(sender, e);
    private void PropertyGridResetButton_Click(object? sender, RoutedEventArgs e) => ResetRequested?.Invoke(sender, e);
    private void PropertyGridActionButton_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(sender, e);
}
