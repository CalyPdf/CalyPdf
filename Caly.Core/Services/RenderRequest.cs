using Caly.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Caly.Core.Services;

/// <summary>
/// Kinds of render request, in the order they should be served. The numeric order is the priority
/// order.
/// </summary>
internal enum RenderRequestTypes : byte
{
    /// <summary>Page geometry. Cheap, and the layout cannot settle without it.</summary>
    PageSize = 0,

    /// <summary>The page the reader is looking at.</summary>
    Picture = 1,

    /// <summary>Text selection and search on that page.</summary>
    TextLayer = 2,

    /// <summary>Sidebar thumbnails. Useful, but nobody is waiting on them to read.</summary>
    Thumbnail = 3
}

/// <summary>
/// Orders queued render requests.
/// </summary>
internal static class RenderRequestPriority
{
    /// <summary>
    /// Compares two requests. Negative means the first is served first.
    /// </summary>
    public static int Compare(RenderRequestTypes xType, int xPage, RenderRequestTypes yType, int yPage)
    {
        int byType = xType.CompareTo(yType);
        return byType != 0 ? byType : xPage.CompareTo(yPage);
    }
}

internal sealed class RenderRequest : IEquatable<RenderRequest>
{
    public PageViewModel Page { get; }

    public RenderRequestTypes Type { get; }

    public CancellationToken Token { get; }

    public RenderRequest(PageViewModel page, RenderRequestTypes type, CancellationToken token)
    {
        Page = page;
        Type = type;
        Token = token;
    }

    public bool Equals(RenderRequest? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Page.Equals(other.Page) && Type == other.Type;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is RenderRequest other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Page, (byte)Type);
    }
}

internal sealed class RenderRequestComparer : IComparer<RenderRequest>
{
    public static readonly RenderRequestComparer Instance = new();

    public int Compare(RenderRequest? x, RenderRequest? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;

        return RenderRequestPriority.Compare(x.Type, x.Page.PageNumber, y.Type, y.Page.PageNumber);
    }
}
