using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Caly.Core.Controls;
using Caly.Core.Utilities;

namespace Caly.Tests
{
    public class DocumentTabViewTabIndexTests
    {
        [AvaloniaFact]
        public void EnsureTabIndexValuesAreCorrect()
        {
            // DocumentTabView references icon geometries via StaticResource, which the
            // test app does not load by default.
            var icons = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Caly.Core/Assets/Icons.axaml"));
            Application.Current!.Styles.Add(icons);

            string?[] tooltips;
            try
            {
                // No DataContext needed: the tab items are declared statically in XAML,
                // and the design-time DocumentViewModel ctor throws outside design mode.
                var docTabView = new DocumentTabView();

                var tabControl = docTabView.FindControl<TabControl>("PART_TabControlNavigation");
                Assert.NotNull(tabControl);

                // Tab items are identified by the tooltip on their header icon.
                tooltips = tabControl.Items
                    .Cast<TabItem>()
                    .Select(t => ToolTip.GetTip((Control)t.Header!) as string)
                    .ToArray();
            }
            finally
            {
                Application.Current.Styles.Remove(icons);
            }

            var expected = Enum.GetValues<LeftNavBarTabIndex>();
            Assert.Equal(expected.Length, tooltips.Length);

            foreach (var tabIndex in expected)
            {
                string? actualTooltip = tooltips[(int)tabIndex];
                Assert.NotNull(actualTooltip);

                actualTooltip = actualTooltip.Trim().Replace(" ", "");
                Assert.Equal(tabIndex.ToString(), actualTooltip);
            }
        }
    }
}
