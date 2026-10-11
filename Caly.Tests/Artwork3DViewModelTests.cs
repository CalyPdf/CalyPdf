using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Caly.Core;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Caly.Avalonia.Scene3D;
using Caly.Pdf.Artwork;
using Caly.Pdf.Models;
using Caly.Tests.Fakes;
using CommunityToolkit.Mvvm.Messaging;
using Scene3D;
using UglyToad.PdfPig.Artwork;
using static Caly.Tests.Fakes.DocumentTestHarness;

namespace Caly.Tests;

/// <summary>
/// One 3D artwork being viewed: loading, the one-view-per-document rule, coming back after the control
/// lost its GPU resources, failures and their messages, and giving everything back on close.
/// </summary>
public class Artwork3DViewModelTests
{
    /// <summary>A loaded document whose service hands out the content of the box of box_base.pdf.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly object _recipient = new();

        public Fixture(int pageCount = 2)
        {
            using (Artwork3DTestDocument box = Artwork3DTestDocument.OpenBox())
            {
                Artwork = box.Artworks[0];
                Content = Artwork3DContentReader.Read(Artwork);
            }

            Service = new RenderingPdfDocumentService { Artwork3DContent = (_, _) => Task.FromResult<Artwork3DContent?>(Content) };
            PageService = new PdfPageService(Service);
            Document = NewLoadedDocument(Service, PageService, pageCount);
            App.Messenger.Register<ShowNotificationMessage>(_recipient, (_, m) => Notifications.Add(m.Value.Message ?? ""));
            Artwork3DAvailability.MarkAvailable();
        }

        public PdfArtwork3D Artwork { get; }

        public Artwork3DContent Content { get; set; }

        public RenderingPdfDocumentService Service { get; }

        public PdfPageService PageService { get; }

        public DocumentViewModel Document { get; }

        public List<string> Notifications { get; } = [];

        public Artwork3DViewModel Activate(int page = 1, int index = 0)
        {
            Document.ActivateArtwork3DCommand.Execute(new Artwork3DActivationRequest(page, index, Artwork));
            return Document.ActiveArtwork3D!;
        }

        public async ValueTask DisposeAsync()
        {
            App.Messenger.UnregisterAll(_recipient);
            Artwork3DAvailability.MarkAvailable();
            await PageService.DisposeAsync();
        }
    }

    private static Artwork3DContent With(Artwork3DContent content, ReadOnlyMemory<byte> payload)
    {
        return new Artwork3DContent
        {
            Payload = payload, Format = content.Format, Views = content.Views, ViewNames = content.ViewNames, DefaultViewIndex = content.DefaultViewIndex
        };
    }

    [AvaloniaFact]
    public async Task ActivationLoadsTheSceneAndSelectsTheDefaultView()
    {
        await using var f = new Fixture();

        Artwork3DViewModel artwork = f.Activate();

        Assert.Same(artwork, f.Document.Pages[0].ActiveArtwork3D);
        Assert.Equal(0, f.Document.Pages[0].ActiveArtwork3DIndex);
        Assert.Equal(-1, f.Document.Pages[1].ActiveArtwork3DIndex);
        Assert.Equal(Artwork3DState.Loading, artwork.State);
        Assert.True(artwork.IsLoading);
        Assert.Null(artwork.Scene);
        Assert.Equal(1, artwork.PageNumber);
        Assert.Equal(0, artwork.ArtworkIndex);
        Assert.True(artwork.Bounds.Width > 0 && artwork.Bounds.Height > 0);

        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        Assert.NotNull(artwork.Scene);
        Assert.True(artwork.Scene.HasGeometry);
        Assert.Equal(["DefaultView", Artwork3DViewModel.WholeModelName], artwork.ViewNames);
        Assert.Equal(0, artwork.SelectedViewIndex);
        Assert.Same(f.Content.Views[0], artwork.SelectedView);
        Assert.Empty(f.Notifications);
    }

    [AvaloniaFact]
    public async Task SelectingTheLastEntryMeansTheWholeModel()
    {
        await using var f = new Fixture();
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        var changed = new List<string?>();
        artwork.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        artwork.SelectedViewIndex = 1;

        Assert.Null(artwork.SelectedView);
        Assert.Contains(nameof(Artwork3DViewModel.SelectedView), changed);

        artwork.SelectedViewIndex = 0;
        Assert.Same(f.Content.Views[0], artwork.SelectedView);
    }

    [AvaloniaFact]
    public async Task ADocumentWithoutViewsShowsTheWholeModel()
    {
        await using var f = new Fixture();
        f.Content = new Artwork3DContent { Payload = f.Content.Payload, Format = Artwork3DFormat.U3D, Views = [], ViewNames = [], DefaultViewIndex = -1 };

        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));

        Assert.Equal([Artwork3DViewModel.WholeModelName], artwork.ViewNames);
        Assert.Equal(0, artwork.SelectedViewIndex);
        Assert.Null(artwork.SelectedView);
    }

    [AvaloniaFact]
    public async Task ActivatingTheSameArtworkAgainChangesNothing()
    {
        await using var f = new Fixture();
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));

        Assert.Same(artwork, f.Activate());
        Assert.NotNull(artwork.Scene);
    }

    [AvaloniaFact]
    public async Task ASecondActivationClosesTheFirst()
    {
        await using var f = new Fixture();
        Artwork3DViewModel first = f.Activate(page: 1);
        Assert.True(await WaitUntil(() => first.State == Artwork3DState.Ready));
        Scene firstScene = first.Scene!;

        Artwork3DViewModel second = f.Activate(page: 2);

        Assert.NotSame(first, second);
        Assert.Null(f.Document.Pages[0].ActiveArtwork3D);
        Assert.Same(second, f.Document.Pages[1].ActiveArtwork3D);
        Assert.Null(first.Scene);
        Assert.False(firstScene.HasGeometry);
        Assert.True(await WaitUntil(() => second.State == Artwork3DState.Ready));
        Assert.Empty(f.Notifications);
    }

    /// <summary>Review focus: a second click while the first model is still being read.</summary>
    [AvaloniaFact]
    public async Task ASecondActivationWhileTheFirstIsLoadingDiscardsItsResult()
    {
        await using var f = new Fixture();
        var gate = new TaskCompletionSource();
        Artwork3DContent content = f.Content;
        int calls = 0;
        f.Service.Artwork3DContent = async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await gate.Task;
            }

            return content;
        };

        Artwork3DViewModel first = f.Activate(page: 1);
        Artwork3DViewModel second = f.Activate(page: 2);
        Assert.True(await WaitUntil(() => second.State == Artwork3DState.Ready));

        gate.SetResult();
        await first.Completion;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(first.Scene);
        Assert.Equal(Artwork3DState.Loading, first.State);
        Assert.Same(second, f.Document.ActiveArtwork3D);
        Assert.NotNull(second.Scene);
        Assert.Empty(f.Notifications);
    }

    [AvaloniaFact]
    public async Task APayloadThatCannotBeReadClosesTheViewAndSaysWhy()
    {
        await using var f = new Fixture();
        f.Content = With(f.Content, new byte[64]);

        Artwork3DViewModel artwork = f.Activate();

        Assert.True(await WaitUntil(() => f.Document.ActiveArtwork3D is null));
        Assert.Null(f.Document.Pages[0].ActiveArtwork3D);
        Assert.Null(artwork.Scene);
        string message = Assert.Single(f.Notifications);
        Assert.StartsWith("This 3D model could not be read", message);
    }

    [AvaloniaFact]
    public async Task ADocumentThatCannotGiveThePayloadClosesTheViewAndSaysWhy()
    {
        await using var f = new Fixture();
        f.Service.Artwork3DContent = (_, _) => throw new Artwork3DContentException("the stream is broken");

        f.Activate();

        Assert.True(await WaitUntil(() => f.Document.ActiveArtwork3D is null));
        Assert.Equal("This 3D model could not be read: the stream is broken", Assert.Single(f.Notifications));
    }

    [AvaloniaFact]
    public async Task AClosedDocumentClosesTheViewSilently()
    {
        await using var f = new Fixture();
        f.Service.Artwork3DContent = (_, _) => Task.FromResult<Artwork3DContent?>(null);

        f.Activate();

        Assert.True(await WaitUntil(() => f.Document.ActiveArtwork3D is null));
        Assert.Empty(f.Notifications);
    }

    [AvaloniaFact]
    public async Task NeedsSceneDecodesAgainAndDisposesTheOldScene()
    {
        await using var f = new Fixture();
        int reads = 0;
        Artwork3DContent content = f.Content;
        f.Service.Artwork3DContent = (_, _) =>
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult<Artwork3DContent?>(content);
        };
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        Scene old = artwork.Scene!;
        artwork.SelectedViewIndex = 1;

        // What the control does once the geometry is on the GPU, and says when it lost the GPU objects.
        old.ReleaseGeometry();
        artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.NeedsScene, null));

        Assert.True(await WaitUntil(() => artwork.Scene is not null && !ReferenceEquals(artwork.Scene, old)));
        Assert.True(artwork.Scene!.HasGeometry);
        Assert.Equal(Artwork3DState.Ready, artwork.State);
        Assert.Equal(1, artwork.SelectedViewIndex);

        // From the payload it kept: the document is not asked again.
        Assert.Equal(1, reads);
        Assert.Throws<ObjectDisposedException>(() => old.NameCount);
    }

    [AvaloniaFact]
    public async Task NeedsSceneWhileStillLoadingStartsNoSecondLoad()
    {
        await using var f = new Fixture();
        var gate = new TaskCompletionSource();
        Artwork3DContent content = f.Content;
        int reads = 0;
        f.Service.Artwork3DContent = async (_, _) =>
        {
            Interlocked.Increment(ref reads);
            await gate.Task;
            return content;
        };
        Artwork3DViewModel artwork = f.Activate();
        Task firstLoad = artwork.Completion;

        artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.NeedsScene, null));

        Assert.Same(firstLoad, artwork.Completion);
        gate.SetResult();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        Assert.Equal(1, reads);
    }

    [AvaloniaFact]
    public async Task AControlThatStaysUnavailableIsRememberedAndTheNextClickIsRefused()
    {
        await using var f = new Fixture();
        TimeSpan grace = Artwork3DViewModel.UnavailableGrace;
        Artwork3DViewModel.UnavailableGrace = TimeSpan.FromMilliseconds(30);
        try
        {
            Artwork3DViewModel artwork = f.Activate();
            Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));

            artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.Unavailable, "3D needs the GPU renderer."));

            // Not at once: OpenGL may only be slow to start.
            Assert.False(Artwork3DAvailability.IsUnavailable);
            Assert.Same(artwork, f.Document.ActiveArtwork3D);

            Assert.True(await WaitUntil(() => f.Document.ActiveArtwork3D is null));
            Assert.True(Artwork3DAvailability.IsUnavailable);
            Assert.Equal(Artwork3DAvailability.UnavailableMessage, Assert.Single(f.Notifications));

            // The next click does not try again.
            f.Document.ActivateArtwork3DCommand.Execute(new Artwork3DActivationRequest(1, 0, f.Artwork));
            Assert.Null(f.Document.ActiveArtwork3D);
            Assert.Equal(2, f.Notifications.Count);
            Assert.Equal(Artwork3DAvailability.UnavailableMessage, f.Notifications[1]);
        }
        finally
        {
            Artwork3DViewModel.UnavailableGrace = grace;
        }
    }

    [AvaloniaFact]
    public async Task AControlThatWasOnlySlowToStartIsNotCalledUnavailable()
    {
        await using var f = new Fixture();
        TimeSpan grace = Artwork3DViewModel.UnavailableGrace;
        Artwork3DViewModel.UnavailableGrace = TimeSpan.FromMilliseconds(60);
        try
        {
            Artwork3DViewModel artwork = f.Activate();
            Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));

            artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.Unavailable, null));
            artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.Uploading, null));
            artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.Ready, null));

            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();

            Assert.False(Artwork3DAvailability.IsUnavailable);
            Assert.Same(artwork, f.Document.ActiveArtwork3D);
            Assert.Empty(f.Notifications);
        }
        finally
        {
            Artwork3DViewModel.UnavailableGrace = grace;
        }
    }

    [AvaloniaFact]
    public async Task AFailedControlClosesTheViewAndSaysWhy()
    {
        await using var f = new Fixture();
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));

        artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.Failed, "The GPU has no memory for the scene's geometry."));

        Assert.Null(f.Document.ActiveArtwork3D);
        Assert.Equal("The 3D view failed: The GPU has no memory for the scene's geometry.", Assert.Single(f.Notifications));
        Assert.False(Artwork3DAvailability.IsUnavailable);
    }

    [AvaloniaFact]
    public async Task CloseGivesEverythingBack()
    {
        await using var f = new Fixture();

        (WeakReference payload, WeakReference scene) = await ActivateAndClose(f);

        // The helper finished by running this continuation: it is still on the stack, and its locals
        // with it. Come back on a fresh one before looking at what is still alive.
        await Task.Yield();

        Assert.Null(f.Document.ActiveArtwork3D);
        Assert.Null(f.Document.Pages[0].ActiveArtwork3D);
        Assert.Equal(-1, f.Document.Pages[0].ActiveArtwork3DIndex);
        Assert.Empty(f.Notifications);

        // Neither the document nor the page may still hold the payload or the scene.
        for (int i = 0; i < 5 && (payload.IsAlive || scene.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(payload.IsAlive);
        Assert.False(scene.IsAlive);
    }

    /// <summary>
    /// Views a copy of the payload that nothing else holds, closes the view, and returns weak references
    /// to that payload and to the scene. A method of its own so that no local keeps them alive.
    /// </summary>
    private static async Task<(WeakReference Payload, WeakReference Scene)> ActivateAndClose(Fixture f)
    {
        Artwork3DContent original = f.Content;
        byte[] copy = original.Payload.ToArray();
        f.Content = With(original, copy);

        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        Scene scene = artwork.Scene!;

        artwork.CloseCommand.Execute(null);

        Assert.Null(artwork.Scene);
        Assert.Throws<ObjectDisposedException>(() => scene.NameCount);

        // Closing twice, or a late word from the control, is harmless.
        artwork.CloseCommand.Execute(null);
        artwork.ControlStatusChangedCommand.Execute(new Artwork3DControlStatus(Scene3DStatus.NeedsScene, null));
        Assert.Null(artwork.Scene);

        f.Content = original;
        return (new WeakReference(copy), new WeakReference(scene));
    }

    /// <summary>Review focus: the document goes away while a model is still being read.</summary>
    [AvaloniaFact]
    public async Task ClosingWhileLoadingDisposesTheLateScene()
    {
        await using var f = new Fixture();
        var gate = new TaskCompletionSource();
        Artwork3DContent content = f.Content;
        f.Service.Artwork3DContent = async (_, _) =>
        {
            await gate.Task;
            return content;
        };
        Artwork3DViewModel artwork = f.Activate();

        artwork.CloseCommand.Execute(null);
        gate.SetResult();
        await artwork.Completion;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(artwork.Scene);
        Assert.Null(f.Document.ActiveArtwork3D);
        Assert.Empty(f.Notifications);
    }

    /// <summary>
    /// The model was read, and the view is closed before the UI thread got to hear of it (cancelling
    /// comes too late to stop anything): the scene must be disposed, not shown.
    /// </summary>
    [AvaloniaFact]
    public async Task AResultThatArrivesAfterCloseIsDisposed()
    {
        await using var f = new Fixture();
        int reads = 0;
        Artwork3DContent content = f.Content;
        f.Service.Artwork3DContent = (_, _) =>
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult<Artwork3DContent?>(content);
        };
        Artwork3DViewModel artwork = f.Activate();

        // The reading happens on the thread pool and then waits for the UI thread, which is this one
        // and is kept busy here.
        for (int i = 0; i < 200 && Volatile.Read(ref reads) == 0; i++)
        {
            Thread.Sleep(5);
        }

        Thread.Sleep(300);
        Assert.Equal(Artwork3DState.Loading, artwork.State);

        artwork.CloseCommand.Execute(null);
        Assert.True(await WaitUntil(() => artwork.Completion.IsCompleted));

        Assert.Null(artwork.Scene);
        Assert.Equal(Artwork3DState.Loading, artwork.State);
        Assert.Empty(artwork.ViewNames);
        Assert.Empty(f.Notifications);
    }

    [AvaloniaFact]
    public async Task DisposingTheDocumentClosesTheView()
    {
        await using var f = new Fixture();
        Artwork3DViewModel artwork = f.Activate();
        Assert.True(await WaitUntil(() => artwork.State == Artwork3DState.Ready));
        Scene scene = artwork.Scene!;

        Task disposal = Task.Run(async () => await f.Document.DisposeAsync());
        Assert.True(await WaitUntil(() => disposal.IsCompleted && artwork.Scene is null));

        await disposal;
        Assert.Null(f.Document.ActiveArtwork3D);
        Assert.Throws<ObjectDisposedException>(() => scene.NameCount);
    }

    [AvaloniaFact]
    public async Task ARequestForAPageThatDoesNotExistIsIgnored()
    {
        await using var f = new Fixture();

        f.Document.ActivateArtwork3DCommand.Execute(new Artwork3DActivationRequest(99, 0, f.Artwork));
        f.Document.ActivateArtwork3DCommand.Execute(new Artwork3DActivationRequest(0, 0, f.Artwork));
        f.Document.ActivateArtwork3DCommand.Execute(null);

        Assert.Null(f.Document.ActiveArtwork3D);
        Assert.Empty(f.Notifications);
    }

    [AvaloniaFact]
    public async Task PrcIsRefusedWithAMessage()
    {
        string? path = Artwork3DCameraConventionTests.CorpusDocuments("tetra4d").FirstOrDefault();
        if (path is null)
        {
            Assert.Skip("Set CALY_3D_SAMPLES to the folder of sample 3D PDFs.");
        }

        await using var f = new Fixture();
        using Artwork3DTestDocument prc = FirstWithArtwork(path, Artwork3DFormat.Prc);

        f.Document.ActivateArtwork3DCommand.Execute(new Artwork3DActivationRequest(1, 0, prc.Artworks.First(a => a.Format == Artwork3DFormat.Prc)));

        Assert.Null(f.Document.ActiveArtwork3D);
        Assert.Equal("This 3D model is in PRC format, which is not supported yet.", Assert.Single(f.Notifications));
    }

    private static Artwork3DTestDocument FirstWithArtwork(string path, Artwork3DFormat format)
    {
        for (int page = 1; ; page++)
        {
            Artwork3DTestDocument document = Artwork3DTestDocument.Open(path, page);
            if (document.Artworks.Any(a => a.Format == format))
            {
                return document;
            }

            document.Dispose();
        }
    }
}
