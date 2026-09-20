// Regenerates the two <ColorPaletteResources> elements in Caly.Core/App.axaml from palette.json.
//
//   dotnet run tools/Palette/GeneratePalette.cs           rewrite App.axaml if it is out of date
//   dotnet run tools/Palette/GeneratePalette.cs --check   exit 1 if App.axaml is out of date, write nothing
//
// Edit the picks and ramps in palette.json, never the generated attributes in App.axaml.
// README.md in this folder explains what each pick drives.

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string scriptDir = ScriptDirectory();
string jsonPath = Path.Combine(scriptDir, "palette.json");
string xamlPath = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", "Caly.Core", "App.axaml"));
bool check = args.Contains("--check");

using JsonDocument json = JsonDocument.Parse(File.ReadAllText(jsonPath), new JsonDocumentOptions
{
    CommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
});

byte[] raw = File.ReadAllBytes(xamlPath);
bool hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
string xaml = Encoding.UTF8.GetString(raw, hasBom ? 3 : 0, raw.Length - (hasBom ? 3 : 0));
string updated = xaml;

foreach (string theme in new[] { "Light", "Dark" })
{
    SortedDictionary<string, string> attributes = BuildAttributes(json.RootElement.GetProperty(theme));
    string element = $"""<ColorPaletteResources x:Key="{theme}" {string.Join(' ', attributes.Select(a => $"{a.Key}=\"{a.Value}\""))} />""";

    Regex existing = new($"""<ColorPaletteResources x:Key="{theme}"[^>]*/>""");
    Match match = existing.Match(updated);
    if (!match.Success)
    {
        Console.Error.WriteLine($"No <ColorPaletteResources x:Key=\"{theme}\" ... /> element found in {xamlPath}");
        return 2;
    }

    foreach (string change in Diff(match.Value, attributes))
    {
        Console.WriteLine($"{theme}: {change}");
    }

    updated = existing.Replace(updated, _ => element, 1);
}

if (updated == xaml)
{
    Console.WriteLine("App.axaml is up to date.");
    return 0;
}

if (check)
{
    Console.Error.WriteLine("App.axaml is out of date. Run: dotnet run tools/Palette/GeneratePalette.cs");
    return 1;
}

File.WriteAllText(xamlPath, updated, new UTF8Encoding(hasBom));
Console.WriteLine("App.axaml updated.");
return 0;

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

static SortedDictionary<string, string> BuildAttributes(JsonElement theme)
{
    JsonElement picks = theme.GetProperty("picks");
    JsonElement ramps = theme.GetProperty("ramps");

    Rgb accent = Pick(picks, "accent");
    Rgb ink = Pick(picks, "ink");
    Rgb surface = Pick(picks, "surface");
    Rgb chrome = Pick(picks, "chrome");
    Rgb paper = Pick(picks, "paper");

    // Shades of ink over surface.
    Rgb baseMediumHigh = Mix(surface, ink, Ramp(ramps, "baseMediumHigh"));
    Rgb baseMedium = Mix(surface, ink, Ramp(ramps, "baseMedium"));
    Rgb baseMediumLow = Mix(surface, ink, Ramp(ramps, "baseMediumLow"));
    Rgb baseLow = Mix(surface, ink, Ramp(ramps, "baseLow"));

    // The frame shade, and the hover shade between it and the surface (stored in the otherwise unused ChromeGray).
    Rgb chromeLow = Mix(surface, ink, Ramp(ramps, "chromeLow"));

    Rgb black = new(0, 0, 0);
    Rgb white = new(255, 255, 255);

    SortedDictionary<string, string> attributes = new(StringComparer.Ordinal)
    {
        ["Accent"] = Argb(accent),

        ["AltHigh"] = Argb(paper),
        ["AltMediumHigh"] = Argb(paper, 0.8),
        ["AltMedium"] = Argb(paper, 0.6),
        ["AltMediumLow"] = Argb(paper, 0.4),
        ["AltLow"] = Argb(paper, 0.2),

        ["BaseHigh"] = Argb(ink),
        ["BaseMediumHigh"] = Argb(baseMediumHigh),
        ["BaseMedium"] = Argb(baseMedium),
        ["BaseMediumLow"] = Argb(baseMediumLow),
        ["BaseLow"] = Argb(baseLow),

        ["ChromeAltLow"] = Argb(baseMediumHigh),
        ["ChromeBlackHigh"] = Argb(black),
        ["ChromeBlackMedium"] = Argb(black, 0.8),
        ["ChromeBlackMediumLow"] = Argb(black, 0.4),
        ["ChromeBlackLow"] = Argb(black, 0.2),
        ["ChromeDisabledHigh"] = Argb(baseLow),
        ["ChromeDisabledLow"] = Argb(baseMediumLow),
        ["ChromeGray"] = Argb(Mix(chromeLow, surface, Ramp(ramps, "tabHover"))),
        ["ChromeHigh"] = Argb(baseLow),
        ["ChromeLow"] = Argb(chromeLow),
        ["ChromeMedium"] = Argb(chrome),
        ["ChromeMediumLow"] = Argb(Mix(surface, ink, Ramp(ramps, "chromeMediumLow"))),
        ["ChromeWhite"] = Argb(white),

        ["ListLow"] = Argb(chrome),
        ["ListMedium"] = Argb(baseLow),

        ["RegionColor"] = Argb(surface)
    };

    if (picks.TryGetProperty("error", out JsonElement error))
    {
        attributes["ErrorText"] = Argb(Parse(error.GetString()!));
    }

    return attributes;
}

// Attributes whose value differs from the existing element, as "Name old -> new".
static IEnumerable<string> Diff(string existingElement, SortedDictionary<string, string> generated)
{
    Dictionary<string, string> old = Regex.Matches(existingElement, @"(\w+)=""([^""]*)""")
        .Where(m => m.Groups[1].Value != "Key")
        .ToDictionary(m => m.Groups[1].Value, m => Normalise(m.Groups[2].Value));

    foreach ((string name, string value) in generated)
    {
        if (!old.TryGetValue(name, out string? was))
        {
            yield return $"{name} (new) {value}";
        }
        else if (was != value)
        {
            yield return $"{name} {was} -> {value}";
        }
    }

    foreach (string name in old.Keys.Where(n => !generated.ContainsKey(n)))
    {
        yield return $"{name} (removed)";
    }
}

static string Normalise(string value) => value.ToLowerInvariant() switch
{
    "white" => "#ffffffff",
    "black" => "#ff000000",
    { Length: 7 } v => "#ff" + v[1..],
    var v => v
};

static Rgb Pick(JsonElement picks, string name)
{
    if (!picks.TryGetProperty(name, out JsonElement value))
    {
        throw new InvalidDataException($"Missing pick '{name}' in palette.json.");
    }

    return Parse(value.GetString()!);
}

static double Ramp(JsonElement ramps, string name)
{
    if (!ramps.TryGetProperty(name, out JsonElement value))
    {
        throw new InvalidDataException($"Missing ramp '{name}' in palette.json.");
    }

    return value.GetDouble();
}

static Rgb Parse(string hex)
{
    if (hex.Length != 7 || hex[0] != '#')
    {
        throw new InvalidDataException($"'{hex}' is not a #rrggbb colour.");
    }

    return new Rgb(
        Convert.ToByte(hex[1..3], 16),
        Convert.ToByte(hex[3..5], 16),
        Convert.ToByte(hex[5..7], 16));
}

static Rgb Mix(Rgb from, Rgb to, double t) => new(Lerp(from.R, to.R, t), Lerp(from.G, to.G, t), Lerp(from.B, to.B, t));

static byte Lerp(byte a, byte b, double t) =>
    (byte)Math.Clamp((int)Math.Round(a + (b - a) * t, MidpointRounding.AwayFromZero), 0, 255);

static string Argb(Rgb c, double alpha = 1.0)
{
    byte a = (byte)Math.Round(255 * alpha, MidpointRounding.AwayFromZero);
    return $"#{a:x2}{c.R:x2}{c.G:x2}{c.B:x2}";
}

readonly record struct Rgb(byte R, byte G, byte B);
