using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.ViewModels;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace FormDesigner.Controls;

// One reviewed struct literal, one existing document transaction. Components are lazy.
public sealed class PropertyGridCompoundEditor : StackPanel
{
    public static readonly StyledProperty<PropertyGridRowViewModel?> RowProperty =
        AvaloniaProperty.Register<PropertyGridCompoundEditor, PropertyGridRowViewModel?>(nameof(Row));
    public PropertyGridRowViewModel? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    private readonly PropertyGridTextEditor _summary = new();
    private readonly ToggleButton _expand = new() { Content = "⋯", MinWidth = 22, Padding = new Thickness(2) };
    private readonly StackPanel _details = new() { IsVisible = false };
    private PropertyGridRowViewModel? _attached;
    private TextBox[] _components = Array.Empty<TextBox>();
    private readonly PropertyGridRowViewModel?[] _editing = new PropertyGridRowViewModel?[4];
    private bool _updating;
    public System.Collections.Generic.IReadOnlyList<TextBox> Components => _components;
    public PropertyGridCompoundEditor()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _summary.Classes.Add("property-editor"); _expand.Classes.Add("property-action"); Grid.SetColumn(_expand, 1);
        header.Children.Add(_summary); header.Children.Add(_expand); Children.Add(header); Children.Add(_details);
        _expand.IsCheckedChanged += (_, _) => { _details.IsVisible = _expand.IsChecked == true; if (_details.IsVisible) { EnsureComponents(); UpdateComponents(); } };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != RowProperty) return;
        if (_attached is not null) _attached.PropertyChanged -= RowChanged;
        Array.Clear(_editing); _attached = Row; _summary.Row = Row;
        if (_attached is not null) _attached.PropertyChanged += RowChanged;
        UpdateComponents();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_attached is not null) _attached.PropertyChanged -= RowChanged;
        _attached = null; Array.Clear(_editing); base.OnDetachedFromVisualTree(e);
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_attached is null && Row is { } row) { _attached = row; row.PropertyChanged += RowChanged; }
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PropertyGridRowViewModel.Value)) UpdateComponents(); }
    public void ExpandComponents() { _expand.IsChecked = true; }
    private void EnsureComponents()
    {
        if (_components.Length != 0) return;
        var labels = Row?.Editor == PropertyGridEditorKind.CornerRadius
            ? new[] { "TopLeft", "TopRight", "BottomRight", "BottomLeft" } : new[] { "Left", "Top", "Right", "Bottom" };
        _components = Enumerable.Range(0, 4).Select(i =>
        {
            var field = new TextBox { MinWidth = 45, FontSize = 12, Padding = new Thickness(4, 1), Name = "Component" + i };
            field.Classes.Add("property-editor"); var line = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*") };
            line.Children.Add(new TextBlock { Text = labels[i], FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(field, 1); line.Children.Add(field); _details.Children.Add(line);
            field.GotFocus += (_, _) => { _editing[i] = Row; if (Row is { } row) Owner()?.BeginPropertyGridTextEdit(row, row.Value); };
            void BeginInput()
            {
                if (!_updating && _editing[i] is null && Row is { } row)
                { _editing[i] = row; Owner()?.BeginPropertyGridTextEdit(row, row.Value); }
            }
            field.AddHandler(InputElement.TextInputEvent, (_, _) => BeginInput(), RoutingStrategies.Tunnel);
            field.AddHandler(InputElement.KeyDownEvent, (_, args) => { if (args.Key is Key.Back or Key.Delete) BeginInput(); }, RoutingStrategies.Tunnel);
            field.LostFocus += (_, _) => CommitComponent(i);
            field.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) { CommitComponent(i); args.Handled = true; }
                if (args.Key == Key.Escape) { if (_editing[i] is { } row) Owner()?.CancelPropertyGridTextEdit(row); _editing[i] = null; UpdateComponents(); args.Handled = true; }
            };
            return field;
        }).ToArray();
    }
    private void UpdateComponents()
    {
        if (_updating || _components.Length == 0 || Row is not { } row) return;
        var type = row.Editor == PropertyGridEditorKind.CornerRadius ? typeof(CornerRadius) : typeof(Thickness);
        if (!MetadataValueCodec.TryParse(type, row.Value, out var value)) return;
        var numbers = value is CornerRadius c ? new[] { c.TopLeft, c.TopRight, c.BottomRight, c.BottomLeft }
            : value is Thickness t ? new[] { t.Left, t.Top, t.Right, t.Bottom } : Array.Empty<double>();
        _updating = true;
        try { for (var i = 0; i < numbers.Length; i++) if (!_components[i].IsFocused || _editing[i] is null) _components[i].Text = numbers[i].ToString("R", CultureInfo.InvariantCulture); }
        finally { _updating = false; }
    }
    public void CommitComponent(int index)
    {
        var token = _editing[index]; _editing[index] = null;
        if (_updating || token is null || !ReferenceEquals(token, Row)) return;
        var texts = _components.Select(c => c.Text ?? "").ToArray();
        if (texts.Any(t => !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)))
        { token.ValidationMessage = "Компоненты должны быть конечными числами DIP."; Owner()?.EndPropertyGridTextEdit(token); return; }
        Owner()?.CommitPropertyGridTextEdit(token, string.Join(",", texts));
    }
    private MainWindowViewModel? Owner() => (this.GetVisualRoot() as Window)?.DataContext as MainWindowViewModel;
}
