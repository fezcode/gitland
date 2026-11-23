namespace Gitland.Core;

/// <summary>
/// Where Gitland keeps its per-user data, following the Fezcode convention:
/// settings in %APPDATA%\fezcode\Gitland (Roaming), anything else - logs, caches -
/// in %LOCALAPPDATA%\fezcode\Gitland. Gitland currently writes only settings.json.
/// The uninstaller's settings_dirs in forge.toml must name the same folders.
/// </summary>
public static class AppPaths {
    public const string AppName = "Gitland";
    public static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fezcode", AppName);
    public static string LocalDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fezcode", AppName);
    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");
    /// <summary>Releases up to 0.14.x kept settings.json in %LOCALAPPDATA%\Gitland.</summary>
    public static string LegacyDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    /// <summary>One-time move of the legacy folder into the settings folder. Never throws.</summary>
    public static void MigrateLegacy() {
        try { MigrateDirectory(LegacyDirectory, SettingsDirectory); } catch { }
    }

    /// <summary>
    /// Moves every file under <paramref name="from"/> to the same relative path under
    /// <paramref name="to"/>. A file already present at the destination is never
    /// overwritten: the destination wins and the stale legacy copy is deleted.
    /// Emptied source folders are removed. Idempotent; individual failures are
    /// skipped, never thrown.
    /// </summary>
    public static void MigrateDirectory(string from, string to) {
        from = Path.GetFullPath(from); to = Path.GetFullPath(to);
        if (!Directory.Exists(from) || string.Equals(from.TrimEnd('\\', '/'), to.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
        if (new DirectoryInfo(from).Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
        foreach (string file in SafeFiles(from)) {
            try {
                string target = Path.Combine(to, Path.GetRelativePath(from, file));
                if (File.Exists(target) || Directory.Exists(target)) {
                    // The new location wins; drop the stale legacy copy.
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // File.Move without overwrite renames on the same volume and copies then
                // deletes across volumes; it throws rather than replace an existing file.
                File.Move(file, target, false);
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        RemoveEmpty(from);
    }

    static IEnumerable<string> SafeFiles(string root) {
        try { return Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    static void RemoveEmpty(string directory) {
        try {
            foreach (string child in Directory.EnumerateDirectories(directory)) {
                if (!new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint)) RemoveEmpty(child);
            }
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
