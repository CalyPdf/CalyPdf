using System;

namespace Caly.Avalonia.Pdf.Text;

public sealed class TextSelectionFocusPageChangedEventArgs : EventArgs
{
    public int OldFocusPageIndex { get; init; } = -1;

    public int NewFocusPageIndex { get; init; } = -1;
}
