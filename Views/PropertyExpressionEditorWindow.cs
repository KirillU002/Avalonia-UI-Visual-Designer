using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.ViewModels;
using System;

namespace FormDesigner.Views;

// Created only by an explicit fx action. No editor controls live in every row,
// and no Binding/ProvideValue/converter or user DataContext is instantiated here.
public sealed class PropertyExpressionEditorWindow : Window
{
    public static int CreationCount { get; private set; }
    public ComboBox ValueMode { get; } = new() { ItemsSource = new[] { "Literal", "Binding", "StaticResource", "DynamicResource" } };
    public TextBox PathEditor { get; } = new();
    public ComboBox BindingModeEditor { get; } = new() { ItemsSource = Enum.GetNames<BindingMode>() };
    public ComboBox TriggerEditor { get; } = new() { ItemsSource = Enum.GetNames<UpdateSourceTrigger>() };
    public TextBox ElementNameEditor { get; } = new();
    public ComboBox RelativeSourceEditor { get; } = new() { ItemsSource = new[] { "", "Self", "TemplatedParent" } };
    public TextBox ConverterEditor { get; } = new();
    public TextBox LiteralEditor { get; } = new();
    public TextBox ResourceKeyEditor { get; } = new();
    public TextBox FallbackEditor { get; } = new();
    public TextBox NullEditor { get; } = new();
    public TextBox FormatEditor { get; } = new();
    public CheckBox HasFallback { get; } = new() { Content = "FallbackValue (literal / (null))" };
    public CheckBox HasTargetNull { get; } = new() { Content = "TargetNullValue (literal / (null))" };
    public CheckBox HasFormat { get; } = new() { Content = "StringFormat" };
    public TextBlock ErrorText { get; } = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    public TextBox ExpressionPreview { get; } = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 55, TextWrapping = TextWrapping.Wrap };
    private readonly PropertyExpressionEditorContext _context;
    private readonly MainWindowViewModel _owner;
    public PropertyExpressionEditorWindow(MainWindowViewModel owner, PropertyExpressionEditorContext context)
    {
        CreationCount++; _owner=owner;_context=context;
        Title="Значение: "+context.Row.Label;Width=620;Height=720;MinWidth=500;MinHeight=430;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var body=new StackPanel{Spacing=7,Margin=new Thickness(18)};
        void Field(Panel parent,string label,Control editor){parent.Children.Add(new TextBlock{Text=label});parent.Children.Add(editor);}
        Field(body,"Режим значения (изменяется только после Применить)",ValueMode);
        var binding=new StackPanel{Spacing=5};body.Children.Add(binding);
        Field(binding,"Path — свойство / вложенный путь / числовой индекс",PathEditor);
        var paths=new ComboBox{ItemsSource=context.Paths,PlaceholderText="Пути существующих BindingSource (metadata, без SQL)"};
        paths.SelectionChanged+=(_,_)=>{if(paths.SelectedItem is string p)PathEditor.Text=p;};binding.Children.Add(paths);
        Field(binding,"Binding Mode",BindingModeEditor);Field(binding,PropertyExpressions.SupportsMarkupUpdateSourceTrigger?"UpdateSourceTrigger":"UpdateSourceTrigger: XAML API не поддерживает (Default)",TriggerEditor);TriggerEditor.IsEnabled=PropertyExpressions.SupportsMarkupUpdateSourceTrigger;
        Field(binding,"ElementName",ElementNameEditor);Field(binding,"RelativeSource",RelativeSourceEditor);
        binding.Children.Add(HasFallback);binding.Children.Add(FallbackEditor);
        binding.Children.Add(HasTargetNull);binding.Children.Add(NullEditor);
        binding.Children.Add(HasFormat);binding.Children.Add(FormatEditor);
        Field(binding,"Converter — существующий StaticResource key (без создания экземпляра)",ConverterEditor);
        var resource=new StackPanel{Spacing=5};body.Children.Add(resource);Field(resource,"Resource Key",ResourceKeyEditor);
        var keys=new ComboBox{ItemsSource=context.ResourceKeys,PlaceholderText="Известные ключи scope / generated theme"};
        keys.SelectionChanged+=(_,_)=>{if(keys.SelectedItem is string k)ResourceKeyEditor.Text=k;};resource.Children.Add(keys);
        var literal=new StackPanel{Spacing=5};body.Children.Add(literal);Field(literal,"Literal — проверяется существующим typed codec",LiteralEditor);
        Field(body,"Выражение (не исполняется)",ExpressionPreview);body.Children.Add(ErrorText);
        body.Children.Add(new TextBlock{Text=context.Notice,TextWrapping=TextWrapping.Wrap,FontSize=11});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};
        var apply=new Button{Content="Применить"};apply.Click+=(_,_)=>{if(Apply())Close();};actions.Children.Add(apply);
        var reset=new Button{Content="Reset — удалить local value"};reset.Click+=(_,_)=>{if(_owner.ResetPropertyExpression(context.Row))Close();else ErrorText.Text="Строка устарела; откройте редактор заново.";};actions.Children.Add(reset);
        var cancel=new Button{Content="Отмена"};cancel.Click+=(_,_)=>Close();actions.Children.Add(cancel);body.Children.Add(actions);
        Content=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        var initial=context.InitialValue;
        ValueMode.SelectedItem=initial.Kind is PropertyValueKind.Binding or PropertyValueKind.StaticResource or PropertyValueKind.DynamicResource?initial.Kind.ToString():"Literal";
        LiteralEditor.Text=initial.Kind==PropertyValueKind.Literal?initial.Text:context.Property.DefaultText;
        var b=PropertyExpressions.TryParseBinding(initial.Text,out var parsed)?parsed:new BindingEditorValue();
        PathEditor.Text=b.Path;BindingModeEditor.SelectedItem=b.Mode;TriggerEditor.SelectedItem=b.UpdateSourceTrigger;
        ElementNameEditor.Text=b.ElementName;RelativeSourceEditor.SelectedItem=b.RelativeSource;ConverterEditor.Text=b.ConverterKey;
        HasFallback.IsChecked=b.FallbackValue is not null;FallbackEditor.Text=b.FallbackValue;
        HasTargetNull.IsChecked=b.TargetNullValue is not null;NullEditor.Text=b.TargetNullValue;
        HasFormat.IsChecked=b.StringFormat is not null;FormatEditor.Text=b.StringFormat;
        if(PropertyExpressions.TryResource(initial.Text,initial.Kind,out var key))ResourceKeyEditor.Text=key;
        void RefreshMode(){binding.IsVisible=ValueMode.SelectedItem as string=="Binding";resource.IsVisible=ValueMode.SelectedItem as string is "StaticResource" or "DynamicResource";literal.IsVisible=ValueMode.SelectedItem as string=="Literal";}
        ValueMode.SelectionChanged+=(_,_)=>RefreshMode();RefreshMode();ExpressionPreview.Text=initial.Text;
        Closed+=(_,_)=>{Content=null;DataContext=null;};
    }
    public bool Apply()
    {
        PropertyExpressionValue value;
        switch(ValueMode.SelectedItem as string)
        {
            case "Binding":
                var binding=new BindingEditorValue(PathEditor.Text??"",BindingModeEditor.SelectedItem as string??"Default",TriggerEditor.SelectedItem as string??"Default",
                    ElementNameEditor.Text??"",RelativeSourceEditor.SelectedItem as string??"",HasFallback.IsChecked==true?FallbackEditor.Text??"":null,
                    HasTargetNull.IsChecked==true?NullEditor.Text??"":null,HasFormat.IsChecked==true?FormatEditor.Text??"":null,ConverterEditor.Text??"");
                if(!PropertyExpressions.TryBinding(binding,out var text,out var error)){ErrorText.Text=error;return false;}
                value=new(PropertyValueKind.Binding,text);break;
            case "StaticResource":case "DynamicResource":
                var kind=Enum.Parse<PropertyValueKind>((string)ValueMode.SelectedItem!);value=new(kind,"{"+kind+" "+ResourceKeyEditor.Text+"}");break;
            default:value=new(PropertyValueKind.Literal,LiteralEditor.Text??"");break;
        }
        ExpressionPreview.Text=value.Text;
        if(!_owner.CommitPropertyExpression(_context.Row,value,out var message)){ErrorText.Text=message;return false;}
        return true;
    }
}
