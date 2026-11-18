using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.DesignerSystem.AxamlRoundTrip;
using FormDesigner.DesignerSystem.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel
{
    private AxamlSyntaxDocument? _sourceTreeSyntax;
    private AxamlElementSyntax? _selectedSourceNode;
    [ObservableProperty] private bool showAxamlSourceTree;
    public bool IsSourceTreeVisible => ShowAxamlSourceTree && ActiveAxamlSourceDocument != null;
    public IReadOnlyList<AxamlElementSyntax> SourceExplorerRoots => IsSourceTreeVisible && _sourceTreeSyntax != null
        ? new[]{_sourceTreeSyntax.Root} : Array.Empty<AxamlElementSyntax>();

    partial void OnShowAxamlSourceTreeChanged(bool value) => RefreshSourceTree(true);

    private void RefreshSourceTree(bool force = false)
    {
        var syntax = ActiveAxamlSourceDocument?.Syntax;
        if (!force && ReferenceEquals(syntax,_sourceTreeSyntax)) return;
        _sourceTreeSyntax = syntax;
        if (ActiveAxamlSourceDocument is {} active && Registry is DesignerSystem.Infrastructure.DesignerRegistry registry)
            active.Recognition.PluginFiles = registry.GetPluginLoadReports().Where(p=>p.ControlCount>0).Select(p=>p.AssemblyPath).ToArray();
        _selectedSourceNode = null;
        OnPropertyChanged(nameof(SelectedSourceNode));
        OnPropertyChanged(nameof(IsSourceTreeVisible));
        OnPropertyChanged(nameof(SourceExplorerRoots));
        OnPropertyChanged(nameof(IsStructureTreeEmptyStateVisible));
        NotifySourceInspector();
    }

    public AxamlElementSyntax? SelectedSourceNode
    {
        get => _selectedSourceNode;
        set
        {
            if (ReferenceEquals(value,_selectedSourceNode)) return;
            var root = value;
            while (root?.Parent != null) root = root.Parent;
            if (value != null && (!IsSourceTreeVisible || root != ActiveAxamlSourceDocument?.Syntax.Root)) return;
            if (value != null)
            {
                var reference = ActiveAxamlSourceDocument!.SourceMap.Controls.FirstOrDefault(r=>r.Element == value);
                if (reference != null && GetControl(reference.ControlId) is {} mapped) SelectSingleControl(mapped);
                else ClearSelection();
            }
            _selectedSourceNode = value;
            OnPropertyChanged();
            NotifySourceInspector();
        }
    }

    private void NotifySourceInspector()
    {
        OnPropertyChanged(nameof(HasSourceInspection));
        OnPropertyChanged(nameof(SourceInspectionText));
    }

    public bool HasSourceInspection => IsSourceTreeVisible && _selectedSourceNode != null;
    public string SourceInspectionText
    {
        get
        {
            if (!HasSourceInspection || ActiveAxamlSourceDocument is not {} document || _selectedSourceNode is not {} node) return "";
            var capabilities = document.Recognition.Inspect(document,node,Registry);
            var identity = capabilities.Identity;
            var reference = document.SourceMap.Controls.FirstOrDefault(r=>r.Element==node);
            var lines = new List<string> { node.Name, "XML namespace · " + node.NamespaceUri };
            if (identity != null)
            {
                lines.Add(identity.FullName + " · " + identity.AssemblyName);
                if (identity.BaseType.Length != 0) lines.Add("Базовый тип · " + identity.BaseType);
            }
            else lines.Add("Тип или свойство не разрешены. Исходный XML сохранён.");
            if (node == document.SourceMap.CanvasElement)
                lines.Add("Корневой Canvas показан в Preview. Вставка дочернего узла · " +
                    (GetAxamlStructuralCapability(null).CanAddChild ? "да" : "нет"));
            else if (reference == null)
                lines.Add("Исходный узел доступен для просмотра. Отдельная визуальная проекция и структурные операции недоступны.");
            else
            {
                var edits = reference.EditableProperties.ToArray();
                var structural = GetAxamlStructuralCapability(reference.ControlId);
                lines.Add("Редактируемых свойств · " + edits.Length + ". Добавление дочернего узла · " + (structural.CanAddChild ? "да" : "нет"));
                lines.Add("Preview воспроизводит поддерживаемые свойства. Выражения и сложное содержимое имеют отдельные ограничения.");
            }
            lines.Add("Экспорт через дескриптор в JSON Designer · " + (capabilities.DescriptorExportable ? "доступен" : "отсутствует"));
            foreach(var attr in node.Attributes)
                lines.Add(attr.Name + " = " + document.OriginalText.Substring(attr.ValueSpan.Start,attr.ValueSpan.Length));
            if (identity != null) lines.Add("Метаданные · " + string.Join(", ",identity.Properties));
            lines.AddRange(document.Recognition.Diagnostics);
            return string.Join(Environment.NewLine,lines);
        }
    }

    [RelayCommand]
    private async Task InspectSourceAssemblyAsync()
    {
        var document = ActiveAxamlSourceDocument;
        if (document == null) return;
        var files = await _hostServices.FilePicker.OpenFilesAsync(new DesignerOpenFilePickerOptions(
            "Метаданные компонентов AXAML",new[]{new DesignerFileTypeFilter("Сборка .NET",new[]{"dll"})}));
        if (files.FirstOrDefault()?.LocalPath is not {} path || document != ActiveAxamlSourceDocument) return;
        try
        {
            document.Recognition.InspectAssembly(path);
            NotifySourceInspector();
            StatusText = "Прочитаны метаданные " + Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        { StatusText = "Ошибка чтения метаданных DLL. " + ex.Message; }
    }
}
