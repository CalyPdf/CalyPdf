// Audits the Fluent palettes in Caly.Core/App.axaml and the brush bindings in Caly.Core/CalyTheme.axaml
// against the rules in GUIDELINES.md: WCAG 2.1 contrast and colour-blindness (protanopia, deuteranopia,
// tritanopia) checks.
//
//   dotnet run --file tools/Palette/AuditPalette.cs             print failures only, exit 1 if any
//   dotnet run --file tools/Palette/AuditPalette.cs -- --all    print every check
//
// Rule ids (T1, G1, ...) match GUIDELINES.md.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

string scriptDir = ScriptDirectory();
string coreDir = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", "Caly.Core"));
string appXaml = File.ReadAllText(Path.Combine(coreDir, "App.axaml"));
string themeXaml = File.ReadAllText(Path.Combine(coreDir, "CalyTheme.axaml"));
bool all = args.Contains("--all");

// Page overlay colours drawn in Caly.Core/Controls/PageInteractiveLayerControl.cs (SelectionColor, SearchColor).
Rgba selectionOverlay = new(0x33, 0x99, 0xFF, 169);
Rgba searchOverlay = new(255, 0, 0, 120);

// CalyTheme.axaml: brush key -> palette attribute name.
Dictionary<string, string> bindings = Regex
    .Matches(themeXaml, """<SolidColorBrush x:Key="(\w+)" Color="\{DynamicResource System(\w+?)Color\}" />""")
    .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value == "Region" ? "RegionColor" : m.Groups[2].Value);

// Machado, Oliveira and Fernandes (2009), severity 1.0, applied in linear RGB.
(string Kind, double[] Matrix)[] cvd =
{
    ("protanopia", new[] { 0.152286, 1.052583, -0.204868, 0.114503, 0.786281, 0.099216, -0.003882, -0.048116, 1.051998 }),
    ("deuteranopia", new[] { 0.367322, 0.860646, -0.227968, 0.280085, 0.672501, 0.047413, -0.011820, 0.042940, 0.968881 }),
    ("tritanopia", new[] { 1.255528, -0.076749, -0.178779, -0.078411, 0.930809, 0.147602, 0.004733, 0.691367, 0.303900 })
};

int failures = 0;
int checks = 0;

void Report(string theme, string id, string what, string measure, bool pass)
{
    checks++;
    if (!pass) failures++;
    if (all || !pass) Console.WriteLine($"{(pass ? "pass" : "FAIL")}  {theme,-5} {id,-4} {what,-64} {measure}");
}

foreach (string theme in new[] { "Light", "Dark" })
{
    Dictionary<string, Rgba> palette = ReadPalette(appXaml, theme);
    Rgba region = palette["RegionColor"];

    Rgba Get(string name)
    {
        string key = bindings.TryGetValue(name, out string? bound) ? bound : name;
        if (!palette.TryGetValue(key, out Rgba c)) throw new InvalidDataException($"Unknown colour '{name}' ({theme}).");
        return Over(c, region);
    }

    Rgba Literal(string name) => name switch
    {
        "SelectionOverlay" => Over(selectionOverlay, new Rgba(255, 255, 255, 255)),
        "SearchOverlay" => Over(searchOverlay, new Rgba(255, 255, 255, 255)),
        _ => Get(name)
    };

    void Contrast(string id, string fg, string bg, double min, string kind)
    {
        double r = Ratio(Get(fg), Get(bg));
        Report(theme, id, $"{kind}: {fg} on {bg}", $"{r:N2}:1 (min {min:N1})", r >= min);
    }

    // ---- Structure (S) ----
    Report(theme, "S1", "no per-theme dictionaries in CalyTheme.axaml", "", !themeXaml.Contains("ThemeDictionaries"));
    Report(theme, "S2", "selected tab, desk and window share RegionColor", "",
        bindings["SelectedTabItemBackgroundBrush"] == "RegionColor" && bindings["CalyDeskBrush"] == "RegionColor");
    double step = LStar(region) - LStar(Get("ChromeLow"));
    Report(theme, "S3", "selected tab vs strip: lightness step (L*)", $"{step:N1} (min 8.0)", step >= 8.0);
    Report(theme, "S4", "ChromeLow darker than RegionColor", "", LStar(Get("ChromeLow")) < LStar(region));
    string[] ramp = { "BaseHigh", "BaseMediumHigh", "BaseMedium", "BaseMediumLow", "BaseLow" };
    double[] dist = ramp.Select(k => Ratio(Get(k), region)).ToArray();
    Report(theme, "S5", "Base ramp strictly ordered by contrast against RegionColor", string.Join(" > ", dist.Select(d => d.ToString("N1"))),
        dist.Zip(dist.Skip(1), (a, b) => a > b).All(x => x) || dist.Zip(dist.Skip(1), (a, b) => a < b).All(x => x));
    (string Alias, string Of)[] aliases =
    {
        ("ChromeAltLow", "BaseMediumHigh"), ("ChromeDisabledHigh", "BaseLow"), ("ChromeDisabledLow", "BaseMediumLow"),
        ("ChromeHigh", "BaseLow"), ("ListMedium", "BaseLow"), ("ListLow", "ChromeMedium")
    };
    Report(theme, "S6", "alias rules hold", string.Join(",", aliases.Where(a => palette[a.Alias] != palette[a.Of]).Select(a => a.Alias)),
        aliases.All(a => palette[a.Alias] == palette[a.Of]));
    Report(theme, "S7", "ErrorText is set", "", palette.ContainsKey("ErrorText"));

    double hoverFromStrip = Math.Abs(LStar(Get("ChromeGray")) - LStar(Get("ChromeLow")));
    double hoverFromSelected = Math.Abs(LStar(Get("ChromeGray")) - LStar(region));
    Report(theme, "S12", "hover shade (ChromeGray) vs strip and vs selected tab: L* step",
        $"{hoverFromStrip:N1} and {hoverFromSelected:N1} (min 3.0)", hoverFromStrip >= 3.0 && hoverFromSelected >= 3.0);
    Report(theme, "S12", "hover brushes are bound to ChromeGray", "",
        bindings["TabItemHeaderBackgroundUnselectedPointerOver"] == "ChromeGray"
        && bindings["TabItemHeaderBackgroundUnselectedPointerOverWindowInactive"] == "ChromeGray");

    // ---- Text, 4.5:1 (T) ----
    foreach (string bg in new[] { "RegionColor", "ChromeLow", "ChromeMediumLow" }) Contrast("T1", "BaseHigh", bg, 4.5, "text");
    foreach (string bg in new[] { "RegionColor", "ChromeLow" })
    {
        Contrast("T2", "BaseMediumHigh", bg, 4.5, "text");
        Contrast("T3", "BaseMedium", bg, 4.5, "text");
    }
    Contrast("T4", "ChromeWhite", "Accent", 4.5, "text on accent fill");
    Contrast("T5", "BaseHigh", "BaseLow", 4.5, "text on default button fill");
    Contrast("T6", "BaseHigh", "ChromeMedium", 4.5, "text on hover fill");
    Contrast("T7", "ErrorText", "RegionColor", 4.5, "error text");

    // ---- Graphics and UI components, 3:1 (G) ----
    Contrast("G1", "BaseMediumLow", "RegionColor", 3.0, "glyph or border");
    Contrast("G1", "BaseMediumLow", "ChromeLow", 3.0, "glyph or border");
    Contrast("G2", "BaseMedium", "RegionColor", 3.0, "control border");
    Contrast("G3", "ErrorText", "RegionColor", 3.0, "error border");
    Contrast("G4", "BaseHigh", "BaseMediumLow", 3.0, "glyph on pressed fill");
    Contrast("G5", "ChromeWhite", "Accent", 3.0, "icon on accent fill");

    // Tab and button brushes bound in CalyTheme.axaml.
    (string Fg, string Bg, double Min, string Kind)[] brushPairs =
    {
        ("CloseItemButtonForeground", "TabItemBackgroundBrush", 3.0, "close glyph"),
        ("CloseItemButtonForeground", "SelectedTabItemBackgroundBrush", 3.0, "close glyph"),
        ("CloseItemButtonPointerOverBrush", "TabItemBackgroundBrush", 3.0, "close glyph, hover"),
        ("CloseItemButtonPointerOverBrush", "SelectedTabItemBackgroundBrush", 3.0, "close glyph, hover"),
        ("CloseItemButtonInactiveWindowBrush", "TabItemBackgroundBrushWindowInactive", 3.0, "close glyph, inactive"),
        ("CloseItemButtonInactiveWindowBrush", "SelectedTabItemBackgroundBrush", 3.0, "close glyph, inactive"),
        ("CloseItemButtonInactiveWindowPointerOverBrush", "TabItemBackgroundBrushWindowInactive", 3.0, "close glyph, inactive hover"),
        ("CloseItemButtonForeground", "CloseItemButtonPressedBrush", 3.0, "close glyph, pressed"),
        ("TabScrollNavigationButtonForegroundBrush", "TabControlWindowActiveBackgroundBrush", 3.0, "scroll glyph"),
        ("TabScrollNavigationButtonForegroundPointerOverBrush", "TabScrollNavigationButtonBackgroundPointerOverBrush", 3.0, "scroll glyph, hover"),
        ("TabScrollNavigationButtonForegroundPointerOverBrush", "TabScrollNavigationButtonBackgroundPressedBrush", 3.0, "scroll glyph, pressed"),
        ("TabScrollNavigationButtonForegroundInactiveBrush", "TabControlWindowActiveBackgroundBrush", 3.0, "scroll glyph, inactive"),
        ("AddItemCommandButtonForegroundPointerOverBrush", "AddItemCommandButtonBackgroundPointerOverBrush", 3.0, "add-tab glyph, hover"),
        ("AddItemCommandButtonForegroundPointerOverBrush", "AddItemCommandButtonBackgroundPressedBrush", 3.0, "add-tab glyph, pressed"),
        ("InactiveWindowAddCommandButtonForegroundBrush", "TabControlWindowActiveBackgroundBrush", 3.0, "add-tab glyph, inactive"),
        ("InactiveWindowAddCommandButtonPointerOverForegroundBrush", "TabControlWindowActiveBackgroundBrush", 3.0, "add-tab glyph, inactive hover"),
        // Text colours Fluent uses for tab titles: selected = BaseHigh, unselected = BaseMedium, hover = BaseMediumHigh.
        ("BaseHigh", "SelectedTabItemBackgroundBrush", 4.5, "tab title, selected"),
        ("BaseMedium", "TabItemBackgroundBrush", 4.5, "tab title, unselected"),
        ("BaseMedium", "TabItemBackgroundBrushWindowInactive", 4.5, "tab title, inactive"),
        ("BaseMediumHigh", "TabItemHeaderBackgroundUnselectedPointerOver", 4.5, "tab title, hover"),
        ("BaseMediumHigh", "TabItemHeaderBackgroundUnselectedPointerOverWindowInactive", 4.5, "tab title, inactive hover"),
        // Selected items in drop-downs sit on the accent fill.
        ("ComboBoxItemForegroundSelected", "Accent", 4.5, "selected item text"),
        ("ComboBoxItemForegroundSelectedPointerOver", "Accent", 4.5, "selected item text, hover"),
        ("ComboBoxItemForegroundSelectedPressed", "Accent", 4.5, "selected item text, pressed"),
    };
    foreach (var (fg, bg, min, kind) in brushPairs) Contrast("G6", fg, bg, min, kind);

    // ---- Decorative and disabled, 1.5:1 (D) ----
    Contrast("D1", "TabItemRightSeparatorBackgroundBrush", "TabControlWindowActiveBackgroundBrush", 1.5, "tab separator");
    Contrast("D2", "BaseMediumLow", "BaseLow", 1.5, "disabled text on disabled fill");
    Contrast("D3", "TabScrollNavigationButtonForegroundDisabledBrush", "TabControlWindowActiveBackgroundBrush", 1.5, "disabled scroll glyph");

    // ---- Colour blindness (C) ----
    foreach (var (kind, m) in cvd)
    {
        Rgba S(string n) => Simulate(Literal(n), m);

        double de = DeltaE(S("ErrorText"), S("BaseMedium"));
        Report(theme, "C1", $"{kind}: error border vs normal control border", $"dE {de:N1} (min 15.0)", de >= 15);

        double rr = Ratio(S("ErrorText"), S("RegionColor"));
        Report(theme, "C2", $"{kind}: error colour on RegionColor", $"{rr:N2}:1 (min 3.0)", rr >= 3.0);

        double sel = DeltaE(S("SelectionOverlay"), S("SearchOverlay"));
        Report(theme, "C3", $"{kind}: selection vs search overlay on white", $"dE {sel:N1} (min 15.0)", sel >= 15);

        foreach (string t in new[] { "BaseHigh", "BaseMediumHigh", "BaseMedium" })
        {
            double tr = Ratio(S(t), S("RegionColor"));
            Report(theme, "C4", $"{kind}: {t} on RegionColor", $"{tr:N2}:1 (min 4.5)", tr >= 4.5);
        }

        double acc = DeltaE(S("Accent"), S("RegionColor"));
        Report(theme, "C5", $"{kind}: accent fill vs RegionColor", $"dE {acc:N1} (min 10.0)", acc >= 10);
    }
}

// ---- Source rules (S8-S10), theme independent ----
var axamls = Directory.EnumerateFiles(coreDir, "*.axaml", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
    .ToArray();

string[] Offenders(string pattern, params string[] allowedFiles) => axamls
    .Where(f => !allowedFiles.Contains(Path.GetFileName(f)))
    .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i + 1))
        .Where(t => Regex.IsMatch(t.line, pattern) && !t.line.TrimStart().StartsWith("<!--")))
    .Select(t => $"{Path.GetFileName(t.f)}:{t.Item3}")
    .ToArray();

string[] opacity = Offenders(@"\sOpacity=""[^""]*""");
Report("both", "S8", "no Opacity attribute in Caly.Core axaml", string.Join(", ", opacity), opacity.Length == 0);

// PageItem.axaml paints the white page itself.
string[] literals = Offenders(@"(Foreground|Background|Fill|Stroke|BorderBrush)=""(#[0-9a-fA-F]{3,8}|White|Black|Red|Green|Blue|Yellow|Gray|Grey|Orange|Purple|Pink)""", "App.axaml", "PageItem.axaml");
Report("both", "S9", "no literal colours in Caly.Core axaml", string.Join(", ", literals), literals.Length == 0);

string[] colourAsBrush = Offenders(@"(Foreground|Background|Fill|Stroke|BorderBrush)=""\{(Dynamic|Static)Resource System\w+Color\}""");
Report("both", "S10", "no ...Color resource assigned to a brush property", string.Join(", ", colourAsBrush), colourAsBrush.Length == 0);

string tabView = File.ReadAllText(Path.Combine(coreDir, "Controls", "DocumentTabView.axaml"));
Report("both", "S13","invalid page number has a non-colour cue (border thickness and font style)", "",
    tabView.Contains(@"Selector=""TextBox#PART_TextBoxPageNumber:error /template/ Border#PART_BorderElement""")
    && tabView.Contains(@"<Setter Property=""BorderThickness"" Value=""2"" />")
    && tabView.Contains(@"<Setter Property=""FontStyle"" Value=""Italic"" />"));

string[] weakText = Offenders(@"\sForeground=""\{DynamicResource SystemControlForegroundBase(MediumLow|Low)Brush\}""");
Report("both", "S11", "BaseMediumLow and BaseLow are not text colours", string.Join(", ", weakText), weakText.Length == 0);

Console.WriteLine($"\n{checks} checks, {failures} failed.");
return failures == 0 ? 0 : 1;

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

static Dictionary<string, Rgba> ReadPalette(string xaml, string theme)
{
    string element = Regex.Match(xaml, $"""<ColorPaletteResources x:Key="{theme}"([^>]*)/>""").Groups[1].Value;
    var result = new Dictionary<string, Rgba>();
    foreach (Match m in Regex.Matches(element, """(\w+)="#([0-9a-fA-F]{8})"""))
    {
        uint v = uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber);
        result[m.Groups[1].Value] = new Rgba((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)(v >> 24));
    }

    return result;
}

static Rgba Over(Rgba top, Rgba bottom)
{
    if (top.A == 255) return top;
    double a = top.A / 255.0;
    return new Rgba(
        (byte)Math.Round(top.R * a + bottom.R * (1 - a)),
        (byte)Math.Round(top.G * a + bottom.G * (1 - a)),
        (byte)Math.Round(top.B * a + bottom.B * (1 - a)), 255);
}

static double Lin(double c) { c /= 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
static double Delin(double v) { v = Math.Clamp(v, 0, 1); return 255.0 * (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055); }
static double Luminance(Rgba c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

static double Ratio(Rgba a, Rgba b)
{
    double l1 = Luminance(a), l2 = Luminance(b);
    if (l1 < l2) (l1, l2) = (l2, l1);
    return (l1 + 0.05) / (l2 + 0.05);
}

static double LStar(Rgba c)
{
    double y = Luminance(c);
    return y > 0.008856 ? 116 * Math.Cbrt(y) - 16 : 903.3 * y;
}

static Rgba Simulate(Rgba c, double[] m)
{
    double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
    return new Rgba(
        (byte)Math.Round(Delin(m[0] * r + m[1] * g + m[2] * b)),
        (byte)Math.Round(Delin(m[3] * r + m[4] * g + m[5] * b)),
        (byte)Math.Round(Delin(m[6] * r + m[7] * g + m[8] * b)), 255);
}

static (double L, double A, double B) Lab(Rgba c)
{
    double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
    double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
    double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
    double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
    static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
    double fx = F(x), fy = F(y), fz = F(z);
    return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
}

// CIEDE2000.
static double DeltaE(Rgba c1, Rgba c2)
{
    var (l1, a1, b1) = Lab(c1);
    var (l2, a2, b2) = Lab(c2);
    double c1s = Math.Sqrt(a1 * a1 + b1 * b1), c2s = Math.Sqrt(a2 * a2 + b2 * b2);
    double cBar = (c1s + c2s) / 2;
    double g = 0.5 * (1 - Math.Sqrt(Math.Pow(cBar, 7) / (Math.Pow(cBar, 7) + Math.Pow(25, 7))));
    double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
    double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
    static double Hue(double b, double a) { double h = Math.Atan2(b, a) * 180 / Math.PI; return h < 0 ? h + 360 : h; }
    double h1p = c1p == 0 ? 0 : Hue(b1, a1p), h2p = c2p == 0 ? 0 : Hue(b2, a2p);
    double dLp = l2 - l1, dCp = c2p - c1p;
    double dhp = 0;
    if (c1p * c2p != 0)
    {
        dhp = h2p - h1p;
        if (dhp > 180) dhp -= 360; else if (dhp < -180) dhp += 360;
    }
    double dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(dhp * Math.PI / 360);
    double lBarP = (l1 + l2) / 2, cBarP = (c1p + c2p) / 2;
    double hBarP = h1p + h2p;
    if (c1p * c2p != 0)
    {
        if (Math.Abs(h1p - h2p) <= 180) hBarP /= 2;
        else hBarP = (h1p + h2p < 360 ? hBarP + 360 : hBarP - 360) / 2;
    }
    double t = 1 - 0.17 * Math.Cos((hBarP - 30) * Math.PI / 180) + 0.24 * Math.Cos(2 * hBarP * Math.PI / 180)
               + 0.32 * Math.Cos((3 * hBarP + 6) * Math.PI / 180) - 0.20 * Math.Cos((4 * hBarP - 63) * Math.PI / 180);
    double dTheta = 30 * Math.Exp(-Math.Pow((hBarP - 275) / 25, 2));
    double rc = 2 * Math.Sqrt(Math.Pow(cBarP, 7) / (Math.Pow(cBarP, 7) + Math.Pow(25, 7)));
    double sl = 1 + 0.015 * Math.Pow(lBarP - 50, 2) / Math.Sqrt(20 + Math.Pow(lBarP - 50, 2));
    double sc = 1 + 0.045 * cBarP, sh = 1 + 0.015 * cBarP * t;
    double rt = -Math.Sin(2 * dTheta * Math.PI / 180) * rc;
    return Math.Sqrt(Math.Pow(dLp / sl, 2) + Math.Pow(dCp / sc, 2) + Math.Pow(dHp / sh, 2) + rt * (dCp / sc) * (dHp / sh));
}

readonly record struct Rgba(byte R, byte G, byte B, byte A);
