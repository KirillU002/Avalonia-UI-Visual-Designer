using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System.Runtime.CompilerServices;
using FormDesigner.DesignerSystem;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.PluginContracts;
using FormDesigner.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    internal bool IsTemplateDraft { get; private set; }

    public bool CanOpenItemTemplate => !IsTemplateDraft && ItemTemplateDesign.SupportsOwner(SelectedControl?.Type);

    internal string TemplateItemsPath { get; set; } = "";

    public MainWindowViewModel OpenItemTemplate(DesignControlModel owner)
    {
        if (IsBusy || HasImportedStyleScope || !Controls.Contains(owner) || !ItemTemplateDesign.SupportsOwner(owner.Type))
            throw new InvalidOperationException("Выберите ListBox, ComboBox или ItemsControl в проекте Designer.");
        owner.ItemTemplate?.Validate(owner.Type);
        return CreateTemplateDraft(_registry, owner.ItemTemplate ?? new(), _formAppearance);
    }

    private static MainWindowViewModel CreateTemplateDraft(IDesignerRegistry registry, ItemTemplateDesign template, FormAppearance? appearance)
    {
        var draft = new MainWindowViewModel(registry) { IsTemplateDraft = true, TemplateItemsPath = template.ItemsPath };
        try
        {
            draft.RefreshToolboxItemsFromRegistry();
            draft.LoadDocumentJson(JsonSerializer.Serialize(template.Document(appearance), JsonOptions));
            return draft;
        }
        catch { draft.CloseItemTemplate(); throw; }
    }

    public void CloseItemTemplate()
    {
        if (IsTemplateDraft) DisposeExportOnlyViewModel();
    }

    public void SaveItemTemplate(string documentId, DesignControlModel owner, MainWindowViewModel? draft, string? itemsPath = null)
    {
        if (IsBusy || HasImportedStyleScope || ActiveDocumentId != documentId || !Controls.Contains(owner)
            || !ItemTemplateDesign.SupportsOwner(owner.Type) || draft is { IsTemplateDraft: false })
            throw new InvalidOperationException("Владелец ItemTemplate изменился.");
        ItemTemplateDesign? template = null;
        if (draft != null)
        {
            var doc = draft.CreatePreviewDocumentSnapshot();
            template = new() { Rows = doc.SurfaceGridRowDefinitions, Columns = doc.SurfaceGridColumnDefinitions, Controls = doc.Controls, ItemsPath = itemsPath ?? draft.TemplateItemsPath };
            template.Validate(owner.Type);
            ValidateDataItems(owner,template);
            draft.BuildItemTemplate();
        }
        _lastHistoryMutationUtc = DateTime.MinValue;
        BeginUndoBatch();
        try { owner.ItemTemplate = template; }
        finally { CommitUndoBatch(); }
    }

    private static void ValidateDataItems(DesignControlModel owner,ItemTemplateDesign template)
    {
        var local = CollectionItemsContract.Read(owner, "Items");
        if (template.ItemsPath.Length > 0 && (CollectionItemsContract.HasExternalSource(owner) ||
            local?.Items.Count > 0 || StandardItemsProperties.Read(owner,"Items",Array.Empty<string>()).Length > 0))
            throw new InvalidOperationException("У владельца уже есть элементы или источник данных. Сначала очистите коллекцию или используйте её текущий источник.");
        if (local?.Items.Any(i => !string.IsNullOrEmpty(i.Name) || !i.IsEnabled || i.Children.Count != 0 || i.ExtensionData?.Count > 0 ||
                !string.IsNullOrEmpty(i.Content)) == true)
            throw new InvalidOperationException("У контейнеров элементов есть собственные настройки. Для ItemTemplate требуется коллекция данных или простые текстовые элементы.");
    }

    private XElement BuildItemTemplate()
    {
        GenerateXaml();
        var window = XElement.Parse(GeneratedXaml);
        var root = window.Elements().Single(e => !e.Name.LocalName.Contains('.'));
        if (root.Name != Av + "Grid")
            throw new InvalidOperationException("Корнем ItemTemplate должен быть Grid.");
        var generatedNames = root.Descendants().ToLookup(e => (string?)e.Attribute(Xaml + "Name"),StringComparer.Ordinal);
        foreach (var child in Controls)
        {
            var targets = generatedNames[GetExportControlName(child)].ToArray();
            var type = child.Type == DesignerControlTypes.Group ? "DockPanel" : child.Type;
            if (targets.Length != 1 || targets[0].Name != Av + type &&
                !(targets[0].Name == Av + "Border" && targets[0].Elements(Av + type).Any()))
                throw new InvalidOperationException("Не удалось сгенерировать нативный компонент шаблона: " + child.Name + " (" + type + ").");
        }
        root.Attribute(Xaml + "Name")?.Remove();
        foreach (var comment in root.DescendantNodes().OfType<XComment>().ToArray()) comment.Remove();
        foreach (var node in root.DescendantsAndSelf())
        {
            if (node.Name.Namespace != Av || !(ItemTemplateDesign.SupportsChild(node.Name.LocalName) || node.Name.LocalName == "Canvas"))
                throw new InvalidOperationException("Неподдерживаемая разметка ItemTemplate: " + node.Name);
            var name = node.Attribute(Xaml + "Name");
            if (name != null)
            {
                // Self bindings can read Name as soon as their attribute is applied.
                var attributes = node.Attributes().Where(attribute => attribute != name).ToArray();
                node.ReplaceAttributes(new XAttribute("Name", name.Value), attributes);
            }
            foreach (var attr in node.Attributes())
            {
                if (attr.IsNamespaceDeclaration) continue;
                if (attr.Name.Namespace != XNamespace.None || attr.Name.LocalName is "Click" or "DataContext" or "Command")
                    throw new InvalidOperationException("Неподдерживаемое свойство ItemTemplate: " + attr.Name);
                string text = attr.Value.TrimStart();
                if (!text.StartsWith('{') || text.StartsWith("{}", StringComparison.Ordinal)) continue;
                if (PropertyExpressions.TryParseBinding(text, out var binding) && binding.ConverterKey.Length == 0) continue;
                if (PropertyExpressions.TryResource(text, PropertyExpressions.Classify(text), out _)) continue;
                throw new InvalidOperationException("Сложное выражение ItemTemplate недоступно для визуального редактирования.");
            }
        }
        return new XElement(Av + "DataTemplate", root);
    }

    private static XElement GenerateItemTemplate(IDesignerRegistry registry, string ownerType, ItemTemplateDesign template, FormAppearance? appearance)
    {
        template.Validate(ownerType);
        var draft = CreateTemplateDraft(registry, template, appearance);
        try { return draft.BuildItemTemplate(); }
        finally { draft.CloseItemTemplate(); }
    }

    private string ApplyItemTemplates(string generated)
    {
        if (!Controls.Any(c => c.ItemTemplate != null)) return generated;
        var root = XElement.Parse(generated, LoadOptions.PreserveWhitespace);
        foreach (var owner in Controls.Where(c => c.ItemTemplate != null))
        {
            ValidateDataItems(owner, owner.ItemTemplate!);
            var node = root.Descendants(Av + owner.Type).Single(e => (string?)e.Attribute(Xaml + "Name") == GetExportControlName(owner));
            var property = Av + (owner.Type + ".ItemTemplate");
            node.Elements(property).Remove();
            foreach (var item in node.Elements().Where(e => e.Name.LocalName is "ListBoxItem" or "ComboBoxItem" or "ContentControl").ToArray())
            {
                var content = (string?)item.Attribute("Content") ?? "";
                item.ReplaceWith(new XElement(Xaml + "String", content.StartsWith("{}", StringComparison.Ordinal) ? content[2..] : content));
            }
            if (owner.ItemTemplate!.ItemsPath.Length > 0) node.SetAttributeValue("ItemsSource","{Binding " + owner.ItemTemplate.ItemsPath + "}");
            node.AddFirst(new XElement(property, GenerateItemTemplate(_registry, owner.Type, owner.ItemTemplate!, _formAppearance)));
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    internal static void ApplyNativeItemTemplate(Control view, string type, ItemTemplateDesign template, IDesignerRegistry registry, FormAppearance? appearance, bool designSurface = false)
    {
        if (StandardControlProperties.NativePropertyTarget(view, type) is not ItemsControl items) return;
        var cache = NativeTemplates.GetOrCreateValue(template);
        var appearanceKey = appearance == null ? "" : JsonSerializer.Serialize(appearance);
        if (cache.Revision != appearanceKey || cache.Template == null || !ReferenceEquals(cache.Registry,registry))
        {
            cache.Template = CompileNativeItemTemplate(type,template,registry,appearance);
            cache.Registry = registry;
            cache.Revision = appearanceKey;
        }
        items.ItemTemplate = cache.Template;
        if (template.ItemsPath.Length > 0)
        {
            if (designSurface) items.DataContext = null; // The Designer shell is not the user's collection owner.
            items.Items.Clear();
            items.Bind(ItemsControl.ItemsSourceProperty,new Avalonia.Data.Binding(template.ItemsPath));
            return;
        }
        if (items.ItemsSource == null && items.Items.OfType<ContentControl>().Any())
        {
            var data = items.Items.Cast<object?>().Select(i => i is ContentControl container ? container.Content : i).ToArray();
            items.Items.Clear();
            foreach (var item in data) items.Items.Add(item);
        }
    }

    // A saved template is replaced atomically. The cache lives only as long as its authoring snapshot.
    private sealed class NativeTemplateEntry
    {
        public string? Revision;
        public IDesignerRegistry? Registry;
        public IDataTemplate? Template;
    }
    private static readonly ConditionalWeakTable<ItemTemplateDesign,NativeTemplateEntry> NativeTemplates = new();

    private static IDataTemplate CompileNativeItemTemplate(string type,ItemTemplateDesign template,IDesignerRegistry registry,FormAppearance? appearance)
    {
        return CompileTemplateProjection(GenerateItemTemplate(registry,type,template,appearance),appearance);
    }

    internal static IDataTemplate CompileTemplateProjection(XElement element,FormAppearance? appearance)
    {
        var host = new XElement(Av + "ContentControl", new XAttribute(XNamespace.Xmlns + "x", Xaml.NamespaceName));
        if (appearance is { Resources.Count: > 0 })
        {
            var resources = new System.Text.StringBuilder();
            AppendAuthoredResources(resources, appearance);
            host.Add(XElement.Parse("<ContentControl.Resources xmlns='" + Av + "' xmlns:x='" + Xaml + "'><ResourceDictionary>" + resources + "</ResourceDictionary></ContentControl.Resources>"));
        }
        var root = element.Elements().Single();
        if (root.Name != Av + "Grid")
        {
            root.Remove();
            root = new XElement(Av + "Grid",root);
            element.Add(root);
        }
        if (appearance is { Styles.Count: > 0 })
            root.AddFirst(XElement.Parse("<Grid.Styles xmlns='" + Av + "'>" + string.Concat(appearance.Styles.Select(FormStyleAuthoring.StyleXml)) + "</Grid.Styles>"));
        if (host.Elements().FirstOrDefault() is {} resourceSlot)
            root.AddFirst(new XElement(Av + "Grid.Resources",resourceSlot.Elements().Select(e => new XElement(e))));
        host.Add(new XElement(Av + "ContentControl.ContentTemplate", element));
        var compiled = (ContentControl)RuntimeAxamlPreviewLoader.Load(host.ToString(SaveOptions.OmitDuplicateNamespaces));
        return compiled.ContentTemplate!;
    }
}
