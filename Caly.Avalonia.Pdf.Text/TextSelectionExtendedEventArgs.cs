using System;

namespace Caly.Avalonia.Pdf.Text;

public sealed class TextSelectionExtendedEventArgs : EventArgs
{
    public int AnchorPageIndex { get; init; } = -1;

    public int FocusPageIndex { get; init; } = -1;
}