using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gitland.Core;
using static Gitland.App.Palette;

namespace Gitland.App;

public sealed partial class MainWindow {
    Button RepoAction(string label, Func<Task> action) { var button = Button(label, () => Run(action)); button.IsEnabled = _repo != null; return button; }
    void RepositoryPage(Control content) {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var tabs = new WrapPanel { Margin = new Thickness(12, 8) };
        foreach (string name in new[] { "History", "Branches", "Tags", "Remotes", "Stashes", "Worktrees", "Rewrite", "Advanced", "Recovery" }) {
            var tab = Button(name, () => { _repositoryTab = name; RenderManagement(); RenderNavigation(); }); tab.Classes.Add("selection-item"); tab.Background = name == _repositoryTab ? SelectedSurface : Brushes.Transparent; tab.BorderThickness = new Thickness(0); tabs.Children.Add(tab);
        }
        Add(root, tabs, 0);
        if (_tools?.Operation is { Length: > 0 } operation) {
            var banner = WrapActions(Text(operation + " in progress", 12, Amber, true), Button("Resolve files", () => Run(() => SetMode("merge"))), RepoAction("Continue " + operation, () => FinishGitOperation(operation, false)), RepoAction("Abort " + operation, () => FinishGitOperation(operation, true)));
            Add(root, new Border { Child = banner, Background = Bar, Padding = new Thickness(16, 8) }, 1);
        }
        Add(root, content, 2); _workspace.Content = root;
    }
    void RenderRepositoryTools() {
        if (_management is not { } model) return;
        var page = new StackPanel { Spacing = 0 };
        Control Actions(params Control[] controls) => WrapActions(controls);
        switch (_repositoryTab) {
            case "Branches":
                page.Children.Add(Section("Local branches", Actions(RepoAction("Create branch", CreateBranchDialog), RepoAction("Merge revision…", () => HistoryDialog("merge", "")), RepoAction("Rebase onto…", () => HistoryDialog("rebase", "")))));
                foreach (var branch in model.Branches) page.Children.Add(Section(branch.Name + (branch.Current ? " · current" : ""), Text(Short(branch.Hash) + (branch.Upstream.Length > 0 ? " · tracks " + branch.Upstream : ""), 11, Faint), Actions(
                    RepoAction("Switch", async () => { await _repo!.SwitchBranchAsync(branch.Name); await LoadManagement(); }),
                    RepoAction("Rename", () => RenameBranch(branch)), RepoAction("Delete", () => DeleteBranch(branch)),
                    RepoAction("Merge into current", () => HistoryDialog("merge", branch.Hash)), RepoAction("Rebase current onto", () => HistoryDialog("rebase", branch.Hash)))));
                break;
            case "Tags":
                var tagSearch = new TextBox { Watermark = "Find a tag by name, message, or commit", Name = "TagSearch" };
                page.Children.Add(Section("Tags · " + model.Tags.Count, Paragraph("Mark a commit with a version, review its annotation, then push it to your remote.", Faint), RepoAction("Create annotated tag", () => TagDialog("HEAD")), tagSearch));
                var tagRows = new List<(Control Row, string Search)>();
                foreach (var tag in model.Tags) {
                    var row = Section(tag.Name, Paragraph(tag.Subject), Text(Short(tag.Commit), 11, Faint), Actions(RepoAction("Edit tag", () => EditTag(tag)), RepoAction("Delete tag", () => DeleteTag(tag)), RepoAction("Push tag", () => PushTag(tag))));
                    page.Children.Add(row); tagRows.Add((row, tag.Name + " " + tag.Subject + " " + tag.Commit));
                }
                if (model.Tags.Count == 0) page.Children.Add(Section("No tags yet", Paragraph("Create an annotated tag to mark your first version.")));
                var noTags = Paragraph("No tags match your search.", Faint); noTags.Margin = new Thickness(18); noTags.IsVisible = false; page.Children.Add(noTags);
                tagSearch.TextChanged += (_, _) => { foreach (var row in tagRows) row.Row.IsVisible = row.Search.Contains(tagSearch.Text ?? "", StringComparison.OrdinalIgnoreCase); noTags.IsVisible = tagRows.Count > 0 && tagRows.All(r => !r.Row.IsVisible); };
                break;
            case "Remotes":
                page.Children.Add(Section("Repository connections", Actions(RepoAction("Add remote", () => RemoteDialog(null)), Button("Clone repository", () => Run(CloneDialog)), Button("Create repository", () => Run(CreateRepositoryDialog)))));
                foreach (var remote in model.Remotes) page.Children.Add(Section(remote.Name, Paragraph(remote.Url), Actions(RepoAction("Fetch", async () => { await _repo!.FetchAsync(remote.Name); await LoadManagement(); _status.Text = "Fetched " + remote.Name + "."; }),
                    RepoAction("Fetch and prune", async () => { await _repo!.FetchAsync(remote.Name, true); await LoadManagement(); _status.Text = "Fetched " + remote.Name + " and pruned deleted branches."; }),
                    RepoAction("Pull…", PullDialog),
                    RepoAction("Push branch", () => PushBranch(remote.Name)),
                    RepoAction("Force push…", () => PushBranch(remote.Name, true)), RepoAction("Edit remote", () => RemoteDialog(remote)), RepoAction("Remove remote", async () => {
                    if (!await ReviewAction("Remove remote", $"Remove {remote.Name} ({remote.Url}) and its local tracking references? The remote repository itself is preserved.", "Remove remote")) return;
                    await _repo!.DeleteRemoteAsync(remote.Name, remote.Url); await LoadManagement();
                }))));
                break;
            case "Stashes":
                page.Children.Add(Section("Saved work", Paragraph("Save unfinished work, switch context, then restore both your files and staging state."), RepoAction("Save stash", SaveStashDialog)));
                foreach (var stash in _tools?.Stashes ?? []) page.Children.Add(Section(stash.Subject, Text(stash.Reference + " · " + Short(stash.Hash), 11, Faint), Actions(
                    RepoAction("View patch", async () => await ShowText("Stash · " + stash.Reference, await _repo!.StashPatchAsync(stash.Hash))),
                    RepoAction("Apply", async () => await OperationResult(await _repo!.ApplyStashAsync(stash.Hash, false))),
                    RepoAction("Pop", async () => await OperationResult(await _repo!.ApplyStashAsync(stash.Hash, true))),
                    RepoAction("Drop", async () => { if (await ReviewAction("Drop stash", stash.Subject + "\nA recovery reference will retain this saved work.", "Drop stash")) { await _repo!.DropStashAsync(stash.Hash); await LoadManagement(); } }))));
                if (_tools?.Stashes.Count is null or 0) page.Children.Add(Section("No saved stashes", Paragraph("Your saved work will appear here.")));
                break;
            case "Worktrees":
                page.Children.Add(Section("Parallel working folders", Paragraph("Work on another branch in a separate folder without disturbing this checkout."), RepoAction("Add worktree", AddWorktreeDialog)));
                foreach (var tree in _tools?.Worktrees ?? []) page.Children.Add(Section(tree.Branch.Length == 0 ? "Detached · " + Short(tree.Head) : tree.Branch, Paragraph(tree.Path + (tree.Locked ? " · locked" : "")), Actions(
                    RepoAction("Open worktree", () => OpenRepository(tree.Path)), RepoAction("Remove worktree", async () => {
                        if (!await ReviewAction("Remove worktree", $"Remove the working folder {tree.Path}? Git refuses removal if it contains local changes or untracked files. Its branch and commits are retained.", "Remove clean worktree")) return;
                        await _repo!.RemoveWorktreeAsync(tree.Path); await LoadManagement();
                    }))));
                break;
            case "Rewrite":
                page.Children.Add(Section("Rewrite local history", Paragraph("These operations change commits that already exist. Gitland records the previous HEAD under Recovery first, and a rewritten branch needs a forced push to reach a remote."),
                    Actions(RepoAction("Interactive rebase…", InteractiveRebaseDialog), RepoAction("Hard reset…", HardResetDialog), RepoAction("Amend last commit…", AmendDialog))));
                page.Children.Add(Section("Apply commits", Paragraph("Bring individual commits onto this branch, or undo one."),
                    Actions(RepoAction("Cherry-pick…", () => HistoryDialog("cherry-pick", "")), RepoAction("Cherry-pick a run…", CherryPickRangeDialog), RepoAction("Revert…", RevertDialog))));
                page.Children.Add(Section("Where HEAD has been", Paragraph("Git's reflog records every move of HEAD, including commits no branch points at any more."),
                    RepoAction("Show reflog", async () => {
                        var entries = await _repo!.ReadReflogAsync();
                        if (entries.Count == 0) { _status.Text = "The reflog is empty."; return; }
                        await ShowListDialog("Reflog", entries.Select(e => $"{e.Selector,-16} {e.ShortHash}  {e.Action,-14} {e.Subject}").ToArray());
                    })));
                break;
            case "Advanced":
                page.Children.Add(Section("Find a breaking commit", Paragraph("Bisect walks the history between a known good and a known bad commit, halving the range each time you mark the checkout."),
                    Actions(RepoAction("Start bisect…", BisectDialog), RepoAction("Mark good", () => MarkBisect("good")), RepoAction("Mark bad", () => MarkBisect("bad")), RepoAction("Skip", () => MarkBisect("skip")), RepoAction("End bisect", async () => { await _repo!.ResetBisectAsync(); await LoadManagement(); _status.Text = "Bisect ended."; }))));
                page.Children.Add(Section("Nested repositories", Paragraph("Submodules pin another repository at a specific commit."),
                    Actions(RepoAction("Add submodule…", AddSubmoduleDialog), RepoAction("Update and init", async () => { await _repo!.UpdateSubmodulesAsync(); await LoadManagement(); _status.Text = "Submodules updated."; }), RepoAction("Sync URLs", async () => { await _repo!.SyncSubmodulesAsync(); _status.Text = "Submodule URLs synced."; }), RepoAction("List", async () => {
                        var modules = await _repo!.ReadSubmodulesAsync();
                        if (modules.Count == 0) { _status.Text = "This repository has no submodules."; return; }
                        await ShowListDialog("Submodules", modules.Select(m => $"{(m.Initialized ? "ready " : "absent")}  {m.Path,-32} {m.Url}").ToArray());
                    }))));
                page.Children.Add(Section("Large files", Paragraph("Git LFS replaces matching files with pointers. Patterns live in .gitattributes."),
                    Actions(RepoAction("Track a pattern…", LfsDialog), RepoAction("Show patterns", async () => {
                        var patterns = await _repo!.ReadLfsPatternsAsync();
                        if (patterns.Count == 0) { _status.Text = "No LFS patterns are configured."; return; }
                        await ShowListDialog("Git LFS patterns", patterns.Select(p => p.Pattern).ToArray());
                    }))));
                page.Children.Add(Section("Patches and export", Paragraph("Move a change between checkouts without a remote, or export a revision's files."),
                    Actions(RepoAction("Export working tree…", () => ExportPatchDialog("")), RepoAction("Apply patch…", ApplyPatchDialog), RepoAction("Archive revision…", ArchiveDialog))));
                page.Children.Add(Section("Hooks and signatures", Paragraph("Hooks run on Git events; .sample files are inactive until renamed."),
                    Actions(RepoAction("Show hooks", async () => {
                        var hooks = await _repo!.ReadHooksAsync();
                        if (hooks.Count == 0) { _status.Text = "This repository has no hooks folder."; return; }
                        await ShowListDialog("Hooks", hooks.Select(h => $"{(h.Enabled ? "active  " : "inactive")}  {h.Name}").ToArray());
                    }), RepoAction("Verify HEAD signature", async () => {
                        var status = await _repo!.VerifySignatureAsync("HEAD");
                        _status.Text = status.Code == "N" ? "HEAD is not signed." : $"HEAD signature {status.Code} · {status.Signer} {status.Key}".TrimEnd();
                    }))));
                break;
            case "Recovery":
                page.Children.Add(Section("Saved before history changes", Paragraph("Gitland keeps references before reset, amend, merge, rebase, cherry-pick, revert, and local branch/tag/stash deletion. Restore a branch to inspect an earlier state.")));
                foreach (var saved in _tools?.Recovery ?? []) page.Children.Add(Section(saved.Subject, Paragraph(saved.Reference), Text(Short(saved.Hash), 11, Faint), Actions(
                    RepoAction("Restore as branch", () => RestoreBranch(saved)),
                    RepoAction("Inspect", async () => await ShowText("Saved history", await _repo!.CommitDetailsAsync(saved.Hash))),
                    saved.Reference.EndsWith("-stash") ? RepoAction("Restore stash files", async () => await OperationResult(await _repo!.ApplyStashAsync(saved.Hash, false))) : Text("", 10))));
                if (_tools?.Recovery.Count is null or 0) page.Children.Add(Section("No recovery entries yet", Paragraph("Saved history appears here after an operation.")));
                break;
        }
        RepositoryPage(new ScrollViewer { Content = page });
    }
    async Task OperationResult(GitOperationResult result) { await LoadManagement(); _status.Text = result.Message; }
    async Task FinishGitOperation(string kind, bool abort) {
        if (abort && !await ReviewAction("Abort " + kind, "Return to the state before this operation? Conflict-resolution edits made during the operation will be discarded.", "Abort operation")) return;
        await OperationResult(await _repo!.FinishOperationAsync(kind, abort));
    }
    async Task HistoryDialog(string kind, string revision) {
        if (_repo == null || _management?.Head is not { } head) return;
        var target = new TextBox { Text = revision, Watermark = "Branch, tag, or commit" };
        string help = kind switch { "merge" => "Integrate the selected revision into the current branch.", "rebase" => "Replay this branch's commits onto the selected revision. This rewrites commit IDs; use it for unpublished work.", "cherry-pick" => "Apply the selected commit's change as a new commit on this branch.", _ => "Create a new commit that reverses the selected commit." };
        await FormDialog(kind + " · " + _state.Branch, [Paragraph(help), Field("Revision", target), Paragraph("Your working tree must be clean. Gitland saves the current HEAD under Recovery before proceeding. If conflicts occur, resolve and stage them, then Continue from Repository.")], kind, async () => await OperationResult(await _repo.HistoryOperationAsync(kind, target.Text ?? "", head)));
    }
    async Task RenameBranch(GitBranch branch) {
        var name = new TextBox { Text = branch.Name };
        await FormDialog("Rename branch", [Field("New name", name)], "Rename branch", async () => { await _repo!.RenameBranchAsync(branch.Name, name.Text ?? "", branch.Hash); await LoadManagement(); });
    }
    async Task DeleteBranch(GitBranch branch) {
        var force = new CheckBox { Content = "Allow deletion of an unmerged branch" };
        await FormDialog("Delete branch · " + branch.Name, [Paragraph("Delete this local branch reference. Checked-out branches are protected. Its tip is saved under Recovery."), force], "Delete local branch", async () => { await _repo!.DeleteBranchAsync(branch.Name, branch.Hash, force.IsChecked == true); await LoadManagement(); });
    }
    async Task EditTag(GitTag tag) {
        string expected = await _repo!.TagObjectAsync(tag.Name);
        var revision = new TextBox { Text = tag.Commit }; var message = new TextBox { Text = await _repo.Git("for-each-ref", "--format=%(contents)", "refs/tags/" + tag.Name), AcceptsReturn = true, MinHeight = 90 };
        await FormDialog("Edit local tag · " + tag.Name, [Field("Target revision", revision), Field("Annotation", message), Paragraph("Updates only the local tag. Published tags are not force-pushed.")], "Update local tag", async () => { await _repo.UpdateTagAsync(tag.Name, revision.Text ?? "", message.Text ?? "", expected); await LoadManagement(); });
    }
    async Task DeleteTag(GitTag tag) {
        string expected = await _repo!.TagObjectAsync(tag.Name);
        if (!await ReviewAction("Delete local tag", $"Delete {tag.Name} at {tag.Commit}? The remote tag is preserved. A recovery reference retains the local tag object.", "Delete local tag")) return;
        await _repo.DeleteTagAsync(tag.Name, expected); await LoadManagement();
    }
    async Task RemoteDialog(GitRemote? remote) {
        var name = new TextBox { Text = remote?.Name ?? "origin" }; var url = new TextBox { Text = remote?.Url, Watermark = "https://github.com/owner/repository.git" };
        await FormDialog(remote == null ? "Add remote" : "Edit remote", [Field("Name", name), Field("URL or local path", url)], "Save remote", async () => {
            GitRepository.ValidateRemoteUrl(url.Text ?? "");
            if (remote == null) await _repo!.AddRemoteAsync(name.Text ?? "", url.Text ?? "");
            else await _repo!.UpdateRemoteAsync(remote.Name, name.Text ?? "", url.Text ?? "", remote.Url);
            await LoadManagement();
        });
    }
    async Task SaveStashDialog() {
        var message = new TextBox { Watermark = "What are you working on?" }; var untracked = new CheckBox { Content = "Include untracked files", IsChecked = true };
        await FormDialog("Save unfinished work", [Field("Stash message", message), untracked, Paragraph("Saves staged and unstaged changes, then restores the working tree to HEAD. Ignored files remain untouched.")], "Save stash", async () => { await _repo!.SaveStashAsync(message.Text ?? "", untracked.IsChecked == true); await LoadManagement(); });
    }
    async Task AddWorktreeDialog() {
        var path = new TextBox { Watermark = "Full path to a new folder" }; var revision = new TextBox { Text = "HEAD" }; var branch = new TextBox { Watermark = "New branch name · leave empty for detached HEAD" };
        await FormDialog("Add worktree", [Field("Folder", path), Field("Start at revision", revision), Field("New branch", branch)], "Create worktree", async () => { await _repo!.AddWorktreeAsync(path.Text ?? "", revision.Text ?? "", branch.Text); await LoadManagement(); });
    }
    async Task RestoreBranch(GitRecovery saved) {
        var name = new TextBox { Watermark = "recovered/my-work" };
        await FormDialog("Restore saved history", [Paragraph(saved.Reference), Field("New branch name", name)], "Create recovery branch", async () => { await _repo!.CreateBranchAsync(name.Text ?? "", saved.Hash, false); _repositoryTab = "Branches"; await LoadManagement(); });
    }
    async Task CloneDialog() {
        if (!await MayLeaveMerge()) return;
        var url = new TextBox { Watermark = "Repository URL or local path" }; var path = new TextBox { Watermark = "Full path to a new destination folder" };
        await FormDialog("Clone repository", [Field("Source", url), Field("Destination", path)], "Clone and open", async () => { var repo = await GitRepository.CloneAsync(url.Text ?? "", path.Text ?? ""); await OpenRepository(repo.Root); });
    }
    async Task ShowText(string title, string value) {
        var window = new Window { Title = title, Width = 940, Height = 690, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Content = new TextBox { Text = value, IsReadOnly = true, AcceptsReturn = true, FontFamily = Mono, FontSize = GitlandApplication.Preferences.CodeSize, Margin = new Thickness(18) };
        await window.ShowDialog(this);
    }
    async Task InspectCommit(GitCommit commit) {
        string details = await _repo!.CommitDetailsAsync(commit.Hash);
        var window = new Window { Title = "Commit " + commit.ShortHash, Width = 900, Height = 700, Background = Ground, Foreground = Ink, FontFamily = Sans, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(18) };
        Func<Task>? followUp = null;
        Button next(string label, Func<Task> run) => Button(label, () => { followUp = run; window.Close(); });
        Add(root, WrapActions(next("View patch", async () => await ShowText("Patch · " + commit.ShortHash, await _repo.CommitPatchAsync(commit.Hash))), next("Cherry-pick", () => HistoryDialog("cherry-pick", commit.Hash)), next("Revert", () => HistoryDialog("revert", commit.Hash)), next("Tag commit", () => TagDialog(commit.Hash)), next("Reset to this commit…", () => ResetDialog(commit.Hash)), next("Amend HEAD message…", AmendMessageDialog)), 0);
        Add(root, new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, FontFamily = Mono, FontSize = 13 }, 1); window.Content = root; await window.ShowDialog(this);
        if (followUp != null) await followUp();
    }
    async Task ResetDialog(string revision) {
        if (_management?.Head is not { } head) return;
        var staged = new CheckBox { Content = "Keep the current index (soft reset)", IsChecked = true };
        await FormDialog("Move current branch", [Paragraph($"Move {_state.Branch} to {Short(revision)}. Working files are kept. The previous HEAD is saved under Recovery. This rewrites local branch history."), staged, Paragraph("Clear the option to also reset the index, leaving changes unstaged (mixed reset).")], "Reset, keep files", async () => { await _repo!.ResetKeepingFilesAsync(revision, head, staged.IsChecked == true); await LoadManagement(); });
    }
    async Task AmendMessageDialog() {
        if (_management?.Head is not { } head) return;
        var message = new TextBox { Text = await _repo!.CommitMessageAsync(head), AcceptsReturn = true, MinHeight = 150 };
        await FormDialog("Amend HEAD message", [Field("Commit message", message), Paragraph("Rewrites the latest commit's message and ID. Existing staged changes are preserved for a later commit. The original commit is saved under Recovery.")], "Amend message", async () => { await _repo.AmendMessageAsync(message.Text ?? "", head); await LoadManagement(); });
    }
}
