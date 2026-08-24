using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FormDesigner.DesignerSystem.Hosting;
using FormDesigner.Views;
using System;

namespace AvaloniaDesigner.VsHost;

/// <summary>
/// Hosts the existing standalone Designer window in the external process. This is intentional:
/// the current DesignerSurface still delegates its long-lived interaction controller to MainWindow,
/// so using the original host avoids introducing a second Canvas or a second interaction engine.
/// </summary>
public sealed class VsHostWindow : MainWindow
{
    private readonly TextBlock _statusText;

    public VsHostWindow(IDesignerHostServices hostServices)
        : base(hostServices)
    {
        var applyButton = new Button
        {
            Content = "Применить изменения",
            Classes = { "editor-primary-button" }
        };
        applyButton.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);

        var reloadButton = new Button
        {
            Content = "Перезагрузить из Visual Studio",
            Classes = { "toolbar-button" }
        };
        reloadButton.Click += (_, _) => ReloadRequested?.Invoke(this, EventArgs.Empty);

        _statusText = new TextBlock
        {
            Text = "Подключение к Visual Studio...",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            MaxWidth = 420,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var bar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0F3B66")),
            BorderBrush = new SolidColorBrush(Color.Parse("#245987")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 46, 16, 0),
            ZIndex = 10_000,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { _statusText, applyButton, reloadButton }
            }
        };

        Opened += (_, _) => AttachBridgeChrome(bar);
    }

    public event EventHandler? ApplyRequested;
    public event EventHandler? ReloadRequested;

    public void SetBridgeStatus(string text) => _statusText.Text = text;

    public void CloseForBridgeShutdown() => CloseForExternalHost();

    private void AttachBridgeChrome(Control chrome)
    {
        if (chrome.Parent is not null)
            return;

        if (Content is Panel panel)
        {
            panel.Children.Add(chrome);
            return;
        }

        // MainWindow currently exposes a Grid root. Retaining this fallback makes the bridge
        // fail visibly rather than silently re-parenting the standalone visual tree.
        throw new InvalidOperationException("VsHost cannot attach bridge chrome to the MainWindow root.");
    }
}
