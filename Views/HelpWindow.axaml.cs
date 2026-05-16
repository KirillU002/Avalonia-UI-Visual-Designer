using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace FormDesigner.Views;

public partial class HelpWindow : Window
{
    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private readonly (string Title, string Description)[] _demoSteps =
    {
        ("Designer canvas", "Toolbox, canvas и properties работают как единый поток: добавьте control, выделите его и настройте внешний вид без перехода в код."),
        ("DataGrid + BindingSource", "BindingSource задает реальные поля, а DataGrid использует их как колонки для preview, export и interactions."),
        ("Interaction Designer", "Свяжите событие с действием: selection заполняет поля, кнопки показывают сообщение, очищают форму или скрывают панель."),
        ("Export XAML/C#", "Code/Export mode показывает XAML, C# и checklist: target, layout mode, DataGrid mode, NuGet и exported interactions."),
        ("Plugin system", "Plugin DLL регистрирует descriptor, preview provider и export provider, после чего control появляется в toolbox."),
        ("Preview mode", "Preview запускает форму как пользовательский сценарий: клики, selection, show/hide и diagnostics проверяются до переноса в проект.")
    };

    private Border[] _demoFrames = Array.Empty<Border>();
    private Border[] _demoDots = Array.Empty<Border>();
    private int _currentDemoIndex;

    private static readonly IBrush ActiveDotBrush = new SolidColorBrush(Color.Parse("#93C5FD"));
    private static readonly IBrush InactiveDotBrush = new SolidColorBrush(Color.Parse("#64748B"));

    public HelpWindow()
    {
        InitializeComponent();
        BuildDemoState();

        Opened += HelpWindow_Opened;
        Closed += HelpWindow_Closed;
        KeyDown += HelpWindow_KeyDown;
        _demoTimer.Tick += DemoTimer_Tick;
    }

    private void BuildDemoState()
    {
        _demoFrames = new[] { DemoFrame0, DemoFrame1, DemoFrame2, DemoFrame3, DemoFrame4, DemoFrame5 };
        _demoDots = new[] { DemoDot0, DemoDot1, DemoDot2, DemoDot3, DemoDot4, DemoDot5 };
        ShowDemoStep(0);
    }

    private void HelpWindow_Opened(object? sender, EventArgs e)
    {
        _demoTimer.Start();
    }

    private void HelpWindow_Closed(object? sender, EventArgs e)
    {
        _demoTimer.Stop();
    }

    private void HelpWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void DemoTimer_Tick(object? sender, EventArgs e)
    {
        ShowDemoStep((_currentDemoIndex + 1) % _demoSteps.Length);
    }

    private void DemoPreviousButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowDemoStep((_currentDemoIndex - 1 + _demoSteps.Length) % _demoSteps.Length);
        RestartDemoTimer();
    }

    private void DemoNextButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowDemoStep((_currentDemoIndex + 1) % _demoSteps.Length);
        RestartDemoTimer();
    }

    private void DemoDot_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border dot)
            return;

        var index = Array.IndexOf(_demoDots, dot);
        if (index < 0)
            return;

        ShowDemoStep(index);
        RestartDemoTimer();
    }

    private void RestartDemoTimer()
    {
        _demoTimer.Stop();
        _demoTimer.Start();
    }

    private void ShowDemoStep(int index)
    {
        if (index < 0 || index >= _demoSteps.Length)
            return;

        _currentDemoIndex = index;

        for (var i = 0; i < _demoFrames.Length; i++)
        {
            _demoFrames[i].IsVisible = i == index;
            _demoDots[i].Background = i == index ? ActiveDotBrush : InactiveDotBrush;
        }

        DemoTitleTextBlock.Text = _demoSteps[index].Title;
        DemoDescriptionTextBlock.Text = _demoSteps[index].Description;
    }
}
