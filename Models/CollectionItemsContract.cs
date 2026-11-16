using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FormDesigner.Models;

// Reviewed values in the existing property bag, never serialized Avalonia instances.
public sealed class CollectionItemsContract
{
    public int Version { get; set; } = 1;
    public List<StandardMenuItem> Items { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public const int MaximumDepth = 16, MaximumItems = 2000;
    public static string StorageKey(string property) => property + ".Collection";
    public static bool HasExternalSource(IEnumerable<(string Key, string ValueJson)> values, string bindingSourceId)
    {
        if (!string.IsNullOrWhiteSpace(bindingSourceId)) return true;
        var raw = values.FirstOrDefault(p => p.Key == "ItemsSource").ValueJson;
        if (raw is null) return false;
        try { using var json = JsonDocument.Parse(raw); return json.RootElement.ValueKind is not JsonValueKind.Null && !(json.RootElement.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(json.RootElement.GetString())); }
        catch (JsonException) { return true; }
    }
    public static bool HasExternalSource(DesignControlModel owner) => HasExternalSource(owner.CustomProperties.Select(p => (p.Key, p.ValueJson)), owner.BindingSourceId);
    public static bool HasExternalSource(DesignerControlFileModel owner) => HasExternalSource(owner.CustomProperties.Select(p => (p.Key, p.ValueJson)), owner.BindingSourceId);
    public static string ItemKind(string owner, string property) => property == "ContextMenu.Items" || property == StandardItemsProperties.MenuKey || owner == "Menu" ? "MenuItem"
        : owner switch { "ItemsControl" or "Carousel" => "ContentControl", "TabStrip" => "TabStripItem", "ComboBox" => "ComboBoxItem", "ListBox" => "ListBoxItem", "TabControl" => "TabItem", "TreeView" => "TreeViewItem", _ => "" };
    public static IEnumerable<StandardMenuItem> Flatten(IEnumerable<StandardMenuItem> items)
    {
        var seen = new HashSet<StandardMenuItem>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var ids = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        foreach (var item in Walk(items, 0)) yield return item;
        IEnumerable<StandardMenuItem> Walk(IEnumerable<StandardMenuItem> values, int depth)
        {
            if (depth > MaximumDepth) throw new FormatException("Превышена допустимая глубина коллекции (16).");
            foreach (var item in values)
            {
                if (item is null || !seen.Add(item) || !ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Id)) throw new FormatException("Цикл или повторяющийся Id элемента коллекции.");
                if (++count > MaximumItems) throw new FormatException("Коллекция превышает ограничение 2000 элементов.");
                yield return item;
                foreach (var child in Walk(item.Children ?? throw new FormatException("Children не может быть null."), depth + 1)) yield return child;
            }
        }
    }
    public static void Validate(CollectionItemsContract value, string kind)
    {
        if (value.Version != 1) throw new FormatException("Неизвестная версия коллекции: " + value.Version + ". Исходный JSON сохранён.");
        if (value.Items is null) throw new FormatException("Items не может быть null; исходный JSON сохранён.");
        if (string.IsNullOrEmpty(kind)) throw new FormatException("Нет безопасного адаптера коллекции.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Flatten(value.Items))
        {
            if (item.Kind != kind && !(kind == "MenuItem" && item.Kind == "Separator")) throw new FormatException("Недопустимый тип элемента: " + item.Kind);
            if (item.Children.Count != 0 && item.Kind is not ("MenuItem" or "TreeViewItem")) throw new FormatException("Этот тип элемента не поддерживает Children.");
            if (item.Text is null || item.Content is null || item.Name is null) throw new FormatException("Текстовые поля должны содержать строку.");
            if (item.Name.Length != 0 && (!IsName(item.Name) || !names.Add(item.Name))) throw new FormatException("Name должен быть уникальным AXAML-идентификатором.");
        }
    }
    private static bool IsName(string text) => (char.IsLetter(text[0]) || text[0] == '_') && text.All(c => char.IsLetterOrDigit(c) || c == '_');
    public static StandardMenuItem Clone(StandardMenuItem item)
    {
        _ = Flatten(new[] { item }).Count();
        return JsonSerializer.Deserialize<StandardMenuItem>(JsonSerializer.Serialize(item), new JsonSerializerOptions { MaxDepth = 64 })!;
    }
    public static CollectionItemsContract? Read(IEnumerable<(string Key, string ValueJson)> values, string owner, string property)
    {
        var raw = values.FirstOrDefault(p => p.Key == StorageKey(property)).ValueJson;
        if (raw is null) return null;
        try
        {
            var result = JsonSerializer.Deserialize<CollectionItemsContract>(raw, new JsonSerializerOptions { MaxDepth = 64 }) ?? throw new FormatException("Пустой контракт коллекции.");
            Validate(result, ItemKind(owner, property)); return result;
        }
        catch (JsonException error) { throw new FormatException("Повреждённый JSON коллекции; исходное значение сохранено.", error); }
    }
    public static CollectionItemsContract? Read(DesignControlModel owner, string property) => Read(owner.CustomProperties.Select(p => (p.Key, p.ValueJson)), owner.Type, property);
    public static CollectionItemsContract? Read(DesignerControlFileModel owner, string property) => Read(owner.CustomProperties.Select(p => (p.Key, p.ValueJson)), owner.Type, property);
    public static void ValidateLegacy(DesignControlModel owner, string property)
    {
        if (Read(owner, property) is not null) return;
        var raw = owner.CustomProperties.FirstOrDefault(p => p.Key == property)?.ValueJson;
        if (raw is null) return;
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("Исходная коллекция не является локальным массивом; значение сохранено.");
            if (property == "Items" && owner.Type == "ListBox" && json.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String)) throw new FormatException("Нестроковые прежние Items требуют отдельного адаптера.");
            if (property == StandardItemsProperties.MenuKey)
            {
                var items = JsonSerializer.Deserialize<List<StandardMenuItem>>(raw) ?? new();
                Validate(new() { Items = items }, "MenuItem");
                if (Flatten(items).Any(i => i.ExtensionData?.Count > 0)) throw new FormatException("Неизвестные свойства прежнего Flyout сохранены без редактирования.");
            }
        }
        catch (JsonException error) { throw new FormatException("Повреждённая прежняя коллекция сохранена без изменений.", error); }
    }
}
