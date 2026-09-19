<#
.SYNOPSIS
    Launches Caly on a sample document, opens a few more as extra tabs, and drives its UI
    through a short tour of features, at a pace meant to be screen-recorded for a README demo.

.DESCRIPTION
    Launches Caly with no document (a static splash screen, in a 1600x720 window - wide enough
    that FileC's first page later renders at full width with no horizontal scrollbar), gives a
    countdown so you can arm your screen recorder, then only once it finishes opens
    Brotli-Prototype-FileC.pdf (Caly.Tests/Documents) and: scrolls and zooms with the keyboard,
    scrolls/zooms/pans with the mouse (plain wheel to scroll, Ctrl+wheel to zoom at the cursor,
    Ctrl+left-drag to pan - the same gestures a user would use), opens four more documents as
    additional tabs (each launch of Caly.exe pipes its file to the already-running instance via
    the single-instance named pipe, which is how opening a second file normally adds a tab),
    opens the Print dialog on the last of those (P.pdf), shows every installed printer via the
    dropdown and selects the OneNote virtual printer (without actually printing), drags a tab to
    reorder it and drags another tab off into its own window before docking it back, switches to
    ICML03-081.pdf and drags across its abstract to select text and copies it, cycles back
    through all the tabs with Ctrl+PageUp to show tab switching, then on the
    original tab opens the Thumbnails/Bookmarks/Search/Document Properties panels (FileC has a
    real outline, so Bookmarks isn't empty), jumps via a bookmark and scrolls to show the active
    bookmark tracking the current page, jumps via a search result, opens the Settings pane, and
    returns to page 1.

    This script does NOT record video - it only drives the UI. Start your own screen recorder
    (or arm it during the countdown) before running.

.NOTES
    Requires a Release build: dotnet build Caly.Desktop -c Release
    Caly must not already be running (single-instance app).
    Your real settings file is backed up and restored when the script exits.
    Clicks use fractional window coordinates (see $Coords below) rather than fixed pixels, so
    they hold up across DPI scales - but they're still tied to today's panel layout and to
    FileC's specific outline/text. If Caly's UI moves or the demo documents change, re-run
    tools/DemoRecording/explore_layout.ps1-style screenshots to remap them before trusting this
    again.
#>
param(
    [int] $CountdownSeconds = 5,
    [string] $Pdf = (Join-Path $PSScriptRoot '..\..\Caly.Tests\Documents\Brotli-Prototype-FileC.pdf'),
    [string[]] $AdditionalPdfs = @(
        'Brotli-Prototype-FileA.pdf',
        'MOZILLA-LINK-6305-5.pdf',
        'ICML03-081.pdf',
        'P.pdf'
    )
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
# Guarded: Add-Type compiles the type into this process, so re-running the script in the same
# PowerShell session (rather than a fresh process, as a plain `.\run_demo.ps1` from an existing
# prompt does) would otherwise fail with "type ... already exists" on the second run. The
# trade-off: if this C# source is edited later, a session that already has the old version
# loaded keeps using it (silently, or with confusing signature-mismatch errors at the call
# site) until that PowerShell window is closed and reopened - the guard only helps re-running
# the *same* version of the script.
if (-not ([System.Management.Automation.PSTypeName]'CalyDemoWin32').Type) {
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CalyDemoWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
}
# This class has grown across several edits to this script, and Add-Type above is guarded to
# allow re-running in the same session (see comment above it) - which means a session holding an
# older, incompatible copy (missing newer members, or with an incompatible parameter type like
# the earlier uint/int mouse_event mismatch) silently keeps using it instead of picking up this
# file's current version. Fail with a clear message rather than a confusing MissingMethodException
# deep in some later call.
if (-not [CalyDemoWin32].GetMethod('EnumWindows')) {
    throw "This PowerShell session has a stale copy of CalyDemoWin32 loaded from an earlier run of an older version of this script (Add-Type can't redefine a type once loaded). Close this window and re-run from a fresh PowerShell session."
}
# Without this, GetWindowRect (always physical pixels) and CopyFromScreen/SetCursorPos (virtualised
# for a DPI-unaware process) disagree on the coordinate space - clicks land in the wrong place and
# screenshots capture whatever is behind Caly at the top-left. Cost this investigation a fair bit of
# confusion; do not remove.
[CalyDemoWin32]::SetProcessDpiAwarenessContext([IntPtr]-4) | Out-Null   # DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exe = Join-Path $repo 'Caly.Desktop\bin\Release\net10.0\Caly.exe'
$docsDir = Join-Path $repo 'Caly.Tests\Documents'
if (-not (Test-Path $exe)) { throw "Caly.exe not found at '$exe'. Build first: dotnet build Caly.Desktop -c Release" }
if (-not (Test-Path $Pdf)) { throw "PDF not found: $Pdf" }

# Additional tabs are given as bare filenames resolved against Caly.Tests/Documents, or full paths.
$AdditionalPdfs = $AdditionalPdfs | ForEach-Object {
    $p = if ([System.IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $docsDir $_ }
    if (-not (Test-Path $p)) { throw "PDF not found: $p" }
    $p
}

$calyDir = Join-Path $env:LOCALAPPDATA 'Caly'
$settingsPath = Join-Path $calyDir 'caly_settings'

if (Get-Process Caly -ErrorAction SilentlyContinue) { throw 'Caly is already running - close it first (single-instance app).' }

$backup = $null
if (Test-Path $settingsPath) { $backup = Get-Content $settingsPath -Raw }
New-Item -ItemType Directory -Force $calyDir | Out-Null
# Fixed size (DIPs; the actual physical window will be larger on a scaled display - that's fine,
# it's still proportioned like a 1600x720 window, which is what matters for a recording), not
# maximised, positioned wherever Caly's own CenterScreen logic puts it. 1600 (rather than a
# narrower 1280) was picked empirically so FileC's first page renders at full width with no
# horizontal scrollbar at 100% zoom.
[System.IO.File]::WriteAllText($settingsPath, '{"Width":1600,"Height":720,"IsMaximised":false,"PaneSize":323,"Debug":{"LogRenderTimings":true}}')

# --- Coordinates: fractions of the actual window rect, mapped by hand against a 1600x720-DIP /
# 2422x1091-physical window on a 150%-scaled display. The left icon rail and side panel are a
# fixed DIP width regardless of window width, so their fractions change whenever Width above
# changes - re-map (and re-run the dry-run screenshots) if either changes again, or if the UI
# layout changes. ---
$Coords = @{
    ThumbnailsTab = 0.0194, 0.1146
    BookmarksTab  = 0.0194, 0.1687
    SearchTab     = 0.0194, 0.2237
    PropertiesTab = 0.0194, 0.2787
    SettingsGear  = 0.0144, 0.9743
    # "3. Normative references" in FileC's real outline, and a "12 (32)" grouped search-result
    # row for the term "the" - both specific to FileC's actual outline/content, not general UI
    # chrome.
    BookmarkEntry = 0.0826, 0.2080
    SearchResult  = 0.0619, 0.4840
    # The Settings pane is a fixed-width overlay anchored to the window's right edge (unlike the
    # rail/side-panel which are anchored left), so this fraction does NOT scale the same way as
    # the left-anchored coordinates above - it was measured directly from a screenshot, not
    # derived from the old value by a width ratio.
    SettingsDebugTab = 0.7862, 0.0843
    # Center of the page content area - not any specific UI chrome, just a safe spot to click to
    # move keyboard focus back into DocumentTabView (needed before Ctrl+G below; see comment there).
    MainContent   = 0.60, 0.5
    # Two points within the page content area (not tied to any specific document content) used
    # only to draw a visible diagonal Ctrl+drag pan gesture.
    PanFrom       = 0.45, 0.35
    PanTo         = 0.75, 0.70
    # The Print dialog (Ctrl+P) is a separate, fixed-size (380x~635 DIP, non-resizable) top-level
    # window, not part of the main Caly window - these two fractions are relative to *that*
    # window's own rect (measured 572x962 physical), not the main window like everything above.
    # PrintOneNoteItem is the plain "OneNote (Desktop)" row in the printer dropdown once it's
    # open (not the "OneNote (Desktop) - Protected" variant just below it).
    PrintPrinterCombo = 0.50, 0.075
    PrintOneNoteItem  = 0.26, 0.161
    # Two points spanning a few lines of ICML03-081's abstract paragraph (page 1, default 100%
    # zoom) - specific to that document's text layout, not general UI chrome.
    TextSelectStart = 0.3167, 0.7003
    TextSelectEnd   = 0.4245, 0.8184
}

function Get-Rect($h) {
    $r = New-Object CalyDemoWin32+RECT
    [CalyDemoWin32]::GetWindowRect($h, [ref]$r) | Out-Null
    return $r
}

# Finds a top-level window by exact title, restricted to windows owned by $procId (e.g. the
# Print dialog, a separate window from Caly's main one). Result goes through $script:__foundHwnd
# rather than a return from inside the EnumWindows callback - the callback runs as a distinct
# closure/scope, so assigning straight to a local variable here would silently never be seen by
# the caller (this exact bug cost a debugging detour once already).
function Find-ChildWindow([int] $procId, [string] $title) {
    $script:__foundHwnd = [IntPtr]::Zero
    $cb = [CalyDemoWin32+EnumWindowsProc]{
        param($hWnd, $lParam)
        $wpid = 0
        [CalyDemoWin32]::GetWindowThreadProcessId($hWnd, [ref]$wpid) | Out-Null
        if ($wpid -eq $procId) {
            $len = [CalyDemoWin32]::GetWindowTextLength($hWnd)
            $sb = New-Object System.Text.StringBuilder ($len + 1)
            [CalyDemoWin32]::GetWindowText($hWnd, $sb, $sb.Capacity) | Out-Null
            if ($sb.ToString() -eq $title) {
                $script:__foundHwnd = $hWnd
                return $false
            }
        }
        return $true
    }
    [CalyDemoWin32]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:__foundHwnd
}

# Same idea as Find-ChildWindow, but returns every match rather than the first - needed because
# a tab torn off into a new window (Tabalonia's DetachedHostFactory, see
# Caly.Core/Controls/DocumentsTabsControl.axaml.cs) gets a plain new MainWindow with the *same*
# title as the original ("Caly Pdf Reader"), so the two can only be told apart by handle.
function Find-AllWindowsByTitle([int] $procId, [string] $title) {
    $script:__foundList = New-Object System.Collections.Generic.List[IntPtr]
    $cb = [CalyDemoWin32+EnumWindowsProc]{
        param($hWnd, $lParam)
        $wpid = 0
        [CalyDemoWin32]::GetWindowThreadProcessId($hWnd, [ref]$wpid) | Out-Null
        if ($wpid -eq $procId) {
            $len = [CalyDemoWin32]::GetWindowTextLength($hWnd)
            $sb = New-Object System.Text.StringBuilder ($len + 1)
            [CalyDemoWin32]::GetWindowText($hWnd, $sb, $sb.Capacity) | Out-Null
            if ($sb.ToString() -eq $title) { $script:__foundList.Add($hWnd) }
        }
        return $true
    }
    [CalyDemoWin32]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:__foundList
}

# A point on the tab strip, computed from pixel coordinates measured against a 2422px-wide
# reference screenshot (window Width is always 1600 DIP in this script, so window physical width
# is a direct, portable stand-in for the current DPI scale). Y also scales off width, not
# height: the tab row's height is fixed DIP chrome, unrelated to the window's (variable) content
# height, so the generic Click/$Coords fraction-of-height convention does not apply here.
function TabPoint($rect, [double] $refX, [double] $refY) {
    $w = $rect.Right - $rect.Left
    $x = $rect.Left + [int]($refX * $w / 2422.0)
    $y = $rect.Top + [int]($refY * $w / 2422.0)
    return @($x, $y)
}

# Drags in absolute screen coordinates rather than fractions of one window's rect - needed for
# reattaching a tab, which drags from a point in the detached window to a point in the main one.
function DragScreen([int] $x1, [int] $y1, [int] $x2, [int] $y2, [int] $steps = 16, [int] $stepMs = 45) {
    [CalyDemoWin32]::SetCursorPos($x1, $y1) | Out-Null
    Start-Sleep -Milliseconds 150
    [CalyDemoWin32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # left down
    Start-Sleep -Milliseconds 100
    for ($i = 1; $i -le $steps; $i++) {
        $x = [int]($x1 + ($x2 - $x1) * $i / $steps)
        $y = [int]($y1 + ($y2 - $y1) * $i / $steps)
        [CalyDemoWin32]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds $stepMs
    }
    Start-Sleep -Milliseconds 150
    [CalyDemoWin32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # left up
    Start-Sleep -Milliseconds 300
}

# A single left-click at absolute screen coordinates - e.g. a tab header via TabPoint, which
# already returns an absolute point rather than a fraction of one window's rect.
function ClickAbs([int] $x, [int] $y) {
    [CalyDemoWin32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 100
    [CalyDemoWin32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # left down
    Start-Sleep -Milliseconds 60
    [CalyDemoWin32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # left up
}

function Focus($proc) {
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { return $null }
    # Alt tap lifts Windows' foreground-lock, which otherwise silently ignores SetForegroundWindow
    # from a background process.
    [CalyDemoWin32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [CalyDemoWin32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    [CalyDemoWin32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 200
    return $h
}

function Click($h, [string] $coordName) {
    $fx, $fy = $Coords[$coordName]
    $r = Get-Rect $h
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    $x = [int]($r.Left + $fx * $w); $y = [int]($r.Top + $fy * $ht)
    [CalyDemoWin32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 100
    [CalyDemoWin32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # left down
    Start-Sleep -Milliseconds 60
    [CalyDemoWin32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # left up
}

function Keys([string] $keys, [int] $pauseMs = 300) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds $pauseMs
}

function MoveTo($h, [string] $coordName) {
    $fx, $fy = $Coords[$coordName]
    $r = Get-Rect $h
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    $x = [int]($r.Left + $fx * $w); $y = [int]($r.Top + $fy * $ht)
    [CalyDemoWin32]::SetCursorPos($x, $y) | Out-Null
    return @($x, $y)
}

# Plain wheel = normal scroll; Ctrl+wheel (caller holds Ctrl via CtrlDown/CtrlUp around this) =
# zoom at the cursor position - see ZoomPanController.OnPointerWheelChanged/ZoomTo. Standard
# wheel convention: positive ticks ("wheel up") zoom IN when Ctrl is held, but scroll UP/BACK
# (toward earlier pages) when it isn't - the same physical rotation means opposite things
# depending on Ctrl, exactly like a real mouse. Pass negative ticks to scroll DOWN/forward.
function Wheel($h, [string] $coordName, [int] $ticks, [int] $pauseMs = 150) {
    MoveTo $h $coordName | Out-Null
    Start-Sleep -Milliseconds 100
    $delta = if ($ticks -ge 0) { 120 } else { -120 }
    # mouse_event's dwData is a DWORD, but a negative wheel delta is meaningful (rotated toward
    # the user) - reinterpret the signed value as its 32-bit two's-complement bit pattern rather
    # than widening, which is what a straight [uint32]$delta cast would reject for negatives.
    $deltaU = [uint32]($delta -band 0xFFFFFFFFL)
    for ($i = 0; $i -lt [Math]::Abs($ticks); $i++) {
        [CalyDemoWin32]::mouse_event(0x0800, 0, 0, $deltaU, [UIntPtr]::Zero)  # MOUSEEVENTF_WHEEL
        Start-Sleep -Milliseconds $pauseMs
    }
}

function CtrlDown { [CalyDemoWin32]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero) }   # VK_CONTROL down
function CtrlUp   { [CalyDemoWin32]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }   # VK_CONTROL up

# Ctrl+left-drag pan (caller holds Ctrl via CtrlDown/CtrlUp around this) - see
# ZoomPanController.OnPointerPressed/OnPointerMoved and CalyExtensions.IsPanning, which requires
# the left button down together with the command modifier.
function Drag($h, [string] $fromCoord, [string] $toCoord, [int] $steps = 12, [int] $stepMs = 50) {
    $x1, $y1 = MoveTo $h $fromCoord
    Start-Sleep -Milliseconds 120
    [CalyDemoWin32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # left down
    Start-Sleep -Milliseconds 80
    $fx2, $fy2 = $Coords[$toCoord]
    $r = Get-Rect $h
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    $x2 = [int]($r.Left + $fx2 * $w); $y2 = [int]($r.Top + $fy2 * $ht)
    for ($i = 1; $i -le $steps; $i++) {
        $x = [int]($x1 + ($x2 - $x1) * $i / $steps)
        $y = [int]($y1 + ($y2 - $y1) * $i / $steps)
        [CalyDemoWin32]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds $stepMs
    }
    [CalyDemoWin32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # left up
}

try {
    # Launched with no document, so the window comes up on the splash screen - nothing for the
    # recording to catch mid-load. The countdown gives time to arm the recorder against a static,
    # empty window; only once it finishes does FileC actually open.
    Write-Host "Launching Caly (no document yet) ..."
    $psi = [System.Diagnostics.ProcessStartInfo]::new($exe)
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = Split-Path $exe
    $proc = [System.Diagnostics.Process]::Start($psi)

    Start-Sleep -Seconds 8
    $h = Focus $proc
    if (-not $h) { throw 'Caly window did not appear.' }

    for ($i = $CountdownSeconds; $i -ge 1; $i--) {
        Write-Host "Recording starts in $i..."
        Start-Sleep -Seconds 1
    }
    Write-Host "Go!"

    # Open FileC now the recording has started - same single-instance pipe path as the
    # AdditionalPdfs tabs below, just for the first/main document.
    Write-Host "Opening $(Split-Path $Pdf -Leaf) ..."
    $mainPsi = [System.Diagnostics.ProcessStartInfo]::new($exe, "`"$Pdf`"")
    $mainPsi.UseShellExecute = $false
    $mainPsi.WorkingDirectory = Split-Path $exe
    [System.Diagnostics.Process]::Start($mainPsi) | Out-Null
    Start-Sleep -Milliseconds 2500
    $h = Focus $proc

    # --- Scroll a few pages ---
    Keys '{PGDN}' 500
    Keys '{PGDN}' 500
    Keys '{PGDN}' 500
    Keys '{PGDN}' 500
    Keys '{PGDN}' 900

    # --- Zoom in, pause, zoom back out ---
    Keys '^=' 400
    Keys '^=' 400
    Keys '^=' 1200
    Keys '^-' 400
    Keys '^-' 400
    Keys '^-' 400
    Start-Sleep -Milliseconds 500

    # --- Same scroll/zoom/pan, but with the mouse this time: plain wheel scrolls, Ctrl+wheel
    # zooms at the cursor, Ctrl+left-drag pans. Ticks up and down are equal so zoom ends back
    # near 100%, matching the keyboard beat above. A real wheel spun by hand fires ticks in a
    # fast burst (tens of ms apart), not one every 150ms - that gap read as a slow, chunky jump
    # rather than a scroll, so this uses more ticks spaced much closer together. ---
    Wheel $h 'MainContent' -14 30
    Start-Sleep -Milliseconds 300
    Wheel $h 'MainContent' 14 30
    Start-Sleep -Milliseconds 500

    CtrlDown
    Wheel $h 'MainContent' 5 200
    CtrlUp
    Start-Sleep -Milliseconds 800

    CtrlDown
    Drag $h 'PanFrom' 'PanTo'
    Start-Sleep -Milliseconds 300
    Drag $h 'PanTo' 'PanFrom'
    CtrlUp
    Start-Sleep -Milliseconds 500

    CtrlDown
    Wheel $h 'MainContent' -5 200
    CtrlUp
    Start-Sleep -Milliseconds 500

    # --- Open a few more documents as tabs. Each Caly.exe launch here is a brand new process,
    # but Caly is single-instance: it detects the already-running instance, pipes its file path
    # over, and exits - the running instance opens the file as a new tab and brings itself to
    # the foreground. This is the same path a user hits opening a second PDF while Caly is
    # already open. ---
    foreach ($extra in $AdditionalPdfs) {
        Write-Host "Opening $(Split-Path $extra -Leaf) as a new tab ..."
        $extraPsi = [System.Diagnostics.ProcessStartInfo]::new($exe, "`"$extra`"")
        $extraPsi.UseShellExecute = $false
        $extraPsi.WorkingDirectory = Split-Path $exe
        [System.Diagnostics.Process]::Start($extraPsi) | Out-Null
        Start-Sleep -Milliseconds 1600
    }
    $h = Focus $proc

    # --- Print dialog demo, on P.pdf (the currently active tab, since it was opened last).
    # Ctrl+P is a MainView-level KeyBinding (like Ctrl+PageUp/Down), so it works regardless of
    # which control inside the document view currently has focus - no MainContent click needed
    # first. Opens the printer dropdown to show every installed printer, selects the OneNote
    # virtual printer, then closes with Escape *without* clicking Print: actually printing to
    # OneNote would pop up OneNote's own external "Select Location" dialog (outside Caly, and a
    # different dialog per OneNote version) and leave a permanent page in the user's notebook -
    # not something this demo script should do. ---
    Keys '^p' 1200
    $printH = Find-ChildWindow $proc.Id 'Print'
    if ($printH -ne [IntPtr]::Zero) {
        [CalyDemoWin32]::SetForegroundWindow($printH) | Out-Null
        Start-Sleep -Milliseconds 400
        Click $printH 'PrintPrinterCombo'
        Start-Sleep -Milliseconds 600
        Click $printH 'PrintOneNoteItem'
        Start-Sleep -Milliseconds 1200
        Keys '{ESC}' 500
    }
    else {
        Write-Host "Print dialog window not found - skipping the print demo."
    }
    $h = Focus $proc

    # --- Tab drag demo: reorder by dragging, then tear a tab off into a new window and dock it
    # back. Reference pixel positions (x, and the shared tab-row y) were measured against a
    # 2422px-wide screenshot of this exact 5-tab layout; TabPoint rescales them to the window's
    # actual current physical size. Tabalonia decides reorder vs. detach by how far the release
    # point falls outside the tab strip's own bounds (TabsControl.DetachTriggerDistance, 32 DIP) -
    # so a reorder drag keeps Y constant (stays inside the strip throughout, not just at the
    # ends - Tabalonia checks continuously as the drag moves) and a detach drag moves mostly in Y.
    $tabRowY = 20.0
    $fileCTabX = 193.5; $fileATabX = 568.5; $mozillaTabX = 931.5; $icmlTabX = 1294.5; $pTabX = 1657.5

    $r = Get-Rect $h
    $px, $py = TabPoint $r $pTabX $tabRowY
    $icmlx, $icmly = TabPoint $r $icmlTabX $tabRowY

    # Reorder: drag P onto ICML03-081's slot (swaps them), pause to show it, then drag back -
    # restoring the original order matters here, since the Ctrl+PageUp walk below counts on it.
    DragScreen $px $py $icmlx $icmly
    Start-Sleep -Milliseconds 1200
    DragScreen $icmlx $icmly $px $py
    Start-Sleep -Milliseconds 800

    # Detach: drag P straight down, well clear of the strip (400px, comfortably past the ~32 DIP
    # trigger margin at any DPI scale this script targets) - a new window opens under the cursor.
    $r = Get-Rect $h
    $px, $py = TabPoint $r $pTabX $tabRowY
    DragScreen $px $py $px ($py + 400)
    Start-Sleep -Milliseconds 1200

    # The new window has the exact same title as the main one ("Caly Pdf Reader" - it's a plain
    # MainWindow, see CreateDetachedHost), so identify it by elimination rather than by title.
    $detachedH = (Find-AllWindowsByTitle $proc.Id 'Caly Pdf Reader') | Where-Object { $_ -ne $h } | Select-Object -First 1
    if ($detachedH) {
        [CalyDemoWin32]::SetForegroundWindow($detachedH) | Out-Null
        Start-Sleep -Milliseconds 800

        # Reattach: drag the detached window's only tab back onto the end of the main strip.
        # Docking there (rather than wherever Tabalonia's live reorder preview would put it)
        # restores the original 5-tab order, and per Tabalonia's rules (see
        # Caly.Tests/docs/tab-detach-reattach-tests.md, M3) the now-empty detached window closes
        # itself once the tab leaves it - nothing here needs to close it explicitly.
        $dr = Get-Rect $detachedH
        $dtx, $dty = TabPoint $dr $fileCTabX $tabRowY
        $r = Get-Rect $h
        $dockX, $dockY = TabPoint $r 1900.0 $tabRowY   # past ICML03-081's right edge
        DragScreen $dtx $dty $dockX $dockY
        Start-Sleep -Milliseconds 1200
    }
    else {
        Write-Host "Detached window not found - leaving it undocked."
    }
    $h = Focus $proc

    # --- Text selection demo, on ICML03-081's abstract paragraph. Click its tab directly (via
    # the same reference coordinates used for the reorder drag above) rather than counting
    # Ctrl+PageUp/Down presses, since that stays correct regardless of tab order. A plain
    # left-drag (no Ctrl) selects text - Ctrl+drag is reserved for panning, see
    # TextSelectionInputHandler.OnPointerPressed's e.IsPanningOrZooming() check - so no
    # CtrlDown/CtrlUp here, unlike the pan demo earlier. Ctrl+C copies the selection, mirroring
    # what a user would do next. Clicking back to the P tab afterwards restores the state the
    # Ctrl+PageUp walk below expects (P.pdf active).
    $r = Get-Rect $h
    $icmlx, $icmly = TabPoint $r $icmlTabX $tabRowY
    ClickAbs $icmlx $icmly
    Start-Sleep -Milliseconds 1200
    Drag $h 'TextSelectStart' 'TextSelectEnd'
    Start-Sleep -Milliseconds 1200
    Keys '^c' 500
    Start-Sleep -Milliseconds 800
    $r = Get-Rect $h
    $px, $py = TabPoint $r $pTabX $tabRowY
    ClickAbs $px $py
    Start-Sleep -Milliseconds 800
    $h = Focus $proc

    # --- Switch back through the tabs with Ctrl+PageUp (Ctrl+PageDown goes the other way) -
    # each newly opened document became the active tab in turn, so this walks back through all
    # of them to the original one. ---
    for ($i = 0; $i -lt $AdditionalPdfs.Count; $i++) {
        Keys '^{PGUP}' 900
    }

    # --- Thumbnails panel ---
    Click $h 'ThumbnailsTab'
    Start-Sleep -Milliseconds 1800

    # --- Bookmarks panel: FileC's real outline, jump via a top-level entry ---
    Click $h 'BookmarksTab'
    Start-Sleep -Milliseconds 1200
    Click $h 'BookmarkEntry'
    Start-Sleep -Milliseconds 1800

    # Scroll the document with the mouse wheel while the Bookmarks panel stays open, to show the
    # active/highlighted bookmark tracking the current page as you scroll
    # (DocumentViewModel.Bookmarks.cs UpdateActiveBookmark) rather than just a one-shot
    # click-to-jump. A plain wheel event routes to whatever's under the cursor (hit-tested, not
    # keyboard-focus based), so - unlike PageDown, which would hit the Bookmarks tree's own
    # focus and either no-op or force an unwanted GoToPage jump by moving the TREE's selection
    # (same class of bug as the Ctrl+G fix below) - it just needs the cursor hovering the page.
    # Ticks fire close together (30ms apart) rather than the earlier 90ms - a real wheel spun by
    # hand fires ticks in a fast burst, and the wider spacing read as a slow, chunky jump rather
    # than a scroll.
    Wheel $h 'MainContent' -60 30

    # --- Search panel: type a term, show grouped results, jump to one ---
    Click $h 'SearchTab'
    Start-Sleep -Milliseconds 600
    Keys 'the' 1500
    Click $h 'SearchResult'
    Start-Sleep -Milliseconds 1800

    # --- Document properties ---
    Click $h 'PropertiesTab'
    Start-Sleep -Milliseconds 2200

    # --- Settings pane: Rendering tab (default), then Debug tab ---
    Click $h 'SettingsGear'
    Start-Sleep -Milliseconds 1400
    Click $h 'SettingsDebugTab'
    Start-Sleep -Milliseconds 1600
    Click $h 'SettingsGear'   # close
    Start-Sleep -Milliseconds 500

    # --- Back to page 1 via Ctrl+G (go to page) ---
    # Ctrl+G is a KeyBinding owned by DocumentTabView, which only fires when focus is inside that
    # control's visual subtree. The Settings gear button lives outside it, so closing Settings
    # leaves focus stranded there and Ctrl+G silently no-ops. Click the page content first to pull
    # focus back into the document view.
    Click $h 'MainContent'
    Start-Sleep -Milliseconds 300
    Keys '^g' 400
    Keys '^a' 150
    Keys '1' 150
    Keys '{ENTER}' 1500

    Write-Host "Done. Closing Caly."
    $proc.Refresh()
    $proc.CloseMainWindow() | Out-Null
    if (-not $proc.WaitForExit(15000)) { $proc.Kill() }
}
finally {
    if ($backup) { [System.IO.File]::WriteAllText($settingsPath, $backup) } else { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
}
