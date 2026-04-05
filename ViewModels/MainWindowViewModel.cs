using System.Collections.ObjectModel;
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


    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedControl is null)
            return;

        Controls.Remove(SelectedControl);
        SelectedControl = null;
        GenerateXaml();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    [RelayCommand]
    public void GenerateXaml()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"<Canvas Width=\"{DesignWidth}\" Height=\"{DesignHeight}\">");

        foreach (var c in Controls)
        {
            switch (c.Type)
            {
                case "Button":
                    sb.AppendLine(
                        $"  <Button Name=\"{c.Name}\" Content=\"{EscapeXml(c.Text)}\" Width=\"{c.Width}\" Height=\"{c.Height}\" Canvas.Left=\"{c.X}\" Canvas.Top=\"{c.Y}\" />");
                    break;

                case "TextBox":
                    sb.AppendLine(
                        $"  <TextBox Name=\"{c.Name}\" Text=\"{EscapeXml(c.Text)}\" Width=\"{c.Width}\" Height=\"{c.Height}\" Canvas.Left=\"{c.X}\" Canvas.Top=\"{c.Y}\" />");
                    break;

                case "TextBlock":
                    sb.AppendLine(
                        $"  <TextBlock Name=\"{c.Name}\" Text=\"{EscapeXml(c.Text)}\" Width=\"{c.Width}\" Height=\"{c.Height}\" Canvas.Left=\"{c.X}\" Canvas.Top=\"{c.Y}\" />");
                    break;

                case "CheckBox":
                    sb.AppendLine(
                        $"  <CheckBox Name=\"{c.Name}\" Content=\"{EscapeXml(c.Text)}\" Width=\"{c.Width}\" Height=\"{c.Height}\" Canvas.Left=\"{c.X}\" Canvas.Top=\"{c.Y}\" />");
                    break;

                case "Grid":
                    sb.AppendLine(
                        $"  <Grid Name=\"{c.Name}\" Width=\"{c.Width}\" Height=\"{c.Height}\" Canvas.Left=\"{c.X}\" Canvas.Top=\"{c.Y}\">");

                    sb.AppendLine("    <Grid.ColumnDefinitions>");
                    for (int i = 0; i < c.Columns; i++)
                        sb.AppendLine("      <ColumnDefinition Width=\"*\" />");
                    sb.AppendLine("    </Grid.ColumnDefinitions>");

                    sb.AppendLine("    <Grid.RowDefinitions>");
                    for (int i = 0; i < c.Rows; i++)
                        sb.AppendLine("      <RowDefinition Height=\"*\" />");
                    sb.AppendLine("    </Grid.RowDefinitions>");

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

            X = x,
            Y = y,
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

        GenerateXaml();
    }

    private void SelectedControl_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        GenerateXaml();
    }
}