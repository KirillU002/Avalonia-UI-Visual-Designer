using CommunityToolkit.Mvvm.ComponentModel;
using FormDesigner.PluginContracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FormDesigner.ViewModels;

public sealed class DescriptorPropertyEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;

    public DescriptorPropertyEditorViewModel(MainWindowViewModel owner, DesignPropertyDescriptor descriptor)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public DesignPropertyDescriptor Descriptor { get; }
    public string Key => Descriptor.Key;
    public string Title => Descriptor.Title;
    public string Category => Descriptor.Category;
    public PropertyEditorKind Editor => Descriptor.Editor;
    public IReadOnlyList<PropertyOption> Options => Descriptor.Options;

    public bool IsTextEditor => Editor is PropertyEditorKind.Text or PropertyEditorKind.Binding or PropertyEditorKind.Collection;
    public bool IsColorEditor => Editor == PropertyEditorKind.Color;
    public bool IsNumberEditor => Editor == PropertyEditorKind.Number;
    public bool IsBoolEditor => Editor == PropertyEditorKind.Bool;
    public bool IsEnumEditor => Editor == PropertyEditorKind.Enum;
    public bool HasOptions => Options.Count > 0;

    public string EditorHint => Editor switch
    {
        PropertyEditorKind.Color => "Можно ввести HEX вручную или выбрать цвет через палитру.",
        PropertyEditorKind.Number => "Числовое значение сохраняется в custom properties как число.",
        PropertyEditorKind.Bool => "Логический флаг из descriptor-схемы.",
        PropertyEditorKind.Enum => "Значение выбирается из вариантов, которые вернул descriptor.",
        PropertyEditorKind.Binding => "Descriptor пометил это свойство как binding-совместимое.",
        PropertyEditorKind.Collection => "Свойство хранится как сериализованная коллекция.",
        _ => "Свойство добавлено descriptor-ом выбранного контрола."
    };

    public string StringValue
    {
        get => _owner.GetDescriptorCustomPropertyString(Descriptor);
        set => _owner.SetDescriptorCustomPropertyFromString(Descriptor, value);
    }

    public bool BoolValue
    {
        get => _owner.GetDescriptorCustomPropertyBool(Descriptor);
        set => _owner.SetDescriptorCustomPropertyFromBool(Descriptor, value);
    }

    public PropertyOption? SelectedOption
    {
        get => Options.FirstOrDefault(option => string.Equals(option.Value, StringValue, StringComparison.Ordinal));
        set
        {
            if (value is null)
                return;

            _owner.SetDescriptorCustomPropertyFromString(Descriptor, value.Value);
        }
    }

    public string ColorPreviewValue => _owner.GetDescriptorCustomPropertyColorPreview(Descriptor);

    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(StringValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(ColorPreviewValue));
    }
}
