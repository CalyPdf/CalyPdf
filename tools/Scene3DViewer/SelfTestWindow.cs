using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Caly.Avalonia.Scene3D;

namespace Scene3DViewer;

/// <summary>The window of the <c>--selftest</c> mode: one 3D view of a fixed size and nothing else.</summary>
internal sealed class SelfTestWindow : Window
{
    public SelfTestWindow()
    {
        Title = "Scene3DViewer — self-test";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;

        var control = new Scene3DControl { Width = 400, Height = 300 };
        var host = new Decorator { Child = control };
        Content = host;

        var test = new SelfTest(control, host, code =>
        {
            Program.ExitCode = code;
            Dispatcher.UIThread.Post(Close);
        });

        control.PropertyChanged += (_, e) =>
        {
            // Without a GPU there is nothing to test, and that is a failure of this mode.
            if (e.Property == Scene3DControl.StatusProperty && control.Status is Scene3DStatus.Unavailable or Scene3DStatus.Failed)
            {
                Program.Log($"selftest: FAIL  the control is {control.Status} — {control.StatusMessage}");
                Program.ExitCode = 4;
                Dispatcher.UIThread.Post(Close);
            }
        };

        Opened += (_, _) => test.Start();

        // Never hang a script that runs this.
        DispatcherTimer.RunOnce(() =>
        {
            Program.Log("selftest: FAIL  timed out");
            Program.ExitCode = 4;
            Close();
        }, TimeSpan.FromSeconds(30));
    }
}
