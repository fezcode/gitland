using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gitland.Core;

/// <summary>What Gitland found when it looked for Git.</summary>
public sealed record GitInstallation(bool Installed, string Version, string Location) {
    public string Summary => Installed ? $"Git {Version}" : "Git was not found on PATH";
}

/// <summary>Finds, and when asked installs, Git for Windows. Gitland runs every repository
/// operation through the git executable, so without it the application cannot do anything.</summary>
public sealed class GitInstaller(ICommandRunner? runner = null, Func<HttpClient>? httpFactory = null) {
    readonly ICommandRunner _runner = runner ?? new CommandRunner();
    readonly Func<HttpClient> _http = httpFactory ?? (() => {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // The GitHub API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.Add("User-Agent", "Gitland");
        return client;
    });

    /// <summary>Only these hosts may serve an executable Gitland is about to run.</summary>
    static readonly string[] AllowedHosts = ["github.com", "objects.githubusercontent.com", "api.github.com", "release-assets.githubusercontent.com"];

    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a Git for Windows release tag such as v2.47.1.windows.1 as 2.47.1.</summary>
    public static string VersionFromTag(string tag) {
        var match = Regex.Match(tag ?? "", @"v?(\d+\.\d+\.\d+)(?:\.windows\.\d+)?");
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>Picks the 64-bit standalone installer from a release's assets.</summary>
    public static string? InstallerAssetUrl(string releaseJson) {
        using var document = JsonDocument.Parse(releaseJson);
        if (!document.RootElement.TryGetProperty("assets", out var assets)) return null;
        foreach (var asset in assets.EnumerateArray()) {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            // Git-2.47.1-64-bit.exe, and never the portable or mingit archives.
            if (Regex.IsMatch(name, @"\AGit-\d+\.\d+\.\d+(\.\d+)?-64-bit\.exe\z") && IsTrustedDownload(url)) return url;
        }
        return null;
    }

    public async Task<GitInstallation> DetectAsync() {
        var version = await _runner.RunAsync(new("git", Environment.CurrentDirectory, ["--version"], TimeoutSeconds: 15));
        if (version.ExitCode != 0) return new(false, "", "");
        string reported = VersionFromTag(version.Output.Trim()) is { Length: > 0 } parsed ? parsed : version.Output.Trim();
        string location = "";
        var where = await _runner.RunAsync(new(OperatingSystem.IsWindows() ? "where" : "which", Environment.CurrentDirectory, ["git"], TimeoutSeconds: 15));
        if (where.ExitCode == 0) location = where.Output.Split('\n').FirstOrDefault()?.Trim() ?? "";
        return new(true, reported, location);
    }

    /// <summary>The newest Git for Windows version, read from the project's releases.</summary>
    public async Task<string> LatestVersionAsync(CancellationToken token = default) {
        using var client = _http();
        string json = await client.GetStringAsync("https://api.github.com/repos/git-for-windows/git/releases/latest", token);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("tag_name", out var tag) ? VersionFromTag(tag.GetString() ?? "") : "";
    }

    async Task<bool> HasWingetAsync() =>
        OperatingSystem.IsWindows() && (await _runner.RunAsync(new("winget", Environment.CurrentDirectory, ["--version"], TimeoutSeconds: 20))).ExitCode == 0;

    /// <summary>Installs or updates Git to the current release, reporting each step.
    /// Windows shows its own elevation prompt; Gitland never bypasses it.</summary>
    public async Task<GitInstallation> InstallAsync(IProgress<string>? progress = null, CancellationToken token = default) {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Gitland can only install Git for you on Windows. Use your package manager on this platform.");

        if (await HasWingetAsync()) {
            progress?.Report("Installing Git with winget…");
            var result = await _runner.RunAsync(new("winget", Environment.CurrentDirectory,
                ["install", "--id", "Git.Git", "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
                TimeoutSeconds: 900));
            // 0 is success; -1978335189 is "no applicable upgrade", which means Git is already current.
            if (result.ExitCode == 0 || result.ExitCode == unchecked((int)0x8A15002B)) {
                progress?.Report("Verifying the installation…");
                return await VerifyAsync(progress);
            }
            progress?.Report("winget could not install Git; falling back to the official installer…");
        }

        progress?.Report("Finding the latest Git for Windows release…");
        using var client = _http();
        string json = await client.GetStringAsync("https://api.github.com/repos/git-for-windows/git/releases/latest", token);
        string url = InstallerAssetUrl(json) ?? throw new InvalidOperationException("That Git for Windows release has no 64-bit installer. Install Git manually from https://gitforwindows.org/.");

        progress?.Report("Downloading " + VersionFromTag(JsonDocument.Parse(json).RootElement.GetProperty("tag_name").GetString() ?? "") + "…");
        string temp = Path.Combine(Path.GetTempPath(), "gitland-git-setup-" + Guid.NewGuid().ToString("N") + ".exe");
        try {
            await using (var stream = await client.GetStreamAsync(url, token))
            await using (var file = File.Create(temp)) await stream.CopyToAsync(file, token);

            // Never hand Windows something that is not an executable, whatever the server returned.
            await using (var check = File.OpenRead(temp)) {
                var header = new byte[2];
                if (await check.ReadAsync(header, token) != 2 || header[0] != 0x4D || header[1] != 0x5A)
                    throw new InvalidOperationException("The downloaded file is not a Windows installer. Nothing was run.");
            }

            progress?.Report("Running the Git installer…");
            var install = await _runner.RunAsync(new(temp, Environment.CurrentDirectory,
                ["/VERYSILENT", "/NORESTART", "/NOCANCEL", "/SP-", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS"],
                TimeoutSeconds: 900));
            if (install.ExitCode != 0) throw new InvalidOperationException($"The Git installer exited with code {install.ExitCode}. Install Git manually from https://gitforwindows.org/.");
        } finally { try { File.Delete(temp); } catch (IOException) { } }

        progress?.Report("Verifying the installation…");
        return await VerifyAsync(progress);
    }

    async Task<GitInstallation> VerifyAsync(IProgress<string>? progress) {
        var found = await DetectAsync();
        if (found.Installed) return found;
        // A fresh install updates the machine PATH, which this already-running process does not see.
        progress?.Report("Git was installed. Restart Gitland so it picks up the new PATH.");
        throw new InvalidOperationException("Git was installed, but this running copy of Gitland cannot see it yet. Close and reopen Gitland.");
    }
}
