using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.Views;

public partial class PreviewWindow
{
    private readonly Dictionary<string, PreviewRowsLoadResult> _standardListBoxRows = new();
    private async System.Threading.Tasks.Task PreloadStandardListBoxesAsync(System.Threading.CancellationToken token)
    {
        foreach (var id in _document.Controls.Where(c => c.Type == DesignerControlTypes.ListBox && !string.IsNullOrEmpty(c.BindingSourceId)).Select(c => c.BindingSourceId).Distinct())
        {
            var source = _document.BindingSources.FirstOrDefault(s => s.Id == id);
            if (source is null) continue;
            var result = await StandardListBoxRowsLoader.LoadAsync(CreateRuntimeBindingSourceModel(source), token);
            _standardListBoxRows[id] = result;
            if (!result.IsRealData && result.DataKind != PreviewRowsLoader.DataModeSchemaOnly)
            {
                DatabaseErrorBanner.IsVisible = true; DatabaseErrorText.Text += result.Status + " ";
            }
        }
    }
    private IEnumerable? StandardListRows(DesignerControlFileModel control)
    {
        if (string.IsNullOrEmpty(control.BindingSourceId)) return null;
        return _standardListBoxRows.TryGetValue(control.BindingSourceId, out var result) && result.IsRealData
            ? BindingPreviewItemsBuilder.ConvertRows(result.Rows) : Array.Empty<object>();
    }
    private Control CreateStandardItemsPreview(DesignerControlFileModel control)
    {
        SetStandardListDisplayPath(control);
        return StandardItemsControlFactory.Create(control, StandardListRows(control),
            () => ExecuteStandardClick(control, control.Text), item =>
            {
                var virtualSource = StandardItemsProperties.MenuSource(CreateRuntimeControlModel(control), item);
                var file = StandardItemsControlFactory.Snapshot(virtualSource);
                ExecuteStandardClick(file, item.Text);
            });
    }
    private void SetStandardListDisplayPath(DesignerControlFileModel control)
    {
        if (control.Type != DesignerControlTypes.ListBox || string.IsNullOrEmpty(control.BindingSourceId) || !string.IsNullOrEmpty(StandardItemsProperties.Read(control, "ItemTemplate.DisplayField", ""))) return;
        var source = _document.BindingSources.FirstOrDefault(s => s.Id == control.BindingSourceId);
        var path = source?.Fields.FirstOrDefault(f => f.IsVisible && f.TypeName == "string")?.Path ?? source?.Fields.FirstOrDefault(f => f.IsVisible)?.Path ?? "";
        control.CustomProperties.RemoveAll(p => p.Key == "ItemTemplate.DisplayField");
        control.CustomProperties.Add(new() { Key = "ItemTemplate.DisplayField", ValueJson = System.Text.Json.JsonSerializer.Serialize(path) });
    }
    private void ExecuteStandardClick(DesignerControlFileModel source, string text, Action? refresh = null)
    {
        var changed = ApplyRuntimeInteractions(source, InteractionModel.EventButtonClick,
            new Dictionary<string, string> { ["Value"] = text, ["Content"] = text, ["Text"] = text });
        if (!changed) return;
        if (refresh is not null) refresh();
        else Dispatcher.UIThread.Post(ScheduleRenderDocument, DispatcherPriority.Background);
    }
    private void WireStandardItemsAxaml(Control root, ExportedAxamlPreviewModel preview)
    {
        var controls = root.GetLogicalDescendants().OfType<Control>().Prepend(root).Where(c => !string.IsNullOrEmpty(c.Name)).GroupBy(c => c.Name!).ToDictionary(g => g.Key, g => g.First());
        string Name(string id, string fallback) => preview.StandardControlNames.TryGetValue(id, out var name) ? name : fallback;
        void Refresh()
        {
            foreach (var model in _document.Controls)
            {
                if (!controls.TryGetValue(Name(model.Id, model.Name), out var actual)) continue;
                // Do not replace authored literals or bindings without a Logic override.
                if (_runtimeVisibilityByControlId.TryGetValue(model.Id, out var visible)) actual.IsVisible = visible;
                if (_runtimeEnabledByControlId.TryGetValue(model.Id, out var enabled)) actual.IsEnabled = enabled;
                if (actual is TextBlock text && _runtimeTextBlockValuesByControlId.TryGetValue(model.Id, out var label)) text.Text = label;
                if (actual is TextBox edit && _runtimeTextBoxValuesByControlId.TryGetValue(model.Id, out var value)) edit.Text = value;
                if (actual is ContentControl content && _runtimeButtonContentByControlId.TryGetValue(model.Id, out var caption)) content.Content = caption;
            }
        }
        foreach (var model in _document.Controls.Where(m => StandardItemsProperties.IsStandardItemsControl(m.Type) || m.Type == DesignerControlTypes.Button))
        {
            if (!controls.TryGetValue(Name(model.Id, model.Name), out var actual)) continue;
            if (model.Type == DesignerControlTypes.Button && actual is Button basic)
            {
                basic.Click += (_, _) => ExecuteStandardClick(model, model.Text, Refresh);
                continue;
            }
            if (actual is ListBox list)
            {
                SetStandardListDisplayPath(model);
                if (!string.IsNullOrEmpty(model.BindingSourceId))
                {
                    var configured = (ListBox)StandardItemsControlFactory.Create(model, StandardListRows(model));
                    list.ItemsSource = configured.ItemsSource; list.ItemTemplate = configured.ItemTemplate;
                }
                continue;
            }
            if (actual is SplitButton split) split.Click += (_, _) => ExecuteStandardClick(model, model.Text, Refresh);
            else if (actual is DropDownButton drop) drop.Click += (_, _) => ExecuteStandardClick(model, model.Text, Refresh);
            var flyout = actual is SplitButton s ? s.Flyout : (actual as DropDownButton)?.Flyout;
            if (flyout is not Flyout { Content: StackPanel stack }) continue;
            foreach (var item in StandardItemsProperties.Menu(model))
            {
                var virtualModel = StandardItemsProperties.MenuSource(CreateRuntimeControlModel(model), item);
                var button = stack.Children.OfType<Button>().FirstOrDefault(b => b.Name == Name(virtualModel.Id, virtualModel.Name));
                if (button is null) continue;
                button.Click += (_, _) => { flyout.Hide(); ExecuteStandardClick(StandardItemsControlFactory.Snapshot(virtualModel), item.Text, Refresh); };
            }
        }
        Refresh();
    }
}
