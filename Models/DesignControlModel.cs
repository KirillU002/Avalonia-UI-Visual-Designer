using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FormDesigner.Models;

public partial class DesignControlModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string type = "";

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private double x;

    [ObservableProperty]
    private double y;

    [ObservableProperty]
    private double width = 140;

    [ObservableProperty]
    private double height = 36;

    [ObservableProperty]
    private int columns = 3;

    [ObservableProperty]
    private int rows = 3;

    [ObservableProperty]
    private bool showGridLines = true;

    partial void OnColumnsChanged(int value)
    {
        if (value < 1)
            Columns = 1;
    }

    partial void OnRowsChanged(int value)
    {
        if (value < 1)
            Rows = 1;
    }

    partial void OnWidthChanged(double value)
    {
        if (value < 40)
            Width = 40;
    }

    partial void OnHeightChanged(double value)
    {
        if (value < 24)
            Height = 24;
    }
}