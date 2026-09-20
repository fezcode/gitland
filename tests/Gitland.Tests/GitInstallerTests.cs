using Gitland.Core;
using Xunit;

namespace Gitland.Tests;

public sealed class GitInstallerTests {
    [Theory]
    [InlineData("v2.47.1.windows.1", "2.47.1")]
    [InlineData("v2.43.0.windows.2", "2.43.0")]
    [InlineData("git version 2.47.1.windows.1", "2.47.1")]
    [InlineData("2.39.5", "2.39.5")]
    [InlineData("not a version", "")]
    [InlineData("", "")]
    public void AReleaseTagReadsAsAPlainVersion(string tag, string expected) =>
        Assert.Equal(expected, GitInstaller.VersionFromTag(tag));

    [Theory]
    [InlineData("https://github.com/git-for-windows/git/releases/download/x/Git-2.47.1-64-bit.exe", true)]
    [InlineData("https://objects.githubusercontent.com/some/asset", true)]
    [InlineData("http://github.com/git-for-windows/git/setup.exe", false)]      // plain HTTP
    [InlineData("https://example.invalid/Git-2.47.1-64-bit.exe", false)]        // wrong host
    [InlineData("https://github.com.evil.invalid/setup.exe", false)]            // lookalike host
    [InlineData("file:///C:/setup.exe", false)]
    [InlineData("nonsense", false)]
    public void OnlyGitHubOverHttpsCountsAsATrustedDownload(string url, bool trusted) =>
        Assert.Equal(trusted, GitInstaller.IsTrustedDownload(url));

    [Fact] public void TheSixtyFourBitInstallerIsPickedOutOfAReleasesAssets() {
        string json = """
        {"tag_name":"v2.47.1.windows.1","assets":[
          {"name":"Git-2.47.1-32-bit.exe","browser_download_url":"https://github.com/git-for-windows/git/releases/download/v2.47.1/Git-2.47.1-32-bit.exe"},
          {"name":"PortableGit-2.47.1-64-bit.7z.exe","browser_download_url":"https://github.com/git-for-windows/git/releases/download/v2.47.1/PortableGit-2.47.1-64-bit.7z.exe"},
          {"name":"Git-2.47.1-64-bit.exe","browser_download_url":"https://github.com/git-for-windows/git/releases/download/v2.47.1/Git-2.47.1-64-bit.exe"}
        ]}
        """;
        Assert.Equal("https://github.com/git-for-windows/git/releases/download/v2.47.1/Git-2.47.1-64-bit.exe", GitInstaller.InstallerAssetUrl(json));
    }

    [Fact] public void AnAssetServedFromAnUntrustedHostIsIgnored() {
        string json = """
        {"tag_name":"v2.47.1.windows.1","assets":[
          {"name":"Git-2.47.1-64-bit.exe","browser_download_url":"https://mirror.invalid/Git-2.47.1-64-bit.exe"}
        ]}
        """;
        Assert.Null(GitInstaller.InstallerAssetUrl(json));
    }

    [Fact] public void AReleaseWithNoUsableInstallerReturnsNothingRatherThanGuessing() {
        Assert.Null(GitInstaller.InstallerAssetUrl("""{"tag_name":"v2.47.1.windows.1","assets":[]}"""));
        Assert.Null(GitInstaller.InstallerAssetUrl("""{"tag_name":"v2.47.1.windows.1"}"""));
    }

    [Fact] public async Task DetectReportsTheInstalledVersionFromGitItself() {
        var installer = new GitInstaller(new StubRunner(("git", 0, "git version 2.47.1.windows.1"), ("where", 0, @"C:\Program Files\Git\cmd\git.exe")));
        var found = await installer.DetectAsync();
        Assert.True(found.Installed);
        Assert.Equal("2.47.1", found.Version);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", found.Location);
        Assert.Equal("Git 2.47.1", found.Summary);
    }

    [Fact] public async Task DetectReportsAMissingGitRatherThanThrowing() {
        var installer = new GitInstaller(new StubRunner(("git", 1, "")));
        var found = await installer.DetectAsync();
        Assert.False(found.Installed);
        Assert.Equal("Git was not found on PATH", found.Summary);
    }

    sealed class StubRunner(params (string Executable, int ExitCode, string Output)[] replies) : ICommandRunner {
        readonly (string Executable, int ExitCode, string Output)[] _replies = replies;
        public Task<CommandResult> RunAsync(CommandRequest command) {
            var reply = _replies.FirstOrDefault(r => r.Executable == command.Executable);
            return Task.FromResult(reply.Executable == null ? new CommandResult(1, "", "not stubbed") : new CommandResult(reply.ExitCode, reply.Output, ""));
        }
    }
}
