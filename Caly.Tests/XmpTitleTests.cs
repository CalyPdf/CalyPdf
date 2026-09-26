using Caly.Core.Services;
using Caly.Pdf.Models;
using System.Xml.Linq;

namespace Caly.Tests;

public class XmpTitleTests
{
    private static XDocument Xmp(string description) => XDocument.Parse(
        $"""
         <x:xmpmeta xmlns:x="adobe:ns:meta/">
           <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
             <rdf:Description rdf:about=""
                 xmlns:dc="http://purl.org/dc/elements/1.1/"
                 xmlns:other="http://example.com/other/">
               {description}
             </rdf:Description>
           </rdf:RDF>
         </x:xmpmeta>
         """);

    [Fact]
    public void PrefersXDefaultEntry()
    {
        var xmp = Xmp("""
                      <dc:title><rdf:Alt>
                        <rdf:li xml:lang="fr-FR">Rapport</rdf:li>
                        <rdf:li xml:lang="x-default"> Report </rdf:li>
                      </rdf:Alt></dc:title>
                      """);

        Assert.Equal("Report", PdfPreferences.GetXmpTitle(xmp));
    }

    [Fact]
    public void FallsBackToFirstEntryWithoutXDefault()
    {
        var xmp = Xmp("""
                      <dc:title><rdf:Alt>
                        <rdf:li xml:lang="fr-FR">Rapport</rdf:li>
                        <rdf:li xml:lang="en-GB">Report</rdf:li>
                      </rdf:Alt></dc:title>
                      """);

        Assert.Equal("Rapport", PdfPreferences.GetXmpTitle(xmp));
    }

    [Fact]
    public void IgnoresTitleFromOtherNamespaces()
    {
        var xmp = Xmp("""
                      <other:title>Not this one</other:title>
                      <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Report</rdf:li></rdf:Alt></dc:title>
                      """);

        Assert.Equal("Report", PdfPreferences.GetXmpTitle(xmp));
    }

    [Fact]
    public void FallsBackToPlainTextValue()
    {
        var xmp = Xmp("<dc:title>Report</dc:title>");

        Assert.Equal("Report", PdfPreferences.GetXmpTitle(xmp));
    }

    [Fact]
    public void ReturnsNullWithoutDcTitle()
    {
        var xmp = Xmp("<other:title>Not this one</other:title>");

        Assert.Null(PdfPreferences.GetXmpTitle(xmp));
    }
}
