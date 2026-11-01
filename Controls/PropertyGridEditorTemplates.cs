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
    public IDataTemplate? Text {get;set;}
    public IDataTemplate? Boolean {get;set;}
    public IDataTemplate? Enumeration {get;set;}
    public IDataTemplate? Color {get;set;}
    public IDataTemplate? Action {get;set;}
    public IDataTemplate? ReadOnly {get;set;}
    private static readonly IDataTemplate Empty=new FuncDataTemplate<object>((_,_)=>new Border());
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value switch
    {
        PropertyGridEditorKind.Text or PropertyGridEditorKind.Number=>Text??Empty,
        PropertyGridEditorKind.Bool=>Boolean??Empty,
        PropertyGridEditorKind.Enum or PropertyGridEditorKind.BindingSource=>Enumeration??Empty,
        PropertyGridEditorKind.Color=>Color??Empty,
        PropertyGridEditorKind.Action or PropertyGridEditorKind.ColumnCollection or PropertyGridEditorKind.Interaction or PropertyGridEditorKind.Asset=>Action??Empty,
        PropertyGridEditorKind.ReadOnly=>ReadOnly??Empty,
        _=>Empty
    };
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
