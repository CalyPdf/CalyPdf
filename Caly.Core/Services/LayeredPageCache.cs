using System;
using System.Collections.Generic;
using UglyToad.PdfPig.Rendering.Skia;

namespace Caly.Core.Services;

/// <summary>
/// The most recently used layered pages of a document. Evicted pages are disposed.
/// </summary>
internal sealed class LayeredPageCache : IDisposable
{
    private readonly int _capacity;
    private readonly LinkedList<(int Page, SkiaLayeredPage Layered)> _order = new();
    private readonly Dictionary<int, LinkedListNode<(int Page, SkiaLayeredPage Layered)>> _nodes = new();
    private readonly object _lock = new();

    public LayeredPageCache(int capacity)
    {
        _capacity = capacity;
    }

    public SkiaLayeredPage? Get(int pageNumber)
    {
        lock (_lock)
        {
            if (!_nodes.TryGetValue(pageNumber, out var node))
            {
                return null;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Layered;
        }
    }

    public void Add(int pageNumber, SkiaLayeredPage page)
    {
        SkiaLayeredPage? replaced = null;
        SkiaLayeredPage? evicted = null;
        lock (_lock)
        {
            if (_nodes.TryGetValue(pageNumber, out var existing))
            {
                _order.Remove(existing);
                _nodes.Remove(pageNumber);
                replaced = existing.Value.Layered;
            }

            _nodes[pageNumber] = _order.AddFirst((pageNumber, page));

            if (_order.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _nodes.Remove(last.Value.Page);
                evicted = last.Value.Layered;
            }
        }

        if (!ReferenceEquals(replaced, page))
        {
            replaced?.Dispose();
        }

        evicted?.Dispose();
    }

    public void Clear()
    {
        List<SkiaLayeredPage> pages;
        lock (_lock)
        {
            pages = new List<SkiaLayeredPage>(_order.Count);
            foreach (var (_, layered) in _order)
            {
                pages.Add(layered);
            }

            _order.Clear();
            _nodes.Clear();
        }

        foreach (var page in pages)
        {
            page.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, layered) in _order)
            {
                layered.Dispose();
            }

            _order.Clear();
            _nodes.Clear();
        }
    }
}
