using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Caly.Core.Models;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.Generic;
using System.Threading;
using Caly.Core.Services.Interfaces;

namespace Caly.Core.Services;

internal sealed class SelectedDocumentChangedMessage(DocumentViewModel value) : ValueChangedMessage<DocumentViewModel>(value);

internal sealed class ShowNotificationMessage(CalyNotification value) : ValueChangedMessage<CalyNotification>(value)
{
    public ShowNotificationMessage(NotificationType type, string? title = null, string? message = null) : this(new CalyNotification()
    {
        Title = title,
        Message = message,
        Type = type
    }) { }
}

internal sealed class CopyToClipboardRequestMessage : AsyncRequestMessage<bool>
{
    public PdfPageService PdfPageService { get; }

    public TextSelection TextSelection { get; }

    public CancellationToken Token { get; }

    public CopyToClipboardRequestMessage(TextSelection textSelection,
        PdfPageService pdfPageService,
        CancellationToken token)
    {
        TextSelection = textSelection;
        PdfPageService = pdfPageService;
        Token = token;
    }
}

internal sealed class ShowPdfPasswordDialogRequestMessage : AsyncRequestMessage<string?>;

internal sealed class ShowPrintDialogRequestMessage : AsyncRequestMessage<bool>
{
    public IPdfDocumentService PdfDocumentService { get; }

    public int CurrentPage { get; }

    public CancellationToken Token { get; }

    public ShowPrintDialogRequestMessage(IPdfDocumentService pdfDocumentService, int currentPage, CancellationToken token)
    {
        PdfDocumentService = pdfDocumentService;
        CurrentPage = currentPage;
        Token = token;
    }
}

internal sealed class OpenLoadDocumentsRequestMessage : AsyncRequestMessage<int>
{
    public IEnumerable<IStorageItem> Documents { get; }

    public CancellationToken Token { get; }

    public OpenLoadDocumentsRequestMessage(IEnumerable<IStorageItem> documents, CancellationToken token)
    {
        Documents = documents;
        Token = token;
    }
}

internal sealed class OpenEmbeddedFileRequestMessage : AsyncRequestMessage<bool>
{
    public PdfEmbeddedFileViewModel EmbeddedFile { get; }

    public OpenEmbeddedFileRequestMessage(PdfEmbeddedFileViewModel embeddedFile)
    {
        EmbeddedFile = embeddedFile;
    }
}

internal sealed class SaveEmbeddedFileRequestMessage : AsyncRequestMessage<IStorageItem?>
{
    public PdfEmbeddedFileViewModel EmbeddedFile { get; }

    public SaveEmbeddedFileRequestMessage(PdfEmbeddedFileViewModel embeddedFile)
    {
        EmbeddedFile = embeddedFile;
    }
}