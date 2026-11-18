using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FormDesigner.Controls;
using FormDesigner.DesignerSystem.Metadata;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Linq;
using System.Collections.Generic;

namespace FormDesigner.Views;

public sealed class FormStyleEditorWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly string _document;
    private readonly TextBlock _message = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ComboBox _target = Choices(FormStyleAuthoring.Targets.ToArray());
    private readonly TextBox _selector = new() { Text = "Button" };
    private readonly ListBox _styles = new() { Height = 88 };
    private readonly ListBox _setters = new() { Height = 105 };
    private readonly ComboBox _property = new();
    private readonly ComboBox _mode = Choices("Literal","StaticResource","DynamicResource");
    private readonly ContentControl _valueHost = new();
    private PropertyGridRowViewModel? _row;
    private Func<string> _readValue = () => "";
    private const string CommonResourceScope = "Общий";
    private readonly TextBox _search = new() { Watermark = "Поиск по ключу, типу или теме" };
    private readonly ComboBox _resourceTypeFilter = Choices(FormStyleAuthoring.ResourceTypes.Prepend("Все типы").ToArray());
    private readonly ListBox _resources = new() { Height = 155 };
    private readonly ComboBox _type = Choices(FormStyleAuthoring.ResourceTypes.ToArray());
    private readonly ComboBox _variant = Choices(CommonResourceScope,"Default","Light","Dark");
    private readonly TextBox _key = new() { Watermark = "Ключ" };
    private readonly TextBox _resourceValue = new() { Watermark = "Значение ресурса" };
    private FormResource[] _visible = Array.Empty<FormResource>();
    private FormAppearance _sheet;
    private bool _refreshing;
    private string _revision;
    public FormStyleEditorWindow(MainWindowViewModel vm)
    {
        _vm = vm;
        _document = vm.ActiveDocumentId;
        _sheet = vm.AppearanceDraft();
        _revision = vm.AppearanceRevision;
        Title = "Стили и ресурсы формы";
        Width = 650; Height = 720; MinWidth = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        if (vm.HasImportedStyleScope) BuildSource(body);
        else
        {
            body.Children.Add(new TabControl { ItemsSource = new[]
            {
                new TabItem { Header = "Стили", Content = BuildStyles() },
                new TabItem { Header = "Ресурсы", Content = BuildResources() }
            }});
            Refresh();
        }
        body.Children.Add(_message);
        Content = new ScrollViewer { Content = body };
        Closed += (_,_) => { Content = null; DataContext = null; };
    }

    private static ComboBox Choices(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, MinWidth = 95 };
    private static StackPanel Panel(params Control[] controls)
    {
        var p = new StackPanel { Spacing = 6 };
        foreach (var c in controls) p.Children.Add(c);
        return p;
    }
    private static WrapPanel Actions(params Control[] controls)
    {
        var p = new WrapPanel();
        foreach (var c in controls) { c.Margin = new Thickness(0,2,4,2); p.Children.Add(c); }
        return p;
    }
    private Button Action(string label, Action click)
    {
        var button = new Button { Content = label };
        button.Click += (_,_) =>
        {
            try { _message.Text = ""; click(); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or OverflowException or System.Xml.XmlException)
            { _message.Text = ex.Message; }
        };
        return button;
    }
    private FormStyle Current(FormAppearance sheet) => _styles.SelectedIndex is var i && i >= 0 && i < sheet.Styles.Count
        ? sheet.Styles[i] : throw new InvalidOperationException("Выберите стиль.");
    private void Change(Action<FormAppearance> change,int? next = null)
    {
        if (_revision != _vm.AppearanceRevision) throw new InvalidOperationException("Форма изменилась. Откройте редактор повторно.");
        var candidate = _sheet.Copy();
        change(candidate);
        _vm.SaveAppearance(_document,candidate);
        _sheet = candidate;
        _revision = _vm.AppearanceRevision;
        Refresh(next);
    }
    private void Refresh(int? next = null)
    {
        _refreshing = true;
        var i = next ?? _styles.SelectedIndex;
        _styles.ItemsSource = _sheet.Styles.Select(s => s.Selector).ToArray();
        _styles.SelectedIndex = Math.Clamp(i,-1,_sheet.Styles.Count-1);
        _refreshing = false;
        LoadStyle(); FilterResources();
    }
    private void LoadStyle()
    {
        if (_refreshing) return;
        if (_styles.SelectedIndex >= 0)
        {
            var s = Current(_sheet);
            _target.SelectedItem = s.Target; _selector.Text = s.Selector;
            _setters.ItemsSource = s.Setters.Select(x => x.Property+" = "+x.Value).ToArray();
        }
        else _setters.ItemsSource = null;
        LoadProperties();
    }
    private void LoadProperties()
    {
        if (_target.SelectedItem is not string target) return;
        _property.ItemsSource = FormStyleAuthoring.Properties(target).Select(p => p.Key).OrderBy(x => x).ToArray();
        _property.SelectedIndex = 0;
        LoadValue();
    }
    private void LoadValue(string? value = null)
    {
        if (_target.SelectedItem is not string t || _property.SelectedItem is not string key) return;
        var p = FormStyleAuthoring.Properties(t).First(v => v.Key == key);
        var kind = _mode.SelectedIndex == 0 ? p.Editor : MetadataEditor.Text;
        var text = value ?? (_mode.SelectedIndex == 0 ? p.DefaultText : "");
        if (kind == MetadataEditor.Enumeration || p.PropertyType.IsEnum && _mode.SelectedIndex == 0)
        {
            var choice = Choices(Enum.GetNames(p.PropertyType)); choice.SelectedItem = text;
            _valueHost.Content = choice; _readValue = () => choice.SelectedItem as string ?? ""; return;
        }
        var editor = kind switch { MetadataEditor.CornerRadius => PropertyGridEditorKind.CornerRadius, MetadataEditor.Thickness => PropertyGridEditorKind.Thickness, _ => PropertyGridEditorKind.Text };
        _row = new(key,key,"Setter",editor,text,"Значение Setter") { IsStyleDraft = true };
        var row = _row;
        if (kind is MetadataEditor.Thickness or MetadataEditor.CornerRadius)
        { _valueHost.Content = new PropertyGridCompoundEditor { Row = row }; _readValue = () => row.Value; }
        else
        { var input = new PropertyGridTextEditor { Row = row }; _valueHost.Content = input; _readValue = () => input.Text ?? ""; }
    }
    private void LoadSetter()
    {
        if (_setters.SelectedIndex < 0 || _styles.SelectedIndex < 0) return;
        var v = Current(_sheet).Setters[_setters.SelectedIndex];
        var kind = PropertyExpressions.Classify(v.Value);
        _property.SelectedItem = v.Property;
        _mode.SelectedIndex = kind == PropertyValueKind.StaticResource ? 1 : kind == PropertyValueKind.DynamicResource ? 2 : 0;
        LoadValue(PropertyExpressions.TryResource(v.Value,kind,out var key) ? key : v.Value);
    }
    private FormStyleSetter InputSetter()
    {
        if (_property.SelectedItem is not string key) throw new InvalidOperationException("Выберите свойство.");
        var value = _readValue();
        return new(key,_mode.SelectedIndex == 0 ? value : "{"+_mode.SelectedItem+" "+value+"}");
    }
    private void MoveSetter(int delta) => Change(sheet =>
    {
        var setters = Current(sheet).Setters; var i = _setters.SelectedIndex;
        if (i < 0 || i+delta < 0 || i+delta >= setters.Count) throw new InvalidOperationException("Выберите соседний Setter.");
        (setters[i],setters[i+delta]) = (setters[i+delta],setters[i]);
    });
    private Control BuildStyles()
    {
        _styles.SelectionChanged += (_,_) => LoadStyle();
        _target.SelectionChanged += (_,_) => { _selector.Text = _target.SelectedItem as string; LoadProperties(); };
        _property.SelectionChanged += (_,_) => LoadValue();
        _mode.SelectionChanged += (_,_) => LoadValue();
        _setters.SelectionChanged += (_,_) => LoadSetter();
        return Panel(_styles,Actions(_target,_selector),
            Actions(Action("Добавить стиль",() => Change(s => s.Styles.Add(new((string)_target.SelectedItem!,_selector.Text ?? "",new())),_sheet.Styles.Count)),
                Action("Изменить селектор",() => Change(s => s.Styles[_styles.SelectedIndex] = Current(s) with { Target=(string)_target.SelectedItem!,Selector=_selector.Text ?? "" })),
                Action("Удалить стиль",() => Change(s => s.Styles.Remove(Current(s))))),
            _setters,Actions(_property,_mode),_valueHost,
            Actions(Action("Добавить Setter",() => Change(s => Current(s).Setters.Add(InputSetter()))),
                Action("Изменить",() => Change(s => Current(s).Setters[_setters.SelectedIndex] = InputSetter())),
                Action("Удалить Setter",() => Change(s => Current(s).Setters.RemoveAt(_setters.SelectedIndex))),
                Action("↑",() => MoveSetter(-1)),Action("↓",() => MoveSetter(1))),
            Actions(Action("Назначить класс выбранному элементу",() => Change(s =>
            {
                var style = Current(s);
                var suffix = FormStyleAuthoring.SelectorSuffix(style.Target,style.Selector);
                if (!suffix.StartsWith('.') || _vm.SelectedControl is not {} c || c.Type != style.Target)
                    throw new InvalidOperationException("Выберите элемент нужного типа и стиль Тип.класс.");
                s.Classes[c.Id] = suffix[1..];
            })),Action("Взять свойство из стиля",() =>
            {
                var style = Current(_sheet);
                var key = (string?)_property.SelectedItem;
                var suffix = FormStyleAuthoring.SelectorSuffix(style.Target,style.Selector);
                if (_vm.SelectedControl is not {} selected || selected.Type != style.Target
                    || !style.Setters.Any(v => v.Property == key)
                    || suffix.StartsWith('#') && selected.Name != suffix[1..]
                    || suffix.StartsWith('.') && _sheet.Classes.GetValueOrDefault(selected.Id) != suffix[1..])
                    throw new InvalidOperationException("Выберите Setter стиля, который применяется к выделенному элементу.");
                var row = _vm.PropertyGridCategories.SelectMany(c => c.Rows).FirstOrDefault(r => r.PropertyMetadata?.Native.Key == key && r.CanEditExpression);
                if (_vm.ActiveDocumentId != _document || row is null) throw new InvalidOperationException("Выберите свойство элемента в Inspector.");
                if (!_vm.CommitPropertyExpression(row,new(PropertyValueKind.Unset,""),out var error)) throw new InvalidOperationException(error);
                _revision = _vm.AppearanceRevision;
                _message.Text = "Локальное значение снято.";
            })));
    }
    private static string ResourceScope(FormResource resource) =>
        resource.Variant.Length == 0 ? CommonResourceScope : resource.Variant;

    private void FilterResources()
    {
        var query = _search.Text?.Trim() ?? "";
        var type = _resourceTypeFilter.SelectedIndex > 0 ? _resourceTypeFilter.SelectedItem as string : null;
        var visible = _sheet.Resources.Where(r => (type == null || r.Type == type)
            && $"{r.Key} {r.Type} {ResourceScope(r)}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_visible.SequenceEqual(visible)) return;

        var index = _resources.SelectedIndex;
        var selected = index >= 0 && index < _visible.Length ? _visible[index] : null;
        _visible = visible;
        _refreshing = true;
        try
        {
            _resources.ItemsSource = visible.Select(r => $"{ResourceScope(r)} · {r.Type} · {r.Key}").ToArray();
            _resources.SelectedIndex = selected == null ? -1 :
                Array.FindIndex(visible, r => r.Key == selected.Key && r.Variant == selected.Variant);
        }
        finally { _refreshing = false; }
    }
    private Control BuildResources()
    {
        _search.TextChanged += (_,_) => FilterResources();
        _resourceTypeFilter.SelectionChanged += (_,_) => FilterResources();
        ToolTip.SetTip(_resourceTypeFilter, "Фильтр по типу ресурса");
        var filters = new DockPanel { LastChildFill = true };
        _resourceTypeFilter.Margin = new Thickness(6,0,0,0);
        DockPanel.SetDock(_resourceTypeFilter, Dock.Right);
        filters.Children.Add(_resourceTypeFilter);
        filters.Children.Add(_search);
        _resources.SelectionChanged += (_,_) =>
        {
            if (!_refreshing && _resources.SelectedIndex is var i && i >= 0 && i < _visible.Length)
            { var r = _visible[i]; _key.Text=r.Key; _resourceValue.Text=r.Value; _type.SelectedItem=r.Type; _variant.SelectedItem=ResourceScope(r); }
        };
        FormResource Input() => new(_key.Text ?? "",(string)_type.SelectedItem!,_resourceValue.Text ?? "",_variant.SelectedIndex == 0 ? "" : (string)_variant.SelectedItem!);
        FormResource Selected() => _resources.SelectedIndex >= 0 ? _visible[_resources.SelectedIndex] : throw new InvalidOperationException("Выберите ресурс.");
        return Panel(filters,_resources,Actions(_variant,_type),_key,_resourceValue,
            new TextBlock { Text="Переименование и удаление ключа требуют проверки ссылок в других документах.", TextWrapping=Avalonia.Media.TextWrapping.Wrap },
            Actions(Action("Добавить ресурс",() => Change(s => s.Resources.Add(Input()))),
            Action("Сохранить ключ и значение",() => Change(s => s.Resources[s.Resources.IndexOf(Selected())] = Input())),
            Action("Удалить ресурс",() => Change(s => s.Resources.Remove(Selected())))));
    }
    private void BuildSource(StackPanel body)
    {
        var fields = _vm.StyleFields.ToArray();
        var list = new ListBox { ItemsSource = fields.Select(f => f.Label).ToArray(), Height = 220 };
        var input = new TextBox();
        list.SelectionChanged += (_,_) => { if (list.SelectedIndex >= 0) input.Text = fields[list.SelectedIndex].Value; };
        body.Children.Add(Panel(list,input,Action("Применить значение к AXAML",() =>
        {
            if (list.SelectedIndex < 0) throw new InvalidOperationException("Выберите значение.");
            _vm.SaveSourceStyleValue(_document,fields[list.SelectedIndex],input.Text ?? ""); Close();
        })));
        var notices = _vm.StyleNotices().ToArray();
        if (notices.Length > 0)
        {
            body.Children.Add(new TextBlock { Text = "Объявления, доступные только для чтения" });
            body.Children.Add(new ListBox { ItemsSource = notices, MaxHeight = 180 });
        }
    }
}
