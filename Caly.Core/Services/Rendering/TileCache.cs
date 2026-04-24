// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Threading;
using Caly.Core.Utilities;
using SkiaSharp;

namespace Caly.Core.Services.Rendering;

/// <summary>
/// Thread-safe LRU tile cache with a configurable memory budget.
/// Tiles are stored as ref-counted <see cref="SKImage"/> instances.
/// </summary>
public sealed class TileCache : IDisposable
{
    private sealed class CacheEntry
    {
        public IRef<SKImage> Image { get; }

        public TileKey Key { get; }

        public int MemorySize { get; }

        public LinkedListNode<TileKey>? LruNode { get; set; }

        public CacheEntry(IRef<SKImage> image, TileKey key, int memorySize)
        {
            Image = image;
            Key = key;
            MemorySize = memorySize;
        }
    }

    private readonly Dictionary<TileKey, CacheEntry> _entries = new();
    private readonly LinkedList<TileKey> _lruList = new();
    private readonly Lock _lock = new();
    private readonly long _maxMemoryBytes;

    /// <summary>
    /// Secondary index: page number → set of tile keys for that page.
    /// Allows O(page tiles) invalidation instead of O(all tiles).
    /// </summary>
    private readonly Dictionary<int, HashSet<TileKey>> _pageKeys = new();

    /// <summary>
    /// Secondary index: page number → set of cached tile levels.
    /// Allows O(1) lookup in <see cref="GetCachedLevelsAbove"/> instead of O(N) scan.
    /// </summary>
    private readonly Dictionary<int, SortedSet<int>> _pageLevels = new();

    private long _currentMemoryBytes;

    /// <summary>
    /// Creates a new tile cache with the specified memory budget.
    /// </summary>
    /// <param name="maxMemoryBytes">Maximum memory budget in bytes. Default is 256 MB.</param>
    public TileCache(long maxMemoryBytes = 256L * 1024 * 1024)
    {
        _maxMemoryBytes = maxMemoryBytes;
    }

    /// <summary>
    /// Tries to get a tile from the cache, moving it to the front of the LRU list.
    /// Returns a cloned reference that the caller must dispose.
    /// </summary>
    public bool TryGet(in TileKey key, out IRef<SKImage>? imageRef)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.LruNode is not null)
                {
                    _lruList.Remove(entry.LruNode);
                    _lruList.AddFirst(entry.LruNode);
                }

                imageRef = entry.Image.Clone();
                return true;
            }
        }

        imageRef = null;
        return false;
    }

    /// <summary>
    /// Adds a tile image to the cache. If adding exceeds the memory budget,
    /// LRU tiles are evicted. If the key already exists, the call is ignored.
    /// The cache takes ownership of the image.
    /// </summary>
    /// <param name="key">The tile key.</param>
    /// <param name="image">The SKImage to cache. The cache takes ownership.</param>
    public void Add(in TileKey key, SKImage image)
    {
        int memorySize = image.Info.BytesSize;
        IRef<SKImage> imageRef = RefCountable.Create(image);
        List<CacheEntry>? evicted = null;

        lock (_lock)
        {
            if (_entries.ContainsKey(key))
            {
                // Already in cache, dispose the new ref
                imageRef.Dispose();
                return;
            }

            // Reject tiles that exceed the entire budget — adding them would evict
            // everything and still blow past the limit.
            if (memorySize > _maxMemoryBytes)
            {
                imageRef.Dispose();
                return;
            }

            // Evict until under budget, collecting entries to dispose outside the lock
            while (_currentMemoryBytes + memorySize > _maxMemoryBytes && _lruList.Count > 0)
            {
                var evictedEntry = EvictOldestLocked();
                if (evictedEntry is not null)
                {
                    (evicted ??= []).Add(evictedEntry);
                }
            }

            var entry = new CacheEntry(imageRef, key, memorySize);
            var node = _lruList.AddFirst(key);
            entry.LruNode = node;

            _entries[key] = entry;
            _currentMemoryBytes += memorySize;

            // Update secondary indexes
            if (!_pageKeys.TryGetValue(key.PageNumber, out var keys))
            {
                keys = new HashSet<TileKey>();
                _pageKeys[key.PageNumber] = keys;
            }
            keys.Add(key);

            if (!_pageLevels.TryGetValue(key.PageNumber, out var levels))
            {
                levels = new SortedSet<int>();
                _pageLevels[key.PageNumber] = levels;
            }
            levels.Add(key.TileLevel);
        }

        // Dispose evicted entries outside the lock
        if (evicted is not null)
        {
            foreach (var entry in evicted)
            {
                entry.Image.Dispose();
            }
        }
    }

    /// <summary>
    /// Removes all tiles for a given page from the cache.
    /// </summary>
    public void InvalidatePage(int pageNumber)
    {
        List<CacheEntry>? toDispose = null;

        lock (_lock)
        {
            if (!_pageKeys.TryGetValue(pageNumber, out var keys))
            {
                return;
            }

            toDispose = new List<CacheEntry>(keys.Count);
            foreach (var key in keys)
            {
                if (_entries.TryGetValue(key, out var entry))
                {
                    toDispose.Add(entry);
                    RemoveEntryFromPrimaryLocked(entry);
                }
            }

            // Clear secondary indexes for this page
            keys.Clear();
            _pageKeys.Remove(pageNumber);
            _pageLevels.Remove(pageNumber);
        }

        // Dispose outside the lock
        foreach (var entry in toDispose)
        {
            entry.Image.Dispose();
        }
    }

    /// <summary>
    /// Removes all tiles for a given page whose tile level differs from <paramref name="keepLevel"/>.
    /// This prevents stale high-res (or low-res) tiles from consuming budget after a zoom change.
    /// </summary>
    public void EvictPageLevelsExcept(int pageNumber, int keepLevel)
    {
        List<CacheEntry>? toDispose = null;

        lock (_lock)
        {
            if (!_pageKeys.TryGetValue(pageNumber, out var keys))
            {
                return;
            }

            List<TileKey>? keysToRemove = null;
            foreach (var key in keys)
            {
                if (key.TileLevel != keepLevel)
                {
                    (keysToRemove ??= []).Add(key);
                }
            }

            if (keysToRemove is null)
            {
                return;
            }

            toDispose = new List<CacheEntry>(keysToRemove.Count);
            foreach (var key in keysToRemove)
            {
                if (_entries.TryGetValue(key, out var entry))
                {
                    toDispose.Add(entry);
                    RemoveEntryFromPrimaryLocked(entry);
                    keys.Remove(key);
                }
            }

            // Every surviving key is at keepLevel (we only removed others), so the level
            // index collapses to {keepLevel} without re-scanning keys.
            if (_pageLevels.TryGetValue(pageNumber, out var levels))
            {
                levels.Clear();
                if (keys.Count > 0)
                {
                    levels.Add(keepLevel);
                }
                else
                {
                    _pageLevels.Remove(pageNumber);
                }
            }

            if (keys.Count == 0)
            {
                _pageKeys.Remove(pageNumber);
            }
        }

        // Dispose outside the lock
        foreach (var entry in toDispose)
        {
            entry.Image.Dispose();
        }
    }

    /// <summary>
    /// Checks whether a tile exists in the cache without modifying LRU order.
    /// </summary>
    public bool Contains(in TileKey key)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(key);
        }
    }

    /// <summary>
    /// Finds tiles within the given grid range that are not in the cache,
    /// acquiring the lock only once for the entire batch.
    /// </summary>
    public void FindMissing(int pageNumber, int tileLevel, int startCol, int startRow, int endCol, int endRow, List<TileCoord> missing)
    {
        lock (_lock)
        {
            for (int r = startRow; r <= endRow; r++)
            {
                for (int c = startCol; c <= endCol; c++)
                {
                    var key = new TileKey(pageNumber, tileLevel, c, r);
                    if (!_entries.ContainsKey(key))
                    {
                        missing.Add(new TileCoord(c, r));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Looks up every tile in the given grid range under a single lock acquisition. Hits are
    /// returned as fresh ref-counted clones in <paramref name="outRefs"/> (row-major order); misses
    /// leave the corresponding slot as <see langword="null"/>. The caller owns the returned
    /// references and must dispose each non-null entry.
    /// </summary>
    /// <remarks>
    /// <see cref="TiledPdfPageControl.Render"/> calls this once per frame instead of calling
    /// <see cref="TryGet"/> per tile. Rendering otherwise acquires the cache lock dozens of times
    /// per frame while the background renderer is concurrently acquiring it to add completed
    /// tiles — batching eliminates that contention on the UI thread.
    /// </remarks>
    public void TryGetRange(int pageNumber, int tileLevel, int startCol, int startRow, int endCol, int endRow,
        Span<IRef<SKImage>?> outRefs)
    {
        lock (_lock)
        {
            int idx = 0;
            for (int r = startRow; r <= endRow; r++)
            {
                for (int c = startCol; c <= endCol; c++)
                {
                    var key = new TileKey(pageNumber, tileLevel, c, r);
                    if (_entries.TryGetValue(key, out var entry))
                    {
                        if (entry.LruNode is not null)
                        {
                            _lruList.Remove(entry.LruNode);
                            _lruList.AddFirst(entry.LruNode);
                        }

                        outRefs[idx] = entry.Image.Clone();
                    }
                    else
                    {
                        outRefs[idx] = null;
                    }

                    idx++;
                }
            }
        }
    }

    /// <summary>
    /// Returns the distinct tile levels strictly above <paramref name="baseLevel"/> for which
    /// the given page has any cached tiles, sorted ascending (closest level first).
    /// Uses the pre-built <see cref="_pageLevels"/> index for O(1) lookup.
    /// </summary>
    public int[]? GetCachedLevelsAbove(int pageNumber, int baseLevel)
    {
        lock (_lock)
        {
            if (!_pageLevels.TryGetValue(pageNumber, out var levels))
            {
                return null;
            }

            // Return a snapshot copy — the live SortedSet view cannot be iterated
            // outside the lock because background eviction may modify it concurrently.
            var above = levels.GetViewBetween(baseLevel + 1, int.MaxValue);
            if (above.Count == 0)
            {
                return null;
            }

            var snapshot = new int[above.Count];
            above.CopyTo(snapshot);
            return snapshot;
        }
    }

    /// <summary>
    /// Removes the oldest entry from the cache and returns it for disposal outside the lock.
    /// Returns null if the LRU list is empty.
    /// </summary>
    private CacheEntry? EvictOldestLocked()
    {
        var oldest = _lruList.Last;
        if (oldest is null)
        {
            return null;
        }

        var entry = _entries[oldest.Value];
        RemoveEntryLocked(entry);
        return entry;
    }

    /// <summary>
    /// Removes an entry from primary structures (entries dict + LRU list) and secondary indexes.
    /// </summary>
    private void RemoveEntryLocked(CacheEntry entry)
    {
        RemoveEntryFromPrimaryLocked(entry);

        // Update secondary indexes
        if (_pageKeys.TryGetValue(entry.Key.PageNumber, out var keys))
        {
            keys.Remove(entry.Key);
            if (keys.Count == 0)
            {
                _pageKeys.Remove(entry.Key.PageNumber);
                _pageLevels.Remove(entry.Key.PageNumber);
            }
            else if (_pageLevels.TryGetValue(entry.Key.PageNumber, out var levels))
            {
                // Check if any other key at this level remains
                bool levelStillPresent = false;
                foreach (var k in keys)
                {
                    if (k.TileLevel == entry.Key.TileLevel)
                    {
                        levelStillPresent = true;
                        break;
                    }
                }

                if (!levelStillPresent)
                {
                    levels.Remove(entry.Key.TileLevel);
                    if (levels.Count == 0)
                    {
                        _pageLevels.Remove(entry.Key.PageNumber);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Removes an entry from the primary structures only (entries dict + LRU list + memory counter).
    /// Does NOT update secondary indexes — caller is responsible.
    /// </summary>
    private void RemoveEntryFromPrimaryLocked(CacheEntry entry)
    {
        _entries.Remove(entry.Key);
        if (entry.LruNode is not null)
        {
            _lruList.Remove(entry.LruNode);
            entry.LruNode = null;
        }

        _currentMemoryBytes -= entry.MemorySize;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Image.Dispose();
            }

            _entries.Clear();
            _lruList.Clear();
            _pageKeys.Clear();
            _pageLevels.Clear();
            _currentMemoryBytes = 0;
        }
    }
}
