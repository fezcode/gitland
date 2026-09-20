using System.Text.Json;

namespace Gitland.Core;

public sealed record UserSettings(string Theme = "xcode-dark", double CodeSize = 13, bool HoswlEnabled = false, bool DefaultUnified = false, bool SyncMergeScroll = true, string InterfaceFont = "geist", string CodeFont = "geist-mono", double SidebarWidth = 0) {
    public UserSettings Normalize() => this with {
        Theme = Theme is "xcode-dark" or "graphite" or "midnight" or "paper" ? Theme : "xcode-dark",
        CodeSize = double.IsFinite(CodeSize) ? Math.Clamp(CodeSize, 11, 18) : 13,
        InterfaceFont = InterfaceFont is "geist" or "inter" or "modern" or "geometric" or "windows" or "swiss" or "editorial" or "monospace" ? InterfaceFont : "geist",
        CodeFont = CodeFont is "geist-mono" or "cascadia-code" or "consolas" ? CodeFont : "geist-mono",
        SidebarWidth = !double.IsFinite(SidebarWidth) || SidebarWidth <= 0 ? 0 : Math.Clamp(SidebarWidth, 220, 600)
    };
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
