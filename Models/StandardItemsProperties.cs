using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;

namespace FormDesigner.Models;

public sealed partial class StandardMenuItem : ObservableObject
{
    [ObservableProperty] private string id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string text = "Пункт меню";
    [ObservableProperty] private bool isEnabled = true;
    public StandardMenuItem Clone() => new() { Id = Id, Text = Text, IsEnabled = IsEnabled };
}

// Uses the existing JSON property bag and its history/clone/document serialization.
public static class StandardItemsProperties
{
    public const string MenuKey = "Flyout.Items";
    public static bool IsMenuButton(string type) => type is DesignerControlTypes.DropDownButton or DesignerControlTypes.SplitButton;
    public static bool IsStandardItemsControl(string type) => IsMenuButton(type) || type == DesignerControlTypes.ListBox;
    public static T Read<T>(DesignControlModel model, string key, T fallback) => Read(model.CustomProperties.Select(p => (p.Key, p.ValueJson)), key, fallback);
    public static T Read<T>(DesignerControlFileModel model, string key, T fallback) => Read(model.CustomProperties.Select(p => (p.Key, p.ValueJson)), key, fallback);
    private static T Read<T>(IEnumerable<(string Key, string ValueJson)> values, string key, T fallback)
    {
        var json = values.FirstOrDefault(p => p.Key == key).ValueJson;
        if (json is null) return fallback;
        try { return JsonSerializer.Deserialize<T>(json) ?? fallback; }
        catch (JsonException)
        {
            // AXAML literal projection stores scalar literals as JSON strings.
            try
            {
                var literal = JsonSerializer.Deserialize<string>(json);
                return literal is null ? fallback : (T)Convert.ChangeType(literal, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidCastException or OverflowException) { return fallback; }
        }
    }
    public static void Write<T>(DesignControlModel model, string key, T value)
    {
        var existing = model.CustomProperties.FirstOrDefault(p => p.Key == key);
        var json = JsonSerializer.Serialize(value);
        if (existing is null) model.CustomProperties.Add(new() { Key = key, ValueJson = json });
        else existing.ValueJson = json;
    }
    public static List<StandardMenuItem> Menu(DesignControlModel model) => Read(model, MenuKey, new List<StandardMenuItem>());
    public static List<StandardMenuItem> Menu(DesignerControlFileModel model) => Read(model, MenuKey, new List<StandardMenuItem>());
    public static DesignControlModel MenuSource(DesignControlModel owner, StandardMenuItem item) => new()
    {
        Id = owner.Id + ":" + item.Id, ParentId = owner.Id, Type = DesignerControlTypes.Button,
        Name = MenuName(owner.Id, item.Id), Text = item.Text, GeneratedButtonActionKey = "None"
    };
    public static string MenuName(string ownerId, string itemId) => "MenuItem_" + Safe(ownerId) + "_" + Safe(itemId);
    private static string Safe(string value) => new(value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
    public static IEnumerable<DesignControlModel> WithMenuSources(IEnumerable<DesignControlModel> controls)
    {
        foreach (var control in controls)
        {
            yield return control;
            if (IsMenuButton(control.Type))
                foreach (var item in Menu(control)) yield return MenuSource(control, item);
        }
    }
}
