// MIT License
//
// Copyright (c) 2025 BobLd
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
using System.Threading.Tasks;
using Avalonia;
using Caly.Core;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Desktop
{
    internal class Program
    {
        private const string _appName = "Caly Pdf Reader";

        private static readonly CalyFileMutex mutex = new(true, _appName);

        private static bool _isRestart = false;
        
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Make sure the current directory is where the app is located, not where a file is opened
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            bool isMainInstance = false;

            try
            {
                // Make sure a single instance of the app is running
                isMainInstance = mutex.WaitOne(TimeSpan.Zero, true);
                
                if (isMainInstance)
                {
                    SendToMainInstance(args);
                }
                else // App instance already running
                {
                    try
                    {
                        SendToRunningInstance(args);
                    }
                    catch (CalyCriticalException e)
                    {
                        if (!_isRestart && e.TryRestartApp)
                        {
                            _isRestart = true; // Prevent infinite loop by checking if it's already a restart
                            Main(args);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is AggregateException a)
                        {
                            ex = a.Flatten();
                        }

                        ShowExceptionSafely(ex);
                        throw;
                    }
                }
            }
            finally
            {
                if (isMainInstance)
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static int SendToMainInstance(string[] args)
        {
            try
            {
                // TODO - should the below be in App.axaml.cs?
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

                return BuildAvaloniaApp()
                    //.WithDeveloperTools()
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                if (ex is AggregateException a)
                {
                    ex = a.Flatten();
                }

                Debug.WriteExceptionToFile(ex);
                throw;
            }
            finally
            {
                AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
                TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            }
        }

        private static void SendToRunningInstance(string[] args)
        {
            if (args.Length == 0)
            {
                FilePipeStream.SendBringToFront();
                return;
            }

            string path = args[0];

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (FilePipeStream.SendPath(path))
            {
                FilePipeStream.SendBringToFront();
            }
        }

        private static void ShowExceptionSafely(Exception? ex)
        {
            try
            {
                if (ex is null)
                {
                    return;
                }

                var dialogService = App.Current?.Services?.GetRequiredService<IDialogService>();
                dialogService?.ShowExceptionWindow(ex);
            }
            finally
            {
                Debug.WriteExceptionToFile(ex);
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // https://docs.avaloniaui.net/docs/getting-started/unhandled-exceptions
            // https://learn.microsoft.com/en-gb/dotnet/api/system.threading.tasks.taskscheduler.unobservedtaskexception?view=net-7.0

            var exception = e.Exception.Flatten();
            ShowExceptionSafely(exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // https://github.com/AvaloniaUI/Avalonia/issues/5387
            if (e.ExceptionObject is not Exception exception)
            {
                return;
            }

            if (exception is AggregateException aEx)
            {
                exception = aEx.Flatten();
            }
            ShowExceptionSafely(exception);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            try
            {
                // GPU rendering disabled for now
                return AppBuilder.Configure<DesktopApp>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .UseSkia()
                    .With(new Win32PlatformOptions
                    {
                        RenderingMode = [Win32RenderingMode.Software],
                    })
                    .With(new X11PlatformOptions
                    {
                        RenderingMode = [X11RenderingMode.Software],
                        WmClass = _appName,
                        ExternalGLibMainLoopExceptionLogger = ShowExceptionSafely,
#pragma warning disable AVALONIA_X11_CSD
                        EnableDrawnDecorations = true,
#pragma warning restore AVALONIA_X11_CSD
                    })
                    .With(new AvaloniaNativePlatformOptions
                    {
                        RenderingMode = [AvaloniaNativeRenderingMode.Software]
                    })
                    .LogToTrace();
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
                throw;
            }
        }
    }
}
