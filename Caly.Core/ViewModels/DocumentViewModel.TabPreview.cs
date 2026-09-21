using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;
public partial class DocumentViewModel
{
    /// <summary>
    /// How long the tab hover preview's render/rasterise (<see cref="CaptureTabPreview"/>,
    /// <see cref="EnsureTabPreview"/>) is allowed to take before giving up. A malformed PDF
    /// page can make Skia's picture replay hang; a <see cref="CancellationToken"/> cannot
    /// interrupt that blocking native call, so this can only mean "stop waiting on it", not
    /// "stop the work" - the abandoned render keeps running until it (if ever) returns.
    /// TODO - The last sentence is very problematic, and needs to be addressed.
    /// </summary>
    private readonly TimeSpan _tabPreviewTimeout;

    /// <summary>
    /// Snapshot of the page the user was last on, shown in this document's tab hover preview.
    /// <para>
    /// Captured as the document goes inactive, while its picture is still cached. Replaced on
    /// every later deactivation; the bitmap it replaces is disposed only after the new one is
    /// published, because the old one can still be on screen in an open tooltip.
    /// </para>
    /// </summary>
    [ObservableProperty]
    public partial WriteableBitmap? TabPreview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabPreviewCaption))]
    public partial int? TabPreviewPageNumber { get; set; }

    /// <summary>
    /// Rotation of the previewed page, snapshotted with the image: the raster itself is
    /// unrotated, exactly as page pictures and sidebar thumbnails are.
    /// </summary>
    [ObservableProperty]
    public partial int TabPreviewRotation { get; set; }

    /// <summary>
    /// Whether a hover-triggered preview render (<see cref="EnsureTabPreview"/>) is currently
    /// in flight.
    /// <para>
    /// Scoped to the hover path only. The eager capture on deactivation is never something the
    /// user is watching, so it gets no loading state of its own.
    /// </para>
    /// </summary>
    [ObservableProperty]
    public partial bool IsTabPreviewLoading { get; set; }

    public string? TabPreviewCaption =>
        TabPreviewPageNumber is { } pageNumber ? $"Page {pageNumber} of {PageCount}" : null;


    /// <summary>
    /// Snapshots the page the user was last on, for this document's tab hover preview.
    /// <para>
    /// Called from <see cref="Clear"/> after the pages have released their own references but
    /// before <see cref="PdfPageService.CancelAndClear"/> empties the picture cache. That is
    /// the one moment the snapshot is a downscale of work already done rather than a fresh
    /// render - which is why it must not move either side of that window.
    /// </para>
    /// <para>
    /// A capture failure is caught and logged here rather than propagated: it degrades to "no
    /// preview" instead of stopping <see cref="Clear"/> short of
    /// <see cref="PdfPageService.CancelAndClear"/> on the line after this call.
    /// </para>
    /// </summary>
    private async Task CaptureTabPreview()
    {
        try
        {
            if (SelectedPageNumber is not { } pageNumber)
            {
                return;
            }

            PageViewModel? page = GetPage(pageNumber);
            if (page is null || !page.IsSizeSet())
            {
                return;
            }

            // Snapshot on the calling thread - as EnsureTabPreview already does for its own
            // Task.Run - then rasterise off it. QueueActivityTransition only guarantees a
            // background thread when the previous transition was still running at the point of
            // the await; when it had already completed (the ordinary case, since the user has
            // usually been reading the document for a while before switching tabs), this method
            // would otherwise run synchronously on whatever thread called SetInactive(), which
            // is the UI thread.
            PixelSize target = page.TabPreviewSize;
            Size pageSize = page.Size;
            int rotation = page.Rotation;

            // Null when the page never rendered, so nothing was ever cached for it. The hover
            // path fills that in if the user asks for it.
            //
            // WaitAsync bounds how long we wait, not how long the rasterisation runs: Skia's
            // picture replay is a blocking native call a CancellationToken cannot interrupt, so
            // a malformed page can only be abandoned here, not stopped. TimeoutException falls
            // through to the catch below like any other capture failure. Linked to _mainToken
            // so a document disposed while this is waiting stops waiting immediately rather
            // than sitting out the full timeout.
            WriteableBitmap? preview = await Task.Run(
                    () => _pdfPageService.TryCapturePreview(pageNumber, target, pageSize))
                .WaitAsync(_tabPreviewTimeout, _mainToken);
            if (preview is null)
            {
                return;
            }

            await SetTabPreview(preview, pageNumber, rotation);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
    }

    /// <summary>
    /// Publishes <paramref name="preview"/> and only then disposes the one it replaced - an
    /// open tooltip may still be drawing the old bitmap.
    /// </summary>
    private async Task SetTabPreview(WriteableBitmap preview, int pageNumber, int rotation)
    {
        WriteableBitmap? previous = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WriteableBitmap? old = TabPreview;

            TabPreview = preview;
            TabPreviewPageNumber = pageNumber;
            TabPreviewRotation = rotation;

            return old;
        });

        previous?.Dispose();
    }

    /// <summary>
    /// The hover render in flight, if any. UI thread only - the command's prologue and
    /// <see cref="CancelTabPreview"/> are both driven by the tooltip opening and closing.
    /// </summary>
    private CancellationTokenSource? _tabPreviewCts;

    /// <summary>
    /// Renders a tab preview for a document whose deactivation had nothing to capture,
    /// because its page had not rendered yet.
    /// <para>
    /// Invoked when the tab's tooltip opens. The tooltip's show delay is the debounce - a
    /// pointer sweeping across the strip never gets here.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task EnsureTabPreview()
    {
        Debug.ThrowNotOnUiThread();

        if (IsActive || TabPreview is not null)
        {
            return;
        }

        if (SelectedPageNumber is not { } pageNumber)
        {
            return;
        }

        PageViewModel? page = GetPage(pageNumber);
        if (page is null || !page.IsSizeSet())
        {
            return;
        }

        _tabPreviewCts?.Cancel();
        _tabPreviewCts?.Dispose();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken);
        _tabPreviewCts = cts;

        PixelSize target = page.TabPreviewSize;
        Size pageSize = page.Size;
        int rotation = page.Rotation;

        IsTabPreviewLoading = true;

        try
        {
            // WaitAsync bounds how long we wait, not how long the render/rasterise runs: a
            // malformed page can make GetRenderPageAsync or Skia's picture replay hang, and
            // neither is interruptible via cts.Token alone (Skia's replay is a blocking native
            // call). Linking the token means a cancelled hover (tooltip closed) stops waiting
            // immediately rather than sitting out the full timeout. TimeoutException falls
            // through to the catch below and is logged like any other failure - "the page
            // seems malformed" is worth knowing about, unlike a plain cancellation.
            WriteableBitmap? preview = await Task.Run(
                    () => _pdfPageService.RenderPreviewAsync(pageNumber, target, pageSize, cts.Token),
                    cts.Token)
                .WaitAsync(_tabPreviewTimeout, cts.Token);

            if (preview is null)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                preview.Dispose();
                return;
            }

            await SetTabPreview(preview, pageNumber, rotation);
        }
        catch (OperationCanceledException)
        { /* The pointer left the tab. Not a failure. */ }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }
        finally
        {
            IsTabPreviewLoading = false;

            // This render put a picture back in the cache of a document that is meant to hold
            // nothing. Same guard as Clear(): the user may have selected the tab while it ran,
            // and tearing down then would blank the document they are now looking at.
            if (!IsActive)
            {
                try
                {
                    await _pdfPageService.CancelAndClear().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // A finally body's own exception is not caught by this method's try/catch
                    // above it. Left unguarded, it would escape EnsureTabPreview entirely and,
                    // since this command runs via ICommand.Execute on the UI thread, surface as
                    // an unhandled exception there instead of a logged error.
                    Debug.WriteExceptionToFile(e);
                }
            }
        }
    }

    /// <summary>
    /// Drops an in-flight hover render, when the tab's tooltip closes. UI thread only.
    /// </summary>
    /// <remarks>
    /// The source is cancelled but not disposed here: the render is still unwinding through
    /// it. Whichever comes first - the next <see cref="EnsureTabPreview"/> or
    /// <see cref="DisposeAsync"/> - disposes it.
    /// </remarks>
    internal void CancelTabPreview()
    {
        Debug.ThrowNotOnUiThread();

        _tabPreviewCts?.Cancel();
    }
}
