using Avalonia.Platform.Storage;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using System.Threading.Tasks;

namespace Caly.Core.Services;

internal partial class PdfDocumentsManagerService
{
    private void RegisterMessagesHandlers()
    {
        App.Messenger.Register<OpenLoadDocumentsRequestMessage>(this, HandleOpenLoadDocumentsRequestMessage);
        App.Messenger.Register<SelectedDocumentChangedMessage>(this, HandleSelectedDocumentChangedMessage);
        App.Messenger.Register<CopyToClipboardRequestMessage>(this, HandleCopyToClipboardRequestMessage);
        App.Messenger.Register<ShowNotificationMessage>(this, HandleShowNotificationMessage);
        App.Messenger.Register<ShowPdfPasswordDialogRequestMessage>(this, HandleShowPdfPasswordDialogRequestMessage);
        App.Messenger.Register<ShowPrintDialogRequestMessage>(this, HandleShowPrintDialogRequestMessage);
        App.Messenger.Register<OpenEmbeddedFileRequestMessage>(this, HandleOpenEmbeddedFileRequestMessage);
        App.Messenger.Register<SaveEmbeddedFileRequestMessage>(this, HandleSaveEmbeddedFileRequestMessage);
    }

    private void HandleSaveEmbeddedFileRequestMessage(object r, SaveEmbeddedFileRequestMessage m)
    {
        m.Reply(Task.Run(async () => (IStorageItem?)await _filesService.SaveFileAsync(m.EmbeddedFile.Data, m.EmbeddedFile.Name)));
    }

    private void HandleOpenEmbeddedFileRequestMessage(object r, OpenEmbeddedFileRequestMessage m)
    {
        m.Reply(Task.Run(async () =>
        {
            using var file = await _filesService.SaveTempFileAsync(m.EmbeddedFile.Data, m.EmbeddedFile.Name);
            string? path = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            
            await CalyExtensions.OpenFile(path);
            return true;
        }));
    }

    private void HandleOpenLoadDocumentsRequestMessage(object r, OpenLoadDocumentsRequestMessage m)
    {
        m.Reply(Task.Run(() => OpenLoadDocuments(m.Documents, m.Token)));
    }

    private void HandleShowPdfPasswordDialogRequestMessage(object r, ShowPdfPasswordDialogRequestMessage m)
    {
        m.Reply(_dialogService.ShowPdfPasswordDialogAsync());
    }
    private void HandleShowPrintDialogRequestMessage(object r, ShowPrintDialogRequestMessage m)
    {
        m.Reply(HandleShowPrintDialogAsync(m));
    }

    private async Task<bool> HandleShowPrintDialogAsync(ShowPrintDialogRequestMessage m)
    {
        await _dialogService.ShowPrintDialogAsync(m.PdfDocumentService, m.CurrentPage, m.Token)
            .ConfigureAwait(false);
        return true;
    }

    private void HandleShowNotificationMessage(object r, ShowNotificationMessage m)
    {
        _dialogService.ShowNotification(m.Value);
    }

    private void HandleCopyToClipboardRequestMessage(object r, CopyToClipboardRequestMessage m)
    {
        m.Reply(_clipboardService.SetAsync(m.TextSelection, m.PdfPageService, m.Token));
    }

    private void HandleSelectedDocumentChangedMessage(object r, SelectedDocumentChangedMessage m)
    {
        foreach (var openedFile in _openedFiles)
        {
            if (openedFile.Value.Document.Equals(m.Value))
            {
                if (openedFile.Value.Document.IsActive)
                {
                    break;
                }

                openedFile.Value.Document.SetActive();
                continue;
            }

            if (openedFile.Value.Document.IsActive)
            {
                openedFile.Value.Document.SetInactive();
                openedFile.Value.Document.ClearCommand.Execute(null);
            }
        }
    }
}
