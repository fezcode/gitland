using Avalonia.Media;

namespace Gitland.App;

public sealed record FontChoice(string Id, string Name, FontFamily Family, bool Bundled) {
    public string FamilyName => new Typeface(Family).GlyphTypeface.FamilyName;
    public string Source => FamilyName + (Bundled ? " · Bundled" : " · Installed");
}

public static class FontCatalog {
    static readonly Lazy<HashSet<string>> Installed = new(() => FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
    static FontChoice Embedded(string id, string name, string family) => new(id, name, new FontFamily("avares://gitland/Assets/Fonts#" + family), true);
    static FontChoice System(string id, string name, string fallback, params string[] families) {
        var available = families.FirstOrDefault(Installed.Value.Contains);
        return available == null ? Embedded(id, name, fallback) : new(id, name, new FontFamily(available), false);
    }
    public static IReadOnlyList<FontChoice> Interface { get; } = [
        Embedded("geist", "Geist · Gitland default", "Geist"),
        Embedded("inter", "Bundled Inter", "Inter"),
        System("modern", "Modern Grotesque · Aptos / Segoe UI", "Inter", "Aptos", "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI"),
        System("geometric", "Geometric Tech · Bahnschrift / DIN", "IBM Plex Sans", "Bahnschrift", "DIN 1451", "DIN Alternate"),
        System("windows", "Windows UI · Segoe UI Variable", "Inter", "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI"),
        Embedded("swiss", "Swiss Neo-Grotesque · Inter / SF Pro / Helvetica Neue", "Inter"),
        System("editorial", "Editorial Serif · Georgia / Garamond / Palatino", "Source Serif 4", "Georgia", "Garamond", "Palatino Linotype", "Palatino"),
        Embedded("monospace", "Monospace / Code · Cascadia Code / Consolas", "Cascadia Code")
    ];
    public static IReadOnlyList<FontChoice> Code { get; } = [
        Embedded("geist-mono", "Geist Mono", "Geist Mono"),
        Embedded("cascadia-code", "Cascadia Code", "Cascadia Code"),
        System("consolas", "Consolas", "Cascadia Code", "Consolas")
    ];
    public static FontChoice InterfaceChoice(string id) => Interface.FirstOrDefault(f => f.Id == id) ?? Interface[0];
    public static FontChoice CodeChoice(string id) => Code.FirstOrDefault(f => f.Id == id) ?? Code[0];
}

public static partial class Palette {
    public static FontFamily Sans { get; private set; } = new("avares://gitland/Assets/Fonts#Geist");
    public static FontFamily Mono { get; private set; } = new("avares://gitland/Assets/Fonts#Geist Mono");
    public static void ApplyFonts(string ui, string code) {
        Sans = FontCatalog.InterfaceChoice(ui).Family;
        Mono = FontCatalog.CodeChoice(code).Family;
    }
}
