using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FormDesigner.Views;

public enum RecentFileUnavailableDialogResult
{
    Keep,
    Remove
}

public partial class RecentFileUnavailableWindow : Window
{
    public RecentFileUnavailableWindow()
    {
        InitializeComponent();
    }

    public RecentFileUnavailableWindow(string filePath)
        : this()
    {
        PathTextBlock.Text = string.IsNullOrWhiteSpace(filePath)
            ? "Путь не задан."
            : filePath;
    }

    private void KeepButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(RecentFileUnavailableDialogResult.Keep);
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(RecentFileUnavailableDialogResult.Remove);
    }
}
