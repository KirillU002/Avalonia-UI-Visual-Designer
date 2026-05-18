using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FormDesigner.Views;

public enum UnsavedChangesDialogResult
{
    Cancel,
    Save,
    Discard
}

public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow()
    {
        InitializeComponent();
    }

    public UnsavedChangesWindow(string documentName)
        : this()
    {
        DocumentNameTextBlock.Text = string.IsNullOrWhiteSpace(documentName)
            ? "Текущий документ изменён."
            : $"Документ: {documentName}";
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesDialogResult.Save);
    }

    private void DiscardButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesDialogResult.Discard);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(UnsavedChangesDialogResult.Cancel);
    }
}
