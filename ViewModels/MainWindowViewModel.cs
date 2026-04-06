using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.Models;

namespace FormDesigner.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<ToolboxItem> ToolboxItems { get; } = new()
    {
        new ToolboxItem { Title = "Button", Type = "Button" },
        new ToolboxItem { Title = "TextBox", Type = "TextBox" },
        new ToolboxItem { Title = "TextBlock", Type = "TextBlock" },
        new ToolboxItem { Title = "CheckBox", Type = "CheckBox" },
        new ToolboxItem { Title = "Grid", Type = "Grid" },
    };

    public ObservableCollection<DesignControlModel> Controls { get; } = new();

    [ObservableProperty]
    private DesignControlModel? selectedControl;

    [ObservableProperty]
    private string generatedXaml = "";

    [ObservableProperty]
    private double designWidth = 1000;

    [ObservableProperty]
    private double designHeight = 700;

    [ObservableProperty]
    private int snapStep = 10;

    public bool IsGridSelected => SelectedControl?.Type == "Grid";

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedControl is null)
            return;

        Controls.Remove(SelectedControl);
        SelectedControl = null;
        GenerateXaml();
        OnPropertyChanged(nameof(IsGridSelected));
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    public double Snap(double value)
    {
        if (SnapStep <= 1)
            return value;

        return Math.Round(value / SnapStep) * SnapStep;
    }

    public void ClampDesignSize()
    {
        if (DesignWidth < 300)
            DesignWidth = 300;

        if (DesignHeight < 200)
            DesignHeight = 200;
    }

    [RelayCommand]
    public void GenerateXaml()
    {
        var sb = new StringBuilder();

        sb.AppendLine(
            $"<Canvas Width=\"{DesignWidth.ToString(CultureInfo.InvariantCulture)}\" Height=\"{DesignHeight.ToString(CultureInfo.InvariantCulture)}\">");

        foreach (var c in Controls)
        {
            var x = c.X.ToString(CultureInfo.InvariantCulture);
            var y = c.Y.ToString(CultureInfo.InvariantCulture);
            var width = c.Width.ToString(CultureInfo.InvariantCulture);
            var height = c.Height.ToString(CultureInfo.InvariantCulture);

            switch (c.Type)
            {
                case "Button":
                    sb.AppendLine(
                        $"  <Button Name=\"{c.Name}\" Content=\"{EscapeXml(c.Text)}\" Width=\"{width}\" Height=\"{height}\" Canvas.Left=\"{x}\" Canvas.Top=\"{y}\" />");
                    break;

                case "TextBox":
                    sb.AppendLine(
                        $"  <TextBox Name=\"{c.Name}\" Text=\"{EscapeXml(c.Text)}\" Width=\"{width}\" Height=\"{height}\" Canvas.Left=\"{x}\" Canvas.Top=\"{y}\" />");
                    break;

                case "TextBlock":
                    sb.AppendLine(
                        $"  <TextBlock Name=\"{c.Name}\" Text=\"{EscapeXml(c.Text)}\" Width=\"{width}\" Height=\"{height}\" Canvas.Left=\"{x}\" Canvas.Top=\"{y}\" />");
                    break;

                case "CheckBox":
                    sb.AppendLine(
                        $"  <CheckBox Name=\"{c.Name}\" Content=\"{EscapeXml(c.Text)}\" Width=\"{width}\" Height=\"{height}\" Canvas.Left=\"{x}\" Canvas.Top=\"{y}\" />");
                    break;

                case "Grid":
                    sb.AppendLine(
                        $"  <Grid Name=\"{c.Name}\" Width=\"{width}\" Height=\"{height}\" Canvas.Left=\"{x}\" Canvas.Top=\"{y}\">");

                    sb.AppendLine("    <Grid.ColumnDefinitions>");
                    for (int i = 0; i < Math.Max(1, c.Columns); i++)
                        sb.AppendLine("      <ColumnDefinition Width=\"*\" />");
                    sb.AppendLine("    </Grid.ColumnDefinitions>");

                    sb.AppendLine("    <Grid.RowDefinitions>");
                    for (int i = 0; i < Math.Max(1, c.Rows); i++)
                        sb.AppendLine("      <RowDefinition Height=\"*\" />");
                    sb.AppendLine("    </Grid.RowDefinitions>");

                    if (c.ShowGridLines)
                    {
                        for (int row = 0; row < Math.Max(1, c.Rows); row++)
                        {
                            for (int col = 0; col < Math.Max(1, c.Columns); col++)
                            {
                                sb.AppendLine(
                                    $"    <Border Grid.Row=\"{row}\" Grid.Column=\"{col}\" BorderBrush=\"Black\" BorderThickness=\"1\" />");
                            }
                        }
                    }

                    sb.AppendLine("  </Grid>");
                    break;
            }
        }

        sb.AppendLine("</Canvas>");
        GeneratedXaml = sb.ToString();
    }

    public DesignControlModel CreateControl(string type, double x, double y)
    {
        int count = Controls.Count(c => c.Type == type) + 1;

        var model = new DesignControlModel
        {
            Type = type,
            Name = $"{type}{count}",
            Text = type switch
            {
                "Button" => "Кнопка",
                "TextBox" => "Введите текст",
                "TextBlock" => "Текст",
                "CheckBox" => "Флажок",
                "Grid" => "Grid",
                _ => type
            },
            Width = type switch
            {
                "TextBlock" => 100,
                "Grid" => 300,
                _ => 140
            },
            Height = type switch
            {
                "TextBlock" => 24,
                "Grid" => 180,
                _ => 36
            },
            Columns = type == "Grid" ? 3 : 1,
            Rows = type == "Grid" ? 3 : 1,
            ShowGridLines = true,
            X = Snap(x),
            Y = Snap(y),
        };

        AttachControl(model);
        Controls.Add(model);
        GenerateXaml();
        return model;
    }

    private void AttachControl(DesignControlModel model)
    {
        model.PropertyChanged += (_, __) => GenerateXaml();
    }

    private DesignControlModel? _trackedSelected;

    partial void OnSelectedControlChanged(DesignControlModel? value)
    {
        if (_trackedSelected is not null)
            _trackedSelected.PropertyChanged -= SelectedControl_PropertyChanged;

        _trackedSelected = value;

        if (_trackedSelected is not null)
            _trackedSelected.PropertyChanged += SelectedControl_PropertyChanged;

        OnPropertyChanged(nameof(IsGridSelected));
        GenerateXaml();
    }

    partial void OnDesignWidthChanged(double value)
    {
        if (value < 300)
            DesignWidth = 300;
    }

    partial void OnDesignHeightChanged(double value)
    {
        if (value < 200)
            DesignHeight = 200;
    }

    private void SelectedControl_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsGridSelected));
        GenerateXaml();
    }
}