using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FormDesigner.Controls;
using FormDesigner.Services;
using FormDesigner.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace FormDesigner.Views;

public partial class DesignerPropertyInspector
{
    private sealed record CategoryView(Control Root, ObservableCollection<Control> Slots, IDataTemplate RowTemplate);
    private readonly ObservableCollection<Control> _categoryViews = new();
    private readonly Dictionary<string, CategoryView> _categoryByKey = new(StringComparer.Ordinal);
    private IDataTemplate? _categoryTemplate;
    private MainWindowViewModel? _presentationOwner;
    private string _presentationSession = "";
    private bool _presentationAttached;
    private bool _reusePresentation;
    private bool _synchronizing, _synchronizePending;
    private bool _nativeReadPending;

    private void InitializePresentation()
    {
        _categoryTemplate = PropertyGridCategoriesItemsControl.ItemTemplate;
        _reusePresentation = Environment.GetEnvironmentVariable("FORMDESIGNER_PROPERTYGRID_REUSE") != "0";
        if (!_reusePresentation)
        {
            var original = _categoryTemplate!;
            PropertyGridCategoriesItemsControl.ItemTemplate = new FuncDataTemplate<PropertyGridCategoryViewModel>((category, _) =>
            {
                var root = original.Build(category)!; root.DataContext = category;
                root.GetLogicalDescendants().OfType<ItemsControl>().First().ItemsSource = category!.Rows;
                return root;
            });
            return;
        }
        PropertyGridCategoriesItemsControl.ItemTemplate = null;
        PropertyGridCategoriesItemsControl.ClearValue(ItemsControl.ItemsSourceProperty);
        PropertyGridCategoriesItemsControl.ItemsSource = _categoryViews;
        if (Resources["ActiveEditorTemplates"] is PropertyGridEditorTemplates templates)
            templates.EnableRecycling();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // The host can measure the shared Inspector before showing a Window.
        // Project its initial Context then, but subscribe only while attached.
        if (change.Property == ContextProperty) AttachPresentationOwner();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _presentationAttached = true; AttachPresentationOwner();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _presentationAttached = false;
        if (_nativeReadPending) { LayoutUpdated -= ReadNativeValues; _nativeReadPending = false; }
        if (_presentationOwner is not null) _presentationOwner.PropertyChanged -= PresentationOwnerChanged;
        _presentationOwner = null; ClearPresentation();
        PropertyGridCategoriesItemsControl.ItemsSource = null;
        base.OnDetachedFromVisualTree(e);
    }
    private void AttachPresentationOwner()
    {
        if (_categoryTemplate is null) return;
        if (_presentationOwner is not null) _presentationOwner.PropertyChanged -= PresentationOwnerChanged;
        var nextOwner = Context as MainWindowViewModel;
        if (!ReferenceEquals(_presentationOwner, nextOwner)) ClearPresentation();
        _presentationOwner = nextOwner;
        if (_presentationAttached && _presentationOwner is not null) _presentationOwner.PropertyChanged += PresentationOwnerChanged;
        SynchronizePresentation();
    }
    private void PresentationOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Explicitly raised once after RebuildPropertyGrid has finished collecting every row.
        if (e.PropertyName == nameof(MainWindowViewModel.PropertyGridContextControlId)) SynchronizePresentation();
    }
    private void ClearPresentation()
    {
        // UIA/compositor can retain detached native visuals. Release this cache's
        // explicit Row/Category contexts before discarding its template instances.
        foreach (var view in _categoryByKey.Values)
        {
            ReleasePresentationContexts(view.Root);
            foreach (var slot in view.Slots.OfType<ContentControl>()) slot.Content = null;
            view.Slots.Clear(); view.Root.DataContext = null;
        }
        _categoryViews.Clear(); _categoryByKey.Clear(); _presentationSession = "";
    }
    private static void ReleasePresentationContexts(Control root)
    {
        var controls = root.GetVisualDescendants().OfType<Control>()
            .Concat(root.GetLogicalDescendants().OfType<Control>()).Prepend(root).Distinct().ToArray();
        foreach (var control in controls)
        {
            if (control.DataContext is PropertyGridRowViewModel or PropertyGridCategoryViewModel) control.DataContext = null;
            if (control is PropertyGridTextEditor text) text.Row = null;
            if (control is PropertyGridEnumEditor choice) choice.Row = null;
            if (control is PropertyGridCompoundEditor compound) compound.Row = null;
            if (control is PropertyGridGridLengthEditor lengths) lengths.Row = null;
        }
    }
    private void SynchronizePresentation()
    {
        if (_synchronizing) { _synchronizePending = true; return; }
        _synchronizing = true;
        try { do { _synchronizePending = false; SynchronizePresentationCore(); } while (_synchronizePending); }
        finally { _synchronizing = false; }
        if (!_nativeReadPending) { _nativeReadPending = true; LayoutUpdated += ReadNativeValues; }
    }
    private void ReadNativeValues(object? sender, EventArgs e)
    {
        LayoutUpdated -= ReadNativeValues; _nativeReadPending = false;
        NativeValuesRequested?.Invoke(this, EventArgs.Empty);
    }
    private void SynchronizePresentationCore()
    {
        using var stage = LayoutDropProfile.Stage("InspectorVisualProjection");
        if (_presentationOwner is not { } owner || _categoryTemplate is null) return;
        if (!_reusePresentation) { PropertyGridCategoriesItemsControl.ItemsSource = owner.PropertyGridCategories; return; }
        PropertyGridCategoriesItemsControl.ItemsSource = _categoryViews;
        var session = owner.DocumentSessionId + ":" + owner.ActiveDocumentId;
        if (_presentationSession != session) { ClearPresentation(); _presentationSession = session; }
        var categories = owner.PropertyGridCategories.ToArray();
        var keys = categories.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _categoryByKey.Keys.Where(k => !keys.Contains(k)).ToArray())
        { ReleasePresentationContexts(_categoryByKey[key].Root); _categoryViews.Remove(_categoryByKey[key].Root); _categoryByKey.Remove(key); }
        for (var i = 0; i < categories.Length; i++)
        {
            var category = categories[i];
            if (!_categoryByKey.TryGetValue(category.Key, out var view))
            {
                var root = _categoryTemplate.Build(category) ?? throw new InvalidOperationException("Missing Inspector category template");
                root.DataContext = category;
                var rows = root.GetLogicalDescendants().OfType<ItemsControl>().First();
                var template = new PropertyGridRecyclingTemplate(rows.ItemTemplate!);
                var slots = new ObservableCollection<Control>(); rows.ItemTemplate = null; rows.ItemsSource = slots;
                view = new(root, slots, template); _categoryByKey.Add(category.Key, view); _categoryViews.Insert(i, root);
            }
            else
            {
                var index = _categoryViews.IndexOf(view.Root);
                if (index != i) _categoryViews.Move(index, i);
                view.Root.DataContext = category;
            }
            for (var j = 0; j < category.Rows.Count; j++)
            {
                if (j < view.Slots.Count) ((ContentControl)view.Slots[j]).Content = category.Rows[j];
                else view.Slots.Add(new ContentControl { Content = category.Rows[j], ContentTemplate = view.RowTemplate,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch });
            }
            while (view.Slots.Count > category.Rows.Count)
            {
                var slot = (ContentControl)view.Slots[^1]; ReleasePresentationContexts(slot); slot.Content = null;
                view.Slots.RemoveAt(view.Slots.Count - 1);
            }
        }
    }
}
