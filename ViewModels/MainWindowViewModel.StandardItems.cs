using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using FormDesigner.Models;
using FormDesigner.DesignerSystem.Binding;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private IEnumerable<DesignControlModel> ButtonLogicSources() => StandardItemsProperties.WithMenuSources(Controls)
        .Where(c => c.Type == DesignerControlTypes.Button || StandardItemsProperties.IsMenuButton(c.Type));

    public string StandardInteractionSourceTitle(string name)
    {
        foreach (var owner in Controls.Where(c => StandardItemsProperties.IsMenuButton(c.Type)))
            foreach (var item in StandardItemsProperties.Menu(owner))
                if (StandardItemsProperties.MenuSource(owner, item).Name == name) return owner.Name + " → " + item.Text;
        return name;
    }

    private void AppendStandardFlyoutHandlers(StringBuilder sb, IReadOnlyDictionary<string, string> names)
    {
        foreach (var owner in Controls.Where(c => StandardItemsProperties.IsMenuButton(c.Type)))
            foreach (var item in StandardItemsProperties.Menu(owner))
            {
                var source = StandardItemsProperties.MenuSource(owner, item);
                var name = names[source.Id];
                sb.AppendLine($"    private void {name}MenuClick(object? sender, RoutedEventArgs e)");
                sb.AppendLine("    {");
                sb.AppendLine($"        {names[owner.Id]}.Flyout?.Hide();");
                if (HasConfiguredInteractionEvent(source, InteractionModel.EventButtonClick)) sb.AppendLine($"        {name}Click(sender, e);");
                sb.AppendLine("    }");
            }
    }

    private static string StandardContentLiteral(string value) => EscapeXml(value.StartsWith('{') ? "{}" + value : value);

    private bool HasBoundListBox() => Controls.Any(c => c.Type == DesignerControlTypes.ListBox && GetBindingSource(c.BindingSourceId) is not null);
    private bool IsListBoxOnlySource(BindingSourceModel source) => Controls.Any(c => c.Type == DesignerControlTypes.ListBox && c.BindingSourceId == source.Id)
        && !Controls.Any(c => c.Type != DesignerControlTypes.ListBox && IsDataBindingControl(c) && c.BindingSourceId == source.Id);

    private DataGridRuntimeDataModeResolution ResolveItemsRuntimeMode(BindingSourceModel source)
    {
        if (!IsListBoxOnlySource(source)) return ResolveRuntimeDataModeForExport(source);
        var real = source.Clone(); real.UseDemoData = false; real.AllowPreviewSampleFallback = false;
        if (real.PreviewRowMode == BindingSourceModel.PreviewRowModeSampleRows) real.PreviewRowMode = BindingSourceModel.PreviewRowModeTopN;
        if (DataSourceIdentity.IsPostgreSql(real.SourceKind)) return ResolveRuntimeDataModeForExport(real);
        if (DataSourceIdentity.IsSqlServer(real.SourceKind) && string.IsNullOrEmpty(real.SourceConnectionString)) real.SourceConnectionString = ResolveSqlConnectionStringForExport(real);
        return DataGridRuntimeDataModeResolver.Resolve(real, includeDemoData: false);
    }

    public void ApplyStandardItems(DesignControlModel control, IReadOnlyList<StandardMenuItem> items)
    {
        if (!Controls.Contains(control)) return;
        var oldNames = StandardItemsProperties.Menu(control).Select(i => StandardItemsProperties.MenuSource(control, i).Name).ToHashSet();
        var newNames = items.Select(i => StandardItemsProperties.MenuSource(control, i).Name).ToHashSet();
        BeginUndoBatch();
        try
        {
            if (control.Type == DesignerControlTypes.ListBox) StandardItemsProperties.Write(control, "Items", items.Select(i => i.Text).ToArray());
            else StandardItemsProperties.Write(control, StandardItemsProperties.MenuKey, items.Select(i => i.Clone()).ToArray());
            foreach (var interaction in Interactions.Where(i => oldNames.Contains(i.SourceControlName) && !newNames.Contains(i.SourceControlName)).ToArray()) Interactions.Remove(interaction);
        }
        finally { CommitUndoBatch(); }
        NotifyDesignerStateChanged();
        RebuildPropertyGrid("StandardItemsChanged");
        OnPropertyChanged(nameof(InteractionSourceControlNames));
    }

    public void EditStandardMenuLogic(DesignControlModel owner, StandardMenuItem item)
    {
        var source = StandardItemsProperties.MenuSource(owner, item);
        var interaction = Interactions.FirstOrDefault(i => i.SourceControlName == source.Name && i.EventName == InteractionModel.EventButtonClick);
        if (interaction is null)
        {
            interaction = new InteractionModel { SourceControlName = source.Name, EventName = InteractionModel.EventButtonClick,
                ActionType = InteractionModel.ActionShowMessage, TextTemplate = item.Text };
            Interactions.Add(interaction);
        }
        SelectedInteraction = interaction;
        StatusText = "Действие пункта меню открыто в Logic: " + item.Text;
    }

    private IEnumerable<PropertyGridRowViewModel> StandardItemsRows(DesignControlModel control)
    {
        if (!StandardItemsProperties.IsStandardItemsControl(control.Type)) yield break;
        void Set<T>(string key, T value)
        {
            StandardItemsProperties.Write(control, key, value); NotifyDesignerStateChanged();
        }
        yield return CreateBoolRow(PropertyGridCategoryBehavior, "IsEnabled", "IsEnabled", StandardItemsProperties.Read(control, "IsEnabled", true),
            "Разрешает ввод и нажатия на настоящий Avalonia-контрол.", value => Set("IsEnabled", value));
        yield return CreateNumberRow(PropertyGridCategoryLayout, nameof(control.Padding), "Padding", control.Padding, "Внутренний отступ Avalonia-контрола.", value => control.Padding = Math.Max(0, value));
        if (control.Type == DesignerControlTypes.ListBox)
        {
            yield return CreateActionRow(PropertyGridCategoryData, "Items", "Items", "", "Статические элементы списка: добавить, переименовать, удалить и переставить.", "Изменить элементы…");
            yield return CreateBindingSourceRow(control, "Настоящие строки выбранного источника. Ошибки и пустые результаты не заменяются sample rows.");
            yield return CreateActionRow(PropertyGridCategoryData, "ListBox.Refresh", "Загрузить строки", "", "Повторно прочитать реальные строки источника.", "Обновить данные");
            yield return CreateTextRow(PropertyGridCategoryData, "ItemsSource", "ItemsSource", StandardItemsProperties.Read(control, "ItemsSource", ""),
                "Путь внешней Avalonia-привязки. Выбранный BindingSource имеет приоритет.", value => Set("ItemsSource", value));
            yield return CreateTextRow(PropertyGridCategoryData, "ItemTemplate.DisplayField", "ItemTemplate", StandardItemsProperties.Read(control, "ItemTemplate.DisplayField", ""),
                "Поле источника для Text-привязки в настоящем Avalonia DataTemplate.", value => Set("ItemTemplate.DisplayField", value));
            yield return CreateNumberRow(PropertyGridCategoryBehavior, "SelectedIndex", "SelectedIndex", StandardItemsProperties.Read(control, "SelectedIndex", -1),
                "Индекс выбранного элемента. -1 означает отсутствие выбора.", value => Set("SelectedIndex", Math.Max(-1, (int)value)));
            yield return CreateTextRow(PropertyGridCategoryBehavior, "SelectedItem", "SelectedItem", StandardItemsProperties.Read(control, "SelectedItem", ""),
                "Выбранная строка статического списка или выражение {Binding ...}.", value => Set("SelectedItem", value));
            yield return CreateEnumRow(PropertyGridCategoryBehavior, "SelectionMode", "SelectionMode", StandardItemsProperties.Read(control, "SelectionMode", "Single"),
                new[] { "Single", "Multiple", "Toggle", "AlwaysSelected", "Multiple, Toggle" }, "Режим выбора Avalonia SelectionMode.", value => Set("SelectionMode", value));
            foreach (var direction in new[] { "Horizontal", "Vertical" })
            {
                var key = "ScrollViewer." + direction + "ScrollBarVisibility";
                yield return CreateEnumRow(PropertyGridCategoryLayout, key, key, StandardItemsProperties.Read(control, key, direction == "Vertical" ? "Auto" : "Disabled"),
                    Enum.GetNames<ScrollBarVisibility>(), "Видимость полосы прокрутки ListBox.", value => Set(key, value));
            }
        }
        else
        {
            yield return CreateActionRow(PropertyGridCategoryData, StandardItemsProperties.MenuKey, "Flyout", "", "Пункты настоящего Flyout. Действия пунктов настраиваются в существующем Logic.", "Изменить меню…");
            yield return CreateEnumRow(PropertyGridCategoryBehavior, "Flyout.Placement", "Flyout.Placement", StandardItemsProperties.Read(control, "Flyout.Placement", "Bottom"),
                Enum.GetNames<PlacementMode>(), "Расположение Flyout относительно кнопки.", value => Set("Flyout.Placement", value));
        }
    }

    private void AppendStandardItemsXaml(StringBuilder sb, DesignControlModel control, int indent)
    {
        var type = control.Type;
        var name = GetExportControlName(control);
        var appearance = $"{PlacementAttributes(control)}{BackgroundAttribute(control)}{ForegroundAttribute(control)}{TextStyleAttributes(control)}{BorderStyleAttributes(control)} CornerRadius=\"{ToInvariant(control.CornerRadius)}\" Padding=\"{ToInvariant(control.Padding)}\"{CommonVisibilityAttributes(control)} IsEnabled=\"{BoolToXaml(StandardItemsProperties.Read(control, "IsEnabled", true))}\"";
        if (type == DesignerControlTypes.ListBox)
        {
            var source = GetBindingSource(control.BindingSourceId);
            var context = source is null ? null : GetCrudGenerationContext(source);
            var rawPath = StandardItemsProperties.Read(control, "ItemsSource", "");
            var binding = source is null ? rawPath : context?.ViewCollectionPropertyName ?? "";
            if (source is not null && ResolveItemsRuntimeMode(source).Mode == DataGridRuntimeDataMode.Demo) binding = "";
            var itemsSource = string.IsNullOrWhiteSpace(binding) ? "" : $" ItemsSource=\"{EscapeXml(binding.StartsWith('{') ? binding : "{Binding " + binding + "}")}\"";
            var selected = StandardItemsProperties.Read(control, "SelectedItem", "");
            if (string.IsNullOrEmpty(selected) && context is not null) selected = "{Binding " + context.SelectedItemPropertyName + ", Mode=TwoWay}";
            var selection = string.IsNullOrEmpty(selected) ? "" : " SelectedItem=\"" + EscapeXml(selected) + "\"";
            sb.AppendLine($"{Indent(indent)}<ListBox x:Name=\"{name}\" {appearance}{itemsSource}{selection} SelectedIndex=\"{StandardItemsProperties.Read(control, "SelectedIndex", -1)}\" SelectionMode=\"{EscapeXml(StandardItemsProperties.Read(control, "SelectionMode", "Single"))}\" ScrollViewer.HorizontalScrollBarVisibility=\"{EscapeXml(StandardItemsProperties.Read(control, "ScrollViewer.HorizontalScrollBarVisibility", "Disabled"))}\" ScrollViewer.VerticalScrollBarVisibility=\"{EscapeXml(StandardItemsProperties.Read(control, "ScrollViewer.VerticalScrollBarVisibility", "Auto"))}\">");
            if (source is null && string.IsNullOrWhiteSpace(rawPath))
                foreach (var item in StandardItemsProperties.Read(control, "Items", Array.Empty<string>())) sb.AppendLine($"{Indent(indent + 1)}<x:String>{EscapeXml(item)}</x:String>");
            var display = StandardItemsProperties.Read(control, "ItemTemplate.DisplayField", "");
            if (string.IsNullOrEmpty(display)) display = source?.Fields.FirstOrDefault(f => f.IsVisible && f.TypeName == "string")?.Path ?? source?.Fields.FirstOrDefault(f => f.IsVisible)?.Path ?? "";
            if (context is not null)
            {
                var field = context.Fields.FirstOrDefault(f => f.Path == display);
                if (field is not null) display = GetGeneratedRowPropertyName(field);
            }
            if (!string.IsNullOrEmpty(display))
                sb.AppendLine($"{Indent(indent + 1)}<ListBox.ItemTemplate><DataTemplate><TextBlock Text=\"{{Binding {EscapeXml(display)}}}\" /></DataTemplate></ListBox.ItemTemplate>");
            sb.AppendLine($"{Indent(indent)}</ListBox>");
            return;
        }
        var click = HasConfiguredInteractionEvent(control, InteractionModel.EventButtonClick) ? $" Click=\"{name}Click\"" : "";
        var text = string.IsNullOrEmpty(control.TextBindingPath) ? StandardContentLiteral(control.Text) : "{Binding " + EscapeXml(control.TextBindingPath) + "}";
        sb.AppendLine($"{Indent(indent)}<{type} x:Name=\"{name}\" Content=\"{text}\" {appearance}{click}>");
        sb.AppendLine($"{Indent(indent + 1)}<{type}.Flyout><Flyout Placement=\"{EscapeXml(StandardItemsProperties.Read(control, "Flyout.Placement", "Bottom"))}\"><StackPanel Spacing=\"3\">");
        foreach (var item in StandardItemsProperties.Menu(control))
        {
            var itemSource = StandardItemsProperties.MenuSource(control, item);
            var itemName = GetExportControlName(itemSource);
            sb.AppendLine($"{Indent(indent + 2)}<Button x:Name=\"{itemName}\" Content=\"{StandardContentLiteral(item.Text)}\" IsEnabled=\"{BoolToXaml(item.IsEnabled)}\" HorizontalAlignment=\"Stretch\" Click=\"{itemName}MenuClick\" />");
        }
        sb.AppendLine($"{Indent(indent + 1)}</StackPanel></Flyout></{type}.Flyout>");
        sb.AppendLine($"{Indent(indent)}</{type}>");
    }
}
