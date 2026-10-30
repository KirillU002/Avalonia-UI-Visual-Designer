using Avalonia.Controls;
using FormDesigner.DesignerSystem.BuiltIn;
using FormDesigner.DesignerSystem.Binding;
using FormDesigner.Models;
using System;
using System.Linq;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private readonly System.Threading.CancellationTokenSource _standardItemsLifetime = new();
    private void InitializeStandardItemsUi()
    {
        Closed += (_, _) => _standardItemsLifetime.Cancel();
        LogicSourceControlSelector.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
            new TextBlock { Text = DataContext is ViewModels.MainWindowViewModel vm ? vm.StandardInteractionSourceTitle(name ?? "") : name });
    }
    private readonly System.Collections.Generic.Dictionary<string, (string Signature, PreviewRowsLoadResult Result)> _standardListBoxRows = new();
    private readonly System.Collections.Generic.HashSet<string> _standardListBoxLoading = new();
    private Control CreateStandardItemsPreview(DesignControlModel model)
    {
        var snapshot = StandardItemsControlFactory.Snapshot(model);
        if (model.Type == DesignerControlTypes.ListBox && GetActiveBindingSource(model.BindingSourceId) is { } source)
        {
            // This view accepts actual cached rows only, irrespective of global demo switches.
            var signature = PreviewRowsLoader.BuildSignature(source);
            var key = DataSourceIdentity.BuildKey(source);
            if (!_standardListBoxRows.TryGetValue(key, out var cached) || cached.Signature != signature)
            {
                if (_standardListBoxLoading.Add(key))
                {
                    var session = VM.DocumentSessionId;
                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            var loaded = await StandardListBoxRowsLoader.LoadAsync(source, _standardItemsLifetime.Token);
                            _standardListBoxRows[key] = (signature, loaded);
                            if (session == VM.DocumentSessionId) RenderDesigner();
                        }
                        catch (System.OperationCanceledException) when (_standardItemsLifetime.IsCancellationRequested) { }
                        finally { _standardListBoxLoading.Remove(key); }
                    });
                }
            }
            var rows = cached.Result is { IsRealData: true } ? cached.Result.Rows : Array.Empty<System.Collections.Generic.Dictionary<string, string>>();
            if (string.IsNullOrEmpty(StandardItemsProperties.Read(model, "ItemTemplate.DisplayField", "")))
                snapshot.CustomProperties.Add(new() { Key = "ItemTemplate.DisplayField", ValueJson = System.Text.Json.JsonSerializer.Serialize(source.Fields.FirstOrDefault(f => f.IsVisible && f.TypeName == "string")?.Path ?? source.Fields.FirstOrDefault(f => f.IsVisible)?.Path ?? "") });
            var list = StandardItemsControlFactory.Create(snapshot, rows); list.IsHitTestVisible = false;
            if (cached.Result is not null) ToolTip.SetTip(list, cached.Result.Status);
            return list;
        }
        var result = StandardItemsControlFactory.Create(snapshot); result.IsHitTestVisible = false; return result;
    }

    private async void OpenStandardItemsEditor()
    {
        if (VM.SelectedControl is not { } control) return;
        if (VM.IsAxamlRoundTripDocument)
        {
            VM.StatusText = "Элементы импортированного AXAML редактируются в исходном файле. В Inspector доступны свойства самого контрола.";
            return;
        }
        var menu = StandardItemsProperties.IsMenuButton(control.Type);
        var items = menu ? StandardItemsProperties.Menu(control) : StandardItemsProperties.Read(control, "Items", Array.Empty<string>()).Select(text => new StandardMenuItem { Text = text }).ToList();
        var editor = new StandardItemsEditorWindow(items, menu, values => VM.ApplyStandardItems(control, values), item =>
        {
            VM.EditStandardMenuLogic(control, item); VM.WorkspaceMode = ViewModels.MainWindowViewModel.WorkspaceModeLogic;
        });
        await editor.ShowDialog(this);
    }
}
