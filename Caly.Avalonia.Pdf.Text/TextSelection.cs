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

using Avalonia;
using Caly.Pdf.Models;
using System;

namespace Caly.Avalonia.Pdf.Text;

// References:
// - https://www.w3.org/TR/selection-api/
// - https://github.com/AvaloniaUI/Avalonia.HtmlRenderer/blob/master/Source/HtmlRenderer/Core/Handlers/SelectionHandler.cs
// - https://developer.mozilla.org/en-US/docs/Web/API/Selection

/// <summary>
/// A selection of text in a pdf document.
/// <para>
/// Note: Anchor and focus should not be confused with the start and end positions of a selection.
/// The anchor can be placed before the focus or vice versa, depending on the direction you made your selection.
/// </para>
/// See <see href="https://developer.mozilla.org/en-US/docs/Web/API/Selection"/>.
/// </summary>
public sealed partial class TextSelection
{
    /*
     * Note: Anchor and focus should not be confused with the start and end positions of a selection.
     * The anchor can be placed before the focus or vice versa, depending on the direction you made your selection.
     *
     * The anchor is where the user began the selection and the focus is where the user ends the selection. If you
     * make a selection with a desktop mouse, the anchor is placed where you pressed the mouse button, and the
     * focus is placed where you released the mouse button.
     */

    public event EventHandler<TextSelectionStartedEventArgs>? TextSelectionStarted;
    public event EventHandler<TextSelectionExtendedEventArgs>? TextSelectionExtended;
    public event EventHandler<TextSelectionFocusPageChangedEventArgs>? TextSelectionFocusPageChanged;
    public event EventHandler<EventArgs>? TextSelectionReset;

    /// <summary>
    /// The mouse location when selection started.
    /// <para>Used to ignore small selections.</para>
    /// </summary>
    public Point? AnchorPoint { get; private set; }

    public int AnchorPageIndex { get; private set; } = -1;

    public int FocusPageIndex { get; private set; } = -1;

    /// <summary>
    /// The <see cref="PdfWord"/> in which the selection begins. Can be <c>null</c> if selection never existed in the document.
    /// The anchor is where the user began the selection. If the selection is made with a desktop mouse, the anchor is placed where the user pressed the mouse button.
    /// <para>
    /// Note: Anchor and focus should not be confused with the start and end positions of a selection.
    /// The anchor can be placed before the focus or vice versa, depending on the direction you made your selection.
    /// </para>
    /// </summary>
    public PdfWord? AnchorWord { get; private set; }

    /// <summary>
    /// The <see cref="PdfWord"/> in which the selection ends. Can be <c>null</c> if selection never existed in the document.
    /// The focus is where the user ends the selection. If the selection is made with a desktop mouse, the focus is placed where the user released the mouse button.
    /// <para>
    /// Note: Anchor and focus should not be confused with the start and end positions of a selection.
    /// The anchor can be placed before the focus or vice versa, depending on the direction you made your selection.
    /// </para>
    /// </summary>
    public PdfWord? FocusWord { get; private set; }

    /// <summary>
    /// The number of characters that the selection's anchor is offset within the <see cref="AnchorWord"/>, if it is partially selected.
    /// <list type="bullet">
    /// <item>This number is zero-based. If the selection begins with the first character in the <see cref="AnchorWord"/>, the value is <c>0</c>.</item>
    /// <item>If the <see cref="AnchorWord"/> is not selected (<c>null</c>) or fully selected, the value is <c>-1</c>.</item>
    /// </list>
    /// </summary>
    public int AnchorOffset { get; private set; } = -1;

    /// <summary>
    /// The number of characters that the selection's focus is offset within the <see cref="FocusWord"/>, if it is partially selected.
    /// <list type="bullet">
    /// <item>This number is zero-based. If the selection ends with the first character in the <see cref="FocusWord"/>, the value is <c>0</c>.</item>
    /// <item>If the <see cref="FocusWord"/> is not selected (<c>null</c>) or fully selected, the value is <c>-1</c>.</item>
    /// </list>
    /// </summary>
    public int FocusOffset { get; private set; } = -1;

    /// <summary>
    /// The selection start offset distance if the first selected word is partially selected (-1 if not selected or fully selected).
    /// </summary>
    public double AnchorOffsetDistance { get; private set; } = -1;

    /// <summary>
    /// The selection end offset distance if the last selected word is partially selected (-1 if not selected or fully selected).
    /// </summary>
    public double FocusOffsetDistance { get; private set; } = -1;

    /// <summary>
    /// Is the selection backward, in reading order.
    /// <para>If the selection is backward, the anchor word comes after the focus word in reading order.</para>
    /// </summary>
    public bool IsBackward { get; private set; }

    /// <summary>
    /// Is the selection forward, in reading order.
    /// <para>If the selection is forward, the focus word comes after the anchor word in reading order.</para>
    /// </summary>
    public bool IsForward => !IsBackward;

    /// <summary>
    /// Has the selection started.
    /// </summary>
    public bool HasStarted => AnchorWord is not null;

    /// <summary>
    /// Is the selection valid.
    /// </summary>
    /// <returns><c>true</c> if both <see cref="AnchorWord"/> and <see cref="FocusWord"/> are defined. <c>false</c> otherwise.</returns>
    public bool IsValid => HasStarted && FocusWord is not null;

    public bool IsSelecting => IsValid &&
                               (AnchorWord != FocusWord || // Multiple words selected
                                (AnchorOffset != -1 && FocusOffset != -1)); // Selection within same word

#if DEBUG
    public int NumberOfPages;
#endif

    public TextSelection(int numberOfPages)
    {
#if DEBUG
        NumberOfPages = numberOfPages;
#endif
    }

    /// <summary>
    /// Get the index of the last word in a page, taking in account the selection direction.
    /// </summary>
    internal Index GetLastWordIndex()
    {
        return new Index(IsBackward ? 0 : 1, !IsBackward);
    }

    /// <summary>
    /// Get the index of the first word in a page, taking in account the selection direction.
    /// </summary>
    internal Index GetFirstWordIndex()
    {
        return new Index(IsBackward ? 1 : 0, IsBackward);
    }

    /// <summary>
    /// Get the start page index, after having checked for selection direction.
    /// </summary>
    public int GetStartPageIndex()
    {
        return IsForward ? AnchorPageIndex : FocusPageIndex;
    }

    /// <summary>
    /// Get the end page index, after having checked for selection direction.
    /// </summary>
    public int GetEndPageIndex()
    {
        return IsForward ? FocusPageIndex : AnchorPageIndex;
    }

    /// <summary>
    /// Start the selection and set the anchor of the selection to a specified point.
    /// </summary>
    /// <param name="pageNumber">The page number where the anchor word is.</param>
    /// <param name="word">The word within which the anchor will be moved.</param>
    /// <param name="location">The location of the anchor. Should NOT be <c>null</c> if 'Allow partial select'. <c>null</c> otherwise.</param>
    public void Start(int pageNumber, PdfWord? word, Point? location = null)
    {
#if DEBUG
        System.Diagnostics.Debug.Assert(pageNumber <= NumberOfPages);
#endif

        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), $"The page number of the anchor should be greater or equal to 1. Current value is {pageNumber}");
        }

        AnchorPageIndex = pageNumber;
        AnchorWord = word;
        AnchorPoint = location;

        if (location.HasValue)
        {
            if (word is null)
            {
                throw new NullReferenceException("Cannot have null word when updating anchor with a location.");
            }

            // Allow partial select
            CalculateWordCharIndexAndOffset(word, location.Value, true, out int index, out double offset);
            AnchorOffset = index;
            AnchorOffsetDistance = offset;
        }
        else
        {
            AnchorOffset = -1;
            AnchorOffsetDistance = -1;
        }

        TextSelectionStarted?.Invoke(this, new TextSelectionStartedEventArgs()
        {
            AnchorPageIndex = AnchorPageIndex
        });
    }

    /// <summary>
    /// Moves the focus of the selection to a specified point.
    /// </summary>
    /// <param name="pageNumber">The page number where the focus word is.</param>
    /// <param name="word">The word within which the focus will be moved.</param>
    /// <param name="location">The location of the focus. Should NOT be <c>null</c> if 'Allow partial select'. <c>null</c> otherwise.</param>
    public void Extend(int pageNumber, PdfWord? word, Point? location = null)
    {
#if DEBUG
        System.Diagnostics.Debug.Assert(pageNumber <= NumberOfPages);
#endif

        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), $"The page number of the focus should be greater or equal to 1. Current value is {pageNumber}");
        }

        int currentFocusPageIndex = FocusPageIndex;

        FocusPageIndex = pageNumber;
        FocusWord = word;

        if (location.HasValue)
        {
            if (word is null)
            {
                throw new NullReferenceException("Cannot have null word when updating focus with a location.");
            }

            // Allow partial select
            CalculateWordCharIndexAndOffset(word, location.Value, false, out int index, out double offset);
            FocusOffset = index;
            FocusOffsetDistance = offset;
        }
        else
        {
            FocusOffset = -1;
            FocusOffsetDistance = -1;
        }

        UpdateSelectionDirection();

        TextSelectionExtended?.Invoke(this, new TextSelectionExtendedEventArgs()
        {
            AnchorPageIndex = AnchorPageIndex,
            FocusPageIndex = FocusPageIndex
        });

        // Check for change of focus page
        if (currentFocusPageIndex != FocusPageIndex)
        {
            TextSelectionFocusPageChanged?.Invoke(this, new TextSelectionFocusPageChangedEventArgs()
            {
                OldFocusPageIndex = currentFocusPageIndex,
                NewFocusPageIndex = FocusPageIndex
            });
        }
    }

    /// <summary>
    /// Reset document selection: anchor and focus words, page indexes, offsets, indexes and clears selected words.
    /// </summary>
    public void ResetSelection()
    {
        AnchorOffsetDistance = -1;
        AnchorOffset = -1;
        FocusOffsetDistance = -1;
        FocusOffset = -1;

        AnchorPageIndex = -1;
        FocusPageIndex = -1;
        AnchorPoint = null;
        AnchorWord = null;
        FocusWord = null;

        TextSelectionReset?.Invoke(this, EventArgs.Empty);
    }

    public bool IsPageInSelection(int pageNumber)
    {
        return pageNumber >= GetStartPageIndex() && pageNumber <= GetEndPageIndex();
    }

    public bool IsWordSelected(int pageNumber, PdfWord word)
    {
#if DEBUG
        System.Diagnostics.Debug.Assert(pageNumber <= NumberOfPages);
#endif

        // TODO - handle word sub selection
        if (!IsValid)
        {
            return false;
        }

        // TODO - Need proper testing
        if (!IsPageInSelection(pageNumber))
        {
            return false;
        }

        if (IsBackward)
        {
            return word.IndexInPage >= FocusWord!.IndexInPage && word.IndexInPage <= AnchorWord!.IndexInPage;
        }

        return word.IndexInPage >= AnchorWord!.IndexInPage && word.IndexInPage <= FocusWord!.IndexInPage;
    }

    /// <summary>
    /// Check if the selection direction is forward or backward.<br/>
    /// Is the selection anchor word is before the focus word.
    /// </summary>
    private void UpdateSelectionDirection()
    {
        if (!IsValid)
        {
            return;
        }

        if (AnchorPageIndex != -1 && FocusPageIndex != -1 &&
            AnchorPageIndex != FocusPageIndex)
        {
            IsBackward = AnchorPageIndex > FocusPageIndex;
        }
        else if (AnchorWord == FocusWord)
        {
            IsBackward = AnchorOffset > FocusOffset;
        }
        else
        {
            IsBackward = AnchorWord!.IndexInPage > FocusWord!.IndexInPage;
        }
    }

    /// <summary>
    /// Calculate the character index and offset by characters for the given word and given offset.<br/>
    /// If the location is below the word line then set the selection to the end.<br/>
    /// If the location is to the right of the word then set the selection to the end.<br/>
    /// If the offset is to the left of the word set the selection to the beginning.<br/>
    /// Otherwise calculate the width of each substring to find the char the location is on.
    /// </summary>
    /// <param name="word">the word to calculate its index and offset</param>
    /// <param name="loc">the location to calculate for</param>
    /// <param name="inclusive">is to include the first character in the calculation</param>
    /// <param name="selectionIndex">return the index of the char under the location</param>
    /// <param name="selectionOffset">return the offset of the char under the location</param>
    private static void CalculateWordCharIndexAndOffset(PdfWord word, Point loc, bool inclusive,
        out int selectionIndex, out double selectionOffset)
    {
        selectionOffset = 0;

        if (word.Count == 0)
        {
            // not a text word - set full selection
            selectionIndex = -1;
            selectionOffset = -1;
        }
        else // TODO - only select letter when cursor is over second half of bbox (careful with bidi text)
        {
            int index = word.FindLetterIndexOver(loc.X, loc.Y);
            if (index == -1)
            {
                index = word.FindNearestLetterIndex(loc.X, loc.Y);
            }

            if (index > -1)
            {
                selectionOffset = word.GetWithinLetterOffset(index, loc.X, loc.Y);
            }

            selectionIndex = index;
        }
    }
}
