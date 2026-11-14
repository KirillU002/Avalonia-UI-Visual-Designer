using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using FormDesigner.ViewModels;
using System;
using System.Globalization;

namespace FormDesigner.Controls;

// Chooses an existing editor template rather than building every hidden editor
// for every row. Row values, validation, context, commands and event handlers stay
// in the existing Inspector infrastructure.
public sealed class PropertyGridEditorTemplates:IValueConverter
{
    internal void EnableRecycling()
    {
        Text = Wrap(Text); Boolean = Wrap(Boolean); Enumeration = Wrap(Enumeration); Thickness = Wrap(Thickness); CornerRadius = Wrap(CornerRadius); GridDefinitions = Wrap(GridDefinitions);
        Color = Wrap(Color); Action = Wrap(Action); ReadOnly = Wrap(ReadOnly);
    }
    private static IDataTemplate? Wrap(IDataTemplate? template) => template is null ? null : new PropertyGridRecyclingTemplate(template);
    public IDataTemplate? Text {get;set;}
    public IDataTemplate? Boolean {get;set;}
    public IDataTemplate? Enumeration {get;set;}
    public IDataTemplate? Color {get;set;}
    public IDataTemplate? Action {get;set;}
    public IDataTemplate? ReadOnly {get;set;}
    public IDataTemplate? Thickness {get;set;}
    public IDataTemplate? CornerRadius {get;set;}
    public IDataTemplate? GridDefinitions {get;set;}
    private static readonly IDataTemplate Empty=new FuncDataTemplate<object>((_,_)=>new Border());
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value switch
    {
        PropertyGridEditorKind.Text or PropertyGridEditorKind.Number or PropertyGridEditorKind.GridLength or PropertyGridEditorKind.Font=>Text??Empty,
        PropertyGridEditorKind.Thickness => Thickness ?? Text ?? Empty,
        PropertyGridEditorKind.CornerRadius => CornerRadius ?? Text ?? Empty,
        PropertyGridEditorKind.GridDefinitions => GridDefinitions ?? Text ?? Empty,
        PropertyGridEditorKind.Bool=>Boolean??Empty,
        PropertyGridEditorKind.Enum or PropertyGridEditorKind.BindingSource=>Enumeration??Empty,
        PropertyGridEditorKind.Color=>Color??Empty,
        PropertyGridEditorKind.Action or PropertyGridEditorKind.ColumnCollection or PropertyGridEditorKind.Interaction or PropertyGridEditorKind.Asset=>Action??Empty,
        PropertyGridEditorKind.ReadOnly=>ReadOnly??Empty,
        _=>Empty
    };
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}

public sealed class PropertyGridBrushPreviewConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && text != FormDesigner.DesignerSystem.Metadata.MetadataValueCodec.NullText)
            try { if (Avalonia.Media.Brush.Parse(text) is Avalonia.Media.ISolidColorBrush brush) return brush; }
            catch (FormatException) { }
        return Avalonia.Media.Brushes.Transparent;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
