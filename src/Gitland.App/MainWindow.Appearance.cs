using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    Control GlassControls(Action render) {
        var appearance = Appearance;
        var content = new StackPanel { Spacing = 16 };
        TextBlock Label(string label) { var text = Text(label.ToUpperInvariant(), 10.5, Faint, true); text.LetterSpacing = 1.2; return text; }
        content.Children.Add(Col(Text("Atelier glass", 17, strong: true), Paragraph("Amethyst-tinted surfaces with a soft sky and peach sheen. The source canvas stays opaque.", Muted)));
        var materials = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, name) in new[] { ("mica", "Mica"), ("acrylic", "Acrylic"), ("blur", "Blur"), ("solid", "Solid") }) {
            var choice = Button("Use " + name + " material", () => { if (SavePreferences(GitlandApplication.Preferences with { Appearance = Appearance with { Material = id } })) render(); });
            choice.Content = Text(name, 12); choice.Classes.Add("selection-item"); choice.Classes.Set("selected", appearance.Material == id);
            choice.Background = appearance.Material == id ? SelectedSurface : Brushes.Transparent; choice.Margin = new Thickness(0, 0, 5, 0); materials.Children.Add(choice);
        }
        content.Children.Add(Col(Label("Material"), materials, Paragraph("Mica uses the Windows backdrop. Acrylic and Blur show more of the window behind. Solid is fully opaque; unsupported materials use a solid fallback.", Faint)));
        var swatches = new WrapPanel();
        foreach (var (name, hex) in Tints) {
            var swatch = Button("Use " + name + " tint", () => { if (SavePreferences(GitlandApplication.Preferences with { Appearance = Appearance with { Tint = hex } })) render(); });
            swatch.Width = swatch.Height = 36; swatch.Padding = new Thickness(5); swatch.Margin = new Thickness(0, 0, 7, 0); swatch.Background = Brushes.Transparent;
            swatch.BorderBrush = appearance.Tint.Equals(hex, StringComparison.OrdinalIgnoreCase) ? Ink : Hairline;
            swatch.Content = new Border { Width = 22, Height = 22, Background = Brush(hex), CornerRadius = new CornerRadius(11) };
            ToolTip.SetTip(swatch, name); swatches.Children.Add(swatch);
        }
        var custom = new TextBox { Name = "GlassTint", Text = appearance.Tint, Watermark = "#8B6CF0", MaxLength = 7, Width = 110 };
        Avalonia.Automation.AutomationProperties.SetName(custom, "Custom glass tint");
        var error = Text("", 11, Red); error.IsVisible = false;
        bool syncingHue = false;
        var hue = new Slider { Name = "GlassHue", Minimum = 0, Maximum = 360, Value = Color.Parse(appearance.Tint).ToHsv().H };
        Avalonia.Automation.AutomationProperties.SetName(hue, "Tint hue");
        var spectrum = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (int i = 0; i <= 6; i++) spectrum.GradientStops.Add(new GradientStop(new HsvColor(1, i * 60, .55, .95).ToRgb(), i / 6d));
        void MarkTint(string hex) { foreach (var swatch in swatches.Children.OfType<Button>()) swatch.BorderBrush = swatch.Content is Border color && color.Background is ISolidColorBrush brush && brush.Color == Color.Parse(hex) ? Ink : Hairline; }
        void SetTint() {
            string hex = custom.Text?.Trim() ?? "";
            bool valid = System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$");
            error.IsVisible = !valid; error.Text = valid ? "" : "Use a six-digit color, such as #8B6CF0.";
            if (valid && SavePreferences(GitlandApplication.Preferences with { Appearance = Appearance with { Tint = hex } })) {
                syncingHue = true; hue.Value = Color.Parse(hex).ToHsv().H; syncingHue = false; MarkTint(hex);
            }
        }
        custom.LostFocus += (_, _) => SetTint(); custom.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetTint(); e.Handled = true; } };
        hue.PropertyChanged += (_, e) => {
            if (e.Property != Slider.ValueProperty || syncingHue) return;
            var hsv = Color.Parse(Appearance.Tint).ToHsv(); var color = new HsvColor(1, hue.Value, Math.Max(.3, hsv.S), Math.Max(.4, hsv.V)).ToRgb();
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            if (SavePreferences(GitlandApplication.Preferences with { Appearance = Appearance with { Tint = hex } })) { custom.Text = hex; error.IsVisible = false; MarkTint(hex); }
        };
        content.Children.Add(Col(Label("Tint"), swatches, new Border { Height = 5, Background = spectrum, CornerRadius = new CornerRadius(3) }, hue, Row(custom, Text("Custom color · Enter to apply", 11, Faint)), error));
        Control Amount(string name, double initial, Func<GlassAppearance, double, GlassAppearance> change) {
            var value = Text($"{initial:P0}", 11, Muted);
            var heading = new Grid { ColumnDefinitions = new("*,Auto") }; heading.Children.Add(Label(name)); Grid.SetColumn(value, 1); heading.Children.Add(value);
            var slider = new Slider { Name = "Glass" + name.Replace(" ", ""), Minimum = 0, Maximum = 1, Value = initial, TickFrequency = .05, IsSnapToTickEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(slider, name);
            slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { value.Text = $"{slider.Value:P0}"; SavePreferences(GitlandApplication.Preferences with { Appearance = change(Appearance, slider.Value) }); } };
            return Col(heading, slider);
        }
        content.Children.Add(Amount("Glassiness", appearance.Glassiness, (a, v) => a with { Glassiness = v }));
        content.Children.Add(Amount("Transparency", appearance.Transparency, (a, v) => a with { Transparency = v }));
        content.Children.Add(Amount("Color level", appearance.ColorLevel, (a, v) => a with { ColorLevel = v }));
        content.Children.Add(Button("Reset Atelier appearance", () => { if (SavePreferences(GitlandApplication.Preferences with { Appearance = new GlassAppearance() })) render(); }));
        return new Border { Child = content, Padding = new Thickness(18), Background = ButtonFill, BorderBrush = RimLight, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
    }
}
