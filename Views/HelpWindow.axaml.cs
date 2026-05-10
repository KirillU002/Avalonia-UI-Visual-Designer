using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace FormDesigner.Views;

public partial class HelpWindow : Window
{
    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromSeconds(3.4) };
    private readonly (string Title, string Description)[] _demoSteps =
    {
        ("Шаг 1. Перетащите компоненты", "Начните с toolbox слева. После drop на рабочую поверхность элемент становится частью документа и сразу доступен для настройки."),
        ("Шаг 2. Настройте свойства", "После выбора элемента меняйте текст, размеры, цвета, типографику, границы и дополнительные descriptor-driven свойства в правой панели."),
        ("Шаг 3. Подключите данные", "Создайте BindingSource вручную, импортируйте его из DLL или подтяните из SQL Server, затем привяжите DataGrid или TreeList и при необходимости примените мастер привязок."),
        ("Шаг 4. Проверьте результат", "Используйте F5 для полноценного preview запуска в отдельном окне.")
    };

    private Border[] _demoFrames = Array.Empty<Border>();
    private Border[] _demoDots = Array.Empty<Border>();
    private int _currentDemoIndex;

    private static readonly IBrush ActiveDotBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush InactiveDotBrush = new SolidColorBrush(Color.Parse("#D7E3EF"));

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
        _demoFrames = new[] { DemoFrame0, DemoFrame1, DemoFrame2, DemoFrame3 };
        _demoDots = new[] { DemoDot0, DemoDot1, DemoDot2, DemoDot3 };
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

    private void RestartDemoTimer()
    {
        _demoTimer.Stop();
        _demoTimer.Start();
    }

    private void ShowDemoStep(int index)
    {
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
