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

using Caly.Pdf.Artwork;
using Caly.Pdf.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services;

internal partial class PdfPigDocumentService
{
    public async Task<Artwork3DContent?> GetArtwork3DContentAsync(PdfArtwork3D artwork, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        return await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            if (_document is null)
            {
                return null;
            }

            // The artwork's stream and views are read through the document's token scanner: only one
            // reader at a time, like a page.
            return await ExecuteWithLockAsync(_ => Artwork3DContentReader.Read(artwork), guardCt)
                .ConfigureAwait(false);
        }, token);
    }
}
