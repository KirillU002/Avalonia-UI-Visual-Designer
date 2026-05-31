using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MinimalDesignerPlugin.Controls;

public sealed class HelloCard : Border
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<HelloCard, string>(nameof(Title), "Hello plugin");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<HelloCard, string>(nameof(Message), "This control comes from an external plugin DLL.");

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<HelloCard, IBrush?>(nameof(AccentBrush), Brushes.DodgerBlue);

    private readonly Border _accentBar;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _messageBlock;

    public HelloCard()
    {
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        BorderBrush = Brush.Parse("#D7E2EE");
        Background = Brushes.White;
        Padding = new Thickness(14);

        _accentBar = new Border
        {
            Width = 5,
            CornerRadius = new CornerRadius(999),
            Background = AccentBrush ?? Brushes.DodgerBlue
        };

        _titleBlock = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#0F172A"),
            TextWrapping = TextWrapping.Wrap
        };

        _messageBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush.Parse("#64748B"),
            TextWrapping = TextWrapping.Wrap
        };

        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
            Children =
            {
                _titleBlock,
                _messageBlock
            }
        };
        Grid.SetColumn(content, 1);

        Child = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                _accentBar,
                content
            }
        };

        UpdateVisualState();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty
            || change.Property == MessageProperty
            || change.Property == AccentBrushProperty)
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        _titleBlock.Text = Title;
        _messageBlock.Text = Message;
        _accentBar.Background = AccentBrush ?? Brushes.DodgerBlue;
    }
}

