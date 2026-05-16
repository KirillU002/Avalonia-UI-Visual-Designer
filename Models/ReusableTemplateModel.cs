using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FormDesigner.Models;

/// <summary>
/// Переиспользуемый блок интерфейса: набор контролов, источников данных и их иерархии.
/// Встроенные шаблоны создаются кодом, пользовательские хранятся в JSON.
/// </summary>
public partial class ReusableTemplateModel : ObservableObject
{
    [ObservableProperty]
    private string id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = "Новый шаблон";

    [ObservableProperty]
    private string description = "";

    [ObservableProperty]
    private string category = "Пользовательские";

    [ObservableProperty]
    private bool isBuiltIn;

    [ObservableProperty]
    private double width = 320;

    [ObservableProperty]
    private double height = 180;

    [ObservableProperty]
    private DateTime createdUtc = DateTime.UtcNow;

    public List<DesignerControlFileModel> Controls { get; set; } = new();

    public List<BindingSourceFileModel> BindingSources { get; set; } = new();

    public List<InteractionFileModel> Interactions { get; set; } = new();

    [JsonIgnore]
    public bool CanEdit => !IsBuiltIn;

    [JsonIgnore]
    public string OriginText => IsBuiltIn ? "Встроенный" : "Пользовательский";

    [JsonIgnore]
    public int ControlCount => Controls.Count;

    [JsonIgnore]
    public int BindingSourceCount => BindingSources.Count;

    [JsonIgnore]
    public int InteractionCount => Interactions.Count;

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var controlsText = ControlCount switch
            {
                0 => "нет элементов",
                1 => "1 элемент",
                _ => $"{ControlCount} элементов"
            };
            var sourcesText = BindingSourceCount switch
            {
                0 => "без источников данных",
                1 => "1 источник данных",
                _ => $"{BindingSourceCount} источника данных"
            };

            var interactionsText = InteractionCount switch
            {
                0 => "без interactions",
                1 => "1 interaction",
                _ => $"{InteractionCount} interactions"
            };

            return $"{controlsText}, {sourcesText}, {interactionsText}";
        }
    }

    partial void OnIsBuiltInChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(OriginText));
    }
}
