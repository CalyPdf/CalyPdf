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
using Avalonia.Interactivity;

namespace Caly.Avalonia.Pdf.Document;

/// <summary>
/// Raised when the user activates a link or interactive annotation in the document.
/// Carries either an external <see cref="Uri"/> or an in-document GoTo destination.
/// The host decides how to act on it (open the URI, navigate to the page, etc.).
/// </summary>
public sealed class PdfLinkActivatedEventArgs : RoutedEventArgs
{
    /// <summary>External link target.</summary>
    public PdfLinkActivatedEventArgs(RoutedEvent routedEvent, Uri uri) : base(routedEvent) => Uri = uri;

    /// <summary>In-document GoTo destination (1-based page, optional Y in PDF coordinates).</summary>
    public PdfLinkActivatedEventArgs(RoutedEvent routedEvent, int destinationPage, double? destinationTop)
        : base(routedEvent)
    {
        DestinationPage = destinationPage;
        DestinationTop = destinationTop;
    }

    /// <summary>The external URI, or <c>null</c> for an in-document destination.</summary>
    public Uri? Uri { get; }

    /// <summary>The 1-based destination page, or <c>null</c> for an external URI.</summary>
    public int? DestinationPage { get; }

    /// <summary>Optional Y offset within the destination page, in PDF coordinates.</summary>
    public double? DestinationTop { get; }
}
