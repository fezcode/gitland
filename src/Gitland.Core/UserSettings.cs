using System.Text.Json;

namespace Gitland.Core;

public sealed record UserSettings(string Theme = "xcode-dark", double CodeSize = 13, bool HoswlEnabled = false, bool DefaultUnified = false, bool SyncMergeScroll = true, string InterfaceFont = "geist", string CodeFont = "geist-mono", double SidebarWidth = 0, IReadOnlyList<string>? RecentRepositories = null) {
    public UserSettings Normalize() => this with {
        Theme = Theme is "xcode-dark" or "graphite" or "midnight" or "paper" ? Theme : "xcode-dark",
        CodeSize = double.IsFinite(CodeSize) ? Math.Clamp(CodeSize, 11, 18) : 13,
        InterfaceFont = InterfaceFont is "geist" or "inter" or "modern" or "geometric" or "windows" or "swiss" or "editorial" or "monospace" ? InterfaceFont : "geist",
        CodeFont = CodeFont is "geist-mono" or "cascadia-code" or "consolas" ? CodeFont : "geist-mono",
        SidebarWidth = !double.IsFinite(SidebarWidth) || SidebarWidth <= 0 ? 0 : Math.Clamp(SidebarWidth, 220, 600),
        RecentRepositories = Recent(RecentRepositories)
    };

    // A record compares a list property by reference, so a saved settings object would never equal
    // the one loaded back. Equality is written out to compare the recents by their contents.
    // RecentRepositoriesEqualityIsByContents guards that every property is still accounted for here.
    public bool Equals(UserSettings? other) =>
        other is not null
        && Theme == other.Theme && CodeSize.Equals(other.CodeSize) && HoswlEnabled == other.HoswlEnabled
        && DefaultUnified == other.DefaultUnified && SyncMergeScroll == other.SyncMergeScroll
        && InterfaceFont == other.InterfaceFont && CodeFont == other.CodeFont && SidebarWidth.Equals(other.SidebarWidth)
        && (RecentRepositories ?? []).SequenceEqual(other.RecentRepositories ?? [], StringComparer.Ordinal);

    public override int GetHashCode() {
        var hash = new HashCode();
        hash.Add(Theme); hash.Add(CodeSize); hash.Add(HoswlEnabled); hash.Add(DefaultUnified);
        hash.Add(SyncMergeScroll); hash.Add(InterfaceFont); hash.Add(CodeFont); hash.Add(SidebarWidth);
        foreach (string path in RecentRepositories ?? []) hash.Add(path, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>The most recently opened repositories, newest first.</summary>
    public const int MaxRecent = 8;

    // C:\Projects\repo and C:\Projects\repo\ are the same repository.
    static readonly char[] PathEnds = ['/', '\\'];

    // A settings file can be hand-edited or carried between machines, so the list is cleaned on
    // the way in: no blanks, no duplicates differing only by case or trailing slash, and capped.
    static IReadOnlyList<string> Recent(IReadOnlyList<string>? paths) {
        if (paths == null) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (string raw in paths) {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string path = raw.Trim().TrimEnd(PathEnds);
            if (path.Length == 0 || !seen.Add(path)) continue;
            kept.Add(path);
            if (kept.Count == MaxRecent) break;
        }
        return kept;
    }

    /// <summary>Puts a repository at the front of the recent list, keeping it deduplicated and capped.</summary>
    public UserSettings WithRecent(string path) {
        if (string.IsNullOrWhiteSpace(path)) return this;
        string wanted = path.Trim().TrimEnd(PathEnds);
        var ordered = new List<string> { wanted };
        ordered.AddRange((RecentRepositories ?? []).Where(p => !string.Equals(p, wanted, StringComparison.OrdinalIgnoreCase)));
        return this with { RecentRepositories = Recent(ordered) };
    }

    /// <summary>Drops a repository from the recent list.</summary>
    public UserSettings WithoutRecent(string path) =>
        this with { RecentRepositories = Recent((RecentRepositories ?? []).Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList()) };
}

public sealed class SettingsStore(string path) {
    public string FilePath => Path.GetFullPath(path);
    public UserSettings Load() {
        try { return (JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new()).Normalize(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(UserSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalize(), new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
