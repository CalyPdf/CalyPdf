using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace Caly.Pdf.Models;

public sealed class PdfPreferences
{
    public PdfPageLayout PageLayout { get; private set; } = PdfPageLayout.CalyDefault;

    public PdfPageMode PageMode { get; private set; } = PdfPageMode.CalyDefault;

    // Table 147 — Entries in a viewer preferences dictionary

    public bool HideToolbar { get; private set; }

    public bool HideMenubar { get; private set; }

    public bool HideWindowUI { get; private set; }

    public bool FitWindow { get; private set; }

    public bool CenterWindow { get; private set; }

    public bool DisplayDocTitle { get; private set; }

    public PdfNonFullScreenPageMode NonFullScreenPageMode { get; private set; }

    public static PdfPreferences GetPdfPreferences(PdfDocument pdfDocument)
    {
        try
        {
            return GetPdfPreferencesInternal(pdfDocument);
        }
        catch (Exception)
        {
            // TODO - Log
            return new PdfPreferences();
        }
    }

    private static PdfPreferences GetPdfPreferencesInternal(PdfDocument pdfDocument)
    {
        var scanner = pdfDocument.Structure.TokenScanner;
        var catalog = pdfDocument.Structure.Catalog.CatalogDictionary;

        var pageLayout = PdfPageLayout.CalyDefault;
        if (catalog.TryGet<NameToken>(NameToken.PageLayout, scanner, out var pageLayoutToken))
        {
            pageLayout = ParseName(pageLayoutToken, pageLayout);
        }

        var pageMode = PdfPageMode.CalyDefault;
        if (catalog.TryGet<NameToken>(NameToken.PageMode, scanner, out var pageModeToken))
        {
            pageMode = ParseName(pageModeToken, pageMode);
        }

        if (catalog.TryGet<DictionaryToken>(NameToken.OpenAction, scanner, out var actionDict))
        {
            // TODO - Need to expose logic in PdfPig
        }
        else if (catalog.TryGet<ArrayToken>(NameToken.OpenAction, scanner, out var actionArr))
        {
            // TODO - Need to expose logic in PdfPig
        }

        bool hideToolbar = false;
        bool hideMenubar = false;
        bool hideWindowUI = false;
        bool fitWindow = false;
        bool centerWindow = false;
        bool displayDocTitle = false;
        var nonFullScreenPageMode = PdfNonFullScreenPageMode.UseNone;
        if (catalog.TryGet<DictionaryToken>(NameToken.ViewerPreferences, scanner, out var viewerPrefDict))
        {
            if (viewerPrefDict.TryGet<BooleanToken>(NameToken.HideToolbar, scanner, out var hideToolbarToken))
            {
                hideToolbar = hideToolbarToken.Data;
            }

            if (viewerPrefDict.TryGet<BooleanToken>(NameToken.HideMenubar, scanner, out var hideMenubarToken))
            {
                hideMenubar = hideMenubarToken.Data;
            }

            if (viewerPrefDict.TryGet<BooleanToken>(NameToken.HideWindowui, scanner, out var hideWindowUIToken))
            {
                hideWindowUI = hideWindowUIToken.Data;
            }

            if (viewerPrefDict.TryGet<BooleanToken>(NameToken.FitWindow, scanner, out var fitWindowToken))
            {
                fitWindow = fitWindowToken.Data;
            }

            if (viewerPrefDict.TryGet<BooleanToken>(NameToken.CenterWindow, scanner, out var centerWindowToken))
            {
                centerWindow = centerWindowToken.Data;
            }

            if (viewerPrefDict.TryGet<BooleanToken>(NameToken.DisplayDocTitle, scanner, out var displayDocTitleToken))
            {
                displayDocTitle = displayDocTitleToken.Data;
            }

            if (viewerPrefDict.TryGet<NameToken>(NameToken.NonFullScreenPageMode, scanner, out var nonFullScreenPageModeToken))
            {
                nonFullScreenPageMode = ParseName(nonFullScreenPageModeToken, nonFullScreenPageMode);
            }
        }

        return new PdfPreferences
        {
            PageLayout = pageLayout,
            PageMode = pageMode,
            HideToolbar = hideToolbar,
            HideMenubar = hideMenubar,
            HideWindowUI = hideWindowUI,
            FitWindow = fitWindow,
            CenterWindow = centerWindow,
            DisplayDocTitle = displayDocTitle,
            NonFullScreenPageMode = nonFullScreenPageMode
        };
    }

    /// <summary>
    /// Maps a PDF name to the enum member of the same name, or returns <paramref name="fallback"/>
    /// when there is none. Unlike <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>, this
    /// never yields <c>default</c> on failure, and rejects numeric strings, comma-separated flag
    /// lists and the non-PDF <c>CalyDefault</c> member.
    /// </summary>
    private static T ParseName<T>(NameToken token, T fallback) where T : struct, Enum
    {
        foreach (var name in Enum.GetNames<T>())
        {
            if (name != nameof(PdfPageLayout.CalyDefault) &&
                name.Equals(token.Data, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<T>(name);
            }
        }

        return fallback;
    }

    private static readonly XNamespace XmpDcNamespace = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace XmpRdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>
    /// Reads the Dublin Core title from XMP metadata. <c>dc:title</c> is a language alternative
    /// (<c>rdf:Alt</c>) holding one <c>rdf:li</c> per language: prefer the <c>x-default</c> entry,
    /// then the first one.
    /// </summary>
    public static string? GetXmpTitle(XDocument metadata)
    {
        var titleElement = metadata.Descendants(XmpDcNamespace + "title").FirstOrDefault();
        if (titleElement is null)
        {
            return null;
        }

        var items = titleElement.Descendants(XmpRdfNamespace + "li").ToArray();
        if (items.Length == 0)
        {
            // Not a well-formed language alternative, fall back to the plain text value.
            return titleElement.Value.Trim();
        }

        var item = items.FirstOrDefault(li => (string?)li.Attribute(XNamespace.Xml + "lang") == "x-default")
                   ?? items[0];

        return item.Value.Trim();
    }
}

public enum PdfNonFullScreenPageMode : byte
{
    CalyDefault = byte.MaxValue,

    /// <summary>
    /// Neither document outline nor thumbnail images visible.
    /// </summary>
    UseNone = 0,

    /// <summary>
    /// Document outline visible.
    /// </summary>
    UseOutlines,

    /// <summary>
    /// Thumbnail images visible.
    /// </summary>
    UseThumbs,

    /// <summary>
    /// Optional content group panel visible.
    /// </summary>
    UseOC
}

public enum PdfPageMode : byte
{
    CalyDefault = byte.MaxValue,

    /// <summary>
    /// Neither document outline nor thumbnail images visible.
    /// </summary>
    None = 0,

    /// <summary>
    /// Document outline visible.
    /// </summary>
    UseOutlines,

    /// <summary>
    /// Thumbnail images visible.
    /// </summary>
    UseThumbs,

    /// <summary>
    /// Full-screen mode, with no menu bar, window controls, or any other window visible.
    /// </summary>
    FullScreen,

    /// <summary>
    /// Optional content group panel visible.
    /// </summary>
    UseOC,

    /// <summary>
    /// Attachments panel visible.
    /// </summary>
    UseAttachments
}

public enum PdfPageLayout : byte
{
    CalyDefault = byte.MaxValue,

    /// <summary>
    /// Display one page at a time.
    /// </summary>
    SinglePage = 0,

    /// <summary>
    /// Display the pages in one column.
    /// </summary>
    OneColumn,

    /// <summary>
    /// Display the pages in two columns, with odd-numbered pages on the left.
    /// </summary>
    TwoColumnLeft,

    /// <summary>
    /// Display the pages in two columns, with odd-numbered pages on the right.
    /// </summary>
    TwoColumnRight,

    /// <summary>
    /// Display the pages two at a time, with odd-numbered pages on the left.
    /// </summary>
    TwoPageLeft,

    /// <summary>
    /// Display the pages two at a time, with odd-numbered pages on the right.
    /// </summary>
    TwoPageRight
}
