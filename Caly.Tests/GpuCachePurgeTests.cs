using Caly.Core.Services.Rendering;

namespace Caly.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GpuCachePurgeCollection
{
    public const string Name = "GpuCachePurge static request flag";
}

// Runs alone: the request flag is process-wide, so a MainView rendering in another test could consume it.
[Collection(GpuCachePurgeCollection.Name)]
public class GpuCachePurgeTests
{
    private static void Drain()
    {
        while (GpuCachePurge.TryConsume())
        {
        }
    }

    [Fact]
    public void PurgeRequest_IsConsumedOnce()
    {
        Drain();

        GpuCachePurge.Request();

        Assert.True(GpuCachePurge.TryConsume());
        Assert.False(GpuCachePurge.TryConsume());
    }

    [Fact]
    public void PurgeRequests_BeforeAConsume_Coalesce()
    {
        Drain();

        GpuCachePurge.Request();
        GpuCachePurge.Request();

        Assert.True(GpuCachePurge.TryConsume());
        Assert.False(GpuCachePurge.TryConsume());
    }

    [Fact]
    public void Request_RaisesPurgeRequested_OnEveryCall()
    {
        Drain();
        int raised = 0;
        Action handler = () => raised++;
        GpuCachePurge.PurgeRequested += handler;
        try
        {
            GpuCachePurge.Request();
            GpuCachePurge.Request();
        }
        finally
        {
            GpuCachePurge.PurgeRequested -= handler;
            Drain();
        }

        Assert.Equal(2, raised);
    }

    [Fact]
    public void TryConsume_WithoutARequest_IsFalse()
    {
        Drain();

        Assert.False(GpuCachePurge.TryConsume());
    }
}
