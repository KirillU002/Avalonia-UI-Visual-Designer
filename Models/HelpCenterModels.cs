using System.Collections.Generic;

namespace FormDesigner.Models;

public sealed class HelpSection
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public string Subtitle { get; init; } = "";

    public string Icon { get; init; } = "";

    public IReadOnlyList<HelpArticle> Articles { get; init; } = new List<HelpArticle>();

    public IReadOnlyList<HelpFeatureCard> FeatureCards { get; init; } = new List<HelpFeatureCard>();

    public IReadOnlyList<HelpQuickAction> QuickActions { get; init; } = new List<HelpQuickAction>();
}

public sealed class HelpArticle
{
    public string Title { get; init; } = "";

    public string Body { get; init; } = "";

    public IReadOnlyList<string> Bullets { get; init; } = new List<string>();
}

public sealed class HelpCarouselSlide
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public string Subtitle { get; init; } = "";

    public string Description { get; init; } = "";

    public string Icon { get; init; } = "";

    public string AccentBrush { get; init; } = "#2563EB";

    public IReadOnlyList<string> Tags { get; init; } = new List<string>();
}

public sealed class HelpFeatureCard
{
    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string Icon { get; init; } = "";

    public string AccentBrush { get; init; } = "#2563EB";
}

public sealed class HelpQuickAction
{
    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string TargetSectionId { get; init; } = "";

    public string Icon { get; init; } = "";
}
