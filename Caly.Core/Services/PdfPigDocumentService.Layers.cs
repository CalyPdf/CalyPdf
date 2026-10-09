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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Utilities;

namespace Caly.Core.Services;

internal sealed partial class PdfPigDocumentService
{
    public async Task<IReadOnlyList<PdfLayerNode>?> GetLayersAsync(CancellationToken token)
    {
        Debug.ThrowOnUiThread();
        return await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            // The state is immutable: no lock needed.
            return _layerState is { } state ? PdfLayers.BuildTree(state) : null;
        }, token);
    }

    public async Task<IReadOnlyList<bool>?> SetLayerVisibilityAsync(int groupIndex, bool isOn, CancellationToken token)
    {
        Debug.ThrowOnUiThread();
        return await GuardDispose(async guardCt =>
        {
            await WaitForDocumentToOpen(guardCt);
            var document = _document;
            if (document is null)
            {
                return null;
            }

            lock (_layerStateLock)
            {
                if (_layerState is not { } state || groupIndex < 0 || groupIndex >= state.Groups.Count)
                {
                    return null;
                }

                var updated = state.WithGroupState(state.Groups[groupIndex], isOn);
                _layerState = updated;
                return PdfLayers.GetStates(updated);
            }
        }, token);
    }
}
