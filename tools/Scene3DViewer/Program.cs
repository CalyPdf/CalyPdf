using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Scene3DViewer;

// Scene3DViewer: a host for Caly.Avalonia.Scene3D.
//
//   Scene3DViewer <file.u3d | file.pdf> [options]
//   Scene3DViewer --selftest [--log file]
//
//   --artwork N         which 3D artwork of a PDF, in page order (default 0)
//   --view N            which authored view (default: the document's default view)
//   --size WxH          size of the 3D view in device-independent pixels
//                       (default: 700 high, as wide as the document's proportions make it)
//   --poster            show the document's own picture of the artwork beside the live view
//   --transparent       no background: the view is drawn over a striped panel that must show through
//   --zoom Z            page zoom to emulate: the view is Z times the size of its viewport (default 1)
//   --at X,Y            the point of the view, in fractions, at the middle of the viewport (default 0.5,0.5)
//   --capture out.png   save what the control drew, then exit
//   --benchmark N       turn the model for N frames, print frame times as a table row, then exit
//   --scenario recycle  take the control out of the window and put it back, as a recycled page does
//   --exit-after S      close after S seconds
//   --selftest          draw built-in scenes, read them back and check them; exit code 0 when all pass
//   --software          start Avalonia with its software renderer (the control must report "Unavailable")
//   --log file          also write the log lines to a file

internal sealed class Options
{
    public string? File { get; set; }

    public int Artwork { get; set; }

    public int? View { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public bool Poster { get; set; }

    public bool Transparent { get; set; }

    public double Zoom { get; set; } = 1;

    public Point Centre { get; set; } = new(0.5, 0.5);

    public string? Capture { get; set; }

    public int BenchmarkFrames { get; set; }

    public string? Scenario { get; set; }

    public double ExitAfterSeconds { get; set; }

    public bool Software { get; set; }

    public bool SelfTest { get; set; }
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
        static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--artwork": Options.Artwork = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--view": Options.View = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--poster": Options.Poster = true; break;
                    case "--transparent": Options.Transparent = true; break;
                    case "--zoom": Options.Zoom = Number(args[++i]); break;
                    case "--capture": Options.Capture = Path.GetFullPath(args[++i]); break;
                    case "--benchmark": Options.BenchmarkFrames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--scenario": Options.Scenario = args[++i]; break;
                    case "--exit-after": Options.ExitAfterSeconds = Number(args[++i]); break;
                    case "--software": Options.Software = true; break;
                    case "--selftest": Options.SelfTest = true; break;
                    case "--log": _log = new StreamWriter(args[++i]); break;
                    case "--size":
                        string[] size = args[++i].Split('x');
                        Options.Width = Number(size[0]);
                        Options.Height = Number(size[1]);
                        break;
                    case "--at":
                        string[] at = args[++i].Split(',');
                        Options.Centre = new Point(Number(at[0]), Number(at[1]));
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
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine("An option has a missing or malformed value.");
            return 1;
        }

        if (Options.File is null && !Options.SelfTest)
        {
            Console.Error.WriteLine("usage: Scene3DViewer <file.u3d | file.pdf> [--artwork N] [--view N] [--size WxH] [--poster] [--zoom Z] [--at X,Y] [--capture out.png] [--benchmark N] [--scenario recycle] [--exit-after S] [--software] [--log file]");
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
            desktop.MainWindow = Program.Options.SelfTest ? new SelfTestWindow() : new MainWindow(Program.Options);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
