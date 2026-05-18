using Avalonia.Controls;
using Avalonia.Interactivity;
using FormDesigner.Models;
using System.Collections.Generic;

namespace FormDesigner.Views;

public partial class BackupRestoreWindow : Window
{
    public BackupRestoreWindow()
    {
        InitializeComponent();
    }

    public BackupRestoreWindow(string documentPath, IReadOnlyList<BackupFileModel> backups)
        : this()
    {
        DocumentPathTextBlock.Text = documentPath;
        BackupsListBox.ItemsSource = backups;
        BackupsListBox.SelectedIndex = backups.Count > 0 ? 0 : -1;
    }

    private void RestoreButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(BackupsListBox.SelectedItem as BackupFileModel);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
