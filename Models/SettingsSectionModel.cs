namespace FormDesigner.Models;

public sealed class SettingsSectionModel
{
    public SettingsSectionModel(string id, string title, string subtitle, string icon)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
    }

    public string Id { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Icon { get; }
}
