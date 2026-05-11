// Copyright (c) BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Core.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core;

public partial class App : Application
{
    #region Cursors definition

    public static readonly Cursor DefaultCursor = Cursor.Default;
    public static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);
    public static readonly Cursor SizeWestEastCursor = new(StandardCursorType.SizeWestEast);
    public static readonly Cursor IbeamCursor = new(StandardCursorType.Ibeam);
    public static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    #endregion

    public static readonly IMessenger Messenger = StrongReferenceMessenger.Default;

    private readonly FilePipeStream _pipeServer;
    private readonly CancellationTokenSource _listeningToFilesCts = new();
    private readonly CancellationToken _listeningToFilesToken;
    private Task? _listeningToFiles;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private IPdfDocumentsManagerService _pdfDocumentsService;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public new static App? Current => Application.Current as App;

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider? Services { get; private set; }

    public App() : this(true) { }

    public App(bool singleInstance) : base()
    {
        _listeningToFilesToken = _listeningToFilesCts.Token;
        _pipeServer = new FilePipeStream(singleInstance && !Design.IsDesignMode);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialise dependencies
        var services = new ServiceCollection();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };

            services.AddSingleton<Visual>(_ => desktop.MainWindow);
            services.AddSingleton<IStorageProvider>(_ => desktop.MainWindow.StorageProvider);
            services.AddSingleton<IClipboard>(_ =>
                desktop.MainWindow.Clipboard ?? throw new ArgumentNullException(nameof(IClipboard)));

            desktop.Startup += Desktop_Startup;
            desktop.Exit += Desktop_Exit;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            MainView? mainView = null;
            activityLifetime.MainViewFactory = () => mainView = new MainView
            {
                DataContext = new MainViewModel()
            };
            services.AddSingleton<Visual>(_ => mainView ??
                throw new InvalidOperationException("MainView has not been created yet."));
            services.AddSingleton<IStorageProvider>(_ =>
                TopLevel.GetTopLevel(mainView)?.StorageProvider ??
                throw new ArgumentNullException(nameof(IStorageProvider)));
            services.AddSingleton<IClipboard>(_ =>
                TopLevel.GetTopLevel(mainView)?.Clipboard ??
                throw new ArgumentNullException(nameof(IClipboard)));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
            services.AddSingleton<Visual>(_ => singleViewPlatform.MainView);
            services.AddSingleton<IStorageProvider>(_ =>
                TopLevel.GetTopLevel(singleViewPlatform.MainView)?.StorageProvider ??
                throw new ArgumentNullException(nameof(IStorageProvider)));
            services.AddSingleton<IClipboard>(_ =>
                TopLevel.GetTopLevel(singleViewPlatform.MainView)?.Clipboard ??
                throw new ArgumentNullException(nameof(IClipboard)));
        }
#if DEBUG
        else if (ApplicationLifetime is null && Avalonia.Controls.Design.IsDesignMode)
        {
            var mainView = new MainView { DataContext = new MainViewModel() };
            services.AddSingleton<Visual>(_ => mainView);
            services.AddSingleton<IStorageProvider>(_ => TopLevel.GetTopLevel(mainView)?.StorageProvider);
            services.AddSingleton<IClipboard>(_ => TopLevel.GetTopLevel(mainView)?.Clipboard);
        }
#endif

        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IFilesService, FilesService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IPdfDocumentsManagerService, PdfDocumentsManagerService>();

        services.AddScoped<IPdfDocumentService, PdfPigDocumentService>();
        services.AddScoped<PdfPageService>();
        services.AddScoped<ITextSearchService, SearchValuesTextSearchService>();
        services.AddScoped<DocumentViewModel>();

        OverrideRegisteredServices(services);

        Services = services.BuildServiceProvider();

        // Load settings
        Services.GetRequiredService<ISettingsService>().Load();

        // We need to make sure IPdfDocumentsService singleton is initiated in UI thread
        _pdfDocumentsService = Services.GetRequiredService<IPdfDocumentsManagerService>();

        base.OnFrameworkInitializationCompleted();
    }

    protected virtual void OverrideRegisteredServices(IServiceCollection services)
    {
        // No-op, for testing purpose
    }

    public bool TryBringToFront()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        if (desktop.MainWindow is null)
        {
            return false;
        }

        try
        {
            desktop.MainWindow.Activate(); // Bring window to front

            Dispatcher.UIThread.Invoke(() =>
            {
                // Popup from taskbar
                if (desktop.MainWindow.WindowState == WindowState.Minimized)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                }
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void Desktop_Startup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Startup -= Desktop_Startup;
            }

            _listeningToFiles = Task.Run(ListenToIncomingFiles); // Start listening

            if (e.Args.Length == 0)
            {
                return;
            }

            await Task.Run(() => OpenDoc(e.Args[0], CancellationToken.None));
        }
        catch (Exception ex)
        {
            ShowExceptionWindowSafely(ex);
            Debug.WriteExceptionToFile(ex);
        }
    }

    private void Desktop_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _listeningToFilesCts.Cancel();
        GC.KeepAlive(_listeningToFiles);
        
        _listeningToFilesCts.Dispose();
        _pipeServer.Dispose();
        
        if (_pdfDocumentsService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= Desktop_Exit;
        }
    }

    private async Task ListenToIncomingFiles()
    {
        Debug.ThrowOnUiThread();

        try
        {
            await Parallel.ForEachAsync(_pipeServer.ReceivePathAsync(_listeningToFilesToken),
                _listeningToFilesToken, async (path, ct) =>
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    await OpenDoc(path, ct);
                });
        }
        catch (OperationCanceledException)
        {
            // No op
        }
        catch (Exception ex)
        {
            // Critical error...
            ShowExceptionWindowSafely(ex);
            Debug.WriteExceptionToFile(ex);
            throw;
        }
        finally
        {
            await _pipeServer.DisposeAsync();
        }
    }

    protected async Task OpenDoc(string? path, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var dialogService = Services?.GetRequiredService<IDialogService>();
                dialogService?.ShowNotification("Cannot open file",
                    "The file does not exist or the path is invalid.",
                    NotificationType.Error);

                return;
            }

            await _pdfDocumentsService.OpenLoadDocument(path, token);
        }
        catch (Exception ex)
        {
            ShowExceptionNotificationSafely(ex);
            Debug.WriteExceptionToFile(ex);
        }
    }

    private void ShowExceptionNotificationSafely(Exception? ex)
    {
        try
        {
            if (ex is null) return;

            var dialogService = Services?.GetRequiredService<IDialogService>();
            dialogService?.ShowNotification("Error", ex.Message, NotificationType.Error);
        }
        catch
        {
            // No op
        }
    }

    private void ShowExceptionWindowSafely(Exception? ex)
    {
        try
        {
            if (ex is null) return;

            var dialogService = Services?.GetRequiredService<IDialogService>();
            dialogService?.ShowExceptionWindow(ex);
        }
        catch
        {
            // No op
        }
    }
}
