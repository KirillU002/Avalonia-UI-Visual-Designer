using Avalonia.Controls;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FormDesigner.DesignerSystem.Metadata;

public sealed record CollectionPropertyMetadata(string PropertyName, Type OwnerType, Type ItemType, Type CollectionType,
    bool IsReadOnly, bool CanAdd, bool CanRemove, bool CanReorder, bool CanEditItems, bool IsControlCollection,
    bool IsDataCollection, string SerializationStrategy, string ImportCapability, string ExportCapability);

// Discovery reads signatures only. Mutation is enabled exclusively by reviewed adapters.
public static class CollectionMetadataCatalog
{
    public static IReadOnlyList<CollectionPropertyMetadata> Get(Type type) => AvaloniaMetadataCatalog.Get(type).Collections;
    internal static IReadOnlyList<CollectionPropertyMetadata> Discover(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
        .Select(p =>
        {
            var item = p.PropertyType.GetInterfaces().Prepend(p.PropertyType).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0] ?? typeof(object);
            var items = p.Name == "Items" && (type == typeof(Menu) || type == typeof(MenuItem) || type == typeof(MenuFlyout) || type == typeof(ContextMenu) || type == typeof(ComboBox) || type == typeof(ListBox) || type == typeof(TabControl) || type == typeof(TreeView) || type == typeof(TreeViewItem));
            var columns = type == typeof(DataGrid) && p.Name == "Columns";
            var children = p.Name == "Children" && (type == typeof(Grid) || type == typeof(Canvas) || type == typeof(StackPanel) || type == typeof(WrapPanel) || type == typeof(DockPanel));
            var editable = items || columns || children;
            var controls = items || typeof(Control).IsAssignableFrom(item);
            return new CollectionPropertyMetadata(p.Name, p.DeclaringType!, item, p.PropertyType, p.SetMethod?.IsPublic != true,
                editable, editable, editable, editable, controls, columns || !controls && item != typeof(object) && p.Name is not ("Styles" or "Resources"),
                items ? "ExistingPropertyBag.CollectionV1" : columns ? "ExistingDataGridColumnsEditor" : children ? "ExistingParentIdAndStackOrder" : "PreserveOnly",
                "SourcePreservingReadOnlyStructure", editable ? "ReviewedAdapter" : "Unsupported");
        }).ToArray();
}
