using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Caly.Scene3D.Avalonia;

namespace Scene3DViewer;

/// <summary>
/// The lighting panel of Caly's 3D view, for the viewer: brightness, ambient, shine, highlight size and
/// colour, with the same ranges. Each slider starts at the file's value; <see cref="Changed"/> gives the
/// <see cref="Scene3DShading"/> they make as they move.
/// </summary>
internal sealed class ShadingPanel : StackPanel
{
    private readonly Slider _light = NewSlider(0, 2, 1);
    private readonly Slider _ambient = NewSlider(0, 1, 0);
    private readonly Slider _shine = NewSlider(0, 3, 1);
    private readonly Slider _size = NewSlider(-2, 2, 0);    // steps of two: above 0 broader, below tighter
    private readonly Slider _colour = NewSlider(0, 1.5, 1);
    private readonly Grid _rows = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,52") };

    public ShadingPanel()
    {
        Spacing = 6;
        AddRow("Brightness", _light, Percent);
        AddRow("Ambient", _ambient, v => "+" + Percent(v));
        AddRow("Shine", _shine, Percent);
        AddRow("Highlight size", _size, v => "×" + Math.Pow(2, v).ToString("0.##", CultureInfo.InvariantCulture));
        AddRow("Colour", _colour, Percent);
        ToolTip.SetTip(_size, "Left: tight highlights. Right: broad ones.");
        Children.Add(_rows);

        var reset = new Button { Content = "Reset", HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(reset, "Lights and materials as the file has them");
        reset.Click += (_, _) => Reset();
        Children.Add(reset);
    }

    /// <summary>Raised as a slider moves, with what the sliders now make.</summary>
    public event Action<Scene3DShading>? Changed;

    public Scene3DShading Shading => new()
    {
        LightIntensity = (float)_light.Value,
        AddedAmbient = (float)_ambient.Value,
        SpecularIntensity = (float)_shine.Value,
        HighlightSharpness = (float)Math.Pow(2, -_size.Value),
        DiffuseIntensity = (float)_colour.Value
    };

    public void Reset()
    {
        _light.Value = 1;
        _ambient.Value = 0;
        _shine.Value = 1;
        _size.Value = 0;
        _colour.Value = 1;
    }

    private static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static Slider NewSlider(double minimum, double maximum, double value) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        SmallChange = (maximum - minimum) / 40,
        LargeChange = (maximum - minimum) / 8,
        Width = 150,
        Height = 32,
        MinHeight = 0,
        Margin = new Thickness(10, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void AddRow(string name, Slider slider, Func<double, string> format)
    {
        int row = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var label = new TextBlock { Text = name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var value = new TextBlock { Text = format(slider.Value), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row);
        Grid.SetRow(slider, row);
        Grid.SetColumn(slider, 1);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 2);
        _rows.Children.Add(label);
        _rows.Children.Add(slider);
        _rows.Children.Add(value);

        slider.ValueChanged += (_, e) =>
        {
            value.Text = format(e.NewValue);
            Changed?.Invoke(Shading);
        };
    }
}
