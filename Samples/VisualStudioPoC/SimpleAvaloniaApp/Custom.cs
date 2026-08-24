using Avalonia;
using Avalonia.Controls;

namespace SimpleAvaloniaApp;

public sealed class Custom : AvaloniaObject
{
    public static readonly AttachedProperty<string?> UnknownProperty =
        AvaloniaProperty.RegisterAttached<Custom, Control, string?>("Unknown");

    public static string? GetUnknown(Control control) => control.GetValue(UnknownProperty);

    public static void SetUnknown(Control control, string? value) => control.SetValue(UnknownProperty, value);
}
