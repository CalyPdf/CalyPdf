// Copyright (c) BobLd
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

using SkiaSharp;
using System;

namespace Caly.Core.Services.Rendering
{
    /// <summary>
    /// A rendered tile image. Always holds pixel data: tiles that render to nothing are recorded
    /// as bare keys by <see cref="TileCache.AddBlank"/> and never become a <see cref="TileImage"/>.
    /// </summary>
    public sealed class TileImage : IDisposable
    {
        public SKImage Image { get; }

        /// <summary>
        /// Size of the image in bytes. Cached at construction so the cache can budget without
        /// crossing into the native image on every call.
        /// </summary>
        public long BytesSize { get; }

        public int Width => Image.Width;

        public int Height => Image.Height;

        public TileImage(SKImage? image)
        {
            if (image is null)
            {
                throw new ArgumentNullException(nameof(image), "TileImage cannot be created with a null image.");
            }

            Image = image;
            BytesSize = Image.Info.BytesSize64;
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Image.Dispose();
        }
    }
}
