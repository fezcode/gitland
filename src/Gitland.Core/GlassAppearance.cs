namespace Gitland.Core;

public sealed record GlassAppearance(string Material = "acrylic", string Tint = "#8B6CF0", double Glassiness = .6, double Transparency = .55, double ColorLevel = .5) {
    public GlassAppearance Normalize() => this with {
        Material = Material is "mica" or "acrylic" or "blur" or "solid" ? Material : "acrylic",
        Tint = System.Text.RegularExpressions.Regex.IsMatch(Tint ?? "", "^#[0-9a-fA-F]{6}$") ? Tint! : "#8B6CF0",
        Glassiness = Clamp(Glassiness), Transparency = Clamp(Transparency), ColorLevel = Clamp(ColorLevel)
    };
    static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : .5;
}
