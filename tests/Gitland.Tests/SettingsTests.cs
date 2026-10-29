using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class SettingsTests {
    [Fact] public void SettingsPersistAndRecoverFromInvalidInput() {
        string directory = Path.Combine(Path.GetTempPath(), "gitland-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json"); var store = new SettingsStore(path);
        try {
            Assert.Equal(new UserSettings(), store.Load());
            var wanted = new UserSettings("paper", 16, true, true, false, "modern", "cascadia-code"); store.Save(wanted);
            Assert.Equal(wanted, new SettingsStore(path).Load());
            File.WriteAllText(path, "{unfinished"); Assert.Equal(new UserSettings(), store.Load());
            store.Save(new("unknown", 100)); Assert.Equal("atelier", store.Load().Theme); Assert.Equal(18, store.Load().CodeSize);
            File.WriteAllText(path, "{\"Theme\":\"midnight\",\"CodeSize\":14}");
            var legacy = store.Load(); Assert.Equal("midnight", legacy.Theme); Assert.Equal("geist", legacy.InterfaceFont); Assert.Equal("geist-mono", legacy.CodeFont);
            store.Save(legacy with { InterfaceFont = "missing-ui-font", CodeFont = "missing-code-font" });
            Assert.Equal("geist", store.Load().InterfaceFont); Assert.Equal("geist-mono", store.Load().CodeFont);
            store.Save(legacy with { SidebarWidth = 385 }); Assert.Equal(385, store.Load().SidebarWidth);
            Assert.Equal(600, (legacy with { SidebarWidth = 900 }).Normalize().SidebarWidth);
            Assert.Equal(220, (legacy with { SidebarWidth = 100 }).Normalize().SidebarWidth);
            Assert.Equal(0, (legacy with { SidebarWidth = double.NaN }).Normalize().SidebarWidth);
            Assert.Equal(0, legacy.SidebarWidth);
            store.Save(legacy with { ReduceMotion = true });
            Assert.True(store.Load().ReduceMotion);
            Assert.NotEqual(legacy, store.Load());
            Assert.Equal(legacy with { ReduceMotion = true }, store.Load());
            var glass = new GlassAppearance("solid", "#6CC3F2", .8, .2, .7);
            store.Save(legacy with { Theme = "atelier", Appearance = glass });
            Assert.Equal(glass, store.Load().Appearance);
            Assert.Equal("atelier", store.Load().Theme);
            Assert.Single(Directory.GetFiles(directory));
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public void GlassSettingsNormalizeAndOldSettingsRemainCompatible() {
        var invalid = new GlassAppearance("invalid", "not-a-color", double.NaN, 3, -2).Normalize();
        Assert.Equal("acrylic", invalid.Material); Assert.Equal("#8B6CF0", invalid.Tint);
        Assert.Equal("acrylic", new UserSettings().Normalize().Appearance!.Material);
        Assert.Equal("mica", new GlassAppearance("mica").Normalize().Material);
        Assert.Equal(.5, invalid.Glassiness); Assert.Equal(1, invalid.Transparency); Assert.Equal(0, invalid.ColorLevel);
        Assert.Equal(new UserSettings(), new UserSettings(Appearance: new()));
        Assert.Equal(new UserSettings().GetHashCode(), new UserSettings(Appearance: new()).GetHashCode());
        Assert.Equal("paper", new UserSettings(Theme: "paper").Normalize().Theme);
    }
}
