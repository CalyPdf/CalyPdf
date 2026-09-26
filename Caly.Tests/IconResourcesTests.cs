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
    public void ToolbarIcon_IsDefined(string key)
    {
        var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));

        Assert.True(icons.TryGetResource(key, null, out var resource));
        Assert.IsAssignableFrom<Geometry>(resource);
    }
}
