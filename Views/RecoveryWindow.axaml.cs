using Avalonia.Controls;
using Avalonia.Interactivity;
using FormDesigner.Models;
namespace FormDesigner.Views;

public enum RecoveryDialogResult
{
    None,
    RestoreDraft,
    OpenNormally,
    DeleteDraft
}

public partial class RecoveryWindow : Window
{
    public RecoveryWindow()
    {
        InitializeComponent();
    }

    public RecoveryWindow(RecoveryDraftFileModel draft)
        : this()
    {
        DraftTitleTextBlock.Text = string.IsNullOrWhiteSpace(draft.DocumentDisplayName)
            ? "Безымянный документ"
            : draft.DocumentDisplayName;
        DraftTimestampTextBlock.Text = $"Автосохранение: {draft.LastAutosaveUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
        DraftPathTextBlock.Text = string.IsNullOrWhiteSpace(draft.DocumentPath)
            ? "Источник: безымянная сессия"
            : $"Источник: {draft.DocumentPath}";
    }

    private void RestoreButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(RecoveryDialogResult.RestoreDraft);
    }

    private void IgnoreButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(RecoveryDialogResult.OpenNormally);
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(RecoveryDialogResult.DeleteDraft);
    }
}
