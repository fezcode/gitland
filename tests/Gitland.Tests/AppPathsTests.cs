using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class AppPathsTests : IDisposable {
    readonly string _root = Path.Combine(Path.GetTempPath(), "gitland-paths-" + Guid.NewGuid().ToString("N"));

    [Fact] public void PathsFollowTheFezcodeConvention() {
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fezcode", "Gitland", "settings.json"), AppPaths.SettingsFile);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fezcode", "Gitland"), AppPaths.LocalDataDirectory);
    }

    [Fact] public void MigrationMovesFilesAndRemovesTheEmptiedLegacyFolder() {
        string from = Path.Combine(_root, "old"), to = Path.Combine(_root, "new");
        Directory.CreateDirectory(Path.Combine(from, "sub"));
        File.WriteAllText(Path.Combine(from, "settings.json"), "legacy");
        File.WriteAllText(Path.Combine(from, "sub", "extra.txt"), "x");
        AppPaths.MigrateDirectory(from, to);
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(to, "settings.json")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(to, "sub", "extra.txt")));
        Assert.False(Directory.Exists(from));
        AppPaths.MigrateDirectory(from, to); // idempotent
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(to, "settings.json")));
    }

    [Fact] public void MigrationKeepsTheNewCopyAndDeletesTheStaleLegacyOne() {
        string from = Path.Combine(_root, "old"), to = Path.Combine(_root, "new");
        Directory.CreateDirectory(from); Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, "settings.json"), "legacy");
        File.WriteAllText(Path.Combine(to, "settings.json"), "current");
        AppPaths.MigrateDirectory(from, to);
        Assert.Equal("current", File.ReadAllText(Path.Combine(to, "settings.json")));
        Assert.False(File.Exists(Path.Combine(from, "settings.json")));
        Assert.False(Directory.Exists(from));
    }

    [Fact] public void MigrationWithoutALegacyFolderDoesNothing() {
        string to = Path.Combine(_root, "new");
        AppPaths.MigrateDirectory(Path.Combine(_root, "missing"), to);
        Assert.False(Directory.Exists(to));
    }

    public void Dispose() {
        if (Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(_root).StartsWith("gitland-paths-") && Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
