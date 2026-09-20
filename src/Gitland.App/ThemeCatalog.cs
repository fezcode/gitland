using Avalonia.Media;

namespace Gitland.App;

public sealed record WorkbenchTheme(string Id, string Name, string Description, bool Light, string Ground, string Bar, string Raised, string Ink, string Muted, string Faint, string Accent);

public static partial class Palette {
    // Brushes keep their identity, so switching themes preserves every live editor and its undo history.
    static Dictionary<string, SolidColorBrush>? _tokens;
    static SolidColorBrush Token(string name) {
        _tokens ??= new();
        if (!_tokens.TryGetValue(name, out var brush)) _tokens[name] = brush = new SolidColorBrush(Colors.Transparent);
        return brush;
    }
    public static readonly SolidColorBrush ButtonFill = Token("ButtonFill"), HoverFill = Token("HoverFill"), InputFill = Token("InputFill"), OnPrimary = Token("OnPrimary");
    public static readonly SolidColorBrush AddedFill = Token("AddedFill"), RemovedFill = Token("RemovedFill"), AddedWord = Token("AddedWord"), RemovedWord = Token("RemovedWord"), SearchFill = Token("SearchFill");
    public static readonly SolidColorBrush OursFill = Token("OursFill"), TheirsFill = Token("TheirsFill"), BaseFill = Token("BaseFill"), MarkerFill = Token("MarkerFill"), OursInk = Token("OursInk"), TheirsInk = Token("TheirsInk"), StringInk = Token("StringInk"), KeywordInk = Token("KeywordInk");
    public static readonly WorkbenchTheme[] Themes = [
        new("xcode-dark", "Xcode Dark", "Clockt’s cool charcoal & periwinkle", false, "#14161a", "#191b20", "#202228", "#eceef3", "#a9b0bf", "#858d9d", "#8aa7ff"),
        new("graphite", "Graphite", "Neutral gray & soft sage", false, "#1c1d1d", "#181919", "#242625", "#e7e9e7", "#b2b8b3", "#858e88", "#a2c4af"),
        new("midnight", "Midnight", "Deep navy & muted iris", false, "#151723", "#11131e", "#1e2130", "#e5e5f3", "#acafc8", "#858ba7", "#b3a6ed"),
        new("paper", "Paper", "Warm white & ink blue", true, "#faf9f6", "#eeede8", "#f4f3ef", "#282e39", "#555e6c", "#6d7580", "#385b9e")
    ];
    public static WorkbenchTheme Current { get; private set; } = Themes[0];
    public static void Apply(string id) {
        Current = Themes.FirstOrDefault(t => t.Id == id) ?? Themes[0]; var t = Current;
        void Set(string role, string hex) => Token(role).Color = Color.Parse(hex);
        void Mix(string role, SolidColorBrush from, SolidColorBrush to, double ratio) {
            byte Part(byte a, byte b) => (byte)Math.Round(a + (b - a) * ratio);
            Token(role).Color = Color.FromRgb(Part(from.Color.R, to.Color.R), Part(from.Color.G, to.Color.G), Part(from.Color.B, to.Color.B));
        }
        Set("Ground", t.Ground); Set("Bar", t.Bar); Set("Raised", t.Raised); Set("Ink", t.Ink); Set("Muted", t.Muted); Set("Faint", t.Faint); Set("Accent", t.Accent);
        Set("Green", t.Light ? "#28734d" : "#92c7a9"); Set("Red", t.Light ? "#a94457" : "#d99aa6"); Set("Amber", t.Light ? "#876319" : "#d6b88a");
        Set("OursInk", t.Light ? "#365f9e" : "#a3bfff"); Set("TheirsInk", t.Light ? "#805399" : "#d5afe8");
        Set("StringInk", t.Light ? "#a34428" : "#ef9a86"); Set("KeywordInk", t.Light ? "#874484" : "#dba0da");
        Set("OnPrimary", "#f6f7fc"); Set("PrimaryFill", t.Light ? "#3c5e9b" : t.Id == "graphite" ? "#3e5b4c" : t.Id == "midnight" ? "#534673" : "#3e527f");
        Mix("Divider", Ground, Ink, .035); Mix("Hairline", Ground, Ink, .13); Mix("Selection", Ground, Accent, .17); Mix("SelectedSurface", Ground, Ink, .105);
        Mix("ButtonFill", Ground, Ink, .065); Mix("HoverFill", Ground, Ink, .06); Mix("InputFill", Ground, Bar, .4);
        Mix("AddedFill", Ground, Green, t.Light ? .12 : .13); Mix("RemovedFill", Ground, Red, .13);
        Mix("AddedWord", Ground, Green, .29); Mix("RemovedWord", Ground, Red, .29); Mix("SearchFill", Ground, Amber, .35);
        Mix("OursFill", Ground, OursInk, .18); Mix("TheirsFill", Ground, TheirsInk, .18); Mix("BaseFill", Ground, Faint, .16); Mix("MarkerFill", Ground, Amber, .20);
    }
}
