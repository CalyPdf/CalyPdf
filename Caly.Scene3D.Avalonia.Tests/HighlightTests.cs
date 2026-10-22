using Caly.Scene3D.Avalonia.Internal;

namespace Caly.Scene3D.Avalonia.Tests;

public class HighlightTests
{
    [Theory]
    [InlineData(0f, 1f)]          // never below 1: a broad sheen, not a flat wash
    [InlineData(0.02f, 1f)]
    [InlineData(0.08f, 2.56f)]    // the Saw's handle: its poster's highlight fits an exponent of 2 to 3
    [InlineData(0.5f, 16f)]
    [InlineData(1f, 32f)]
    [InlineData(4f, 32f)]
    [InlineData(-1f, 1f)]
    [InlineData(float.NaN, 1f)]
    public void TheExponentIsAQuarterOfOpenGlsRange(float reflectivity, float exponent)
    {
        Assert.Equal(exponent, Highlight.Exponent(reflectivity), 4);
    }
}
