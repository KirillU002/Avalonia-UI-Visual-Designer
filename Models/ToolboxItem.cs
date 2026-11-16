namespace FormDesigner.Models;

public class ToolboxItem
{
    public string Title { get; set; } = "";

    public string Type { get; set; } = "";

    public string Category { get; set; } = "General";

    public string Description { get; set; } = "";

    public string ProviderId { get; set; } = "";

    public string ProviderBadge { get; set; } = "";
    public string Icon => Type switch
    {
        "ContentControl"=>"Cc", "ItemsControl"=>"Ic", "Carousel"=>"↔", "TabStrip"=>"▤",
        "ScrollBar" => "↕", "GridSplitter" => "⋮", "Rectangle" => "▭", "Ellipse" => "○",
        "Viewbox"=>"⤢", "UniformGrid"=>"▦", "Expander"=>"⌄", "ScrollViewer"=>"↕", "Line"=>"╱", "Polygon"=>"△", "Polyline"=>"⌁", "Path"=>"∿",
        "HyperlinkButton" => "↗", "RepeatButton" => "↻", "Label"=>"Lb", "AccessText"=>"_A", "ToggleButton"=>"☑", "CalendarDatePicker"=>"▦", "MaskedTextBox"=>"#_", "ButtonSpinner"=>"↕", _ => ProviderBadge
    };
}
