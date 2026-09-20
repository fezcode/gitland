using Avalonia.Threading;

namespace Gitland.App;

public sealed partial class MainWindow {
    FileSystemWatcher? _watcher;
    DispatcherTimer? _watchTimer;
    bool _watchPending;

    /// <summary>Watches the open repository so edits made in an editor appear without pressing F5.
    /// Events are coalesced: a build or a branch switch produces hundreds, and each one would
    /// otherwise start a full status read.</summary>
    void WatchRepository(string? root) {
        _watcher?.Dispose(); _watcher = null;
        _watchTimer?.Stop(); _watchTimer = null;
        _watchPending = false;
        if (root == null || !Directory.Exists(root)) return;

        _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _watchTimer.Tick += async (_, _) => {
            if (!_watchPending) return;
            _watchPending = false;
            _watchTimer!.Stop();
            // Never refresh underneath an unsaved merge result or a dialog; the edit would be lost.
            if (_busy || _mergeDirty || OwnedWindows.Count > 0 || _repo == null) { _watchPending = true; _watchTimer.Start(); return; }
            try { await Refresh(); } catch (Exception) { /* a half-written index recovers on the next tick */ }
            _watchTimer.Start();
        };

        try {
            _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024 };
            _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
            void Changed(object? sender, FileSystemEventArgs e) {
                // .git churns constantly; only the refs and index that change what Gitland shows count.
                string relative = Path.GetRelativePath(root, e.FullPath).Replace('\\', '/');
                if (relative.StartsWith(".git/")) {
                    bool interesting = relative is ".git/HEAD" or ".git/index" or ".git/MERGE_HEAD" || relative.StartsWith(".git/refs/");
                    if (!interesting) return;
                }
                _watchPending = true;
            }
            _watcher.Changed += Changed; _watcher.Created += Changed; _watcher.Deleted += Changed;
            _watcher.Renamed += (s, e) => Changed(s, e);
            // A dropped buffer means edits were missed, so refresh rather than show stale state.
            _watcher.Error += (_, _) => _watchPending = true;
            _watcher.EnableRaisingEvents = true;
            _watchTimer.Start();
        } catch (Exception) {
            // A repository on a share or a path the platform will not watch simply stays manual.
            _watcher?.Dispose(); _watcher = null; _watchTimer.Stop(); _watchTimer = null;
        }
    }
}
