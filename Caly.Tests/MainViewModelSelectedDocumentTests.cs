using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Pdf.Models;
using CommunityToolkit.Mvvm.Messaging;
using SkiaSharp;

namespace Caly.Tests;

/// <summary>
/// Regression tests for <see cref="MainViewModel.SelectedDocument"/> change notification.
/// The status bar binds to <c>SelectedDocument.InteractiveActionOver</c>, so a
/// <c>PropertyChanged</c> for <c>SelectedDocument</c> must be raised whenever the resolved
/// document changes — including when <see cref="MainViewModel.SelectedDocumentIndex"/> stays
/// the same but a removal shifts a different document into that index.
/// </summary>
public class MainViewModelSelectedDocumentTests
{
    /// <summary>
    /// A never-opened document service. <see cref="DocumentViewModel"/>'s constructor asserts
    /// the document has no pages yet, so <see cref="FakePdfDocumentService"/> (5 pages) cannot
    /// be used here. Members not needed before a document is opened throw.
    /// </summary>
    private sealed class UnopenedPdfDocumentService : IPdfDocumentService
    {
        public int NumberOfPages => 0;
        public string? FileName => "unopened.pdf";
        // IsActive has an internal setter in the interface; implemented directly via InternalsVisibleTo.
        public bool IsActive { get; set; }

        public double PpiScale => throw new NotImplementedException();
        public long? FileSize => throw new NotImplementedException();
        public string? LocalPath => throw new NotImplementedException();
        public bool IsPasswordProtected => throw new NotImplementedException();
        public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

        public Task<DocumentOpeningState> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
            => throw new NotImplementedException();

        public Task<DocumentPropertiesViewModel?> GetDocumentPropertiesAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfBookmarkNode>?> GetPdfBookmark(CancellationToken token)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<PdfEmbeddedFileViewModel>?> GetEmbeddedFileAsync(CancellationToken token)
            => throw new NotImplementedException();

        public Task<UglyToad.PdfPig.Rendering.Skia.PdfPageSize?> GetPageSizeAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public Task<PdfTextLayer?> GetPageTextLayerAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public Task<IRef<SKPicture>?> GetRenderPageAsync(int pageNumber, CancellationToken token)
            => throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopTextSearchService : ITextSearchService
    {
        public Task BuildPdfDocumentIndex(IProgress<int> progress, CancellationToken token) => Task.CompletedTask;

        public IEnumerable<TextSearchResult> Search(string text, IReadOnlyCollection<int> pagesToSkip, CancellationToken token) => [];

        public void Dispose()
        {
        }
    }

    private static DocumentViewModel NewDocument()
    {
        var pdfService = new UnopenedPdfDocumentService();
        return new DocumentViewModel(pdfService, new PdfPageService(pdfService), new NoopTextSearchService());
    }

    /// <summary>
    /// Creates a <see cref="MainViewModel"/> with its background document-load pipeline
    /// detached (it expects documents added through the manager service, i.e. with a pending
    /// open, and logs failures to disk otherwise). The selected-document notification path
    /// under test is wired through direct event handlers that are unaffected by Dispose.
    /// </summary>
    private static MainViewModel NewMainViewModel()
    {
        var vm = new MainViewModel();
        vm.Dispose();
        return vm;
    }

    private static List<string?> RecordPropertyChanges(MainViewModel vm)
    {
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }

    [AvaloniaFact]
    public void RemovingSelectedDocument_NotifiesForDocumentShiftedIntoSameIndex()
    {
        var main = NewMainViewModel();
        var docA = NewDocument();
        var docB = NewDocument();
        main.PdfDocuments.Add(docA);
        main.PdfDocuments.Add(docB);
        Dispatcher.UIThread.RunJobs(); // Settle: docA (index 0) is announced.
        Assert.Same(docA, main.SelectedDocument);

        var changed = RecordPropertyChanges(main);

        DocumentViewModel? announced = null;
        var recipient = new object();
        App.Messenger.Register<SelectedDocumentChangedMessage>(recipient, (_, m) => announced = m.Value);
        try
        {
            // Removing the selected document shifts docB into index 0;
            // SelectedDocumentIndex itself does not change.
            main.PdfDocuments.Remove(docA);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            App.Messenger.Unregister<SelectedDocumentChangedMessage>(recipient);
        }

        Assert.Same(docB, main.SelectedDocument);
        Assert.Contains(nameof(MainViewModel.SelectedDocument), changed);
        Assert.Same(docB, announced);
    }

    [AvaloniaFact]
    public void RemovingLastDocument_NotifiesSelectedDocumentIsNull()
    {
        var main = NewMainViewModel();
        var doc = NewDocument();
        main.PdfDocuments.Add(doc);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(doc, main.SelectedDocument);

        var changed = RecordPropertyChanges(main);

        main.PdfDocuments.Remove(doc);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(main.SelectedDocument);
        Assert.Contains(nameof(MainViewModel.SelectedDocument), changed);
    }

    [AvaloniaFact]
    public void ChangingSelectedDocumentIndex_NotifiesSelectedDocument()
    {
        // SelectedDocumentIndex no longer carries [NotifyPropertyChangedFor(nameof(SelectedDocument))];
        // index changes must still notify through the coalesced announce, once posted jobs run.
        var main = NewMainViewModel();
        var docA = NewDocument();
        var docB = NewDocument();
        main.PdfDocuments.Add(docA);
        main.PdfDocuments.Add(docB);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(docA, main.SelectedDocument);

        var changed = RecordPropertyChanges(main);

        main.SelectedDocumentIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(docB, main.SelectedDocument);
        Assert.Contains(nameof(MainViewModel.SelectedDocument), changed);
    }
}
