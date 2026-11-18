using Avalonia.Controls;
using Avalonia.Interactivity;
using FormDesigner.Models;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.Views;

public partial class MainWindow
{
    private MainWindowViewModel? _templateDraft;
    private DesignControlModel? _templateOwner;
    private string _templateDocument = "";
    private string _templateRevision = "";
    private readonly List<(Control View, Panel Parent, int Index, Border Slot)> _templateViews = new();

    internal void EditSelectedItemTemplate()
    {
        if (_templateDraft != null || VM.SelectedControl is not {} owner) return;
        var form = VM;
        try
        {
            if (form.HasImportedStyleScope)
            {
                OpenImportedItemTemplate(form, owner);
                return;
            }
            CancelPendingContextMenu();
            _nativeDropVisualChange = null;
            _templateDraft = form.OpenItemTemplate(owner);
            _templateOwner = owner;
            _templateDocument = form.ActiveDocumentId;
            _templateRevision = form.AppearanceRevision;
            TemplateOwnerLabel.Text = owner.Name + " · ItemTemplate";
            TemplateItemsPathInput.Text = _templateDraft.TemplateItemsPath;
            TemplateWorkspace.DataContext = _templateDraft;
            TemplateMessage.Text = "Binding без явного источника получает текущий элемент коллекции.";
            MoveToTemplate(DesignerToolbox, TemplateToolboxSlot);
            MoveToTemplate(TemplateStructureTree, TemplateExplorerSlot);
            MoveToTemplate(DesignerPropertyInspector, TemplateInspectorSlot);
            MoveToTemplate(DesignerSurface, TemplateSurfaceSlot);
            _templateDraft.DesignerChanged += ViewModel_DesignerChanged;
            FormWorkspace.IsVisible = false;
            TemplateWorkspace.IsVisible = true;
            RenderDesigner();
        }
        catch (Exception ex)
        {
            if (_templateDraft != null) LeaveTemplate();
            form.StatusText = ex.Message;
        }
    }

    private async void OpenImportedItemTemplate(MainWindowViewModel form, DesignControlModel owner)
    {
        var doc = form.ActiveDocumentId;
        var fields = form.StyleFields.Where(f => f.TemplateOwner == owner.Name).ToArray();
        if (fields.Length == 0) { form.StatusText = "Для этого ItemTemplate отсутствуют однозначно сопоставленные редактируемые значения."; return; }
        var list = new ListBox { ItemsSource = fields.Select(f => f.Label).ToArray() };
        var input = new TextBox();
        var message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        list.SelectionChanged += (_,_) => { if (list.SelectedIndex >= 0) input.Text = fields[list.SelectedIndex].Value; };
        list.SelectedIndex = 0;
        var reset = new Button { Content = "Восстановить исходное значение" };
        reset.Click += (_,_) => { if (list.SelectedIndex >= 0) input.Text = form.OriginalTemplateFieldValue(fields[list.SelectedIndex]); };
        var apply = new Button { Content = "Применить к AXAML" };
        var dialog = new Window { Title = owner.Name + " · ItemTemplate", Width = 520, Height = 440,
            Content = new ScrollViewer { Content = new StackPanel { Spacing = 8, Children = { list,input,reset,apply,message } } } };
        apply.Click += (_,_) =>
        {
            try { form.SaveSourceStyleValue(doc,fields[list.SelectedIndex],input.Text ?? ""); dialog.Close(); }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        await dialog.ShowDialog(this);
    }

    private void MoveToTemplate(Control view, Border slot)
    {
        var parent = (Panel)view.Parent!;
        _templateViews.Add((view,parent,parent.Children.IndexOf(view),slot));
        parent.Children.Remove(view);
        slot.Child = view;
    }

    private void LeaveTemplate()
    {
        CancelPendingContextMenu();
        _nativeDropVisualChange = null;
        var draft = _templateDraft!;
        draft.DesignerChanged -= ViewModel_DesignerChanged;
        _templateDraft = null;
        _templateOwner = null;
        TemplateWorkspace.IsVisible = false;
        foreach (var (view, parent, index, slot) in _templateViews)
        {
            slot.Child = null;
            parent.Children.Insert(index,view);
        }
        _templateViews.Clear();
        TemplateWorkspace.DataContext = null;
        FormWorkspace.IsVisible = true;
        draft.CloseItemTemplate();
        RenderDesigner();
    }

    private void CommitTemplate_Click(object? sender, RoutedEventArgs e) => CommitTemplate(false);
    private void DeleteTemplate_Click(object? sender, RoutedEventArgs e) => CommitTemplate(true);
    private void CancelTemplate_Click(object? sender, RoutedEventArgs e) => LeaveTemplate();
    private void CommitTemplate(bool delete)
    {
        try
        {
            var form = (MainWindowViewModel)DataContext!;
            if (form.AppearanceRevision != _templateRevision)
                throw new InvalidOperationException("Форма изменилась во время редактирования. Откройте шаблон повторно.");
            _templateDraft!.TemplateItemsPath = TemplateItemsPathInput.Text?.Trim() ?? "";
            form.SaveItemTemplate(_templateDocument, _templateOwner!, delete ? null : _templateDraft);
            LeaveTemplate();
        }
        catch (Exception ex) { TemplateMessage.Text = ex.Message; }
    }

    private async void PreviewTemplate_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var draft = _templateDraft!;
            var doc = draft.CreatePreviewDocumentSnapshot();
            var template = new ItemTemplateDesign { Controls = doc.Controls, Columns = doc.SurfaceGridColumnDefinitions, Rows = doc.SurfaceGridRowDefinitions };
            var items = new ItemsControl();
            MainWindowViewModel.ApplyNativeItemTemplate(items, "ItemsControl", template, draft.Registry, doc.Appearance);
            var count = new ComboBox { ItemsSource = new[] { 0, 1, 3 }, SelectedIndex = 2 };
            var theme = new ComboBox { ItemsSource = new[] { Avalonia.Styling.ThemeVariant.Light,Avalonia.Styling.ThemeVariant.Dark }, SelectedIndex = 0 };
            var preview = new Window { Title = "Предпросмотр ItemTemplate", Width = 520, Height = 380,
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
            theme.SelectionChanged += (_,_) => preview.RequestedThemeVariant = (Avalonia.Styling.ThemeVariant?)theme.SelectedItem;
            var thumbnail = new Avalonia.Media.DrawingImage { Drawing = new Avalonia.Media.GeometryDrawing {
                Brush = Avalonia.Media.Brushes.RoyalBlue, Geometry = new Avalonia.Media.RectangleGeometry(new Avalonia.Rect(0,0,16,16)) } };
            void SetItems() => items.ItemsSource = System.Linq.Enumerable.Range(1,(int)(count.SelectedItem ?? 0))
                .Select(i => new TemplatePreviewItem(i == 3 ? "Пример длинного названия элемента коллекции" : "Элемент " + i, i % 2 != 0, thumbnail)).ToArray();
            count.SelectionChanged += (_, _) => SetItems();
            SetItems();
            var panel = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Примеры данных · Title, IsChecked, Thumbnail" }, count, theme, items } };
            preview.Content = new ScrollViewer { Content = panel };
            await preview.ShowDialog(this);
        }
        catch (Exception ex) { TemplateMessage.Text = ex.Message; }
    }

    private sealed record TemplatePreviewItem(string Title, bool IsChecked,Avalonia.Media.IImage Thumbnail);
}
