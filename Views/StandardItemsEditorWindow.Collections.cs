using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;

namespace FormDesigner.Views;

public sealed partial class StandardItemsEditorWindow
{
    public CollectionEditorSession? Session { get; private set; }
    public TreeView? CollectionTree { get; }
    public DesignerPropertyInspector? ItemInspector { get; }
    public StandardItemsEditorWindow(MainWindowViewModel owner, CollectionEditorSession session)
        : this(Array.Empty<StandardMenuItem>(), false, _ => { })
    {
        Session = session; DataContext = owner; Title = "Коллекция — " + session.PropertyName;
        Width = 900; Height = 650;
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new("Auto,*,Auto") };
        var tools = new WrapPanel { Orientation = Orientation.Horizontal }; root.Children.Add(tools);
        var ownedClicks = new System.Collections.Generic.List<(Button Button, EventHandler<Avalonia.Interactivity.RoutedEventArgs> Handler)>();
        void Button(string name, Action action)
        {
            var button = new Button { Content = name, Margin = new Thickness(0, 0, 6, 6) };
            EventHandler<Avalonia.Interactivity.RoutedEventArgs> click = (_, _) => action();
            button.Click += click; ownedClicks.Add((button, click)); tools.Children.Add(button);
        }
        Button("Добавить", () => session.Add());
        if (session.ItemKind is "MenuItem" or "TreeViewItem") Button("Вложенный", () => session.Add(nested: true));
        if (session.ItemKind == "MenuItem") Button("Разделитель", () => session.Add(separator: true));
        Button("Удалить", () => session.Remove()); Button("Выше ↑", () => session.Move(-1)); Button("Ниже ↓", () => session.Move(1));
        Button("Отменить", () => { owner.EndPropertyGridTextEdit(); owner.UndoEditorCommand?.Execute(null); });
        Button("Повторить", () => { owner.EndPropertyGridTextEdit(); owner.RedoEditorCommand?.Execute(null); });
        Button("Закрыть", Close);
        var search = new TextBox { Watermark = "Поиск свойств элемента", Width = 210, Margin = new Thickness(0, 0, 6, 6) };
        var searchBinding = search.Bind(TextBox.TextProperty, new Binding(nameof(MainWindowViewModel.PropertyGridSearchText)) { Source = owner, Mode = BindingMode.TwoWay });
        tools.Children.Add(search);
        if (session.PropertyName == StandardItemsProperties.MenuKey) Button("Действие в Logic…", () =>
        {
            if (session.SelectedItem is { Kind: "MenuItem" } item && owner.GetControl(session.OwnerId) is { } control)
            { owner.EditStandardMenuLogic(control, item); Close(); owner.WorkspaceMode = MainWindowViewModel.WorkspaceModeLogic; }
        });
        var body = new Grid { ColumnDefinitions = new("2*,3*") }; Grid.SetRow(body, 1); root.Children.Add(body);
        CollectionTree = new TreeView
        {
            ItemsSource = session.Roots,
            ItemTemplate = new FuncTreeDataTemplate(typeof(CollectionTreeNode), (_, _) =>
            {
                var title = new TextBlock(); title.Bind(TextBlock.TextProperty, new Binding(nameof(CollectionTreeNode.Title))); return title;
            }, value => value is CollectionTreeNode node ? node.Children : Array.Empty<CollectionTreeNode>())
        };
        body.Children.Add(CollectionTree);
        ItemInspector = new DesignerPropertyInspector { Context = owner, ToggleFavoriteCommand = owner.TogglePropertyGridFavoriteCommand };
        void Reset(object? sender, Avalonia.Interactivity.RoutedEventArgs args) { if (sender is Button { DataContext: PropertyGridRowViewModel row }) owner.ResetPropertyGridProperty(row); }
        ItemInspector.ResetRequested += Reset;
        Grid.SetColumn(ItemInspector, 1); body.Children.Add(new Border { Child = new ScrollViewer { Content = ItemInspector }, Margin = new Thickness(12, 0, 0, 0) });
        Grid.SetColumn(body.Children[^1], 1);
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; Grid.SetRow(status, 2); root.Children.Add(status);
        void Refresh(object? sender, EventArgs args)
        {
            if (session.IsDisposed) { Close(); return; }
            CollectionTree.SelectedItem = session.SelectedNode;
            status.Text = session.Count == 0 ? "Коллекция пуста — добавьте первый элемент." : session.Count + " элементов. Изменения применяются сразу; Undo/Redo атомарны.";
            if (session.ValidationMessage.Length != 0) status.Text = session.ValidationMessage;
        }
        void Select(object? sender, SelectionChangedEventArgs args)
        { if (!session.IsSynchronizing && CollectionTree.SelectedItem is CollectionTreeNode item) session.Select(item.Id); }
        CollectionTree.SelectionChanged += Select;
        session.Changed += Refresh;
        EventHandler? closed = null;
        closed = (_, _) =>
        {
            Closed -= closed; session.Changed -= Refresh; CollectionTree.SelectionChanged -= Select; ItemInspector.ResetRequested -= Reset;
            searchBinding.Dispose();
            foreach (var subscription in ownedClicks) subscription.Button.Click -= subscription.Handler;
            ownedClicks.Clear();
            owner.CloseCollectionEditor(session); Session = null; CollectionTree.ItemsSource = null;
            ItemInspector.Context = null; ItemInspector.ToggleFavoriteCommand = null; Content = null; DataContext = null;
        };
        Closed += closed;
        Content = root; Refresh(null, EventArgs.Empty);
    }
}
