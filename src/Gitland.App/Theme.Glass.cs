using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Gitland.Core;

namespace Gitland.App;

public static partial class Palette {
    public static readonly SolidColorBrush Backdrop = Token("Backdrop"), Chrome = Token("Chrome"), Pane = Token("Pane"), GlassBase = Token("GlassBase"), Popup = Token("Popup"), PrimaryHover = Token("PrimaryHover"), PrimaryPressed = Token("PrimaryPressed");
    public static readonly LinearGradientBrush Sheen = new() { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new() { new(Colors.Transparent, 0), new(Colors.Transparent, .25), new(Colors.Transparent, .55), new(Colors.Transparent, .82), new(Colors.Transparent, 1) } };
    public static readonly LinearGradientBrush RimLight = new() { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = new() { new(Colors.Transparent, 0), new(Colors.Transparent, .6), new(Colors.Transparent, 1) } };
    public static readonly (string Name, string Hex)[] Tints = [("Amethyst", "#8B6CF0"), ("Lavender", "#B7A4F5"), ("Iris", "#6F7DF7"), ("Sky", "#6CC3F2"), ("Rose", "#EC8DCB"), ("Peach", "#FFA97E"), ("Graphite", "#8E93A3")];
    static event Action? AppearanceChanged;
    public static GlassAppearance Appearance => (GitlandApplication.Preferences.Appearance ?? new()).Normalize();
    static Color Blend(Color a, Color b, double amount) {
        byte Part(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        return Color.FromArgb(Part(a.A, b.A), Part(a.R, b.R), Part(a.G, b.G), Part(a.B, b.B));
    }
    static Color Alpha(Color color, double opacity) => Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B);
    static Color ButtonTone(Color color, Color ink, double maxLuminance) {
        static double Linear(byte channel) { double c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        double Luminance() => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        // Custom white, peach or sky tints still need readable white button labels.
        while (Luminance() > maxLuminance) color = Blend(color, ink, .04);
        return color;
    }
    static void ApplyGlass() {
        void Set(string name, Color color) => Token(name).Color = color;
        if (Current.Id == "atelier") {
            // Ported from Atelier/Theme/GlassTheme.cs: amethyst ink, frosted chrome,
            // white edge light, and a subtle sky/peach sheen. Source surfaces stay opaque.
            var a = Appearance; var tint = Color.Parse(a.Tint); var ink = Color.Parse("#0C0A12");
            var basis = Blend(ink, tint, .04 + .26 * a.ColorLevel);
            double see = a.Material == "solid" ? 0 : a.Transparency, g = a.Glassiness;
            Set("GlassBase", basis); Set("Backdrop", Alpha(basis, 1 - .75 * see));
            Set("Chrome", Alpha(Blend(basis, Colors.White, .03), .62 - .34 * see));
            Set("Pane", Alpha(Blend(basis, Colors.White, .02), .55 - .28 * see));
            Set("Ground", Blend(ink, tint, .055)); Set("Bar", Blend(basis, Colors.White, .025)); Set("Raised", Blend(basis, Colors.White, .06));
            Set("Popup", Blend(ink, tint, .08 + .18 * a.ColorLevel));
            Set("Accent", Blend(tint, Colors.White, .28)); Set("OnPrimary", Colors.White);
            Set("PrimaryFill", ButtonTone(Blend(tint, ink, .22), ink, .14));
            Set("PrimaryHover", ButtonTone(Blend(tint, ink, .08), ink, .18)); Set("PrimaryPressed", ButtonTone(Blend(tint, ink, .34), ink, .10));
            Set("Ink", Color.Parse("#F5F2FF")); Set("Muted", Blend(Color.Parse("#D2CDE2"), tint, .12 * a.ColorLevel)); Set("Faint", Blend(Color.Parse("#A6A0BA"), tint, .15 * a.ColorLevel));
            Set("Green", Color.Parse("#86E3B5")); Set("Red", Color.Parse("#FF8FA3")); Set("Amber", Color.Parse("#FFB38A"));
            Set("Hairline", Alpha(Colors.White, .06 + .07 * g)); Set("Divider", Alpha(Colors.White, .05 + .04 * g));
            Set("ButtonFill", Alpha(Colors.White, .04 + .05 * g)); Set("HoverFill", Alpha(Colors.White, .08 + .07 * g));
            Set("InputFill", Blend(basis, Colors.White, .025)); Set("SelectedSurface", Blend(basis, tint, .20)); Set("Selection", Blend(Ground.Color, tint, .27));
            Set("AddedFill", Blend(Ground.Color, Green.Color, .12)); Set("RemovedFill", Blend(Ground.Color, Red.Color, .12));
            Set("AddedWord", Blend(Ground.Color, Green.Color, .28)); Set("RemovedWord", Blend(Ground.Color, Red.Color, .28));
            Set("SearchFill", Blend(Ground.Color, Amber.Color, .32));
            Set("OursFill", Blend(Ground.Color, OursInk.Color, .18)); Set("TheirsFill", Blend(Ground.Color, TheirsInk.Color, .18)); Set("BaseFill", Blend(Ground.Color, Faint.Color, .16)); Set("MarkerFill", Blend(Ground.Color, Amber.Color, .20));
            Color[] sheen = [Alpha(Colors.White, .10 * g), Alpha(Blend(tint, Colors.White, .4), .05 * g * (.4 + a.ColorLevel)), Colors.Transparent, Alpha(Color.Parse("#8FD3FF"), .05 * g * (.3 + a.ColorLevel)), Alpha(Color.Parse("#FFB38A"), .07 * g * (.3 + a.ColorLevel))];
            for (int i = 0; i < sheen.Length; i++) Sheen.GradientStops[i].Color = sheen[i];
            RimLight.GradientStops[0].Color = Alpha(Colors.White, .10 + .22 * g);
            RimLight.GradientStops[1].Color = Alpha(Colors.White, .04 + .04 * g);
            RimLight.GradientStops[2].Color = Alpha(Colors.White, .03 + .03 * g);
        } else {
            Set("Backdrop", Bar.Color); Set("GlassBase", Bar.Color); Set("Chrome", Bar.Color); Set("Pane", Bar.Color); Set("Popup", Raised.Color);
            Set("PrimaryHover", PrimaryFill.Color); Set("PrimaryPressed", PrimaryFill.Color);
            foreach (var stop in Sheen.GradientStops) stop.Color = Colors.Transparent;
            foreach (var stop in RimLight.GradientStops) stop.Color = Hairline.Color;
        }
        SyncControlResources(); AppearanceChanged?.Invoke();
    }
    public static void SyncControlResources() {
        if (Application.Current is not { } app) return;
        // Fluent templates must share the palette too: combo popups, menus and focus
        // states otherwise retain unrelated system colors beneath our custom styles.
        foreach (var (key, brush) in new (string, IBrush)[] {
            ("ButtonBackground", ButtonFill), ("ButtonBackgroundPointerOver", HoverFill), ("ButtonBackgroundPressed", SelectedSurface),
            ("ButtonBorderBrush", Hairline), ("ButtonBorderBrushPointerOver", Hairline), ("ButtonBorderBrushPressed", Hairline),
            ("ButtonForeground", Ink), ("ButtonForegroundPointerOver", Ink), ("ButtonForegroundPressed", Ink),
            ("TextControlBackground", InputFill), ("TextControlBackgroundPointerOver", InputFill), ("TextControlBackgroundFocused", InputFill),
            ("TextControlBorderBrush", Hairline), ("TextControlBorderBrushPointerOver", Faint), ("TextControlBorderBrushFocused", Accent),
            ("TextControlForeground", Ink), ("TextControlForegroundPointerOver", Ink), ("TextControlForegroundFocused", Ink),
            ("ComboBoxBackground", ButtonFill), ("ComboBoxBackgroundPointerOver", HoverFill), ("ComboBoxBackgroundPressed", SelectedSurface),
            ("ComboBoxBorderBrush", Hairline), ("ComboBoxBorderBrushPointerOver", Hairline), ("ComboBoxDropDownBackground", Popup), ("ComboBoxDropDownBorderBrush", Hairline),
            ("MenuFlyoutPresenterBackground", Popup), ("MenuFlyoutPresenterBorderBrush", Hairline), ("MenuFlyoutItemBackgroundPointerOver", HoverFill), ("MenuFlyoutItemBackgroundPressed", SelectedSurface),
            ("MenuFlyoutSubItemBackgroundPointerOver", HoverFill), ("MenuFlyoutSubItemBackgroundSubMenuOpened", SelectedSurface),
            ("FlyoutPresenterBackground", Popup), ("FlyoutBorderThemeBrush", Hairline), ("ToolTipBackground", Popup), ("ToolTipBorderBrush", Hairline), ("ToolTipForeground", Ink),
            ("SystemControlHighlightListAccentLowBrush", SelectedSurface), ("SystemControlHighlightListAccentMediumBrush", SelectedSurface), ("SystemControlHighlightListAccentHighBrush", SelectedSurface),
            ("SystemControlHighlightListLowBrush", HoverFill), ("SystemControlHighlightListMediumBrush", SelectedSurface)
        }) app.Resources[key] = brush;
        app.Resources["ControlCornerRadius"] = new CornerRadius(8);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(12);
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent) {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light }) {
                if (!fluent.Palettes.TryGetValue(variant, out var palette)) fluent.Palettes[variant] = palette = new ColorPaletteResources();
                palette.Accent = Accent.Color;
            }
        }
    }
    public static void AttachWindow(Window window, bool shell = false) {
        void Sync() {
            RenderOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
            window.TransparencyLevelHint = shell && Current.Id == "atelier" ? Appearance.Material switch {
                "mica" => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur],
                "acrylic" => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur],
                "blur" => [WindowTransparencyLevel.Blur, WindowTransparencyLevel.AcrylicBlur],
                _ => [WindowTransparencyLevel.None]
            } : [WindowTransparencyLevel.None];
            window.Background = shell && Current.Id == "atelier" && window.ActualTransparencyLevel != WindowTransparencyLevel.None ? Brushes.Transparent : shell ? GlassBase : Popup;
        }
        void OnProperty(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == TopLevel.ActualTransparencyLevelProperty) Sync(); }
        window.PropertyChanged += OnProperty; AppearanceChanged += Sync;
        window.Closed += (_, _) => { AppearanceChanged -= Sync; window.PropertyChanged -= OnProperty; };
        Sync();
    }
}
