using System;

namespace Caly.Avalonia.Pdf.Text;

public sealed class TextSelectionStartedEventArgs : EventArgs
{
    public int AnchorPageIndex { get; init; } = -1;
}