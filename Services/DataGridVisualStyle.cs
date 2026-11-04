using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FormDesigner.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace FormDesigner.Services;

// Local DataGrid styles only. The same rules feed generated AXAML and native
// previews; application themes and the designer's chrome are never modified.
public sealed class DataGridVisualStyle
{
    public sealed record Brushes(string HeaderBackground, string HeaderForeground, string RowForeground,
        string GridLine, string HoverBackground, string SelectedBackground, string SelectedForeground);
    private sealed record Rule(string Selector, IReadOnlyDictionary<string, string> Values);
    private readonly List<Rule> _rules;
    public Thickness HeaderPadding { get; }
    public Thickness CellPadding { get; }
    public Thickness HeaderBorder { get; }
    public double HeaderHeight { get; }
    public string HeaderBackground { get; }
    private DataGridVisualStyle(DesignControlModel model, Brushes colors)
    {
        var headerHeight = Math.Max(24, model.DataGridHeaderHeight); var rowHeight = Math.Max(18, model.DataGridRowHeight);
        HeaderHeight = headerHeight;
        HeaderBackground = colors.HeaderBackground;
        HeaderPadding = CompactPadding(model.DataGridCellPadding, headerHeight, model.DataGridHeaderFontSize, 6);
        CellPadding = CompactPadding(model.DataGridCellPadding, rowHeight, model.DataGridRowFontSize, 4);
        HeaderBorder = new Thickness(model.DataGridShowColumnLines ? 1 : 0, model.DataGridShowRowLines ? 1 : 0, 0, model.DataGridShowRowLines ? 1 : 0);
        var cellBorder = new Thickness(0, 0, model.DataGridShowColumnLines ? 1 : 0, model.DataGridShowRowLines ? 1 : 0);
        _rules = new()
        {
            new("DataGridColumnHeader", new Dictionary<string,string>
            {
                ["Background"]=colors.HeaderBackground, ["Foreground"]=colors.HeaderForeground, ["BorderBrush"]=colors.GridLine,
                ["BorderThickness"]=HeaderBorder.ToString(), ["Padding"]=PaddingText(HeaderPadding), ["MinHeight"]=Number(headerHeight),
                ["FontFamily"]=model.FontFamily, ["FontSize"]=Number(model.DataGridHeaderFontSize), ["FontWeight"]=model.DataGridHeaderFontWeight
            }),
            new("DataGridCell", new Dictionary<string,string>
            {
                ["Foreground"]=colors.RowForeground, ["BorderBrush"]=colors.GridLine, ["BorderThickness"]=cellBorder.ToString(),
                ["Padding"]=PaddingText(CellPadding), ["MinHeight"]=Number(rowHeight),
                ["FontFamily"]=model.FontFamily, ["FontSize"]=Number(model.DataGridRowFontSize), ["FontWeight"]=model.DataGridRowFontWeight,
                ["HorizontalContentAlignment"]=DesignControlModel.NormalizeDataGridTextAlignment(model.DataGridTextAlignment), ["VerticalContentAlignment"]="Center"
            }),
            new("DataGridCell:pointerover", new Dictionary<string,string>{{"Background",colors.HoverBackground}}),
            new("DataGridCell:selected", new Dictionary<string,string>{{"Background",colors.SelectedBackground},{"Foreground",colors.SelectedForeground}}),
            new("DataGridRow", new Dictionary<string,string>{{"MinHeight",Number(rowHeight)}})
        };
    }
    public static DataGridVisualStyle Create(DesignControlModel model, Brushes? colors = null) => new(model, colors ?? new(
        model.DataGridHeaderBackground, model.DataGridHeaderForeground, model.DataGridRowForeground, model.DataGridGridLineBrush,
        model.DataGridHoverRowBackground, model.DataGridSelectedRowBackground, model.DataGridSelectedRowForeground));

    public static Thickness CompactPadding(double requestedPadding, double height, double fontSize, double maximumVertical)
    {
        var requested = Math.Max(0, requestedPadding); if (requested == 0) return default;
        var vertical = Math.Max(1, Math.Min(maximumVertical, Math.Floor((Math.Max(18, height) - Math.Max(8, fontSize) - 4) / 2)));
        return new Thickness(Math.Round(Math.Min(requested,10),1), Math.Round(Math.Min(requested,vertical),1));
    }
    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    private static string PaddingText(Thickness value) => value.Left == value.Top ? Number(value.Left) : Number(value.Left)+","+Number(value.Top);
    public void AppendAxaml(StringBuilder writer, int indent)
    {
        string Pad(int level) => new(' ',level*2);
        writer.AppendLine(Pad(indent)+"<DataGrid.Styles>");
        foreach(var rule in _rules)
        {
            writer.AppendLine(Pad(indent+1)+$"<Style Selector=\"{rule.Selector}\">");
            foreach(var item in rule.Values)
                writer.AppendLine(Pad(indent+2)+new XElement("Setter",new XAttribute("Property",item.Key),new XAttribute("Value",item.Value)).ToString(SaveOptions.DisableFormatting));
            writer.AppendLine(Pad(indent+1)+"</Style>");
        }
        writer.AppendLine(Pad(indent)+"</DataGrid.Styles>");
    }
    public void Apply(DataGrid grid)
    {
        foreach(var old in grid.Styles.OfType<LocalDataGridStyle>().ToArray()) grid.Styles.Remove(old);
        foreach(var rule in _rules)
        {
            var target = rule.Selector.StartsWith("DataGridColumnHeader",StringComparison.Ordinal) ? typeof(DataGridColumnHeader)
                : rule.Selector.StartsWith("DataGridCell",StringComparison.Ordinal) ? typeof(DataGridCell) : typeof(DataGridRow);
            var style = new LocalDataGridStyle { Selector = new Style(s => s.OfType(target)).Selector };
            if(rule.Selector.Contains(':')) style.Selector = style.Selector!.Class(rule.Selector[rule.Selector.IndexOf(':')..]);
            foreach(var item in rule.Values)
            {
                var property = (AvaloniaProperty)target.GetField(item.Key+"Property",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy)!.GetValue(null)!;
                object value = item.Key switch
                {
                    "Background" or "Foreground" or "BorderBrush" => Brush.Parse(item.Value),
                    "Padding" or "BorderThickness" => Thickness.Parse(item.Value),
                    "FontFamily" => new FontFamily(item.Value),
                    "FontWeight" => Enum.Parse<FontWeight>(item.Value),
                    "HorizontalContentAlignment" => Enum.Parse<HorizontalAlignment>(item.Value),
                    "VerticalContentAlignment" => Enum.Parse<VerticalAlignment>(item.Value),
                    _ => double.Parse(item.Value,CultureInfo.InvariantCulture)
                };
                style.Setters.Add(new Setter(property,value));
            }
            grid.Styles.Add(style);
        }
    }
    private sealed class LocalDataGridStyle : Style { }
}
