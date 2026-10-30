using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FormDesigner.Views;

public sealed class StandardItemsEditorWindow : Window
{
    public ObservableCollection<StandardMenuItem> Items { get; }
    private readonly ListBox _list;
    private readonly TextBox _text;
    private readonly CheckBox _enabled;
    private readonly Action<IReadOnlyList<StandardMenuItem>> _apply;
    public StandardItemsEditorWindow(IEnumerable<StandardMenuItem> items, bool menu,
        Action<IReadOnlyList<StandardMenuItem>> apply, Action<StandardMenuItem>? editLogic = null)
    {
        Title = menu ? "Пункты Flyout" : "Элементы ListBox";
        Width = 740; Height = 460; MinWidth = 600; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Items = new(items.Select(i => i.Clone())); _apply = apply;
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto") };
        root.Children.Add(new TextBlock { Text = Title, FontSize = 21, FontWeight = FontWeight.SemiBold });
        var body = new Grid { ColumnDefinitions = new("*,*"), Margin = new Thickness(0, 14) }; Grid.SetRow(body, 1); root.Children.Add(body);
        _list = new ListBox { ItemsSource = Items, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<StandardMenuItem>((item, _) =>
        {
            var label = new TextBlock(); label.Bind(TextBlock.TextProperty, new Binding(nameof(StandardMenuItem.Text))); return label;
        }) };
        body.Children.Add(_list);
        var editor = new StackPanel { Spacing = 10, Margin = new Thickness(18, 0, 0, 0) }; Grid.SetColumn(editor, 1); body.Children.Add(editor);
        editor.Children.Add(new TextBlock { Text = "Название элемента" });
        _text = new TextBox { Watermark = "Введите название" }; editor.Children.Add(_text);
        _enabled = new CheckBox { Content = "Доступен", IsVisible = menu }; editor.Children.Add(_enabled);
        _list.SelectionChanged += (_, _) =>
        {
            _text.DataContext = _list.SelectedItem; _enabled.DataContext = _list.SelectedItem;
        };
        _text.Bind(TextBox.TextProperty, new Binding(nameof(StandardMenuItem.Text)) { Mode = BindingMode.TwoWay });
        _enabled.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(StandardMenuItem.IsEnabled)) { Mode = BindingMode.TwoWay });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal }; editor.Children.Add(actions);
        void ActionButton(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => action(); actions.Children.Add(button);
        }
        ActionButton("Добавить", AddItem); ActionButton("Удалить", RemoveSelected);
        ActionButton("Выше ↑", () => MoveSelected(-1)); ActionButton("Ниже ↓", () => MoveSelected(1));
        if (menu && editLogic is not null)
            ActionButton("Настроить действие в Logic…", () =>
            {
                if (_list.SelectedItem is not StandardMenuItem selected) return;
                _apply(Items.ToList()); editLogic(selected); Close();
            });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        var save = new Button { Content = "Применить", IsDefault = true }; save.Click += (_, _) => { _apply(Items.ToList()); Close(); }; footer.Children.Add(save);
        var cancel = new Button { Content = "Отмена", IsCancel = true }; cancel.Click += (_, _) => Close(); footer.Children.Add(cancel);
        Content = root; _list.SelectedIndex = Items.Count > 0 ? 0 : -1;
    }
    public void AddItem() { var item = new StandardMenuItem(); Items.Add(item); _list.SelectedItem = item; }
    public void RemoveSelected() { var index = _list.SelectedIndex; if (index < 0) return; Items.RemoveAt(index); _list.SelectedIndex = Math.Min(index, Items.Count - 1); }
    public void MoveSelected(int delta) { var index = _list.SelectedIndex; var next = index + delta; if (index < 0 || next < 0 || next >= Items.Count) return; Items.Move(index, next); _list.SelectedIndex = next; }
}
