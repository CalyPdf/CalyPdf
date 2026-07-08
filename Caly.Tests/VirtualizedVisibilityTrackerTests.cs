using Caly.Core.Controls;

namespace Caly.Tests;

public class VirtualizedVisibilityTrackerTests
{
    /*
     * ToOneBasedRange converts the tracker's realized-index pair — 0-based first
     * index (GetFirstRealizedIndex) and 0-based exclusive end (GetLastRealizedIndex) —
     * to the 1-based, exclusive-end page-number Range bound to RealisedPages /
     * RealisedThumbnails. Realized 0-based indexes [f..l] are 1-based pages
     * [f+1..l+1], so the exclusive end is (l+1)+1 = lastExclusive + 1.
     */

    [Fact]
    public void MapsRealizedIndexesToOneBasedExclusiveRange()
    {
        // 0-based indexes 2..4 realized (exclusive end 5) => 1-based pages 3..5.
        Assert.Equal(new Range(3, 6), VirtualizedVisibilityTracker.GetPageRange(2, 5));
    }

    [Fact]
    public void SingleRealizedItem_CoversExactlyThatPage()
    {
        // Only index 0 realized (exclusive end 1) => page 1 only.
        Assert.Equal(new Range(1, 2), VirtualizedVisibilityTracker.GetPageRange(0, 1));
    }

    [Fact]
    public void AllItemsRealized_CoversExactlyTheItemCount()
    {
        // Indexes 0..9 realized in a 10-item list (exclusive end 10) => pages 1..10,
        // and crucially not a page 11 that does not exist.
        Assert.Equal(new Range(1, 11), VirtualizedVisibilityTracker.GetPageRange(0, 10));
    }

    [Fact]
    public void NothingRealized_ReturnsNull()
    {
        Assert.Null(VirtualizedVisibilityTracker.GetPageRange(-1, -1));
        Assert.Null(VirtualizedVisibilityTracker.GetPageRange(0, -1));
        Assert.Null(VirtualizedVisibilityTracker.GetPageRange(-1, 0));
    }
}
