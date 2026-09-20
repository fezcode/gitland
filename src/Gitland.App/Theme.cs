using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Shapes;
using Avalonia.Automation;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Gitland.App;

public static partial class Palette {
    public static readonly SolidColorBrush Ground = Token("Ground"), Bar = Token("Bar"), Raised = Token("Raised"), Divider = Token("Divider"), Hairline = Token("Hairline"), Ink = Token("Ink"), Muted = Token("Muted"), Faint = Token("Faint"), Accent = Token("Accent"), Selection = Token("Selection"), Green = Token("Green"), Red = Token("Red"), Amber = Token("Amber"), Blue = Accent;
    public static readonly SolidColorBrush PrimaryFill = Token("PrimaryFill"), SelectedSurface = Token("SelectedSurface"), Glass = Ground, Rim = Hairline;
    public static IBrush Gradient(string start, string end) => new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.Parse(start), 0), new(Color.Parse(end), 1) } };
    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    public static TextBlock Text(string text, double size = 13, IBrush? brush = null, bool strong = false) => new() { Text = text, FontSize = size, Foreground = brush ?? Ink, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    public static StackPanel Row(params Control[] controls) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in controls) p.Children.Add(c); return p; }
    public static StackPanel Col(params Control[] controls) { var p = new StackPanel { Spacing = 8 }; foreach (var c in controls) p.Children.Add(c); return p; }
    public static Border Panel(Control child, IBrush? background = null, Thickness? padding = null) => new() { Background = background ?? Raised, Child = child, Padding = padding ?? new Thickness(12), BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1) };
    public static Border Card(Control child, Thickness? padding = null, double radius = 0) => new() { Child = child, Background = Ground, BorderBrush = Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Padding = padding ?? new Thickness(18), ClipToBounds = true };
    public static Border Badge(string label, IBrush? color = null) => new() { Child = Text(label, 10, color ?? Muted), Background = Raised, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3), VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Eyebrow(string label) => Text(label, 11, Faint, true);
    public static Border Segmented(params Button[] buttons) { var row = Row(buttons); row.Spacing = 2; foreach (var b in buttons) b.Classes.Add("segment"); return new Border { Background = Bar, BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = row }; }
    public static Button Button(string label, Action action, string? icon = null, bool primary = false) {
        var caption = Text(label, 12); caption.ClearValue(TextBlock.ForegroundProperty);
        var button = new Button { Content = icon == null ? caption : Row(Icon(icon, primary ? Ink : Muted, 14), caption), MinHeight = 29, Padding = new Thickness(10, 5), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        AutomationProperties.SetName(button, label);
        if (primary) button.Classes.Add("primary");
        button.Click += (_, _) => action(); return button;
    }
    public static Button IconButton(string label, Action action, string icon) {
        var b = Button(label, action); b.Content = Icon(icon, Muted, 14); b.Width = 29; b.Padding = new Thickness(6); b.Classes.Add("quiet"); ToolTip.SetTip(b, label); return b;
    }
    public static Control Icon(string key, IBrush? color = null, double size = 16) {
        var data = key switch {
            "branch" => "M6 3L6 15M6 15C6 9 18 15 18 9M6 3A2 2 0 1 0 6 7A2 2 0 1 0 6 3M6 15A2 2 0 1 0 6 19A2 2 0 1 0 6 15M18 5A2 2 0 1 0 18 9A2 2 0 1 0 18 5",
            "folder" => "M3 7L3 19L21 19L21 7L12 7L10 4L3 4Z",
            "diff" => "M4 3L14 3L20 9L20 21L4 21ZM14 3L14 9L20 9M8 12L15 12M11.5 8.5L11.5 15.5M8 18L15 18",
            "merge" => "M6 4L6 12Q6 17 12 17L18 17M18 5L18 20M14 8L18 4L22 8M3 4A3 3 0 1 0 9 4A3 3 0 1 0 3 4",
            "compare" => "M4 7L20 7M16 3L20 7L16 11M20 17L4 17M8 13L4 17L8 21",
            "search" => "M10 3A7 7 0 1 0 10 17A7 7 0 1 0 10 3M15 15L21 21",
            "refresh" => "M20 10A8 8 0 1 0 20 16M20 3L20 10L13 10",
            "check" => "M4 12L9 17L20 6",
            "up" => "M6 15L12 9L18 15",
            "down" => "M6 9L12 15L18 9",
            "file" => "M5 3L14 3L20 9L20 21L5 21ZM14 3L14 9L20 9",
            "settings" => "M4 7L20 7M4 17L20 17M9 4L9 10M15 14L15 20",
            "layers" => "M3 8L12 3L21 8L12 13ZM3 12L12 17L21 12M3 16L12 21L21 16",
            "chevron" => "M9 5L16 12L9 19",
            "close" => "M6 6L18 18M18 6L6 18",
            "expand" => "M14 3L21 3L21 10M21 3L14 10M10 21L3 21L3 14M3 21L10 14",
            "arrow-out" => "M7 17L20 4M10 4L20 4L20 14",
            "arrow-down" => "M12 3L12 21M5 14L12 21L19 14",
            "wand" => "M3 19L15 7L18 10L6 22ZM12 10L15 13M5 3L5 7M3 5L7 5M18 1L18 5M16 3L20 3M21 14L21 18M19 16L23 16",
            "copy" => "M8 8L20 8L20 21L8 21ZM16 8L16 3L3 3L3 16L8 16",
            "save" => "M4 3L17 3L21 7L21 21L3 21L3 3ZM7 3L7 9L16 9L16 3M7 21L7 14L17 14L17 21",
            "cloud" => "M6 18A4 4 0 1 1 5 10A7 7 0 0 1 19 9A5 5 0 0 1 19 19L15 19M12 21L12 11M8 15L12 11L16 15",
            "tag" => "M3 3L12 3L22 13L13 22L3 12ZM7 7L7.1 7",
            "sparkles" => "M12 2L15 9L22 12L15 15L12 22L9 15L2 12L9 9ZM20 2L20 6M18 4L22 4",
            "minimize" => "M5 12L19 12",
            "restore" => "M4 8L16 8L16 20L4 20ZM8 8L8 4L20 4L20 16L16 16",
            "maximize" => "M5 5L19 5L19 19L5 19Z",
            "clock" => "M12 3A9 9 0 1 0 12 21A9 9 0 1 0 12 3M12 7L12 12L16 14",
            _ => "M5 12L19 12M12 5L12 19"
        };
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Stroke = color ?? Muted, StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round } } } };
    }
    public static Styles Styles() {
        var styles = new Styles();
        var control = new Style(x => x.OfType<Window>()); control.Setters.Add(new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty, Sans)); control.Setters.Add(new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 13d)); styles.Add(control);
        var button = new Style(x => x.OfType<Button>()); button.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, ButtonFill)); button.Setters.Add(new Setter(Avalonia.Controls.Button.ForegroundProperty, Ink)); button.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, Hairline)); button.Setters.Add(new Setter(Avalonia.Controls.Button.BorderThicknessProperty, new Thickness(1))); button.Setters.Add(new Setter(Avalonia.Controls.Button.CornerRadiusProperty, new CornerRadius(5))); styles.Add(button);
        var quiet = new Style(x => x.OfType<Button>().Class("quiet")); quiet.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, Brushes.Transparent)); quiet.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, Brushes.Transparent)); styles.Add(quiet);
        var segment = new Style(x => x.OfType<Button>().Class("segment")); segment.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, Brushes.Transparent)); segment.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, Brushes.Transparent)); segment.Setters.Add(new Setter(Avalonia.Controls.Button.CornerRadiusProperty, new CornerRadius(4))); segment.Setters.Add(new Setter(Avalonia.Controls.Button.MinHeightProperty, 25d)); styles.Add(segment);
        var primary = new Style(x => x.OfType<Button>().Class("primary")); primary.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, PrimaryFill)); primary.Setters.Add(new Setter(Avalonia.Controls.Button.ForegroundProperty, OnPrimary)); primary.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, PrimaryFill)); styles.Add(primary);
        var selected = new Style(x => x.OfType<Button>().Class("segment").Class("primary")); selected.Setters.Add(new Setter(Avalonia.Controls.Button.BackgroundProperty, SelectedSurface)); selected.Setters.Add(new Setter(Avalonia.Controls.Button.ForegroundProperty, Ink)); selected.Setters.Add(new Setter(Avalonia.Controls.Button.BorderBrushProperty, Hairline)); styles.Add(selected);
        var hover = new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); hover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, HoverFill)); hover.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Hairline)); styles.Add(hover);
        var primaryHover = new Style(x => x.OfType<Button>().Class("primary").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); primaryHover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, PrimaryFill)); primaryHover.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, OnPrimary)); styles.Add(primaryHover);
        var segmentHover = new Style(x => x.OfType<Button>().Class("segment").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); segmentHover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, SelectedSurface)); segmentHover.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Ink)); styles.Add(segmentHover);
        var pressed = new Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); pressed.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Selection)); pressed.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Ink)); styles.Add(pressed);
        var disabled = new Style(x => x.OfType<Button>().Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); disabled.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Raised)); disabled.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Hairline)); disabled.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Faint)); disabled.Setters.Add(new Setter(ContentPresenter.OpacityProperty, 0.6d)); styles.Add(disabled);
        var focus = new Style(x => x.OfType<Button>().Class(":focus-visible").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); focus.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Accent)); styles.Add(focus);
        // Selection is a neutral surface. A full outline appears only for keyboard focus.
        var itemHover = new Style(x => x.OfType<Button>().Class("selection-item").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); itemHover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, HoverFill)); itemHover.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)); styles.Add(itemHover);
        var itemPressed = new Style(x => x.OfType<Button>().Class("selection-item").Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); itemPressed.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, SelectedSurface)); itemPressed.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)); styles.Add(itemPressed);
        var activeItem = new Style(x => x.OfType<Button>().Class("selection-item").Class("selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); activeItem.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, SelectedSurface)); activeItem.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)); styles.Add(activeItem);
        var itemFocus = new Style(x => x.OfType<Button>().Class("selection-item").Class(":focus-visible").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")); itemFocus.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Accent)); itemFocus.Setters.Add(new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(1))); styles.Add(itemFocus);
        var textBox = new Style(x => x.OfType<TextBox>()); textBox.Setters.Add(new Setter(TextBox.BackgroundProperty, InputFill)); textBox.Setters.Add(new Setter(TextBox.BorderBrushProperty, Hairline)); textBox.Setters.Add(new Setter(TextBox.CornerRadiusProperty, new CornerRadius(5))); textBox.Setters.Add(new Setter(TextBox.MinHeightProperty, 30d)); textBox.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(10, 6))); styles.Add(textBox);
        textBox.Setters.Add(new Setter(TextBox.ForegroundProperty, Ink)); textBox.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Selection)); textBox.Setters.Add(new Setter(TextBox.SelectionForegroundBrushProperty, Ink));
        var inputFocus = new Style(x => x.OfType<TextBox>().Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"));
        inputFocus.Setters.Add(new Setter(Border.BackgroundProperty, InputFill)); inputFocus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent)); styles.Add(inputFocus);
        var editorBorder = new Style(x => x.OfType<TextBox>().Class("merge-editor").Template().OfType<Border>().Name("PART_BorderElement"));
        editorBorder.Setters.Add(new Setter(Border.BackgroundProperty, Ground)); editorBorder.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(0))); editorBorder.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0))); styles.Add(editorBorder);
        var editorFocus = new Style(x => x.OfType<TextBox>().Class("merge-editor").Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"));
        editorFocus.Setters.Add(new Setter(Border.BackgroundProperty, Ground)); editorFocus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent)); editorFocus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1))); styles.Add(editorFocus);
        var combo = new Style(x => x.OfType<ComboBox>()); combo.Setters.Add(new Setter(ComboBox.ForegroundProperty, Ink)); combo.Setters.Add(new Setter(ComboBox.MinHeightProperty, 30d)); combo.Setters.Add(new Setter(ComboBox.BackgroundProperty, Ground)); combo.Setters.Add(new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(5))); combo.Setters.Add(new Setter(ComboBox.PaddingProperty, new Thickness(10, 5))); styles.Add(combo);
        var checkbox = new Style(x => x.OfType<CheckBox>());
        checkbox.Setters.Add(new Setter(CheckBox.MinHeightProperty, 25d));
        checkbox.Setters.Add(new Setter(CheckBox.TemplateProperty, new FuncControlTemplate<CheckBox>((control, _) => {
            var glyph = Icon("check", OnPrimary, 12);
            glyph.Bind(Control.IsVisibleProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = control, Converter = new FuncValueConverter<bool?, bool>(value => value == true) });
            var mark = new Border { Name = "CheckMark", Width = 17, Height = 17, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = glyph, VerticalAlignment = VerticalAlignment.Center };
            mark.Bind(Border.BackgroundProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = control, Converter = new FuncValueConverter<bool?, IBrush>(value => value == true ? PrimaryFill : Ground) });
            mark.Bind(Border.BorderBrushProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = control, Converter = new FuncValueConverter<bool?, IBrush>(value => value == true ? Accent : Faint) });
            var caption = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
            caption.Bind(ContentPresenter.ContentProperty, new Binding(nameof(CheckBox.Content)) { Source = control });
            return new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 3), Child = Row(mark, caption) };
        })));
        styles.Add(checkbox);
        var checkFocus = new Style(x => x.OfType<CheckBox>().Class(":focus-visible").Template().OfType<Border>().Name("CheckMark")); checkFocus.Setters.Add(new Setter(Border.BoxShadowProperty, BoxShadows.Parse("0 0 0 3 #60798eaf"))); styles.Add(checkFocus);
        return styles;
    }
}
