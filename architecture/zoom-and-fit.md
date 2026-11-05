# Zoom and fit modes

`DocumentViewModel.ZoomMode` (`Manual`, `FitPage`, `FitWidth`, `FitContent`, `FitContentWidth`) is per document and bound two-way to `PageItemsControl.ZoomMode`. The view model only holds the mode. `PageItemsControl.ZoomFit.cs` computes and applies each fit, because only the control knows the viewer's size.

- **Sticky:** a fit mode stays on until a zoom is picked by hand. Zoom in/out and actual size (view model commands), and Ctrl+wheel and pinch (`ZoomPanController`), switch it back to `Manual`. Zoom changes made by a fit never touch the mode.
- **Choosing the active mode again** goes through `Manual` (`DocumentViewModel.SetZoomMode`), so it re-applies the mode and snaps the page back into view.
- **The maths** is in pure `ZoomFit`:
  - **Reference page:** the current page in continuous view, kept while scrolling so a page of another size never re-zooms. The page shown in single-page view.
  - **1 px of slack:** layout rounding at scale 1 is a bare `Math.Ceiling`, so an exact fit would round over the viewport and bring a scroll bar.
  - **10 px margin** (`ZoomFit.FitMargin`) on either side of what is fitted: the page in fit page and fit width, the content box in the content modes. Fit content also keeps it above and below the box; fit page keeps none there. Fit page shares fit width's side margins, so it never zooms past fit width. The margin also absorbs pages that end up a little wider than the fitted page, which brought the horizontal scroll bar with the slack alone: a slightly wider neighbour, or the pages panel rounding a fractional page width up to a whole pixel before the zoom scales it.
  - **Scroll bars:** the four bar states are tried in turn, and the first consistent one wins.
  - **Content cap:** content modes are capped at `max(800%, the matching page-mode zoom)`.
- **Triggers:** each requests one coalesced fit (see Timing):
  - the mode changing;
  - the `ScrollViewer`'s outer size changing;
  - an extent change (pages streaming in, a size arriving, rotation);
  - the end of a tab-switch restore;
  - `PageItem.ContentBoundsChanged`.
  A viewport-only change, such as a scroll bar toggling, never triggers. The computation uses the outer size, not the viewport, so a fit cannot chase its own scroll bars. Fits that nobody asked for act only when the zoom moves by more than 0.1%.
- **Timing:** the fit runs from `LayoutUpdated`, after the layout pass and before the frame is drawn: Avalonia's `MediaContext` runs the layout passes queued during its render callbacks before it commits, so the zoom change is laid out in the same frame. `RequestFit` invalidates the control's arrange so that a layout pass always follows a request. A job at `Loaded` priority would run after the render: the frame showing a resize or new pages would show them at the old zoom, with the horizontal bar on a shrinking window. While a zoom is in flight (`ZoomPanController.IsZooming`, cleared at `Loaded`), the fit waits for a `Loaded` job instead, as running it from layout passes would loop within the frame. A fit for a new viewer size is the exception: run from `LayoutUpdated`, whose pass has laid out the zoom in flight, it zooms at once (`TryZoomTo(..., afterLayout: true)`), so a window resize step that comes before the last step's zoom has settled is fitted in its frame too. A zoom never changes the viewer's size, so that cannot feed itself. Tests read the committed frame with `Compositor.RequestCompositionUpdate` (`PageItemsControlZoomFitMaintenanceTests.FirstFrameAfter`).
- **Tab switches:** a mode that arrives with a tab switch (`_pendingScrollToPage`) keeps the restored position unless the zoom has to change.
- **Fit zooms keep the content at the viewport's top edge in place** (`ZoomPanController.TryZoomTo(zoom, keepTopInPlace: true)`), unlike the zoom buttons, which keep the centre. Saved positions and reading anchor at the top: zooming in around the centre after opening, or after enlarging the window, pushed the top of page 1 out of view.
- **Landing:**
  - Fit page uses `GoToPage(page, 0)`, which lands on the page's top.
  - Fit content uses `GoToPage(..., PageOffsetSpace.UnrotatedPage)`, the `GoToWord` path, to centre the content box.
  - The width modes set only the horizontal offset. `GoToPage`'s `ScrollIntoView` would snap a partly visible page to its top or bottom.
  - Horizontally, fit page and the width modes put the page or its content box `FitMargin` from the viewport's left edge (`AlignWithFitMargin`). The offset is relative to the document, because an offset relative to the page stops at the page's edge. A page narrower than the viewport cannot scroll: the scroll viewer centres it.
  - In single-page view, `GoToPage` fits the target page before it lands (`FitBeforeLanding`).
- **The content box** is `PageViewModel.ContentBounds`, the page picture's `CullRect` × `PpiScale`. It is kept after the picture is evicted. Pages are recorded with an R-tree, so Skia trims the cull rect to what was *drawn*. Clips are ignored and strokes and text are bounded loosely. Scans, page-sized white rectangles, `DrawPaint` fills and soft masks (a page-sized `DstIn` draw) therefore give the whole page.
- **Opening zoom:** `CalySettings.DefaultZoomMode` (Settings > Appearance > Documents) is applied in `LoadDocumentCore`. It is `Manual` (100%) by default. Settings aren't loaded on mobile yet, so mobile always opens at 100%.
