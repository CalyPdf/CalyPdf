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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Utilities;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    private readonly Lazy<Task<HierarchicalTreeDataGridSource<PdfLayerNode>?>> _layersTask;
    public Task<HierarchicalTreeDataGridSource<PdfLayerNode>?> LayersSource => _layersTask.Value;

    private IReadOnlyList<PdfLayerNode>? _layerRoots;

    /// <summary>
    /// The last layer change; each change waits for the previous one, so they apply in click order.
    /// </summary>
    private Task _layerChange = Task.CompletedTask;

    private async Task<HierarchicalTreeDataGridSource<PdfLayerNode>?> GetLayers()
    {
        try
        {
            _mainToken.ThrowIfCancellationRequested();

            var roots = await Task.Run(() => _pdfService.GetLayersAsync(_mainToken), _mainToken);
            if (roots is null || roots.Count == 0)
            {
                return null;
            }

            _layerRoots = roots;
            foreach (var node in PdfLayers.Flatten(roots))
            {
                node.Toggled += OnLayerToggled;
            }

            var source = new HierarchicalTreeDataGridSource<PdfLayerNode>(roots)
            {
                Columns =
                {
                    new HierarchicalExpanderColumn<PdfLayerNode>(
                        new TemplateColumn<PdfLayerNode>(null, "PdfLayerCellTemplate"), x => x.Children)
                }
            };
            source.ExpandAll();
            return source;
        }
        catch (OperationCanceledException)
        { /* No op */ }

        return null;
    }

    private void OnLayerToggled(PdfLayerNode node, bool isOn) => _ = SetLayerVisibilityAsync(node, isOn);

    /// <summary>
    /// Turns a layer on or off, then reloads everything derived from page content: pictures, tiles,
    /// thumbnails, text layers (the selection is cleared) and the search index (an active search is run
    /// again). Must be called on the UI thread.
    /// </summary>
    internal Task SetLayerVisibilityAsync(PdfLayerNode node, bool isOn)
    {
        Debug.ThrowNotOnUiThread();

        // Stop the index build first, so it does not keep caching text layers of the old state.
        Task previousIndex = ResetSearchIndex();
        _layerChange = ChangeLayer(_layerChange, previousIndex, node, isOn);
        return _layerChange;
    }

    private async Task ChangeLayer(Task previousChange, Task previousIndex, PdfLayerNode node, bool isOn)
    {
        try
        {
            await previousChange.ConfigureAwait(false);
        }
        catch
        { /* Reported by the change that failed */ }

        try
        {
            try
            {
                await previousIndex.ConfigureAwait(false);
            }
            catch
            {
                /* Cancelled on purpose; a failure is reported by the search awaiting the build, and
                   must not stop the layer change */
            }

            if (node.GroupIndex is not int groupIndex)
            {
                return;
            }

            var states = await Task.Run(() => _pdfService.SetLayerVisibilityAsync(groupIndex, isOn, _mainToken), _mainToken)
                .ConfigureAwait(false);
            if (states is null)
            {
                return;
            }

            Task reload = Task.CompletedTask;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_layerRoots is not null)
                {
                    PdfLayers.ApplyStates(_layerRoots, states);
                }

                // The selection refers to words that may no longer exist.
                TextSelection?.ResetSelection();

                // Again, now that the state has changed: a search started since the click began a
                // build that may have indexed text layers of the old state. Not awaited: cancelled,
                // it can only drop its results (text-layer caching is guarded by the content version).
                _ = ResetSearchIndex();

                // Sequenced with tab (de)activation, which also releases and restores content.
                reload = _activityTransition = QueueActivityTransition(ReloadContent);
            });

            await reload.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        { /* No op */ }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }

    private async Task ReloadContent()
    {
        await ReleaseContent().ConfigureAwait(false);

        // No-op for an inactive document: it is restored when activated.
        await RestoreContent().ConfigureAwait(false);

        // Started, not awaited: the search only ends once the whole index is rebuilt, and the next layer
        // change or tab (de)activation must not wait for that. The next change's ResetSearchIndex, and
        // the search it starts, cancel this one.
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(TextSearch))
            {
                SearchTextCommand.Execute(null);
            }
        });
    }
}
