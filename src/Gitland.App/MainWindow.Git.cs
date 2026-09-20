using Avalonia.Controls;
using Avalonia.Threading;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    readonly GitInstaller _gitInstaller = new();
    TextBlock? _gitStatus, _gitDetail;
    Button? _gitInstall;

    /// <summary>Reports whether Git is present and whether a newer release exists. The version
    /// check needs the network, so a failure there must not hide a working local Git.</summary>
    async Task RefreshGitStatus() {
        if (_gitStatus == null) return;
        _gitStatus.Text = "Checking for Git…"; _gitStatus.Foreground = Muted;
        if (_gitDetail != null) _gitDetail.Text = "";
        if (_gitInstall != null) _gitInstall.IsEnabled = false;

        GitInstallation found;
        try { found = await _gitInstaller.DetectAsync(); }
        catch (Exception e) { found = new(false, "", ""); if (_gitDetail != null) _gitDetail.Text = e.Message; }
        if (_gitStatus == null) return;                      // the window closed while we looked

        _gitStatus.Text = found.Summary;
        _gitStatus.Foreground = found.Installed ? Green : Amber;
        if (_gitDetail != null && found.Location.Length > 0) _gitDetail.Text = found.Location;
        if (_gitInstall != null) {
            _gitInstall.IsEnabled = true;
            _gitInstall.Content = Row(Icon("arrow-down"), Text(found.Installed ? "Update to the latest Git" : "Install the latest Git"));
            Avalonia.Automation.AutomationProperties.SetName(_gitInstall, found.Installed ? "Update to the latest Git" : "Install the latest Git");
        }

        try {
            string latest = await _gitInstaller.LatestVersionAsync();
            if (_gitStatus == null || latest.Length == 0) return;
            if (!found.Installed) { _gitStatus.Text = $"Git was not found on PATH · {latest} is available"; return; }
            _gitStatus.Text = found.Version == latest ? $"Git {found.Version} · up to date" : $"Git {found.Version} · {latest} is available";
            _gitStatus.Foreground = found.Version == latest ? Green : Amber;
        } catch (Exception) {
            // Offline, or GitHub is unreachable. The local result above still stands.
            if (_gitDetail != null && _gitDetail.Text?.Length == 0) _gitDetail.Text = "Could not reach GitHub to check for a newer release.";
        }
    }

    async Task InstallGit() {
        if (_gitInstall == null || _gitStatus == null) return;
        var found = await _gitInstaller.DetectAsync();
        string action = found.Installed ? "Update Git" : "Install Git";
        if (!await ReviewAction(action, found.Installed
                ? $"Gitland will install the latest Git for Windows over {found.Version}. Windows will ask for administrator permission. Close other Git tools first; the installer may need to replace files they are using."
                : "Gitland will install the latest Git for Windows, using winget when available and otherwise the official installer from the Git for Windows project on GitHub. Windows will ask for administrator permission.", action)) return;

        _gitInstall.IsEnabled = false;
        var progress = new Progress<string>(message => Dispatcher.UIThread.Post(() => { if (_gitStatus != null) { _gitStatus.Text = message; _gitStatus.Foreground = Muted; } }));
        try {
            var installed = await _gitInstaller.InstallAsync(progress);
            _gitStatus!.Text = $"Git {installed.Version} installed";
            _gitStatus.Foreground = Green;
            if (_gitDetail != null) _gitDetail.Text = installed.Location;
            _status.Text = "Git " + installed.Version + " is ready.";
        } catch (Exception e) {
            if (_gitStatus != null) { _gitStatus.Text = e.Message; _gitStatus.Foreground = Amber; }
        } finally {
            if (_gitInstall != null) _gitInstall.IsEnabled = true;
        }
    }
}
