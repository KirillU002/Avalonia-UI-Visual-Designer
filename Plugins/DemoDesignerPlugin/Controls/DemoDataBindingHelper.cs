using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DemoDesignerPlugin.Controls;

internal static class DemoDataBindingHelper
{
    public static IReadOnlyList<DemoColumnDefinition> ResolveColumns(
        string? columnsDefinitionText,
        IEnumerable? itemsSource,
        bool autoGenerateColumns,
        int maxColumns = 4)
    {
        var explicitColumns = ParseColumns(columnsDefinitionText);
        if (explicitColumns.Count > 0)
            return explicitColumns;

        if (!autoGenerateColumns || itemsSource is null)
            return CreateFallbackColumns(maxColumns);

        var firstItem = itemsSource.Cast<object?>().FirstOrDefault(item => item is not null);
        if (firstItem is null)
            return CreateFallbackColumns(maxColumns);

        if (firstItem is IDictionary<string, object?> dictionary)
        {
            return dictionary.Keys
                .Take(maxColumns)
                .Select(key => new DemoColumnDefinition(key, key))
                .ToList();
        }

        var properties = firstItem.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead)
            .Take(maxColumns)
            .Select(property => new DemoColumnDefinition(property.Name, property.Name))
            .ToList();

        return properties.Count > 0 ? properties : CreateFallbackColumns(maxColumns);
    }

    public static IReadOnlyList<DemoColumnDefinition> ParseColumns(string? columnsDefinitionText)
    {
        if (string.IsNullOrWhiteSpace(columnsDefinitionText))
            return Array.Empty<DemoColumnDefinition>();

        var result = new List<DemoColumnDefinition>();
        var columnChunks = columnsDefinitionText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var chunk in columnChunks)
        {
            var parts = chunk.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            var header = parts[0];
            var path = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : header;
            result.Add(new DemoColumnDefinition(header, path));
        }

        return result;
    }

    public static IReadOnlyList<object?> MaterializeItems(IEnumerable? itemsSource, int maxItems = 24)
    {
        if (itemsSource is null)
            return Array.Empty<object?>();

        return itemsSource.Cast<object?>()
            .Take(Math.Max(1, maxItems))
            .ToList();
    }

    public static object? ResolveValue(object? item, string? path)
    {
        if (item is null)
            return null;

        if (string.IsNullOrWhiteSpace(path))
            return item;

        object? current = item;
        foreach (var token in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
                return null;

            if (current is IDictionary<string, object?> objectDictionary)
            {
                current = objectDictionary.TryGetValue(token, out var value)
                    ? value
                    : TryResolveDictionaryIgnoreCase(objectDictionary, token);
                continue;
            }

            if (current is IDictionary dictionary)
            {
                current = ResolveFromNonGenericDictionary(dictionary, token);
                continue;
            }

            var property = current.GetType().GetProperty(token, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            current = property?.GetValue(current);
        }

        return current;
    }

    public static IEnumerable? ResolveChildren(object? item, string? childrenPath)
    {
        var value = ResolveValue(item, string.IsNullOrWhiteSpace(childrenPath) ? "Children" : childrenPath);
        return value as IEnumerable;
    }

    private static IReadOnlyList<DemoColumnDefinition> CreateFallbackColumns(int maxColumns)
    {
        var count = Math.Max(1, maxColumns);
        return Enumerable.Range(1, count)
            .Select(index => new DemoColumnDefinition($"Колонка {index}", $"Column{index}"))
            .ToList();
    }

    private static object? TryResolveDictionaryIgnoreCase(IDictionary<string, object?> dictionary, string key)
    {
        foreach (var pair in dictionary)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    private static object? ResolveFromNonGenericDictionary(IDictionary dictionary, string key)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }
}

internal sealed record DemoColumnDefinition(string Header, string Path);
