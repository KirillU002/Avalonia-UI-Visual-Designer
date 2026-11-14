using FormDesigner.DesignerSystem.Metadata;

namespace FormDesigner.ViewModels;

public enum PropertyGridValueSource { Model, RegistrationDefault, LocalValue, Style, Binding, Resource, Inherited, Complex, Unknown }

// Capability is distinct from discovery. Contains schema/values only, never a native control.
public sealed record PropertyGridPropertyMetadata(AvaloniaPropertyInfo Native, string DisplayName, string Category,
    bool Editable, bool Serializable, bool Importable, bool Exportable, string ValidationRules)
{
    public bool Discovered => true;
    public bool Displayable => true;
    public bool IsNativeReadOnly => Native.ReadOnly;
    public string Name => Native.Name;
    public System.Type OwnerType => Native.OwnerType;
    public System.Type PropertyType => Native.PropertyType;
    public object? DefaultValue => Native.DefaultValue;
    public bool IsReadOnly => !Editable;
    public bool IsNullable => Native.Nullable;
    public bool IsAttached => Native.Attached;
    public bool IsInherited => Native.Inherited;
    public bool InheritsValue => Native.InheritsValue;
    public bool IsBindable => Native.Bindable;
    public MetadataEditor EditorType => Native.Editor;
}
