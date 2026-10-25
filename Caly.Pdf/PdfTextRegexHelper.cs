using System.Text.RegularExpressions;

namespace Caly.Pdf
{
    internal partial class PdfTextRegexHelper
    {
        // Not RegexOptions.NonBacktracking: the source generator doesn't support it and falls back to building
        // the regex at runtime, which keeps the whole regex engine in the Native AOT binary. The backtracking
        // engine finds the same matches for this pattern and stays linear on long lines; the timeout stays as a guard.
        [GeneratedRegex(@"(((https?|ftps?):\/\/)|www\.)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}(?::(?:0|[1-9]\d{0,3}|[1-5]\d{4}|6[0-4]\d{3}|65[0-4]\d{2}|655[0-2]\d|6553[0-5]))?(?:\/(?:[-a-zA-Z0-9@%_\+.~#?&=]+\/?)*)?",
            RegexOptions.None, 5_000)]
        public static partial Regex UrlMatch();
    }
}
