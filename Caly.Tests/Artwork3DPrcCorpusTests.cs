using Caly.Pdf.Artwork;
using Scene3D;
using Scene3D.Prc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Artwork;

namespace Caly.Tests;

/// <summary>
/// PRC artwork of real documents (CALY_3D_SAMPLES; skipped without it): what the PDF's views say about
/// the model has to find its place in the scene the reader builds.
/// </summary>
public class Artwork3DPrcCorpusTests(ITestOutputHelper output)
{
    // Entity types of PRC: what a node name's identifier says the node is.
    private const uint ProductOccurrence = 310, BrepModel = 232, Set = 238, PolyBrepModel = 239;

    private static IReadOnlyList<string> AllDocuments()
    {
        string? root = Environment.GetEnvironmentVariable("CALY_3D_SAMPLES");
        return Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }

    /// <summary>
    /// The type of the entity a node name refers to: the fifth unsigned integer of its identifier, after
    /// the four of the file structure (see <see cref="PrcNodeNames"/>); 0 for a name without one.
    /// </summary>
    private static uint TypeOf(string name)
    {
        ReadOnlySpan<char> hex = PrcNodeNames.IdentifierOf(name);
        if (hex.IsEmpty)
        {
            return 0;
        }

        byte[] bytes = Convert.FromHexString(hex);
        int bit = 0;
        uint value = 0;
        for (int i = 0; i < 5; i++)
        {
            value = 0;
            for (int shift = 0; Bits(1) != 0; shift += 8)
            {
                value |= Bits(8) << shift;
            }
        }

        return value;

        uint Bits(int count)
        {
            uint result = 0;
            for (int i = 0; i < count; i++, bit++)
            {
                result = (result << 1) | (bit < bytes.Length * 8 ? (uint)(bytes[bit >> 3] >> (7 - (bit & 7))) & 1 : 0);
            }

            return result;
        }
    }

    /// <summary>
    /// A view shows, hides and moves parts of the model by the name of their node, which ends in an
    /// identifier of the PRC entity. Every product occurrence and every surface item the views of a
    /// document name must be a node of the scene: otherwise the view would silently leave that part as
    /// it is. (Views also name markup, views and construction geometry, which the scene does not have.)
    /// </summary>
    [Fact]
    public void EveryPartAViewNamesIsInTheScene()
    {
        IReadOnlyList<string> documents = AllDocuments();
        if (documents.Count == 0)
        {
            Assert.Skip("Set CALY_3D_SAMPLES to the folder of sample 3D PDFs.");
        }

        int artworks = 0, withNames = 0;
        long parts = 0, found = 0, sameName = 0, others = 0, withoutIdentifier = 0;
        var problems = new List<string>();
        foreach (string path in documents)
        {
            using PdfDocument document = PdfDocument.Open(path);
            foreach ((Annotation annotation, Artwork3D artwork) in Artwork3DCameraConventionTests.Artworks(document))
            {
                if (artwork.Format != Artwork3DFormat.Prc)
                {
                    continue;
                }

                artworks++;
                using Scene scene = PrcReader.Read(artwork.GetData());
                var names = new HashSet<string>(StringComparer.Ordinal);
                var identifiers = new HashSet<string>(StringComparer.Ordinal);
                foreach (SceneNode node in scene.Nodes)
                {
                    string name = scene.GetName(node.Name);
                    names.Add(name);
                    identifiers.Add(PrcNodeNames.IdentifierOf(name).ToString());
                }

                int partsHere = 0, foundHere = 0, sameHere = 0, othersHere = 0, resolvedHere = 0;
                var missing = new List<string>();
                foreach (Artwork3DView source in artwork.Views)
                {
                    SceneView view = Artwork3DViewConverter.Convert(source, annotation.Rectangle.Width, annotation.Rectangle.Height);
                    SceneView resolved = PrcNodeNames.Resolve(scene, view);
                    Assert.Equal(view.NodeOverrides.Count, resolved.NodeOverrides.Count);
                    for (int i = 0; i < view.NodeOverrides.Count; i++)
                    {
                        string name = view.NodeOverrides[i].Name;
                        uint type = TypeOf(name);
                        withoutIdentifier += type == 0 ? 1 : 0;
                        // "PRODUCT VIEWS" and "3D PMI" are a viewer's own folders under an occurrence, with its identifier.
                        if (type is not (ProductOccurrence or BrepModel or PolyBrepModel or Set)
                            || name.Contains(".PRODUCT VIEWS.", StringComparison.Ordinal) || name.Contains(".3D PMI.", StringComparison.Ordinal))
                        {
                            othersHere++;
                            continue;
                        }

                        partsHere++;
                        sameHere += names.Contains(name) ? 1 : 0;
                        if (identifiers.Contains(PrcNodeNames.IdentifierOf(name).ToString()))
                        {
                            foundHere++;

                            // Resolved, the view names the node as the scene does.
                            Assert.Contains(resolved.NodeOverrides[i].Name, names);
                            resolvedHere++;
                        }
                        else if (missing.Count < 3 && !missing.Contains(name))
                        {
                            missing.Add(name);
                        }
                    }
                }

                parts += partsHere;
                found += foundHere;
                sameName += sameHere;
                others += othersHere;
                withNames += partsHere + othersHere > 0 ? 1 : 0;
                output.WriteLine($"{Path.GetFileName(path)}: {scene.Nodes.Length} nodes, {artwork.Views.Count} views; parts named {partsHere}, found {foundHere} (same whole name {sameHere}); other things named {othersHere}{(missing.Count == 0 ? "" : "; missing e.g. " + string.Join(" | ", missing.Select(m => $"'{m}'")))}");
                if (foundHere != partsHere)
                {
                    problems.Add($"{Path.GetFileName(path)}: {partsHere - foundHere} of {partsHere} parts not found, e.g. {string.Join(" | ", missing.Select(m => $"'{m}'"))}");
                }
            }
        }

        output.WriteLine($"{artworks} PRC artworks, {withNames} with views that name nodes; parts: {found} of {parts} found, {sameName} with the same whole name; {others} names of other things ({withoutIdentifier} without identifier)");
        Assert.True(artworks > 0);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
