using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FormDesigner.Controls;

// Uses the existing Grid definition item/parser and existing string model adapter.
// No add/remove collection workflow; those operations stay in the Grid Editor.
public sealed class PropertyGridGridLengthEditor : StackPanel
{
    public static readonly StyledProperty<PropertyGridRowViewModel?> RowProperty =
        AvaloniaProperty.Register<PropertyGridGridLengthEditor, PropertyGridRowViewModel?>(nameof(Row));
    public PropertyGridRowViewModel? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    private readonly PropertyGridTextEditor _summary = new();
    private readonly StackPanel _tracks = new() { IsVisible = false };
    private readonly ToggleButton _expand = new() { Content = "⋯", Padding = new Thickness(2), MinWidth = 22 };
    private bool _updating;
    private bool _restoringExpansion;
    public IReadOnlyList<LayoutDefinitionEditorItem> Definitions { get; private set; } = Array.Empty<LayoutDefinitionEditorItem>();
    public PropertyGridGridLengthEditor()
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _summary.Classes.Add("property-editor"); _expand.Classes.Add("property-action"); Grid.SetColumn(_expand, 1);
        line.Children.Add(_summary); line.Children.Add(_expand); Children.Add(line); Children.Add(_tracks);
        _expand.IsCheckedChanged += (_, _) =>
        {
            if (!_restoringExpansion && Row is { } row && (this.GetVisualRoot() as Window)?.DataContext is MainWindowViewModel owner)
                owner.SetPropertyGridExpanded(row, _expand.IsChecked == true);
            _tracks.IsVisible = _expand.IsChecked == true; if (_tracks.IsVisible) BuildTracks();
        };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowProperty)
        {
            _summary.Row = Row;
            _tracks.Children.Clear(); Definitions = Array.Empty<LayoutDefinitionEditorItem>();
            RestoreExpansion();
            if (_tracks.IsVisible) BuildTracks();
        }
    }
    public void ExpandDefinitions() => _expand.IsChecked = true;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); RestoreExpansion(); }
    private void RestoreExpansion()
    {
        if (Row is not { } row || (this.GetVisualRoot() as Window)?.DataContext is not MainWindowViewModel owner) return;
        _restoringExpansion = true; try { _expand.IsChecked = owner.IsPropertyGridExpanded(row); } finally { _restoringExpansion = false; }
    }
    private GridLength[] Read() => (string.IsNullOrWhiteSpace(Row?.Value) ? new[] { "*" } : Row.Value.Split(',')).Select(GridLength.Parse).ToArray();
    private void BuildTracks()
    {
        if (_updating || Row is not { } row) return;
        _updating = true;
        try
        {
            var values = Read(); _tracks.Children.Clear(); var items = new List<LayoutDefinitionEditorItem>();
            for (var i = 0; i < values.Length; i++)
            {
                var index = i; var value = values[i];
                var item = new LayoutDefinitionEditorItem(i, value.IsAuto ? "Auto" : value.IsStar ? "Star" : "Fixed", value.Value, null, true); items.Add(item);
                var mode = new ComboBox { ItemsSource = new[] { "Auto", "Star", "Pixel" }, SelectedIndex = value.IsAuto ? 0 : value.IsStar ? 1 : 2, MinWidth = 64, FontSize = 11 };
                var number = new TextBox { Text = value.Value.ToString("R", CultureInfo.InvariantCulture), IsEnabled = !value.IsAuto, MinWidth = 35, FontSize = 11, Padding = new Thickness(3, 1) };
                var reset = new Button { Content = "↺", Padding = new Thickness(3, 1) }; reset.Classes.Add("property-action"); ToolTip.SetTip(reset, "Вернуть Star (*) для этого определения");
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                Grid.SetColumn(number, 1); Grid.SetColumn(reset, 2); line.Children.Add(mode); line.Children.Add(number); line.Children.Add(reset); _tracks.Children.Add(line);
                void Commit()
                {
                    if (_updating || !ReferenceEquals(row, Row)) return;
                    if (!double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < 0)
                    { row.ValidationMessage = "GridLength требует конечное неотрицательное значение."; return; }
                    CommitDefinition(index, mode.SelectedIndex == 0 ? GridLength.Auto : new GridLength(n, mode.SelectedIndex == 1 ? GridUnitType.Star : GridUnitType.Pixel));
                }
                mode.SelectionChanged += (_, _) => { number.IsEnabled = mode.SelectedIndex != 0; Commit(); };
                number.KeyDown += (_, args) => { if (args.Key == Key.Enter) { Commit(); args.Handled = true; } };
                number.LostFocus += (_, _) => { if (number.IsEnabled) Commit(); };
                reset.Click += (_, _) => { if (ReferenceEquals(row, Row)) CommitDefinition(index, GridLength.Star); };
            }
            Definitions = items;
        }
        catch (Exception error) when (error is FormatException or ArgumentException) { row.ValidationMessage = "Недопустимые Grid definitions."; }
        finally { _updating = false; }
    }
    public void CommitDefinition(int index, GridLength length)
    {
        if (Row is not { } row || row.IsReadOnly) return;
        var values = Read(); values[index] = length;
        var vm = (this.GetVisualRoot() as Window)?.DataContext as MainWindowViewModel;
        vm?.CommitPropertyGridTextEdit(row, string.Join(",", values.Select(v => v.IsAuto ? "Auto" : v.Value.ToString("R", CultureInfo.InvariantCulture) + (v.IsStar ? "*" : ""))));
        if (ReferenceEquals(Row, row)) BuildTracks();
    }
}
