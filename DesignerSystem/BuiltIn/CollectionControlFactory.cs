using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FormDesigner.Models;
using System;
using System.Linq;
using System.Text;

namespace FormDesigner.DesignerSystem.BuiltIn;

public static class CollectionControlFactory
{
    public static bool IsRoot(string type) => type is "Menu" or "ComboBox" or "TabControl" or "TreeView" or "ItemsControl" or "Carousel" or "TabStrip";
    public static bool Supports(string type) => IsRoot(type) || type == "ListBox" || StandardItemsProperties.IsMenuButton(type);
    public static string NativeName(string ownerId, StandardMenuItem item) => string.IsNullOrEmpty(item.Name) ? StandardItemsProperties.MenuName(ownerId, item.Id) : item.Name;
    public static Control Create(DesignerControlFileModel owner)
    {
        if (CollectionItemsContract.HasExternalSource(owner))
            throw new NotSupportedException("Этот adapter поддерживает локальные Items; внешний ItemsSource сохранён и не заменён. Используйте существующий ListBox BindingSource workflow.");
        var values = CollectionItemsContract.Read(owner, "Items")?.Items ?? new();
        ItemsControl root = owner.Type switch { "ItemsControl" => new ItemsControl(), "Carousel" => new Carousel(), "TabStrip" => new TabStrip(), "Menu" => new Menu(), "ComboBox" => new ComboBox(), "TabControl" => new TabControl(), "TreeView" => new TreeView(), _ => throw new InvalidOperationException("Unsupported collection root") };
        foreach (var item in values) root.Items.Add(CreateItem(owner.Id, item));
        if (root is SelectingItemsControl selecting) selecting.SelectedIndex = StandardItemsProperties.Read(owner, "SelectedIndex", owner.Type == "TabControl" ? 0 : -1);
        if (root is TreeView tree && Enum.TryParse<SelectionMode>(StandardItemsProperties.Read(owner, "SelectionMode", "Single"), out var mode)) tree.SelectionMode = mode;
        AttachContextMenu(root, owner); return root;
    }
    public static Control CreateItem(string ownerId, StandardMenuItem item, Action<StandardMenuItem>? click = null)
    {
        Control result = item.Kind switch
        {
            "ContentControl" => new ContentControl { Content = item.Text },
            "TabStripItem" => new TabStripItem { Content = item.Text },
            "Separator" => new Separator(),
            "MenuItem" => new MenuItem { Header = item.Text },
            "ComboBoxItem" => new ComboBoxItem { Content = item.Text },
            "ListBoxItem" => new ListBoxItem { Content = item.Text },
            "TabItem" => new TabItem { Header = item.Text, Content = item.Content },
            "TreeViewItem" => new TreeViewItem { Header = item.Text, IsExpanded = item.IsExpanded },
            _ => throw new FormatException("Неизвестный тип элемента коллекции: " + item.Kind)
        };
        result.Name = NativeName(ownerId, item); result.IsEnabled = item.IsEnabled;
        if (result is ItemsControl parent) foreach (var child in item.Children) parent.Items.Add(CreateItem(ownerId, child, click));
        if (result is MenuItem menu && click is not null) menu.Click += (_, _) => { if (item.Children.Count == 0) click(item); };
        return result;
    }
    public static void AttachContextMenu(Control root, DesignerControlFileModel owner)
    {
        if (CollectionItemsContract.Read(owner, "ContextMenu.Items") is not { } data) return;
        var menu = new ContextMenu(); foreach (var item in data.Items) menu.Items.Add(CreateItem(owner.Id, item)); root.ContextMenu = menu;
    }
    private static string Xml(string text) => System.Security.SecurityElement.Escape(text) ?? "";
    private static string Literal(string text) => Xml(text.StartsWith('{') ? "{}" + text : text);
    public static void AppendItems(StringBuilder output, string ownerId, System.Collections.Generic.IEnumerable<StandardMenuItem> items, int indent, bool handlers = false, Func<StandardMenuItem, string>? resolveName = null)
    {
        foreach (var item in items)
        {
            var padding = new string(' ', indent * 2); var name = resolveName?.Invoke(item) ?? NativeName(ownerId, item);
            var caption = item.Kind is "MenuItem" or "TreeViewItem" or "TabItem" ? "Header" : "Content";
            var attributes = $" x:Name=\"{Xml(name)}\" IsEnabled=\"{(item.IsEnabled ? "True" : "False")}\"";
            if (item.Kind != "Separator") attributes += $" {caption}=\"{Literal(item.Text)}\"";
            if (item.Kind == "TabItem") attributes += $" Content=\"{Literal(item.Content)}\"";
            if (item.Kind == "TreeViewItem") attributes += $" IsExpanded=\"{item.IsExpanded}\"";
            if (handlers && item.Kind == "MenuItem") attributes += $" Click=\"{name}MenuClick\"";
            if (item.Children.Count == 0) output.AppendLine($"{padding}<{item.Kind}{attributes} />");
            else { output.AppendLine($"{padding}<{item.Kind}{attributes}>"); AppendItems(output, ownerId, item.Children, indent + 1, handlers, resolveName); output.AppendLine($"{padding}</{item.Kind}>"); }
        }
    }
    public static void AppendContextMenu(StringBuilder output, DesignControlModel owner, int indent)
    {
        if (CollectionItemsContract.Read(owner, "ContextMenu.Items") is not { } data) return;
        var pad = new string(' ', indent * 2); output.AppendLine($"{pad}<{owner.Type}.ContextMenu><ContextMenu>");
        AppendItems(output, owner.Id, data.Items, indent + 1); output.AppendLine($"{pad}</ContextMenu></{owner.Type}.ContextMenu>");
    }
}
