# Bug: page-render timeout when opening many documents at once

- **Date:** 2026-09-06
- **Reported by:** BobLd
- **Repro:** drag-and-drop every file in
  `UglyToad.PdfPig.Tests/Integration/Documents` (189 files, including
  non-PDFs) onto Caly in one drop. Two documents — `GHOSTSCRIPT-695513-0.pdf`
  and `GHOSTSCRIPT-697984-3.pdf` — consistently (2/2 runs) show "Could not
  display page after 30 seconds" on page 1. Both open and render fine when
  dropped individually. Key clue: activating one of these tabs *after* the
  batch has settled shows the timeout error immediately — the page never
  attempts to load at that point.
- **File changed:** `Caly.Core/Services/PdfDocumentsManagerService.cs` only.

---

## Root cause

`PdfDocumentsManagerService.OpenLoadDocumentInternal` makes **every** newly
opened document the selected/active tab as soon as it's added:

```csharp
target.PdfDocuments.Add(document);
target.SelectedDocumentIndex = Math.Max(0, target.PdfDocuments.Count - 1);
```

Becoming the selected tab realizes the document's page 1 in the UI, which
fires a real render request through `PdfPageService` →
`PdfPigDocumentService.GetRenderPageAsync` → `GetPageAsSKPicture`, arming the
30-second `PageTimeOut`.

During a 189-file drop this runs once per document, unconditionally, so up
to 189 page-1 renders get kicked off automatically — one per document, each
superseded a moment later when the next one opens and takes the selection.
Nobody asked to see any of these tabs; the render still runs. Once
`GetRenderPageAsync` times out, the result is a real, valid `SKPicture` (the
error text drawn by `GetTimeOutPicture`) and it gets **cached** exactly like
a successful render (`PdfPageService.GetPicture`:
`_cachePictures[pageNumber] = picture;`, no distinction between success and
timeout). So the failure from that early, unwanted render sticks: the next
time the user actually clicks the tab, the cached failure is returned
instantly — which is exactly "the page is already timed out when activated".

We could not pin down precisely why an in-flight render, once started for a
tab that's immediately superseded, stalls for the full 30 seconds for these
two files specifically rather than completing quickly in the background a
bit late (a synthetic contention probe against the real `PdfPig.Rendering.Skia`
call could not reproduce more than ~2.7 s worst case, so it isn't simple CPU
contention — see git history below for what that ruled out). It no longer
matters for the fix below, because the fix removes the reason a render is
triggered on a tab nobody asked to see.

## Why "this never used to happen"

Traced with git blame/log, not guessed:

- The auto-select-on-open line has been in the codebase, essentially
  unchanged, since commit `7669f2a` ("Ensure view models Exception property
  and PdfDocuments docs are removed on UI thread"), **2025-08-10** — over a
  year before this report. It survived the service refactor
  (`fb0ca3e`, 2026-02-14) and the multi-window rework
  (`da4e9a9`, "Open documents into the window that asked for them",
  2026-08-31, which introduced `EnqueueOpenRequest`) with identical
  semantics each time — confirmed by diffing each commit. `EnqueueOpenRequest`
  itself is not part of the root cause; it's plumbing for resolving which
  *window* an open belongs to, added on top of a selection behavior that
  predates it by over a year.
- What *is* comparatively recent is the visible failure mode: `PageTimeOut`
  and the "Could not display page after 30 seconds" error were only added in
  `5920ecb`/`e5b2b78` ("Add page time out for rendering and text layer"),
  **2026-05-17**. Before that, a render that never finished had no timeout
  and no error toast — it would just never complete, silently. So it's
  plausible this exact condition was always reachable but never visibly
  failed before; more likely, nobody had previously drag-dropped ~190 files
  in one go before this report — normal usage (a handful of files at a time)
  never puts enough documents through the auto-select churn for it to
  matter.

## Fix

`OpenDocumentRequest` gained a `SelectOnOpen` flag:

- Single-file entry points (file picker, command line / second instance, a
  lone drag-drop) always pass `selectOnOpen: true` — unchanged behavior,
  opening one document still makes it active immediately.
- `OpenLoadDocuments` (the multi-file drop entry point) now enqueues every
  file with `selectOnOpen: false` **except the last one**. Every dropped file
  still opens and appears as a tab; only the final one takes over the tab
  strip and gets its page rendered.
- `OpenLoadDocumentInternal` only sets `SelectedDocumentIndex` when
  `request.SelectOnOpen` is true, or when this is the first tab in an
  otherwise-empty window (so a window is never left showing nothing).

Dropping 189 files no longer fires 189 speculative page-1 renders for tabs
nobody is looking at. Parsing all 189 documents still happens concurrently in
the background as before (parsing doesn't touch `PageTimeOut`), so every tab
is ready to click into — it just doesn't burn its 30-second render budget
before anyone asked for it.

## Discarded: process-wide render/parse throttle

An earlier version of this fix added a static `SemaphoreSlim` gate around
`PdfDocument.Open` and `GetPageAsSKPicture`, on the theory that unbounded
cross-document concurrency was starving the two slow documents of CPU. That
theory does not hold up: documents are independent (each gets its own DI
scope, semaphore, and render queue — `AddScoped` in `App.axaml.cs`), and a
standalone probe against the real `PdfPig.Rendering.Skia`/`SkiaSharp`/Unicolour
stack showed the whole 150-file folder rendering in ~2.7 s worst case even
under harsher-than-realistic unbounded concurrency — nowhere near 30 s, and
nowhere near enough to explain `GHOSTSCRIPT-697984-3.pdf`'s 34 ms solo
baseline turning into a 30 s stall. That change added complexity without a
confirmed mechanism behind it and has been reverted; the diff against the
pre-investigation commit for `PdfPigDocumentService*.cs` is now empty.

**Verification:** `Caly.Core` builds clean; full `Caly.Tests` suite passes
(329/329). Not verified end-to-end against the real drag-and-drop repro —
there is no CLI/headless entry point that drives that code path
(`Desktop_Startup` only ever opens `args[0]`). Re-running the original
189-file drag-and-drop is the recommended confirmation step.
