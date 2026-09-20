# Gitland 0.9.1 — comparison and repository management

## Three-way comparison

Open **3-way → Choose three revisions**. Left and Right accept branches, tags, or commit hashes. Base can be explicit or empty for Git's common ancestor. The file list includes paths changed on either side relative to the base. Commit IDs are pinned for a stable review. To follow moving branches, choose the revisions again.

Each source has independent line numbers. Insertions occupy aligned gaps rather than shifting the other panes. Changed side lines are green, affected ancestor lines red, and words within replacements receive stronger highlighting. The three panes share vertical and horizontal movement. Ctrl+F searches all sources; Enter/Shift+Enter navigate matches. Alt+Down/Up navigate change regions; **Navigate divergent** restricts navigation to lines changed differently by both sides. **Changes + context** retains three context lines and labeled gaps; click a gap to expand.

**Compare three files** loads UTF-8 text outside Git. Source copying uses the original text. Text rendering treats a terminal newline as metadata rather than a separate line; whitespace filtering does not alter source content. Renames currently appear as removed/added paths. Binary files and files over 2 MiB are not supported by the text comparator. Three-way review is read-only; use **Resolve** for editable conflicts, ancestor inspection, per-conflict choices, smart suggestions, and atomic save/stage.

## Magic resolve in the merge workspace

Choose **Merge → Resolve conflicts → Magic resolve** to preview suggestions for the current result. Each independently resolvable block shows the proposed text and why it can be combined. Select the suggestions to apply; overlapping edits and blocks with no usable ancestor remain for a manual decision. The action recognizes identical changes, one-sided changes, and independent token edits, including edits to different portions of the same line.

Magic resolve can analyze the current hand-edited result. It preserves text outside conflict markers, and only reuses reconstructed ancestor hints when both source alternatives still match. Cancel changes nothing. Apply updates the in-memory result; **Undo resolution** restores the exact previous text, manual-edit state, and conflict choices. Saving and staging remain explicit through **Save & mark resolved**. No AI service or network request is involved.

## Repository

The History tab shows a graph derived from commit parent IDs. Search filters loaded subjects, authors, hashes, and references. Graph lines hide during filtering to avoid implying connections between hidden commits. All branches includes local branches, fetched remote references, tags, and HEAD. The default batch is 200 commits; Load more increases the window to 2,000. Clicking a commit opens details and history actions.

Branches, Tags, and Remotes expose creation, inspection, update, and deletion. Branch deletion defaults to merged branches only; Git protects checked-out branches, including those in other worktrees. Tag updates use an expected object ID so concurrent changes are rejected. Local tag/branch deletion does not delete remote references. Remote removal removes its local tracking configuration, not the hosted repository. Pushes retain explicit destination review and never force-push.

Stashes preserve both staged and unstaged work. Apply and Pop require a clean checkout; ignored files are not included. Pop only drops a stash after successful application. Applying a conflicting stash leaves the stash available and exposes conflicts in Resolve. Unlike merge/rebase, Git stash application has no Continue/Abort operation; resolve and commit the restored work normally.

Worktrees create separate working folders at a selected revision, detached or on a new branch. Removing a worktree delegates to Git without force; dirty, untracked, locked, current, and main worktrees are protected by the application or Git.

## History changes and recovery

Merge, rebase, cherry-pick, and revert require a clean checkout and the same HEAD that was reviewed. An operation that stops for conflicts appears in Repository with Continue and Abort. Resolve and stage the conflicts before continuing. Abort returns to the operation's original state and discards conflict-resolution edits. Reverting a merge commit asks which parent's history to keep, and cherry-pick accepts a run of commits applied in order.

Amend either rewrites the message alone, leaving staged content for a later commit, or folds the current index into the last commit. Soft reset keeps the index and worktree; mixed reset keeps working files and resets the index; hard reset also replaces the working tree. All move local history and can diverge from already-published branches, so pushing afterwards needs a forced push, which uses `--force-with-lease` and refuses to overwrite commits the remote gained since your last fetch.

Interactive rebase reads the commits between a revision and HEAD as a plan. Reorder the rows, or set each to pick, reword, edit, squash, fixup, or drop. Gitland validates the plan before running it — a plan that drops everything, or that opens with a squash, is refused rather than started and abandoned mid-way. The plan is handed to Git directly, so no editor window opens, and a reworded message is applied without prompting.

## Discarding work

Discard is available for a single hunk, a file, or the whole working tree, and untracked files can be deleted. Every one of these is destructive to uncommitted work, so each takes a snapshot first: tracked content becomes a commit recorded under Recovery, and untracked files are copied into the Git directory's `gitland-backups` folder before deletion. Discarding a file from the unstaged view keeps what is already staged; discarding from the staged view returns the file to its last commit.

These snapshots are ordinary Git objects and ordinary files. They survive Gitland restarts, but `git gc` prunes unreachable recovery refs on its own schedule, and deleting the repository deletes both. Treat discard as recoverable for the session, not as an archive.

Recovery references keep original objects reachable before history operations, tag edits/deletion, branch deletion, and stash removal. Restore a branch to inspect older commits, or restore saved stash files. These are durable Git references, not automatic reversal of all filesystem/configuration effects. Gitland also retains the existing per-file merge-save backups under the Git directory's `gitland-backups` folder.

## Validation and limits

Real-repository tests cover CRUD, stale selection checks, dirty worktree protection, clone, staged/unstaged/untracked stash restoration, merge/rebase/cherry-pick/revert conflicts and abort, reset, amend, and pinned three-way reads. Native offscreen tests exercise forms, alignment, colors, navigation, compact layout, and existing commit/merge workflows. Network publishing remains dependent on the user's Git/GitHub credentials; validation uses local repositories and does not create hosted resources.

This release closes the gaps measured against Fork, Tower, GitKraken, SourceTree, and VS Code's SCM: discard at every granularity, untracked deletion, hard reset, full amend, hunk unstaging, forced push with lease, pull modes, fetch pruning, file history and searched history, blame, interactive rebase, reflog, bisect, submodules, LFS patterns, hooks, signature verification, patch export/apply, archives, and GitHub Enterprise hosts.

Remaining work: hosting providers beyond GitHub, arbitrary binary comparison, and structural/delete-modify conflict resolution. Interactive rebase depends on the POSIX shell Git for Windows ships, which Gitland already requires.

## Product and command references

- [JetBrains: comparing files and folders](https://www.jetbrains.com/help/idea/comparing-files-and-folders.html)
- [JetBrains: differences viewer](https://www.jetbrains.com/help/idea/differences-viewer.html)
- [Tower: stash workflows](https://www.git-tower.com/help/guides/working-copy/stash/windows)
- [Tower: merge and rebase](https://www.git-tower.com/help/guides/branches-and-tags/merge-rebase/windows)
- [Git: worktree porcelain format and removal](https://git-scm.com/docs/git-worktree)
- [Git: remote configuration](https://git-scm.com/docs/git-remote)
