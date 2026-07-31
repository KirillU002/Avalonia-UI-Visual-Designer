using FormDesigner.Models;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.DesignerSystem.Binding;

internal static class BindingPreviewItemsBuilder
{
    public static IReadOnlyList<Dictionary<string, object?>> BuildSampleItems(BindingSourceModel source, int rowCount = 6)
    {
        return BuildSampleItems(
            source.Fields.Select(field => new BindingFieldMetadata
            {
                Header = field.Header,
                Path = field.Path,
                SampleValue = field.SampleValue,
                Width = field.Width,
                TypeName = field.TypeName,
                IsVisible = field.IsVisible,
                IsSortable = field.IsSortable,
                CanWrite = field.CanWrite,
                MinWidth = field.MinWidth,
                MaxWidth = field.MaxWidth,
                AllowResize = field.AllowResize,
                AllowSort = field.AllowSort,
                AllowFilter = field.AllowFilter,
                VisibleIndex = field.VisibleIndex
            }),
            rowCount);
    }

    public static IReadOnlyList<Dictionary<string, object?>> BuildSampleItems(BindingSourceMetadata source, int rowCount = 6)
    {
        return BuildSampleItems(source.Fields, rowCount);
    }

    public static IReadOnlyList<Dictionary<string, object?>> ConvertRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        return rows
            .Select(row => row.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<Dictionary<string, object?>> BuildSampleItems(IEnumerable<BindingFieldMetadata> fields, int rowCount)
    {
        var selectedFields = fields
            .Where(field => field.IsVisible)
            .DefaultIfEmpty()
            .Where(field => field is not null)
            .Cast<BindingFieldMetadata>()
            .ToList();

        if (selectedFields.Count == 0)
            return Array.Empty<Dictionary<string, object?>>();

        var totalRows = Math.Max(3, rowCount);
        var rows = new List<Dictionary<string, object?>>(totalRows);
        for (var rowIndex = 0; rowIndex < totalRows; rowIndex++)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in selectedFields)
            {
                var value = string.IsNullOrWhiteSpace(field.SampleValue)
                    ? field.Header
                    : field.SampleValue;
                row[field.Path] = value;
            }

            rows.Add(row);
        }

        return rows;
    }
}
