using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace EremexDesignerPlugin.Services;

public partial class EremexPreviewThemeResource : Styles
{
    public EremexPreviewThemeResource()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
