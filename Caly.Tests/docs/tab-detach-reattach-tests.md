# Tab detach & reattach — test cases

Living record of the manual and automated coverage for Caly.Tabalonia tab detach/reattach in
Caly. Add new cases as they are found; keep the IDs stable so regressions can be referred
to by number.

> **Note:** the `external/Tabalonia` submodule carries two Caly-specific changes beyond
> upstream — `IsSingleTabHost()` in `TabsControl.cs`, so dragging the only tab of *any*
> window moves that window instead of tearing off (Bug 6 / M24); and
> `DetachedHostFactory`, which lets Caly supply the new window together with its own
> XAML-built strip instead of re-wiring a bare one in code (Bug 11). Run
> `dotnet test external/Tabalonia/Tabalonia.Tests/Tabalonia.Tests.csproj` after touching it.

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

## The rules being tested

Three rules drive nearly every case below. Most bugs so far came from breaking one of them.

1. **Every window owns its own `MainViewModel` and `PdfDocuments` collection.** Ownership is
   resolved by scanning live windows, never cached — Tabalonia moves models between
   collections with bare `Remove`/`Add`.
2. **Window closing is driven by intent, never by an empty collection.** Only two signals
   close a window: Tabalonia's `LastTabClosedAction`, and Caly's own close-tab path. When a
   tab is dragged *onto another window's strip*, Tabalonia empties the source collection and
   merely **hides** the window so it can re-show it — nothing may close it there.
3. **An empty window closes unless it is the last one left.** The last window always falls
   back to the splash screen; closing it would exit the app and leave no way back in. The
   startup window gets no special treatment.

---

## Manual test cases

Legend: ✅ verified · ⬜ not yet run

### Detaching

| # | Steps | Expected | Status |
|---|---|---|---|
| M1 | Open two PDFs. Drag one tab well clear of the strip and release. | A new Caly window appears under the pointer with that document, full chrome. The original window keeps the other tab. | ✅ |
| M2 | In the detached window, use the sidebar, page navigation, zoom, search and print. | All work exactly as in the main window. | ✅ |
| M3 | Detach a tab, then drag it back onto the main window's strip. | It docks into the main window; the detached window closes. | ✅ |
| M4 | Detach two tabs into one window, then drag one of those onto the main window. | The detached window stays open with its remaining tab. | ✅ |
| M5 | Reorder tabs inside one window with short drags. | No window closes, no tab is lost. | ✅ |

### Window lifetime

| # | Steps | Expected | Status |
|---|---|---|---|
| M6 | Open 2 documents. Detach one. Drag the remaining document from the main window into the detached window. | **The main window closes.** An empty window only stays open when it is the only one. | ✅ |
| M7 | With a main and a detached window open, close the main window. | The detached window survives and the app keeps running. Close it too → the app exits. | ✅ |
| M8 | Open 2 documents, detach one, close the main window, then close the tab in the detached window. | The detached window **stays open on the splash screen** — it is the last window. | ✅ |
| M9 | Open one document in a single window and close its tab. | The window stays open on the splash screen; the app does not exit. | ✅ |

### Document lifecycle

| # | Steps | Expected | Status |
|---|---|---|---|
| M10 | Close the tab in a detached window, then reopen the same PDF from the main window. | It opens. *(If it silently does nothing, `_openedFiles` holds a stale record — see Bug 3.)* | ✅ |
| M11 | Detach a tab, then close the **detached window itself** (title-bar ×) while its tab is still in it. Reopen that PDF. | It opens. The closing window must hand its documents over to be unloaded. | ✅ |
| M12 | With two windows open, focus the detached one and press Ctrl+O. The picker should also appear over that window. | The document opens in the **detached** window. | ✅ |
| M13 | With two windows open, check both still render their selected document; switch tabs in one. | The other window does not blank — document activity is per-window. | ✅ |
| M14 | Open a PDF that is already open in another window (Ctrl+O, same file). | The owning window is brought to the front with that tab selected; no duplicate tab. | ✅ |
| M28 | **In a Native AOT build**: open two documents, detach one, leave the detached window active, then open a further document by double-clicking a file. | The document opens in the detached window **and its tab is activated**. Also check the “+” and “×” buttons work in that detached window — they were wired the same way. | ✅ |
| M25 | Start opening a large PDF, then close its window before the tab finishes loading (while it still reads `Opening '…'…`). | The window closes, no error dialog appears, and no exception file is written. Reopening that PDF afterwards works. | ✅ |
| M29 | With two windows open, click the **detached** window to focus it, then drag a PDF from the file manager onto the **main** window without focusing it first. | The document opens in the **main** window — the one it was dropped on. A drop does not activate the window it lands on, so the target cannot come from "whichever window is active". | ✅ |
| M30 | Drop the **same** PDF twice in quick succession (or drop a selection containing the same file twice). | One tab appears. No error dialog, no exception file, and the tab renders normally rather than coming up blank. | ✅ |

### Drag edge cases

| # | Steps | Expected | Status |
|---|---|---|---|
| M15 | Drag a tab from one window onto another window's strip, then **drag it back out again before releasing**. | The original floating window reappears with the tab. No `Cannot re-show a closed window`. *(Bug 1's exact path.)* | ✅ |
| M16 | Drag a tab out and release it over the taskbar / off-screen edge. | A window is created; the document is never lost. | ✅ |
| M17 | Close (not drag — see M24) the last tab of a window that is not the last window. | The source window closes. *Dragging* the only tab out is impossible by design: `IsSingleTabHost()` moves the window instead. | ✅ |
| M24 | Drag the **only** tab of a window out into empty space (try both the main window and a detached one). | The window itself moves; no new window is created and the old one is not closed. Dragging that tab onto another window's strip must still work. | ✅ |

### Content interactions after the `DocumentTabView` extraction

These moved out of `DocumentsTabsControl` into the per-tab control and previously used
`this.FindDescendantOfType<...>()` with cached results — the bug being that a detached
window would reach into the main window's tree.

| # | Steps | Expected | Status |
|---|---|---|---|
| M18 | Drag the splitter between sidebar and page view. | Pane resizes, clamped to 200–500 px. In a detached window it resizes **that** window's pane. | ✅ |
| M19 | Type a page number in the top box and press Enter. | Jumps to that page; focus returns to the page view. | ✅ |
| M20 | Press the go-to-page hotkey. | The page-number box focuses with its text selected. | ✅ |
| M21 | Narrow a window to less than twice the pane width. | The pane auto-closes. | ✅ |
| M22 | Open a PDF with attachments; double-click one in the Embedded Files tab. | It opens. | ✅ |
| M23 | Drop a non-PDF file onto a window, with another window focused. | An error notification appears in **that** window's bottom-right, not the focused one. *(Bug 15.)* | ✅ |
| M26 | With two windows open, toggle the sidebar in one of them and resize its pane. | Only that window's sidebar and pane width change; the other is unaffected. The *persisted* width is still app-wide — last window resized wins — so a fresh session starts every window at that width. | ✅ |
| M27 | Close the sidebar (or resize it), then tear that tab off into a new window. | The new window inherits both the sidebar state and the pane width of the window it came from. | ✅ |

---

## Automated tests

```bash
dotnet test Caly.Tests
dotnet test Caly.Tests --filter "FullyQualifiedName~CalyWindowRegistryTests"
```

### `Caly.Tests/CalyWindowRegistryTests.cs`

| Test | Covers |
|---|---|
| `FindOwnerOf_ReturnsTheWindowHoldingTheDocument` | Rule 1 |
| `FindOwnerOf_FollowsTheDocumentWhenItMovesBetweenWindows` | Rule 1 — does exactly what Tabalonia's transfer does (bare `Remove` + `Add`) |
| `FindOwnerOf_ReturnsNullWhenNoWindowHoldsTheDocument` | Rule 1 |
| `Active_FallsBackToPrimaryAndIgnoresUnregisteredWindows` | Routing target for new documents |
| `FindContext_ResolvesTheContextForAViewModel` | Lookup |
| `CloseWindowIfEmpty_ClosesANonPrimaryEmptyWindowWhileOthersRemain` | Rule 3 |
| `CloseWindowIfEmpty_ClosesThePrimaryWindowWhenAnotherWindowRemains` | Rule 3 / **M6** |
| `CloseWindowIfEmpty_LeavesTheLastRemainingWindowOpen` | Rule 3 / **M8** |
| `CloseWindowIfEmpty_LeavesAWindowThatStillHasDocuments` | Rule 3 |
| `EmptyingTheCollection_DoesNotCloseTheWindowOnItsOwn` | Rule 2 / **M15** — regression for `Cannot re-show a closed window` |
| `Unregister_ReportsTheDocumentsAClosingWindowStillHeld` | **M11** — orphaned documents must be unloaded |
| `Unregister_ReportsNothingForAnAlreadyEmptyWindow` | No spurious unload during a drag |
| `IsDocumentPaneOpen_IsIndependentPerWindow` | **M26** — sidebar is window state, not app state |
| `PaneSize_IsIndependentPerWindow` | M26 — pane width is window state too |
| `ActivateSearchTextTab_OpensOnlyItsOwnWindowsPane` | M26 — Ctrl+F opens only its own window's pane |
| `ActiveAndPrimary_AreNullOnceEveryWindowHasClosed` | Teardown paths must get "no window", not an exception |
| `ReRegisteringAfterUnregister_ReplacesTheContextRatherThanAccumulating` | Android activity recreation must not accumulate dead contexts |
| `RegisterWhenOpened_RegistersOnlyOnceTheWindowIsActuallyShown` | **Bug 16** — a detached host Tabalonia abandons must leave nothing behind |
| `RegisterWhenOpened_RegistersImmediatelyWithoutAWindow` | Single-view lifetimes have no window to wait on |

### `Caly.Tests/DocumentTabViewPaneBindingTests.cs`

| Test | Covers |
|---|---|
| `TabContent_CanResolveItsHostStripsViewModel` | **M26** — the ancestor lookup the per-window sidebar binding depends on |
| `TabContentInTwoWindows_ResolvesEachWindowsOwnViewModel` | M26 — each strip resolves its own window's view model |
| `TogglingTheTabContent_WritesBackToItsWindowsViewModel` | M27 — the toggle must reach the view model, not just the view |
| `TogglingThroughTheStyledProperty_ReachesTheWindowsViewModel` | M27 — the two-hop chain used by the real control |
| `ResolveSourceViewModel_PrefersTheStripsOwnDataContextOverTheActiveWindow` | **M27** — detach reads the source window from the strip, not from whatever is active |
| `ResolveSourceViewModel_FallsBackToTheActiveWindowWhenTheStripHasNoViewModel` | M27 — fallback |
| `DeclaredStrip_CarriesItsCommandsAndItemsFromXaml` | **M28** — the strip handed to a detached window really does carry its XAML commands and items binding |
| `DeclaredStrip_LeavesDetachingOffWhenThereIsNoDesktopLifetime` | Detaching stays off where there is no `Window` to create |

### `Caly.Tests/PdfPigDocumentServiceOpenDocumentTests.cs`

| Test | Covers |
|---|---|
| `DisposeAsync_DefersTeardownWhileAnOperationIsStillRunning` | **M25** — teardown must not run under an in-flight parse |
| `DisposeAsync_ReleasesImmediatelyWhenNoOperationIsRunning` | Normal close still frees the file handle promptly |
| `ReleaseResources_IsIdempotent` | Dispose path and last operation can both reach the release |

### `Caly.Tests/PdfDocumentsManagerServiceOwnershipTests.cs`

| Test | Covers |
|---|---|
| `RemoveDocumentFromOwnerWindow_RemovesFromTheOwningWindowOnly` | Rule 1 — close acts on the owning window |
| `RemoveDocumentFromOwnerWindow_MovesTheOwningWindowsSelectionToANeighbour` | Selection after close |
| `RemoveDocumentFromOwnerWindow_IsANoOpWhenNoWindowOwnsTheDocument` | Already-removed document |
| `RemoveDocumentFromOwnerWindow_ReturnsTheOwnerWithoutClosingItsWindow` | **M10** — unload must complete before the window closes |
| `ShouldBeActive_IsTrueForTheSelectedDocumentOfEveryWindow` | **M13** — per-window activity |
| `ShouldBeActive_IsFalseForADocumentNoWindowOwns` | Per-window activity |
| `ResolveOpenTarget_HonoursTheCapturedWindowEvenWhenAnotherBecameActive` | **M12** — the picker activates a window, so the target is captured when the user acts |
| `ResolveOpenTarget_FallsBackToActiveWhenTheCapturedWindowHasClosed` | M12 — captured window closed mid-open |
| `ResolveOpenTarget_FallsBackToActiveWhenNothingWasCaptured` | M12 — drop / pipe entry points |
| `ResolveOpenTarget_ReturnsNullWhenNoWindowsRemain` | **M25** — every window gone while an open was queued; must abort, not throw |
| `TryRemoveRecord_LeavesARecordThatHasSinceBeenReplaced` | **Bug 12** — a pending orphan unload must not dispose a document reopened under the same path |
| `TryRemoveRecord_RemovesTheRecordItWasGiven` | Ordinary unload still removes |
| `TryRemoveRecord_IgnoresADocumentThatDoesNotHoldTheKey` | Bug 12 — removal is by record, not by path |
| `ShowExistingDocument_ReportsOpeningWhileAnotherRequestIsStillOpeningTheFile` | **Bug 13** — "no window owns it" means "still opening" until the document reaches a window |
| `ShowExistingDocument_ReportsStaleOnceTheDocumentsWindowHasClosed` | M10 / M11 — the stale-record fall-through still works |
| `ShowExistingDocument_SelectsTheDocumentInTheWindowThatOwnsIt` | **M14** — reopening selects the tab where it lives |

### `Caly.Tests/MainViewDropTargetTests.cs`

| Test | Covers |
|---|---|
| `DropTarget_IsTheViewsOwnWindowNotWhicheverIsFocused` | **M29** / Bug 14 — the drop target comes from the view, not the active window |
| `DropTarget_IsNullWhenTheViewHasNoViewModel` | Design-time surface falls back to the active window |

### Not covered automatically

The drag gestures themselves (M1–M6, M15–M17) are Tabalonia's and need a real pointer.
Tabalonia has its own `Tabalonia.Tests/DragSessionTests.cs`. The Caly-side tests above cover
the state transitions those gestures produce, not the gestures.

The same goes for the file-drop plumbing (M29, M30): `MainViewDropTargetTests` covers where the
drop's target comes from, not the `DragDrop` event that delivers the files. M30's timing — two
requests for one file in flight together — is a race, so a manual run passing is weak evidence;
`ShowExistingDocument_ReportsOpeningWhileAnotherRequestIsStillOpeningTheFile` is the real guard.

---

## Bug log

Bugs found by manual testing, with the case that now guards each.

| # | Symptom | Root cause | Guard |
|---|---|---|---|
| 1 | `InvalidOperationException: Cannot re-show a closed window` when dragging a tab onto another window's strip | Closing was driven by `PdfDocuments.Count == 0`. Tabalonia empties and **hides** the floating window (`suppressEmptySourceAction: true`, `TabsControl.cs:1017-1024`) so it can `Show()` it again. An empty collection is ambiguous; the events carry intent. | M15, `EmptyingTheCollection_DoesNotCloseTheWindowOnItsOwn` |
| 2 | Closing the last tab of the only remaining window closed it and exited the app | `IsPrimary` was fixed at construction, so a detached window that became the last window still auto-closed. | M8, `CloseWindowIfEmpty_LeavesTheLastRemainingWindowOpen` |
| 3 | Reopening a PDF after closing its tab in a detached window silently did nothing | Two faults: a window closing never unloaded the documents it still held, leaving stale `_openedFiles` records; and the already-open branch returned without opening when no window owned the record. | M10, M11, `Unregister_ReportsTheDocumentsAClosingWindowStillHeld` |
| 4 | Dragging the main window's last document into a detached window left an empty main window | The primary window was exempt from the close rule. Removed — an empty window now closes unless it is the last one. | M6, `CloseWindowIfEmpty_ClosesThePrimaryWindowWhenAnotherWindowRemains` |
| 5 | Ctrl+O in a detached window opened the document in the main window | `FilesService` captured the `IStorageProvider` bound to `desktop.MainWindow`, so the picker was owned by — and activated — the main window, flipping `registry.Active` before the document was routed. Fixed on both sides: the picker resolves the active window's provider per call, and the target window is captured when the user acts and carried through the open queue. | M12, `ResolveOpenTarget_*` |
| 6 | Dragging the only tab of the main window creates a new window and closes the old one | `IsDetachedSingleTabHost()` required `_isDetachedHost`, private and set only for windows Tabalonia itself created. A detached single-tab window correctly *moved* instead of tearing off; the main window could not. Fixed in the submodule: the check is now `IsSingleTabHost()` — any single-tab strip moves its window — and the dead `_isDetachedHost` field is gone. | M24 |
| 7 | A window closed while one of its documents was still loading | The orphan cleanup disposes the document's DI scope, which cancels `_mainCts` and so cancels the in-flight load. `OpenLoadDocumentInternal` left `state = Error`, showing *“Critical error — Cannot load pages…”* and writing a log file for an ordinary user action. `OperationCanceledException` is now treated as `Canceled`. Also hardened: `ResolveOpenTarget` returns null instead of throwing when no window remains, and record removal no longer depends on the mutable `LocalPath`. | M25, `ResolveOpenTarget_ReturnsNullWhenNoWindowsRemain` |
| 8 | `ObjectDisposedException: Cannot access a closed file`, repeatedly, after closing a window during a slow load | `PdfPigDocumentService.DisposeAsync` waited up to 5s for in-flight operations and then disposed the file stream **anyway**. `PdfDocument.Open` is a long synchronous parse that cancellation cannot interrupt, so on a large PDF it outlived the wait and kept seeking into a closed stream. Teardown is now owned by whoever finishes last: dispose defers if an operation is running, and the operation releases on its way out. Documents are also disposed before their stream, not after. | M25, `DisposeAsync_DefersTeardownWhileAnOperationIsStillRunning` |
| 9 | A torn-off window opened with the sidebar shown even though the source window had it closed | The detached-window factory read the source state from `registry.Active`, which is only a guess about where the drag came from — activation moves during a drag, and the new window is activated right after the factory returns. It now reads `tabsControl.DataContext`, which Tabalonia copies from the source strip, so the source window is identified exactly. | M27, `ResolveSourceViewModel_*` |
| 10 | **AOT only**: a document opened into an active detached window added the tab but never activated it | The detached strip was wired with `new Binding("SelectedDocumentIndex")`. `Avalonia.Data.Binding` resolves its path by name through reflection, and `Caly.Desktop.csproj` publishes with `PublishAot` + `IlcTrimMetadata`, which strip that metadata — so the binding failed silently. The main window was fine because its binding comes from XAML and is compiled (`AvaloniaUseCompiledBindingsByDefault`). The add and close commands were wired the same way and were broken too, just less visibly. A strip belongs to one window whose view model never changes, so it now assigns the commands and syncs the selection by hand, with no binding at all. | M28, `WireTabsControlToViewModel_SyncsSelectionBothWaysWithoutReflection` |
| 11 | *(review finding, not a reported failure)* `DocumentsTabsControl.axaml.cs` re-implemented in C# what `DocumentsTabsControl.axaml` already declares — commands, items and selection — because Tabalonia built the detached strip itself and only let Caly supply the window. That duplication caused Bugs 5-style drift and Bug 10, and hid a dead field, two redundant property re-sets and an unregistered fallback window. Fixed upstream with `DetachedHostFactory`, which asks for the window *and* its strip, so Caly returns a `MainWindow` and its XAML strip and re-wires nothing. | M28, `DeclaredStrip_*` |
| 12 | *(review finding, not a reported failure)* Reopening a PDF seconds after its window closed could leave a live tab backed by a disposed service, and drop the file from `_openedFiles` so a further open duplicated the tab | A closing window unloads its documents on a background task, serialised behind each other's teardown — seconds for a multi-tab window. A reopen in that gap replaces the record under the same path key, and the pending unload then removed by key alone, disposing the **new** record's DI scope. Removal now matches on the record, via `ConcurrentDictionary`'s value-matching `TryRemove`. | `TryRemoveRecord_*` |
| 13 | *(review finding, not a reported failure)* Two requests for the same file in flight together tore each other down: a visible tab flicker and the file parsed twice | The open queue drains through `Parallel.ForEachAsync`, so the second request could find the first's record before the first had put its document into a window. `FindOwnerOf` returning null was read as "the record outlived its window" and the first request's record was dropped mid-parse. The record now tracks whether the document ever reached a window, so "still opening" and "its window closed" are distinguishable. | M30, `ShowExistingDocument_ReportsOpeningWhileAnotherRequestIsStillOpeningTheFile` |
| 14 | *(review finding, not a reported failure)* With two windows open, a PDF dropped on the unfocused one opened as a tab in the other | `MainView.Drop` was `static` and discarded `sender`, so the target fell through to `registry.Active`. Unlike the picker and the menu, a drop does **not** activate the window it lands on, so the active window is simply the wrong answer. The drop now carries its own `MainViewModel` on the request. | M29, `DropTarget_IsTheViewsOwnWindowNotWhicheverIsFocused` |
| 15 | A failed open reported itself in the wrong window: first always in the detached one, then — once the manager's notification was routed — one notification in *each* window, both different | Same root cause as Bug 14, one layer further on, in **two** senders. `DialogService` resolves the notification manager per call and the active window is not who the message is about. `ShowNotificationMessage` now carries the window it concerns: `OpenLoadDocumentInternal` fills it in from the open's target, and `ViewModelBase.OnExceptionChanging` — which raised the *second*, more specific message — takes it from a new `NotificationTarget` hook, overridden by `MainViewModel` to itself and by `DocumentViewModel` to the window that owns it. Senders with no window in mind still pass none and land in the active window. | M23 |
| 16 | *(review finding, not a reported failure)* A detached window Tabalonia asked for but then abandoned would stay in the registry forever | `CreateDetachedHost` registered the context before Tabalonia committed. `DetachItemToNewWindow` calls the host factory first and can still `return false` afterwards, never showing the window - which then has no `Closed` event to unregister itself. The stuck entry makes `Windows.Count > 1` permanently true, so the last real window would close on its last tab and the app would exit instead of falling back to the splash screen. Registration now waits for `Window.Opened`; the transfer's selection change is posted rather than synchronous, so nothing reads the registry before `Show()`. | `RegisterWhenOpened_*` |
