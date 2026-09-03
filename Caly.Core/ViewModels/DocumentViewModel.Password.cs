using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels
{
    public partial class DocumentViewModel
    {
        [ObservableProperty]
        public partial bool IsPasswordProtected { get; set; }

        /// <summary>
        /// Whether this tab is currently blocked waiting for the user to enter a password, shown
        /// inline in place of the page view rather than as a separate window - the user may be
        /// opening several documents at once and not know in advance which ones need one.
        /// </summary>
        [ObservableProperty]
        public partial bool IsAwaitingPassword { get; set; }

        /// <summary>
        /// Whether the last password entered was rejected. Reset whenever a new prompt is shown.
        /// </summary>
        [ObservableProperty]
        public partial bool PasswordAttemptFailed { get; set; }

        [ObservableProperty]
        public partial string? PasswordInput { get; set; }

        /// <summary>
        /// Completed by <see cref="SubmitPassword"/> or <see cref="CancelPassword"/> while
        /// <see cref="IsAwaitingPassword"/> is <c>true</c>. UI thread only.
        /// </summary>
        private TaskCompletionSource<string?>? _passwordTcs;

        private bool _hasPromptedForPassword;

        /// <summary>
        /// Asks the user for this document's password, shown inline in the tab in place of the page
        /// view rather than as a separate window. Called off the UI thread by
        /// <see cref="IPdfDocumentService.PasswordPrompt"/> - possibly more than once, if a previous
        /// attempt was rejected.
        /// </summary>
        private async Task<string?> RequestPasswordAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var registration = token.Register(() => tcs.TrySetCanceled(token));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _passwordTcs = tcs;
                PasswordInput = null;
                PasswordAttemptFailed = _hasPromptedForPassword;
                _hasPromptedForPassword = true;
                IsAwaitingPassword = true;
            });

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsAwaitingPassword = false;
                    _passwordTcs = null;
                });
            }
        }

        /// <summary>
        /// Submits <see cref="PasswordInput"/> to whichever <see cref="RequestPasswordAsync"/> call
        /// is currently pending. No-op if the field is empty or nothing is pending.
        /// </summary>
        [RelayCommand]
        private void SubmitPassword()
        {
            Debug.ThrowNotOnUiThread();

            if (string.IsNullOrEmpty(PasswordInput))
            {
                return;
            }

            _passwordTcs?.TrySetResult(PasswordInput);
        }

        /// <summary>
        /// Cancels whichever <see cref="RequestPasswordAsync"/> call is currently pending, stopping
        /// the open.
        /// </summary>
        [RelayCommand]
        private void CancelPassword()
        {
            Debug.ThrowNotOnUiThread();

            _passwordTcs?.TrySetResult(null);
        }

    }
}
