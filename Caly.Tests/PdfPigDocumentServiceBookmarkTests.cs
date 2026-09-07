using Avalonia.Headless.XUnit;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using System.Reflection;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;

namespace Caly.Tests;

/// <summary>
/// Tests for <c>PdfPigDocumentService.BuildPdfBookmarkNode</c>. The outline tree of a crafted
/// document can be tens of thousands of levels deep (an outline cycle, for example), so the
/// conversion to <see cref="PdfBookmarkNode"/> must not recurse.
/// </summary>
public class PdfPigDocumentServiceBookmarkTests
{
    /// <summary>
    /// Depth of <c>outline_20000cycle_first.pdf</c>, which used to overflow the stack.
    /// </summary>
    private const int DeepOutlineDepth = 20_000;

    private sealed class FakeSettingsService : ISettingsService
    {
        public void SetProperty(CalySettings.CalySettingsProperty property, object value)
        {
        }

        public CalySettings GetSettings() => CalySettings.Default;

        public ValueTask<CalySettings> GetSettingsAsync() => ValueTask.FromResult(CalySettings.Default);

        public void Load()
        {
        }

        public Task LoadAsync() => Task.CompletedTask;

        public void Save()
        {
        }

        public Task SaveAsync() => Task.CompletedTask;
    }

    private static readonly MethodInfo BuildPdfBookmarkNodeMethod =
        typeof(PdfPigDocumentService).GetMethod("BuildPdfBookmarkNode", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static PdfBookmarkNode Build(PdfPigDocumentService service, BookmarkNode root, CancellationToken token)
    {
        return (PdfBookmarkNode)BuildPdfBookmarkNodeMethod.Invoke(service, new object[] { root, token })!;
    }

    [AvaloniaFact]
    public async Task BuildPdfBookmarkNode_DeeplyNestedOutline_DoesNotOverflowTheStack()
    {
        // The dispose path asserts it does not run on the UI thread; with a headless session
        // active, Task.Run reliably lands off it.
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());

            // A single chain of DeepOutlineDepth containers ending on a leaf.
            BookmarkNode node = new ContainerBookmarkNode("leaf", DeepOutlineDepth, []);
            for (int level = DeepOutlineDepth - 1; level >= 0; --level)
            {
                node = new ContainerBookmarkNode($"level {level}", level, [node]);
            }

            PdfBookmarkNode root = Build(service, node, CancellationToken.None);

            int depth = 0;
            PdfBookmarkNode? current = root;
            while (current is not null)
            {
                Assert.Equal(depth == DeepOutlineDepth ? "leaf" : $"level {depth}", current.Title);
                ++depth;
                current = current.Nodes?.Single();
            }

            Assert.Equal(DeepOutlineDepth + 1, depth);
        });
    }

    [AvaloniaFact]
    public async Task BuildPdfBookmarkNode_PreservesTitlesDestinationsAndChildren()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());

            var firstChild = new DocumentBookmarkNode("first child", 1,
                new ExplicitDestination(3, ExplicitDestinationType.XyzCoordinates,
                    new ExplicitDestinationCoordinates(0, 100)), []);
            var secondChild = new ContainerBookmarkNode("second child", 1, []);
            var node = new ContainerBookmarkNode("root", 0, [firstChild, secondChild]);

            PdfBookmarkNode root = Build(service, node, CancellationToken.None);

            Assert.Equal("root", root.Title);
            Assert.Null(root.PageNumber);
            Assert.Null(root.OffsetY);
            Assert.NotNull(root.Nodes);
            Assert.Equal(2, root.Nodes.Count);

            PdfBookmarkNode first = root.Nodes[0];
            Assert.Equal("first child", first.Title);
            Assert.Equal(3, first.PageNumber);
            Assert.Equal(100.0 * service.PpiScale, first.OffsetY);

            // Leaves have no children collection at all.
            Assert.Null(first.Nodes);
            Assert.Equal("second child", root.Nodes[1].Title);
            Assert.Null(root.Nodes[1].Nodes);
        });
    }

    [AvaloniaFact]
    public async Task BuildPdfBookmarkNode_CanceledToken_Throws()
    {
        await Task.Run(async () =>
        {
            await using var service = new PdfPigDocumentService(new FakeSettingsService());

            var node = new ContainerBookmarkNode("root", 0, []);

            var exception = Assert.Throws<TargetInvocationException>(
                () => Build(service, node, new CancellationToken(true)));

            Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        });
    }
}
