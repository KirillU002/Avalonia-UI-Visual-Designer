using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.Models;

/// <summary>The children of the template's implicit Grid use the ordinary form layout contract.</summary>
public sealed class ItemTemplateDesign
{
    public int Version { get; set; } = 1;
    public string Columns { get; set; } = "*";
    public string Rows { get; set; } = "Auto";
    public string ItemsPath { get; set; } = "";
    public List<DesignerControlFileModel> Controls { get; set; } = new();

    public static bool SupportsOwner(string? type) => type is "ListBox" or "ComboBox" or "ItemsControl";
    public static bool SupportsChild(string? type) => type is "TextBlock" or "Image" or "Border" or "StackPanel"
        or "Grid" or "DockPanel" or "WrapPanel" or "Button" or "CheckBox";

    internal static bool SupportsDescriptor(string type) => type == DesignerControlTypes.Group || SupportsChild(type) && type != "DockPanel";

    internal void Validate(string owner)
    {
        if (Version != 1 || !SupportsOwner(owner) || Controls == null || Controls.Any(c => c == null ||
            !SupportsDescriptor(c.Type) || c.Type == DesignerControlTypes.Group && c.ChildLayoutMode != DesignerLayoutModes.Dock || !string.IsNullOrEmpty(c.PluginId) || !string.IsNullOrEmpty(c.DescriptorId) && c.DescriptorId != c.Type || c.ItemTemplate != null || string.IsNullOrWhiteSpace(c.Id) ||
            c.CustomProperties == null || c.CustomProperties.Any(p => p == null) ||
            !string.IsNullOrEmpty(c.BindingSourceId) || !string.IsNullOrEmpty(c.GeneratedButtonActionKey)))
            throw new InvalidOperationException("Некорректное описание ItemTemplate.");
        if (ItemsPath == null || ItemsPath.Length > 512 || ItemsPath.Length != 0 &&
            !FormDesigner.DesignerSystem.Metadata.PropertyExpressions.TryBinding(new(ItemsPath),out _,out _))
            throw new InvalidOperationException("Недопустимый путь ItemsSource.");
        if (Controls.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Повторяющийся идентификатор внутри ItemTemplate.");
        var byId = Controls.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in Controls)
        {
            var branch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var node = child;
            while (!verified.Contains(node.Id) && !string.IsNullOrEmpty(node.ParentId))
            {
                if (!branch.Add(node.Id) || !byId.TryGetValue(node.ParentId, out node!)
                    || node.Type is not ("Border" or "StackPanel" or "Grid" or "Group" or "WrapPanel"))
                    throw new InvalidOperationException("Некорректный родитель внутри ItemTemplate.");
            }
            verified.UnionWith(branch);
        }
        if (Controls.GroupBy(c => c.ParentId).Any(g => !string.IsNullOrEmpty(g.Key) && byId[g.Key].Type == "Border" && g.Count() > 1))
            throw new InvalidOperationException("Border допускает только один дочерний элемент.");
    }

    internal DesignerDocumentFileModel Document(FormAppearance? appearance) => new()
    {
        FormTitle = "ItemTemplate", LayoutSemanticsVersion = 2, SurfaceLayoutMode = DesignerLayoutModes.Grid,
        SurfaceGridColumnDefinitions = Columns, SurfaceGridRowDefinitions = Rows, Controls = Controls,
        Appearance = appearance, DesignWidth = 480, DesignHeight = 240
    };
}
