using Avalonia;
using Caly.Core.Converters;

namespace Caly.Tests;

public class ProgressRingPlacementConvertersTests
{
    private static readonly ProgressRingSizeConverter SizeConverter = new();
    private static readonly ProgressRingMarginConverter MarginConverter = new();

    [Fact]
    public void Size_IsTenPercentOfSmallestVisibleAreaDimension()
    {
        object result = SizeConverter.Convert([new Rect(10, 20, 300, 400), new Size(1000, 1000)], typeof(double), null, null!);

        Assert.Equal(30d, Assert.IsType<double>(result));
    }

    [Fact]
    public void Size_IsClampedToMinimumOfFive()
    {
        object result = SizeConverter.Convert([new Rect(0, 0, 30, 40), new Size(1000, 1000)], typeof(double), null, null!);

        Assert.Equal(5d, Assert.IsType<double>(result));
    }

    [Fact]
    public void Size_FallsBackToPageSize_WhenVisibleAreaIsNull()
    {
        object result = SizeConverter.Convert([null, new Size(200, 100)], typeof(double), null, null!);

        Assert.Equal(10d, Assert.IsType<double>(result));
    }

    [Fact]
    public void Margin_CentersRingInVisibleArea()
    {
        // Area centre is (160, 220); ring diameter is 30, so the top-left corner is offset by 15.
        object result = MarginConverter.Convert([new Rect(10, 20, 300, 400), new Size(1000, 1000)], typeof(Thickness), null, null!);

        Assert.Equal(new Thickness(145, 205, 0, 0), Assert.IsType<Thickness>(result));
    }

    [Fact]
    public void Converters_TolerateUnsetValues()
    {
        object size = SizeConverter.Convert([AvaloniaProperty.UnsetValue, AvaloniaProperty.UnsetValue], typeof(double), null, null!);
        object margin = MarginConverter.Convert([AvaloniaProperty.UnsetValue, AvaloniaProperty.UnsetValue], typeof(Thickness), null, null!);

        Assert.Equal(5d, Assert.IsType<double>(size));
        Assert.IsType<Thickness>(margin);
    }
}
