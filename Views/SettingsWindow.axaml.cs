using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using FormDesigner.Localization;
using FormDesigner.ViewModels;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FormDesigner.Views;

public partial class SettingsWindow : Window
{
    private readonly Func<Task> _saveSettingsAsync;

    public SettingsWindow()
    {
        InitializeComponent();
        _saveSettingsAsync = () => Task.CompletedTask;
    }

    public SettingsWindow(MainWindowViewModel ownerViewModel, Func<Task> saveSettingsAsync, string settingsFilePath, string initialSectionId = "general")
    {
        InitializeComponent();
        _saveSettingsAsync = saveSettingsAsync;

        var viewModel = new SettingsWindowViewModel(ownerViewModel, settingsFilePath);
        viewModel.ApplyRequested += SettingsViewModel_ApplyRequested;
        viewModel.SaveRequested += SettingsViewModel_SaveRequested;
        viewModel.CloseRequested += SettingsViewModel_CloseRequested;
        viewModel.PropertyChanged += SettingsViewModel_PropertyChanged;
        viewModel.SelectSection(initialSectionId);
        DataContext = viewModel;

        Opened += SettingsWindow_Opened;
        Closed += SettingsWindow_Closed;
    }

    private void SettingsWindow_Opened(object? sender, EventArgs e)
    {
        var viewModel = DataContext as SettingsWindowViewModel;
        Debug.WriteLine($"SETTINGS_WINDOW_OPENED section={viewModel?.SelectedSection?.Id ?? "-"}; currentLanguage={viewModel?.InterfaceLanguage ?? "-"}; currentTheme={viewModel?.AppThemeMode ?? "-"}; size={Width:0}x{Height:0}");
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.ApplyRequested -= SettingsViewModel_ApplyRequested;
            viewModel.SaveRequested -= SettingsViewModel_SaveRequested;
            viewModel.CloseRequested -= SettingsViewModel_CloseRequested;
            viewModel.PropertyChanged -= SettingsViewModel_PropertyChanged;
        }
    }

    private void SettingsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsWindowViewModel.SelectedSection))
            return;

        Dispatcher.UIThread.Post(ResetContentScrollAfterSectionChange, DispatcherPriority.Background);
    }

    private void ResetContentScrollAfterSectionChange()
    {
        SettingsContentScrollViewer.Offset = new Vector(0, 0);
        SettingsContentScrollViewer.InvalidateMeasure();
        Debug.WriteLine($"SETTINGS_CONTENT_SCROLL_RESET section={(DataContext as SettingsWindowViewModel)?.SelectedSection?.Id ?? "-"}; offset=0");
    }

    private async void SettingsViewModel_ApplyRequested(object? sender, EventArgs e)
    {
        if (sender is SettingsWindowViewModel viewModel)
            ApplyTheme(viewModel.AppThemeMode);

        await _saveSettingsAsync();

        if (sender is SettingsWindowViewModel appliedViewModel)
            appliedViewModel.StatusText = appliedViewModel.Texts.AppliedStatus;
    }

    private async void SettingsViewModel_SaveRequested(object? sender, EventArgs e)
    {
        if (sender is SettingsWindowViewModel viewModel)
            ApplyTheme(viewModel.AppThemeMode);

        await _saveSettingsAsync();
        Close();
    }

    private void SettingsViewModel_CloseRequested(object? sender, EventArgs e)
    {
        Close();
    }

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
            return;

        Application.Current.RequestedThemeVariant = theme switch
        {
            SettingsTextCatalog.ThemeDark => ThemeVariant.Dark,
            SettingsTextCatalog.ThemeSystem => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };
    }
}
