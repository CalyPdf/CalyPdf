using System.Globalization;
using Caly.Core.Converters;
using Caly.Core.Models;

namespace Caly.Tests;

/// <summary>
/// The names the Settings "Default zoom" box shows. <see cref="ZoomMode.Manual"/> is how a
/// document opens when no fit is chosen, i.e. at 100%.
/// </summary>
public class ZoomModeDisplayNameConverterTests
{
    [Theory]
    [InlineData(ZoomMode.Manual, "Actual size (100%)")]
    [InlineData(ZoomMode.FitPage, "Fit page")]
    [InlineData(ZoomMode.FitWidth, "Fit width")]
    [InlineData(ZoomMode.FitContent, "Fit content")]
    [InlineData(ZoomMode.FitContentWidth, "Fit content width")]
    public void EachMode_HasItsName(ZoomMode mode, string expected)
    {
        object? name = ZoomModeDisplayNameConverter.Instance.Convert(mode, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, name);
    }
}
