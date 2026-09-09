using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FormDesigner.Models;

public sealed class WorkspaceModel
{
    public string Version { get; set; } = "1.0";

    public string WorkspaceId { get; set; } = Guid.NewGuid().ToString("N");

    public DesignerProjectModel Project { get; set; } = new();

    public WorkspaceSessionModel Session { get; set; } = new();
}

public sealed class DesignerProjectModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Avalonia UI Project";

    public string RootPath { get; set; } = "";

    public string DefaultNamespace { get; set; } = "AvaloniaApplication1";

    public string TargetFramework { get; set; } = "net6.0";

    public string AvaloniaVersion { get; set; } = "11.1.1";

    public List<DesignerFormDocument> Forms { get; set; } = new();

    public List<DesignerDocumentModel> ViewModels { get; set; } = new();

    public List<DesignerAssetModel> Assets { get; set; } = new();

    public List<DesignerResourceModel> Resources { get; set; } = new();

    public List<DesignerExportProfileModel> ExportProfiles { get; set; } = new();

    public DesignerProjectSettingsModel Settings { get; set; } = new();
}

public class DesignerDocumentModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Document";

    public string Kind { get; set; } = "Document";

    public string RelativePath { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DesignerFormDocument : DesignerDocumentModel
{
    public DesignerFormDocument()
    {
        Kind = "Form";
    }

    public DesignerDocumentFileModel Document { get; set; } = new();

    public bool IsDirty { get; set; }

    public string CurrentSnapshot { get; set; } = "";

    public string SavedSnapshot { get; set; } = "";

    public List<string> UndoSnapshots { get; set; } = new();

    public List<string> RedoSnapshots { get; set; } = new();

    public double Zoom { get; set; } = 1.0;

    public double ViewportOffsetX { get; set; }

    public double ViewportOffsetY { get; set; }

    public string SelectedControlId { get; set; } = "";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Document.FormTitle : Name;

    public string TabTitle => IsDirty ? $"{DisplayName} *" : DisplayName;
}

public sealed class DesignerAssetModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "asset";

    public string RelativePath { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public string Kind { get; set; } = "Image";

    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayPath => string.IsNullOrWhiteSpace(RelativePath) ? SourcePath : RelativePath;
}

public sealed class DesignerResourceModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Resources.axaml";

    public string RelativePath { get; set; } = "Resources/Resources.axaml";

    public string Kind { get; set; } = "ResourceDictionary";

    public string Content { get; set; } = "";
}

public sealed class DesignerExportProfileModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Debug";

    public string Namespace { get; set; } = "AvaloniaApplication1";

    public string TargetFramework { get; set; } = "net6.0";

    public string DataGridExportMode { get; set; } = "Visual table without NuGet";

    public string LayoutExportMode { get; set; } = "Canvas layout";

    public bool IncludeDemoData { get; set; }

    public bool IncludePluginRuntime { get; set; }

    public string OutputFolder { get; set; } = "";
}

public sealed class DesignerProjectSettingsModel
{
    public string BuildProfile { get; set; } = "Debug";

    public string OutputFolder { get; set; } = "";

    public bool ReopenDocumentsOnStartup { get; set; } = true;
}

public sealed class WorkspaceSessionModel
{
    public List<string> OpenDocumentIds { get; set; } = new();

    public List<string> RecentlyClosedDocumentIds { get; set; } = new();

    public string ActiveDocumentId { get; set; } = "";

    public string SelectedProjectItemId { get; set; } = "";

    public string ActiveProjectExplorerTab { get; set; } = "Explorer";
}

public sealed class DesignerDocumentTabModel : INotifyPropertyChanged
{
    private bool _isActive;

    public DesignerDocumentTabModel(DesignerFormDocument document)
    {
        Document = document;
    }

    public DesignerFormDocument Document { get; }

    public string DocumentId => Document.Id;

    public string Title => Document.TabTitle;

    public string Kind => Document.Kind;

    public bool IsDirty => Document.IsDirty;

    public string Background => IsActive ? "#DBEAFE" : "#FFFFFF";

    public string BorderBrush => IsActive ? "#60A5FA" : "#CBD5E1";

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Background));
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsDirty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ProjectExplorerItemModel : INotifyPropertyChanged
{
    private bool _isActive;
    private bool _isSelected;
    private bool _isExpanded = true;
    private bool _isRenaming;
    private string _name = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ItemType { get; set; } = "Folder";

    public string TargetId { get; set; } = "";

    public string Icon { get; set; } = "Folder";

    public object? Source { get; set; }

    public string Description { get; set; } = "";

    public int Count { get; set; } = -1;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;
            _name = value;
            if (Source is DesignerFormDocument form)
            {
                form.Name = value;
                form.Document.FormTitle = value;
            }
            else if (Source is DesignerResourceModel resource)
            {
                resource.Name = value;
            }
            else if (Source is DesignerAssetModel asset)
            {
                asset.Name = value;
            }
            else if (Source is DesignerExportProfileModel profile)
            {
                profile.Name = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayName => Name;

    public string DisplayText
    {
        get
        {
            if (Source is DesignerFormDocument form)
                return form.IsDirty ? $"{DisplayName} *" : DisplayName;
            if (Count >= 0)
                return $"{DisplayName} ({Count})";
            return DisplayName;
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowBackground));
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowBackground));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
                return;
            _isRenaming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisplayNameVisible));
            OnPropertyChanged(nameof(IsRenameEditorVisible));
        }
    }

    public ObservableCollection<ProjectExplorerItemModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    public bool IsFolder => ItemType == "Folder" || ItemType == "Project";

    public bool IsEmptyPlaceholder => ItemType == "Empty";

    public bool CanOpen => Source is DesignerFormDocument || ItemType == "Export";

    public bool CanOpenForm => Source is DesignerFormDocument;

    public bool CanRename => Source is DesignerFormDocument or DesignerAssetModel;

    public bool CanDuplicate => Source is DesignerFormDocument;

    public bool CanDelete => Source is DesignerFormDocument or DesignerAssetModel;

    public bool CanAddForm => (ItemType is "Project" or "Folder") && (TargetId == "Forms" || TargetId == "Project");

    public bool CanAddAsset => (ItemType is "Project" or "Folder") && (TargetId == "Assets" || TargetId == "Project");

    public bool CanAddResource => (ItemType is "Project" or "Folder") && (TargetId == "Resources" || TargetId == "Project");

    public bool CanOpenProjectSettings => ItemType == "Project";

    public bool CanPreviewAsset => Source is DesignerAssetModel;

    public bool CanOpenExportPipeline => ItemType == "Export";

    public bool IsDisplayNameVisible => !IsRenaming;

    public bool IsRenameEditorVisible => IsRenaming;

    public string RowBackground => IsActive
        ? "#DBEAFE"
        : IsSelected
            ? "#E0F2FE"
            : IsEmptyPlaceholder
                ? "#00000000"
                : "Transparent";

    public string SecondaryText => !string.IsNullOrWhiteSpace(Description)
        ? Description
        : ItemType switch
    {
        "Form" when Source is DesignerFormDocument form => form.IsDirty ? "Modified form" : "Form",
        "Asset" when Source is DesignerAssetModel asset => asset.DisplayPath,
        "Resource" => "ResourceDictionary",
        "ExportProfile" => "Export profile",
        "Project" => "Designer project model",
        "Export" => "Export pipeline",
        "Empty" => "Empty",
        _ => ItemType
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
