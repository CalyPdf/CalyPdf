using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Scene3DViewer;

// Scene3DViewer: a host for Caly.Avalonia.Scene3D.
//
//   Scene3DViewer <file.u3d | file.pdf> [options]
//
//   --artwork N        which 3D artwork of a PDF, in page order (default 0)
//   --view N           which authored view (default: the document's default view)
//   --size WxH         size of the 3D view in device-independent pixels (default 1200x900)
//   --capture out.png  save what the control drew, then exit
//   --benchmark N      turn the model for N frames, print frame times as a table row, then exit
//   --software         start Avalonia with its software renderer (the control must report "Unavailable")
//   --log file         also write the log lines to a file

internal sealed class Options
{
    public string? File { get; set; }

    public int Artwork { get; set; }

    public int? View { get; set; }

    public double Width { get; set; } = 1200;

    public double Height { get; set; } = 900;

    public string? Capture { get; set; }

    public int BenchmarkFrames { get; set; }

    public bool Software { get; set; }
}

internal static class Program
{
    private static StreamWriter? _log;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    public static Options Options { get; } = new();

    /// <summary>Set by a mode that finishes on its own; the process exits with it.</summary>
    public static int ExitCode { get; set; }

    public static void Log(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{Clock.Elapsed.TotalSeconds,7:0.000}  {message}");
        Console.WriteLine(line);
        _log?.WriteLine(line);
        _log?.Flush();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--artwork": Options.Artwork = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--view": Options.View = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--capture": Options.Capture = Path.GetFullPath(args[++i]); break;
                case "--software": Options.Software = true; break;
                case "--benchmark": Options.BenchmarkFrames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--log": _log = new StreamWriter(args[++i]); break;
                case "--size":
                    string[] parts = args[++i].Split('x');
                    Options.Width = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    Options.Height = double.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                default:
                    if (args[i].StartsWith('-') || Options.File is not null)
                    {
                        Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                        return 1;
                    }

                    Options.File = args[i];
                    break;
            }
        }

        if (Options.File is null)
        {
            Console.Error.WriteLine("usage: Scene3DViewer <file.u3d | file.pdf> [--artwork N] [--view N] [--size WxH] [--capture out.png] [--benchmark N] [--software] [--log file]");
            return 1;
        }

        AppBuilder builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (Options.Software)
        {
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        }

        int code = builder.StartWithClassicDesktopLifetime([]);
        _log?.Dispose();
        return code != 0 ? code : ExitCode;
    }
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Program.Options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
