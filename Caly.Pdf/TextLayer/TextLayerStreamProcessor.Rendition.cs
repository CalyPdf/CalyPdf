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

using System.Diagnostics.CodeAnalysis;
using Caly.Pdf.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.TextLayer
{
    public partial class TextLayerStreamProcessor
    {
        // Rendition dictionary keys (PDF 2.0, 13.2.3 "Renditions"). These are not part of PdfPig's
        // NameToken constants, so they are interned on demand. NameToken.Create returns the shared
        // instance, so equality with resolved keys works as expected.
        private static readonly NameToken MediaClipKey = NameToken.Create("C");          // Rendition -> media clip
        private static readonly NameToken ContentTypeKey = NameToken.Create("CT");       // Media clip data -> content type
        private static readonly NameToken MediaDataKey = NameToken.Create("D");          // Media clip data -> file specification

        /// <summary>
        /// Extracts the embedded audio of a media rendition action, or returns <see langword="null"/>
        /// when the action carries no self-contained audio we can play (e.g. a selector rendition, an
        /// external/URL media file, or non-audio content such as video).
        /// </summary>
        private PdfRenditionMedia? TryGetRenditionMedia(RenditionAction renditionAction)
        {
            // Only media renditions (/S /MR) embed a single media clip. Selector renditions (/S /SR)
            // pick among several based on viewer capabilities and are not handled here.
            DictionaryToken? rendition = renditionAction.Rendition;
            if (rendition is null)
            {
                return null;
            }

            // Rendition -> media clip dictionary (/C). For a media clip data dictionary (/S /MCD) the
            // content type and embedded data live directly here.
            if (!rendition.TryGet(MediaClipKey, PdfScanner, out DictionaryToken? mediaClip))
            {
                return null;
            }

            // Content type (/CT), e.g. "audio/mpeg". Optional - we fall back to the file name extension.
            string? contentType = null;
            if (mediaClip.TryGet(ContentTypeKey, PdfScanner, out StringToken? contentTypeToken))
            {
                contentType = contentTypeToken.Data;
            }

            // Media clip data -> file specification (/D).
            if (!mediaClip.TryGet(MediaDataKey, PdfScanner, out DictionaryToken? fileSpecification))
            {
                return null;
            }

            // File name, preferring the Unicode form (/UF) over the legacy form (/F). Informational only.
            string? fileName = null;
            if (fileSpecification.TryGet(NameToken.Uf, PdfScanner, out StringToken? unicodeNameToken))
            {
                fileName = unicodeNameToken.Data;
            }
            else if (fileSpecification.TryGet(NameToken.F, PdfScanner, out StringToken? nameToken))
            {
                fileName = nameToken.Data;
            }

            if (!IsSupportedAudio(contentType, fileName))
            {
                // Non-audio (e.g. video) or unknown media. Out of scope.
                return null;
            }

            // Embedded file stream: file specification -> /EF -> /F (or /UF). An external file
            // specification (no /EF, e.g. a /URL file system) is not self-contained and is skipped.
            if (!fileSpecification.TryGet(NameToken.Ef, PdfScanner, out DictionaryToken? embeddedFile))
            {
                return null;
            }

            if (!embeddedFile.TryGet(NameToken.F, PdfScanner, out StreamToken? mediaStream) &&
                !embeddedFile.TryGet(NameToken.Uf, PdfScanner, out mediaStream))
            {
                return null;
            }

            // Decode using the document's scanner and filter provider (the stream is typically
            // FlateDecode-compressed). This yields the raw media file bytes (e.g. the .mp3 contents).
            byte[] data = mediaStream.Decode(FilterProvider, PdfScanner).ToArray();
            if (data.Length == 0)
            {
                return null;
            }

            return new PdfRenditionMedia
            {
                Data = data,
                ContentType = contentType,
                FileName = fileName,
                Operation = renditionAction.Operation ?? RenditionOperation.PlayAndAssociate
            };
        }

        private static bool IsSupportedAudio(string? contentType, string? fileName)
        {
            if (!string.IsNullOrEmpty(contentType) &&
                contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return HasAudioExtension(fileName);
        }

        private static bool HasAudioExtension([NotNullWhen(true)] string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            ReadOnlySpan<char> extension = Path.GetExtension(fileName.AsSpan());
            return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".aiff", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".aif", StringComparison.OrdinalIgnoreCase);
        }
    }
}
