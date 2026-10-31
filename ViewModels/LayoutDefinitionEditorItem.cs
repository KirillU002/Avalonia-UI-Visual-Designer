using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace FormDesigner.ViewModels;

public partial class LayoutDefinitionEditorItem : ObservableObject
{
    private readonly Action? _changed;
    private readonly bool _nativeSizes;
    private bool _isSyncing;

    public LayoutDefinitionEditorItem(int index, string sizeKind, double value, Action? changed)
        : this(index, sizeKind, value, changed, false) { }

    public LayoutDefinitionEditorItem(int index, string sizeKind, double value, Action? changed, bool nativeSizes)
    {
        _changed = changed;
        _nativeSizes = nativeSizes;
        Index = index;
        SizeKind = NormalizeKind(sizeKind);
        Value = NormalizeValue(SizeKind, value);
    }

    [ObservableProperty]
    private int index;

    [ObservableProperty]
    private string sizeKind = "Star";

    [ObservableProperty]
    private double value = 1;

    public string Preview => SizeKind switch
    {
        "Auto" => "Auto",
        "Fixed" => NormalizeValue(SizeKind, Value).ToString(_nativeSizes ? "R" : "0.##", CultureInfo.InvariantCulture),
        _ => (_nativeSizes ? Value == 1 : Math.Abs(Value - 1) < 0.001)
            ? "*"
            : $"{NormalizeValue(SizeKind, Value).ToString(_nativeSizes ? "R" : "0.##", CultureInfo.InvariantCulture)}*"
    };

    public bool IsValueEditable => SizeKind is "Star" or "Fixed";
    public string DisplaySizeKind { get => SizeKind == "Fixed" ? "Pixel" : SizeKind; set => SizeKind = value == "Pixel" ? "Fixed" : value; }

    public void Reset(int index, string sizeKind, double value)
    {
        _isSyncing = true;
        try
        {
            Index = index;
            SizeKind = NormalizeKind(sizeKind);
            Value = NormalizeValue(SizeKind, value);
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(IsValueEditable));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnSizeKindChanged(string value)
    {
        var normalized = NormalizeKind(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SizeKind = normalized;
            return;
        }

        Value = NormalizeValue(normalized, Value);
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(IsValueEditable));
        OnPropertyChanged(nameof(DisplaySizeKind));
        NotifyChanged();
    }

    partial void OnValueChanged(double value)
    {
        var normalized = NormalizeValue(SizeKind, value);
        if (_nativeSizes ? !value.Equals(normalized) : Math.Abs(value - normalized) > 0.001)
        {
            Value = normalized;
            return;
        }

        OnPropertyChanged(nameof(Preview));
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (!_isSyncing)
            _changed?.Invoke();
    }

    private static string NormalizeKind(string? value)
    {
        return value switch
        {
            "Auto" => "Auto",
            "Fixed" => "Fixed",
            _ => "Star"
        };
    }

    private double NormalizeValue(string kind, double value)
    {
        if (!double.IsFinite(value)) return 1;
        return kind == "Auto" ? 1 : Math.Max(_nativeSizes ? 0 : 1, value);
    }
}
