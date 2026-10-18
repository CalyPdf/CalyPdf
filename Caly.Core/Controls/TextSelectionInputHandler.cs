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
using Avalonia;
using Avalonia.Input;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Pdf.Models;
using UglyToad.PdfPig.Actions;

namespace Caly.Core.Controls;

/// <summary>
/// Handles pointer input over the pages' interactive layers on behalf of
/// <see cref="PageItemsControl"/>: text selection (single/multiple click and drag),
/// annotation display, link activation, and hover cursor/status feedback.
/// Owns the pointer-interaction state so the control itself stays focused on
/// layout and virtualization. The pure selection semantics live in
/// <see cref="TextSelectionLogic"/>.
/// </summary>
internal sealed class TextSelectionInputHandler
{
    private readonly PageItemsControl _owner;

    /// <summary>
    /// <c>true</c> if we are currently selecting text. <c>false</c> otherwise.
    /// </summary>
    private bool _isSelecting;

    /// <summary>
    /// <c>true</c> if we are selecting text though multiple click (full word selection).
    /// </summary>
    private bool _isMultipleClickSelection;

    private Point? _startPointerPressed;

    /// <summary>
    /// How far, in page units, the pointer may stray between press and release and still have
    /// clicked a 3D artwork rather than dragged across it.
    /// </summary>
    private const double Artwork3DClickTolerance = 3.0;

    // The artwork last hovered and the status text said for it.
    private PdfArtwork3D? _hoveredArtwork3D;
    private string? _hoveredArtwork3DLabel;

    public TextSelectionInputHandler(PageItemsControl owner)
    {
        ArgumentNullException.ThrowIfNull(owner, nameof(owner));
        _owner = owner;
    }

    /// <summary>
    /// Clears the pointer-interaction state, e.g. when the owner's DataContext changes.
    /// </summary>
    public void Reset()
    {
        _isSelecting = false;
        _isMultipleClickSelection = false;
        _startPointerPressed = null;
    }

    public void OnPointerPressed(PageInteractiveLayerControl control, PointerPressedEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        var textSelection = _owner.TextSelection;
        if (textSelection is null || control.PdfTextLayer is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            // Panning pages is not handled here
            control.HideAnnotation();
            return;
        }

        bool clearSelection = false;

        _isMultipleClickSelection = e.ClickCount > 1;

        var pointerPoint = e.GetCurrentPoint(control);
        var point = pointerPoint.Position;

        if (pointerPoint.Properties.IsLeftButtonPressed)
        {
            _startPointerPressed = point;

            // Text selection
            PdfWord? word = control.PdfTextLayer.FindWordOver(point.X, point.Y);

            if (word is not null && textSelection.IsWordSelected(control.PageNumber!.Value, word))
            {
                clearSelection = e.ClickCount == 1; // Clear selection if single click
                if (e.ClickCount >= 2)
                {
                    HandleMultipleClick(control, e, word);
                }
            }
            else if (word is not null && e.ClickCount == 2)
            {
                // TODO - do better multiple click selection
                HandleMultipleClick(control, e, word);
            }
            else
            {
                clearSelection = true;
            }
        }
        else if (pointerPoint.Properties.IsRightButtonPressed)
        {
            // Always hide annotation on right-click to not conflict with context flyout. This works
            // on Windows but would need to be tested on other platforms
            control.HideAnnotation();
        }

        if (clearSelection)
        {
            _owner.ClearSelection?.Execute(null);
        }

        e.Handled = true;
        e.PreventGestureRecognition();
    }

    private void HandleMultipleClick(PageInteractiveLayerControl control, PointerPressedEventArgs e, PdfWord word)
    {
        // The only caller (OnPointerPressed) has already verified both are non-null.
        System.Diagnostics.Debug.Assert(_owner.TextSelection is not null && control.PdfTextLayer is not null);
        var textSelection = _owner.TextSelection!;

        if (!TextSelectionLogic.TryGetMultipleClickSelection(control.PdfTextLayer!, word, e.ClickCount,
                out PdfWord startWord, out PdfWord endWord))
        {
            System.Diagnostics.Debug.WriteLine($"HandleMultipleClick: Not handled, got {e.ClickCount} click(s).");
            return;
        }

        _owner.ClearSelection?.Execute(null);

        int pageNumber = control.PageNumber!.Value;
        textSelection.Start(pageNumber, startWord);
        textSelection.Extend(pageNumber, endWord);

        System.Diagnostics.Debug.WriteLine($"HandleMultipleClick: {startWord} -> {endWord}.");
    }

    public void OnPointerReleased(PageInteractiveLayerControl control, PointerReleasedEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        if (control.PdfTextLayer is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            // Panning pages is not handled here
            return;
        }

        Point? startPointerPressed = _startPointerPressed;
        _startPointerPressed = null;

        var pointerPoint = e.GetCurrentPoint(control);

        bool ignore = _isSelecting || _isMultipleClickSelection;
        if (!ignore && pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _owner.ClearSelection?.Execute(null);

            var point = pointerPoint.Position;

            // 3D artwork comes first, as it does when hovering: a click on it asks to view it.
            if (TryActivateArtwork3D(control, startPointerPressed, point))
            {
                // Nothing else lies under a 3D artwork that a click could mean.
            }
            else if (!HandleAnnotationAction(_owner, control.PdfTextLayer.FindAnnotationOver(point.X, point.Y)))
            {
                // Annotation not found or action not handled, we check for words.
                // Words
                PdfWord? word = control.PdfTextLayer.FindWordOver(point.X, point.Y);
                if (word is not null && control.PdfTextLayer.GetLine(word) is { IsInteractive: true } line)
                {
                    /*
                     * TODO - Use TopLevel.GetTopLevel(source)?.Launcher
                     *  if (e.Source is Control source && TopLevel.GetTopLevel(source)?.Launcher is {}
                     *  launcher && word is not null && control.PdfTextLayer.GetLine(word) is { IsInteractive: true } line)
                     *  ...
                     *  launcher.LaunchUriAsync(new Uri(match.ToString()))
                     */

                    if (!string.IsNullOrEmpty(line.InteractiveLink))
                    {
                        CalyExtensions.OpenUriAsync(line.InteractiveLink);
                    }
                }
            }
        }

        _isSelecting = false;

        // Right-button releases must stay unhandled: this runs in the tunnel phase, and
        // Control.OnPointerReleased on the layer only raises ContextRequested (which
        // opens the page's ContextFlyout) for unhandled events. Opening the flyout
        // marks the event handled right after, so the rest of the route is unaffected.
        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            e.Handled = true;
        }

        e.PreventGestureRecognition();
    }

    /// <summary>
    /// Asks the owner to show the 3D artwork under a click, if there is one.
    /// </summary>
    /// <returns><c>true</c> if the click was on 3D artwork, whether or not anything can show it.</returns>
    private bool TryActivateArtwork3D(PageInteractiveLayerControl control, Point? pressed, Point released)
    {
        if (control.PdfTextLayer is not { } textLayer || !control.PageNumber.HasValue)
        {
            return false;
        }

        PdfArtwork3D? artwork = textLayer.FindArtwork3DOver(released.X, released.Y, out int index);
        if (artwork is null)
        {
            return false;
        }

        // A press that wandered off before the release was a drag, even where there was no text to select.
        if (!pressed.HasValue || pressed.Value.Euclidean(released) > Artwork3DClickTolerance)
        {
            return true;
        }

        if (_owner.ActivateArtwork3D is { } command)
        {
            control.HideAnnotation();
            var request = new Artwork3DActivationRequest(control.PageNumber.Value, index, artwork);
            if (command.CanExecute(request))
            {
                command.Execute(request);
            }
        }

        return true;
    }

    /// <summary>
    /// Handles the annotation's action.
    /// </summary>
    /// <returns><c>true</c> if the action was handled, <c>false</c> otherwise.</returns>
    private static bool HandleAnnotationAction(PageItemsControl owner, PdfAnnotation? annotation)
    {
        if (annotation?.Action is null)
        {
            return false;
        }
        
        switch (annotation.Action.Type)
        {
            case ActionType.URI:
                string? uri = ((UriAction)annotation.Action)?.Uri;
                if (!string.IsNullOrEmpty(uri))
                {
                    CalyExtensions.OpenUriAsync(uri);
                    return true;
                }
                break;

            case ActionType.GoTo:
            case ActionType.GoToE:
            case ActionType.GoToR:
                var goToAction = (AbstractGoToAction)annotation.Action;
                var dest = goToAction?.Destination;
                if (dest is not null)
                {
                    // Ignore destination types for the moment
                    if (dest.Coordinates.Top.HasValue)
                    {
                        double scaledTop = dest.Coordinates.Top.Value * annotation.PpiScale;
                        // A null left keeps the current horizontal position.
                        double? scaledLeft = dest.Coordinates.Left * annotation.PpiScale;
                        owner.GoToPage(dest.PageNumber, scaledTop, scaledLeft, true);
                    }
                    else
                    {
                        owner.GoToPage(dest.PageNumber, 0); // Top of page
                    }
                    return true;
                }

                // TODO - Log error
                break;
        }

        return false;
    }
    
    public void OnPointerExited(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        if (sender is not PageInteractiveLayerControl interactiveLayer)
        {
            return;
        }

        interactiveLayer.SetDefaultCursor();
        interactiveLayer.HideAnnotation();
        _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, null);
    }

    public void OnPointerMoved(PageInteractiveLayerControl control, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();

        // Needs to be on UI thread to access
        if (control.PdfTextLayer is null)
        {
            return;
        }

        if (e.IsPanningOrZooming())
        {
            // Panning pages is not handled here
            return;
        }

        var pointerPoint = e.GetCurrentPoint(control);
        var loc = pointerPoint.Position;

        if (e is PointerWheelEventArgs we)
        {
            // TODO - Looks like there's a bug in Avalonia (TBC) where the position of the pointer
            // is 1 step behind the actual position.
            // We need to add back this step (1 scroll step is 50, see link below)
            // https://github.com/AvaloniaUI/Avalonia/blob/dadc9ab69284bb228ad460f36d5442b4eee4a82a/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs#L684

            var adjPoint = new Point(50, 50);
            var matrix = control.GetLayoutTransformMatrix();

            if (!matrix.IsIdentity && matrix.TryInvert(out var inverted))
            {
                adjPoint = inverted.Transform(adjPoint);
            }

            double x = Math.Max(loc.X - we.Delta.X * adjPoint.X, 0);
            double y = Math.Max(loc.Y - we.Delta.Y * adjPoint.Y, 0);

            loc = new Point(x, y);

            // TODO - We have an issue when scrolling and changing page here, similar the TrySwitchCapture
            // not sure how we should address it
        }

        if (pointerPoint.Properties.IsLeftButtonPressed && _startPointerPressed.HasValue && _startPointerPressed.Value.Euclidean(loc) > 1.0)
        {
            // Text selection
            HandleMouseMoveSelection(control, e, loc);
        }
        else
        {
            HandleMouseMoveOver(control, pointerPoint.Properties, loc);
        }
    }

    private void HandleMouseMoveSelection(PageInteractiveLayerControl control, PointerEventArgs e, Point loc)
    {
        var textSelection = _owner.TextSelection;
        if (_isMultipleClickSelection || textSelection is null)
        {
            return;
        }

        if (!control.Bounds.Contains(loc))
        {
            _owner.TrySwitchCapture(e);
            return;
        }

        // Get the line under the cursor or nearest from the top
        PdfTextLine? lineBox = control.PdfTextLayer!.FindLineOver(loc.X, loc.Y);

        PdfWord? word = null;
        if (textSelection.HasStarted && lineBox is null)
        {
            // Try to find the closest line as we are already selecting something
            word = TextSelectionLogic.FindNearestWordWhileSelecting(loc.X, loc.Y, control.PdfTextLayer);
        }

        if (lineBox is null && word is null)
        {
            return;
        }

        if (lineBox is not null && word is null)
        {
            // Get the word under the cursor
            word = lineBox.FindWordOver(loc.X, loc.Y);

            // If no word found under the cursor use the last or the first word in the line
            if (word is null)
            {
                word = lineBox.FindNearestWord(loc.X, loc.Y);
            }
        }

        if (word is null)
        {
            return;
        }

        // If there is matching word. Partial (within-word) selection is always allowed
        // here: the multiple-click case already returned at the top of the method.
        if (!textSelection.HasStarted)
        {
            textSelection.Start(control.PageNumber!.Value, word, loc);
        }

        // Always set the focus word
        textSelection.Extend(control.PageNumber!.Value, word, loc);

        control.SetIbeamCursor();

        _isSelecting = textSelection.IsSelecting;
    }

    /// <summary>
    /// Handle mouse hover over words, links or others
    /// </summary>
    private void HandleMouseMoveOver(PageInteractiveLayerControl control, PointerPointProperties properties, Point loc)
    {
        // 3D artwork comes first: its annotation may also carry plain contents, and the artwork's
        // description (format, authored views) says more than those do.
        PdfArtwork3D? artwork = control.PdfTextLayer!.FindArtwork3DOver(loc.X, loc.Y, out int artworkIndex);
        if (artwork is not null)
        {
            if (!properties.IsRightButtonPressed)
            {
                control.ShowArtwork3D(artwork, artworkIndex);
            }

            // A hand where a click will open the model.
            if (control.GetArtwork3DHint(artwork, artworkIndex) == Artwork3DHint.ClickToView)
            {
                control.SetHandCursor();
            }
            else
            {
                control.SetDefaultCursor();
            }

            // Built once per artwork, not on every pointer move over it.
            if (!ReferenceEquals(_hoveredArtwork3D, artwork))
            {
                _hoveredArtwork3D = artwork;
                _hoveredArtwork3DLabel = $"3D model ({artwork.FormatDisplayName})";
            }

            _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, _hoveredArtwork3DLabel);
            return;
        }

        PdfAnnotation? annotation = control.PdfTextLayer!.FindAnnotationOver(loc.X, loc.Y);

        if (annotation is not null)
        {
            if (!string.IsNullOrEmpty(annotation.Content) && !properties.IsRightButtonPressed)
            {
                // We do not show annotation when right-clicking
                // to not conflict with context flyout. This works
                // on Windows but would need to be tested on other platforms
                control.ShowAnnotation(annotation);
            }

            if (annotation.IsInteractive)
            {
                control.SetHandCursor();
                if (annotation.Action is UriAction uriAction)
                {
                    _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, $"Open '{uriAction.Uri}'");
                }

                return;
            }
        }
        else
        {
            control.HideAnnotation();
        }

        PdfWord? word = control.PdfTextLayer!.FindWordOver(loc.X, loc.Y);
        if (word is not null)
        {
            if (control.PdfTextLayer.GetLine(word) is { IsInteractive: true } line)
            {
                control.SetHandCursor();
                _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, $"Open '{line.InteractiveLink}'");
            }
            else
            {
                control.SetIbeamCursor();
                _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, null);
            }
        }
        else
        {
            control.SetDefaultCursor();
            _owner.SetCurrentValue(PageItemsControl.InteractiveActionOverProperty, null);
        }
    }
}
