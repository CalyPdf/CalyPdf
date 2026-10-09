using Caly.Core.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace Caly.Tests;

public class LayeredPageCacheTests
{
    private static SkiaLayeredPage[] OpenPages(int count)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Documents", "GWG151_OptionalContent-RBGroup_X4.pdf");
        using var document = PdfDocument.Open(path, SkiaRenderingParsingOptions.Instance);
        document.AddSkiaPageFactory();
        return Enumerable.Range(0, count).Select(_ => document.GetPage<SkiaLayeredPage>(1)).ToArray();
    }

    [Fact]
    public void Add_EvictsAndDisposesTheLeastRecentlyUsedPage()
    {
        var pages = OpenPages(3);
        using var cache = new LayeredPageCache(2);

        cache.Add(1, pages[0]);
        cache.Add(2, pages[1]);
        Assert.Same(pages[0], cache.Get(1));
        cache.Add(3, pages[2]);

        Assert.Null(cache.Get(2));
        Assert.NotNull(cache.Get(1));
        Assert.NotNull(cache.Get(3));
        Assert.Throws<ObjectDisposedException>(() => pages[1].Compose(null));
        pages[0].Compose(null).Dispose();
    }

    [Fact]
    public void Dispose_DisposesTheCachedPages()
    {
        var pages = OpenPages(1);
        var cache = new LayeredPageCache(2);
        cache.Add(1, pages[0]);

        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pages[0].Compose(null));
    }

    [Fact]
    public void Clear_DisposesAndEmptiesTheCache()
    {
        var pages = OpenPages(2);
        using var cache = new LayeredPageCache(2);
        cache.Add(1, pages[0]);
        cache.Add(2, pages[1]);

        cache.Clear();

        Assert.Null(cache.Get(1));
        Assert.Null(cache.Get(2));
        Assert.Throws<ObjectDisposedException>(() => pages[0].Compose(null));
        Assert.Throws<ObjectDisposedException>(() => pages[1].Compose(null));
    }
}
