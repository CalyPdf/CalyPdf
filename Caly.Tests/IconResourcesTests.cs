using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace Caly.Tests;

public class IconResourcesTests
{
    [AvaloniaTheory]
    [InlineData("document_regular")] // Single-page view toggle
    [InlineData("copy_regular")] // Continuous scrolling toggle (placeholder)
    [InlineData("full_screen_zoom_regular")] // Zoom fit drop-down, no fit mode
    [InlineData("mobile_optimized_regular")] // Fit page
    [InlineData("arrow_autofit_width_regular")] // Fit width
    [InlineData("page_fit_regular")] // Fit content
    [InlineData("autofit_content_regular")] // Fit content width
    public void ToolbarIcon_IsDefined(string key)
    {
        var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));

        Assert.True(icons.TryGetResource(key, null, out var resource));
        Assert.IsAssignableFrom<Geometry>(resource);
    }
}
