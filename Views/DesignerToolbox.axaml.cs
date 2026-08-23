using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace FormDesigner.Views;

/// <summary>Переиспользуемый Toolbox Designer без знания о concrete host.</summary>
public partial class DesignerToolbox : UserControl
{
    public static readonly StyledProperty<object?> ContextProperty =
        AvaloniaProperty.Register<DesignerToolbox, object?>(nameof(Context));

    public DesignerToolbox()
    {
        InitializeComponent();
    }

    public object? Context
    {
        get => GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public event EventHandler<PointerPressedEventArgs>? ToolboxItemPointerPressed;

    private void ToolboxItem_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        ToolboxItemPointerPressed?.Invoke(sender, e);
}
