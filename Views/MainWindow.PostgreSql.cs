using Avalonia.Interactivity;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private void AddPostgreSqlBindingSourceButton_Click(object? sender, RoutedEventArgs e)
    {
        VM.AddPostgreSqlBindingSource();
    }
    private void OpenPostgreSqlSettingsButton_Click(object? sender, RoutedEventArgs e) => OpenSettingsWindow("postgres");
}
