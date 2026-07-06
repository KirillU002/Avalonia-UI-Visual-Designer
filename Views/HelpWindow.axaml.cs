using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FormDesigner.ViewModels;
using System;
using System.Diagnostics;
using System.Linq;

namespace FormDesigner.Views;

public partial class HelpWindow : Window
{
    public const double DefaultWidthRatio = 0.96;
    public const double DefaultHeightRatio = 0.94;
    public const double DefaultMinWidth = 1100;
    public const double DefaultMinHeight = 720;
    public static WindowState PreferredWindowState => WindowState.Maximized;

    private readonly DispatcherTimer _carouselTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    public HelpWindow()
    {
        InitializeComponent();
        DataContext = new HelpWindowViewModel();

        Opened += HelpWindow_Opened;
        Closed += HelpWindow_Closed;
        KeyDown += HelpWindow_KeyDown;
        PointerEntered += HelpWindow_PointerEntered;
        PointerExited += HelpWindow_PointerExited;
        _carouselTimer.Tick += CarouselTimer_Tick;
    }

    public static Size CalculateAdaptiveSize(double workingWidth, double workingHeight)
    {
        if (workingWidth <= 0 || workingHeight <= 0)
            return new Size(DefaultMinWidth, DefaultMinHeight);

        var width = workingWidth * DefaultWidthRatio;
        var height = workingHeight * DefaultHeightRatio;

        if (workingWidth >= DefaultMinWidth)
            width = Math.Max(DefaultMinWidth, width);
        else
            width = Math.Max(720, workingWidth * 0.96);

        if (workingHeight >= DefaultMinHeight)
            height = Math.Max(DefaultMinHeight, height);
        else
            height = Math.Max(520, workingHeight * 0.94);

        width = Math.Min(width, workingWidth);
        height = Math.Min(height, workingHeight);
        return new Size(Math.Round(width), Math.Round(height));
    }

    private void HelpWindow_Opened(object? sender, EventArgs e)
    {
        ApplyAdaptiveSize();
        _carouselTimer.Start();
    }

    private void HelpWindow_Closed(object? sender, EventArgs e)
    {
        _carouselTimer.Stop();
        _carouselTimer.Tick -= CarouselTimer_Tick;
        Debug.WriteLine("HELP_WINDOW_CLOSED");
    }

    private void HelpWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void HelpWindow_PointerEntered(object? sender, PointerEventArgs e)
    {
        _carouselTimer.Stop();
    }

    private void HelpWindow_PointerExited(object? sender, PointerEventArgs e)
    {
        _carouselTimer.Start();
    }

    private void CarouselTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is HelpWindowViewModel viewModel)
            viewModel.NextSlideCommand.Execute(null);
    }

    private void HelpSearchTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && DataContext is HelpWindowViewModel viewModel)
            viewModel.SearchQuery = textBox.Text ?? "";
    }

    private void ApplyAdaptiveSize()
    {
        var screen = Screens?.ScreenFromVisual(this)
            ?? Screens?.ScreenFromWindow(this)
            ?? Screens?.All.FirstOrDefault(item => item.IsPrimary)
            ?? Screens?.All.FirstOrDefault();

        if (screen is null)
            return;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var workingWidth = screen.WorkingArea.Width / scaling;
        var workingHeight = screen.WorkingArea.Height / scaling;
        var size = CalculateAdaptiveSize(workingWidth, workingHeight);

        Width = size.Width;
        Height = size.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = PreferredWindowState;

        Debug.WriteLine($"HELP_WINDOW_OPENED windowState={WindowState}; workingArea={workingWidth:0}x{workingHeight:0}; fallbackSize={Width:0}x{Height:0}; version=Alpha 3.0");
        Debug.WriteLine($"HELP_WINDOW_MAXIMIZED success={WindowState == WindowState.Maximized}; windowState={WindowState}");
    }
}
