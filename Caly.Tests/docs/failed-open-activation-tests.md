# Failed open & document activation — test cases

Living record of the manual and automated coverage for what happens to the document the user
is reading when another document takes the selection away and gives it straight back. Add new
cases as they are found; keep the IDs stable so regressions can be referred to by number.

Run the app with:

```bash
dotnet run --project Caly.Desktop -f net10.0 -c Debug
```

Exceptions are written to `%LOCALAPPDATA%\Caly\logs`. After a manual session, check for new
files there even if nothing looked wrong on screen:

```bash
find "$LOCALAPPDATA/Caly/logs" -type f -mmin -30
```

---

## The rule being tested

**Becoming active again is the exact inverse of becoming inactive.**

Caly keeps one live document per window. Everything the others have rendered — page pictures,
text layers, thumbnails, and the picture/text caches behind them — is released when they stop
being the selected document, and has to be requested again when they come back. Two things
follow, and nearly every bug in this area comes from breaking one of them:

1. **Activation restores.** `SetActive` re-requests the visible pages and thumbnails;
   `IPdfDocumentService.IsActive` is bookkeeping, so nothing else in the pipeline reacts to
   it. A document whose view has never reported a visible range is left alone — it has
   nothing to restore, and the view drives its first render itself.
2. **The teardown is sequenced against what follows it.** `SetInactive`'s clear is
   asynchronous and cancels whichever render generation is current *when it runs*. Left
   unsequenced it can land after a reactivation and cancel the generation that reactivation
   just started, so the renders it queued are dropped as the workers pick them up — the page
   stays on its loading skeleton and the thumbnails on their HotPink placeholder, for good.

3. **An in-flight tile entry is a promise that the tile is coming.** `TileRenderService`
   deduplicates tile requests against `_inFlight`, so an entry may only stand while a render
   for that key is genuinely on its way. `CancelPage` and `InvalidatePage` doom every queued
   request for their page, so they retire its entries too. Leaving them behind deduplicated
   the request that would have repaired the page against renders that were already going to
   be dropped — the page went **blank** (not skeleton: the picture is there, the tiles are
   not) and only a scroll brought it back, because a scroll asks for different tile keys.

A document that fails to open is what makes all of this visible: its tab goes into the strip
and is selected while it parses, then is taken away again when the parse fails. The selection
is stolen and returned within a few dispatcher frames, so the transitions overlap — and the
page containers detach and re-attach across the swap, which is what cancels the page's tiles.

**Telling the two failures apart on screen:** the loading skeleton means there is no picture
(rule 1 or 2); a plain blank page means the picture is there but no tiles were drawn (rule 3).

---

## Automated coverage

`Caly.Tests/DocumentActivationRestoreTests.cs`:

| Test | Covers |
|---|---|
| `SetActive_PutsBackThePictureSetInactiveReleased` | Rule 1 — the round trip restores the page picture |
| `SetActive_ImmediatelyAfterSetInactive_StillEndsUpWithARenderedPage` | Rule 2 — reactivation overtaking a teardown still ends up rendered |
| `SetInactive_StillReleasesThePictureWhenTheDocumentStaysInactive` | The teardown still happens for a document that stays inactive — the point of it is giving the memory back |
| `AFailedOpenStealingTheSelectionLeavesTheOpenDocumentRendered` | The whole sequence through `PdfDocumentsManagerService`'s selected-document message |

`Caly.Tests/TileRenderServiceTests.cs`:

| Test | Covers |
|---|---|
| `CancelPage_LetsTheSameTilesBeRequestedAgain` | Rule 3 — cancelling a page frees its in-flight keys |
| `InvalidatePage_LetsTheSameTilesBeRequestedAgain` | Rule 3 — same for the invalidating variant |
| `CancelPage_LeavesOtherPagesInFlightEntriesAlone` | The retirement is page-scoped, so live requests elsewhere still deduplicate |

```bash
dotnet test Caly.Tests --filter "FullyQualifiedName~DocumentActivationRestoreTests"
dotnet test Caly.Tests --filter "FullyQualifiedName~TileRenderServiceTests"
```

What these cannot cover: the drag-and-drop gesture itself (the drop runs inside the platform's
own modal drag loop, which the headless test host has no equivalent of), and whether the page
actually *looks* right afterwards. That is what the manual cases below are for.

---

## Manual test cases

Legend: ✅ verified · ⬜ not yet run

### Failed open

| # | Case | Expected | Status |
|---|---|---|---|
| M1 | With one PDF open and rendered, **drag & drop** a non-PDF file (e.g. a `.png`) onto the window | Two error toasts appear; the dropped file's tab disappears; the open document is still fully rendered — page **and** every thumbnail — and stays interactive | ⬜ |
| M2 | Same as M1 but pass the non-PDF on the command line to a second instance (`Caly.exe some.png`) | As M1 | ⬜ |
| M3 | Same as M1 while scrolled to a middle page of a large, slow-rendering document (a PDF/X test form is a good one), immediately after scrolling so a render is still in flight | As M1 — no page left on the loading skeleton, no thumbnail left HotPink | ⬜ |
| M4 | Same as M1 with **several** non-PDF files dropped at once | As M1, one pair of toasts per file | ⬜ |
| M5 | Drop a non-PDF onto a window that is **not** focused, while another window has a document open | The toasts appear in the window the file was dropped on; **both** windows' documents stay rendered | ⬜ |
| M6 | Drop a non-PDF onto an **empty** window (splash screen) | Toasts appear, the window stays open and shows the splash screen | ⬜ |
| M7 | Drop a **password-protected** PDF and cancel the password prompt | The "Could not open password protected document" toast appears; the document that was open is still fully rendered | ⬜ |
| M8 | Drop a valid PDF onto a window with a document already open | Both tabs present; switching between them renders each correctly | ⬜ |
| M8b | Repeat M1 **many times in a row** (the timing windows are narrow — one drop in tens can be enough) | Never a blank page and never a skeleton left behind; in particular, never a page that only renders once you scroll | ⬜ |

### Ordinary activation

| # | Case | Expected | Status |
|---|---|---|---|
| M9 | With two documents open, switch tabs back and forth several times, pausing on each | Each document is fully rendered every time it comes back — no skeleton, no HotPink thumbnails | ⬜ |
| M10 | As M9 but switching rapidly, without waiting for a render to finish | As M9, once the switching stops | ⬜ |
| M11 | Switch away from a document and back while its pages are still loading for the first time | The document finishes loading and renders | ⬜ |
| M12 | Switch away from a document, then close its tab from another tab | Closes cleanly, no exception in `%LOCALAPPDATA%\Caly\logs` | ⬜ |
| M13 | Drag a tab out into its own window, then switch between the two windows | Each window's document stays rendered — activity is per window, so both are live at once | ⬜ |
| M14 | Switch away from a document, then close the window holding it | Closes cleanly, no exception in the logs | ⬜ |

### Tiles

| # | Case | Expected | Status |
|---|---|---|---|
| M16 | Scroll a page out of view and straight back before its tiles finish rendering | The page renders — scrolling out cancels its tiles, coming back must ask for them again | ⬜ |
| M17 | Zoom in and out repeatedly while tiles are still rendering | Each zoom level fills in; no level left permanently blank | ⬜ |
| M18 | Scroll fast through a long document, then stop | Every page that settles in view renders; none stay blank | ⬜ |

### Memory

| # | Case | Expected | Status |
|---|---|---|---|
| M15 | Open several large documents, switch through them all, and watch the process's memory | Memory does not grow without bound — the teardown of inactive documents is what caps it, so a fix that simply stopped clearing would show up here | ⬜ |
