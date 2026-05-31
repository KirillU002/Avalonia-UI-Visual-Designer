using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace FormDesigner.ViewModels;

public partial class LayoutDefinitionEditorItem : ObservableObject
{
    private readonly Action? _changed;
    private bool _isSyncing;

    public LayoutDefinitionEditorItem(int index, string sizeKind, double value, Action? changed)
    {
        _changed = changed;
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
        "Fixed" => Math.Max(1, Value).ToString("0.##", CultureInfo.InvariantCulture),
        _ => Math.Abs(Value - 1) < 0.001
            ? "*"
            : $"{Math.Max(1, Value).ToString("0.##", CultureInfo.InvariantCulture)}*"
    };

    public bool IsValueEditable => SizeKind is "Star" or "Fixed";

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
        NotifyChanged();
    }

    partial void OnValueChanged(double value)
    {
        var normalized = NormalizeValue(SizeKind, value);
        if (Math.Abs(value - normalized) > 0.001)
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

    private static double NormalizeValue(string kind, double value)
    {
        return kind == "Auto" ? 1 : Math.Max(1, value);
    }
}
