using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FormDesigner.Models;
using FormDesigner.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

namespace FormDesigner.ViewModels;

public sealed partial class HelpWindowViewModel : ViewModelBase
{
    private readonly IReadOnlyList<HelpSection> _allSections;
    private readonly IReadOnlyList<HelpCarouselSlide> _allSlides;

    [ObservableProperty]
    private HelpSection? selectedSection;

    [ObservableProperty]
    private HelpCarouselSlide? selectedSlide;

    [ObservableProperty]
    private int selectedSlideIndex;

    [ObservableProperty]
    private string searchQuery = "";

    public HelpWindowViewModel()
        : this(new HelpContentService())
    {
    }

    public HelpWindowViewModel(HelpContentService contentService)
    {
        _allSections = contentService.CreateSections();
        _allSlides = contentService.CreateCarouselSlides();

        Sections = new ObservableCollection<HelpSection>(_allSections);
        CarouselSlides = new ObservableCollection<HelpCarouselSlide>(_allSlides);
        SelectedSection = Sections.FirstOrDefault();
        SelectedSlide = CarouselSlides.FirstOrDefault();
        SelectedSlideIndex = SelectedSlide is null ? -1 : 0;
    }

    public ObservableCollection<HelpSection> Sections { get; }

    public ObservableCollection<HelpCarouselSlide> CarouselSlides { get; }

    public string VersionText => "Alpha 3.0";

    public string ProductTitle => "Avalonia UI Visual Designer Help Center";

    public string ProductSubtitle => "Справочный центр Avalonia Designer";

    public string SearchSummary =>
        string.IsNullOrWhiteSpace(SearchQuery)
            ? "Все разделы справки"
            : $"Найдено разделов: {Sections.Count}";

    public string SlideCounter =>
        CarouselSlides.Count == 0 || SelectedSlideIndex < 0
            ? "0 / 0"
            : $"{SelectedSlideIndex + 1} / {CarouselSlides.Count}";

    public IReadOnlyList<HelpArticle> SelectedArticles =>
        SelectedSection?.Articles ?? Array.Empty<HelpArticle>();

    public IReadOnlyList<HelpFeatureCard> SelectedFeatureCards =>
        SelectedSection?.FeatureCards ?? Array.Empty<HelpFeatureCard>();

    public IReadOnlyList<HelpQuickAction> SelectedQuickActions =>
        SelectedSection?.QuickActions ?? Array.Empty<HelpQuickAction>();

    public bool HasQuickActions => SelectedQuickActions.Count > 0;

    partial void OnSelectedSectionChanged(HelpSection? value)
    {
        OnPropertyChanged(nameof(SelectedArticles));
        OnPropertyChanged(nameof(SelectedFeatureCards));
        OnPropertyChanged(nameof(SelectedQuickActions));
        OnPropertyChanged(nameof(HasQuickActions));
        if (value is not null)
            EmitDiagnostic("HELP_SECTION_SELECTED", $"sectionId={value.Id}; title={value.Title}");
    }

    partial void OnSelectedSlideChanged(HelpCarouselSlide? value)
    {
        SelectedSlideIndex = value is null ? -1 : CarouselSlides.IndexOf(value);
        OnPropertyChanged(nameof(SlideCounter));
        if (value is not null)
            EmitDiagnostic("HELP_CAROUSEL_SLIDE_CHANGED", $"slideIndex={SelectedSlideIndex}; title={value.Title}");
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplySearch(value);
    }

    [RelayCommand]
    private void SelectSection(HelpSection? section)
    {
        if (section is null)
            return;

        SelectedSection = section;
    }

    [RelayCommand]
    private void GoToSection(string? sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        var section = _allSections.FirstOrDefault(item => string.Equals(item.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (section is null)
            return;

        if (!Sections.Contains(section))
        {
            SearchQuery = "";
            ApplySearch("");
        }

        SelectedSection = section;
    }

    [RelayCommand]
    private void NextSlide()
    {
        if (CarouselSlides.Count == 0)
            return;

        var next = SelectedSlideIndex < 0 ? 0 : (SelectedSlideIndex + 1) % CarouselSlides.Count;
        SelectedSlide = CarouselSlides[next];
    }

    [RelayCommand]
    private void PreviousSlide()
    {
        if (CarouselSlides.Count == 0)
            return;

        var previous = SelectedSlideIndex < 0
            ? 0
            : (SelectedSlideIndex - 1 + CarouselSlides.Count) % CarouselSlides.Count;
        SelectedSlide = CarouselSlides[previous];
    }

    [RelayCommand]
    private void SelectSlide(HelpCarouselSlide? slide)
    {
        if (slide is null)
            return;

        SelectedSlide = slide;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = "";
    }

    private void ApplySearch(string? query)
    {
        var normalized = query?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(normalized)
            ? _allSections
            : _allSections.Where(section => Matches(section, normalized)).ToList();

        Sections.Clear();
        foreach (var section in filtered)
            Sections.Add(section);

        if (SelectedSection is null || !Sections.Contains(SelectedSection))
            SelectedSection = Sections.FirstOrDefault();

        OnPropertyChanged(nameof(SearchSummary));
        EmitDiagnostic("HELP_SEARCH_PERFORMED", $"query={normalized}; results={Sections.Count}");
    }

    private static bool Matches(HelpSection section, string query)
    {
        return Contains(section.Title, query)
            || Contains(section.Subtitle, query)
            || section.FeatureCards.Any(card => Contains(card.Title, query) || Contains(card.Description, query))
            || section.Articles.Any(article =>
                Contains(article.Title, query)
                || Contains(article.Body, query)
                || article.Bullets.Any(bullet => Contains(bullet, query)));
    }

    private static bool Contains(string value, string query) =>
        value?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void EmitDiagnostic(string eventName, string details)
    {
        Debug.WriteLine($"{eventName} {details}");
    }
}
