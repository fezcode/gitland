![Gitland — A clear view of your code](docs/assets/banner.svg)

# Gitland

A native Git client for Windows, built around readable diffs, three-way review, and an editable merge result. Clockt’s **Xcode Dark** palette, configurable typography, and a resizable workspace keep your code at the center.

[**Download for Windows x64**](https://github.com/fezcode/gitland/releases/latest) · [Release notes](docs/releases/0.9.0.md) · [Workflow guide](docs/git-client.md)

## Get started

1. Download `Gitland-0.9.0-win-x64.zip` from Releases and extract the entire folder.
2. Run `gitland.exe`.
3. Choose **Open repository**, **Clone repository**, or **Create repository**.

The Windows release includes the .NET runtime. Install [Git for Windows](https://gitforwindows.org/) and keep `git` on PATH. GitHub publishing and release management additionally use the [GitHub CLI](https://cli.github.com/) and your existing `gh` authentication. The app starts empty, with no sample repository or generated history.

You can also launch a repository directly:

```powershell
.\gitland.exe 'C:\Projects\my-repository'
```

## Review and commit

- **Working changes:** separate Unstaged, Staged, and Conflicts groups. All / Changed / Conflicts filters take you to the relevant workspace.
- **Diffs:** side-by-side and unified views, line and word highlights, syntax colors, change map, search, context folding, whitespace filtering, and hunk staging.
- **Commit composer:** summary and description beside your files. The diagonal expand arrow opens a resizable writing window with staged-file statistics and an insertable changes summary. Closing keeps the draft for the current session.
- **Precise staging:** partially staged files appear in both groups; each opens its own version. Commits validate the reviewed index, HEAD, and branch before writing.

## One Merge workspace

**Merge** contains both **Resolve conflicts** and **Three-way comparison**.

For a conflict, compare ours and theirs above an editable result. Inspect the ancestor, move between conflicts, or use the downward arrows to accept a source. Expand the result when you need more room. Source copying uses outward arrows with descriptive tooltips.

For revision review, choose Left / Base / Right branches, tags, or commits. Leave Base blank to find the common ancestor, or compare three local files. The three panes share aligned rows and synchronized scrolling; pinned commit IDs keep a comparison stable while your repository changes.

### Magic resolve

The wand opens a preview of resolutions for identical, one-sided, and independent token edits. Review the proposed code and apply the suggestions you choose. Ambiguous edits remain unresolved. Manual text outside the markers is preserved, and **Undo resolution** restores the previous result.

Magic resolve runs locally using deterministic text analysis. Review and test the result before saving. **Save & mark resolved** checks for remaining markers and outside changes, preserves UTF-8 BOM and newline style, keeps a backup, and stages the file. Finish the merge or rebase as a separate action.

## Manage the repository

| Area | Available actions |
| --- | --- |
| Repositories | Open, create, clone, publish to GitHub |
| History | Parent-derived graph, all branches, search, inspect commits, load more |
| Branches | Create, switch, rename, delete, merge, rebase |
| Tags | Direct Tags navigation; search, create annotated tags, edit, delete, push |
| Remotes | Add, edit, rename, remove, fetch, fast-forward pull, push |
| Saved work | Stash, inspect, apply, pop, drop; create and remove worktrees |
| History operations | Cherry-pick, revert, message-only amend, soft/mixed reset, Continue/Abort |
| Recovery | Retained Git references for prior tips, deleted tags/branches, and removed stashes |
| GitHub | Publish repositories; list and create draft, prerelease, or published releases |

Pushes are normal, non-force pushes. GitHub release creation verifies that the existing remote tag points at the selected commit. See the [workflow guide](docs/git-client.md) for operation details.

## Make it yours

- Four themes: **Xcode Dark**, **Graphite**, **Midnight**, and **Paper**.
- Independent interface and source fonts. Geist, Geist Mono, Inter, IBM Plex Sans, Cascadia Code, and Source Serif 4 are bundled; installed proprietary families have explicit fallbacks. [Font guide](docs/fonts.md).
- Drag the sidebar divider; use arrow keys when focused; double-click to reset. Width, fonts, themes, and editor preferences persist.
- Optional [Hisashi OS Window Layer integration](docs/hoswl-integration.md), available in **Settings → Integrations**.

## Keyboard

| Shortcut | Action |
| --- | --- |
| Ctrl+O | Open repository |
| Ctrl+Enter | Commit from Working changes or the message window |
| Ctrl+, | Settings |
| Ctrl+F | Find in the current diff or three-way comparison |
| Alt+Down / Alt+Up | Next / previous change |
| F5 | Refresh |
| F11 / Escape | Enter / leave full screen |
| Ctrl+Z in the merge result | Undo a manual edit |

## Build and verify

Install the **.NET 10 SDK** and Git, then run:

```powershell
./build.ps1 -Test
./run.ps1 -Repository 'C:\Projects\my-repository'
```

`build.ps1` runs core/integration tests against temporary repositories, exercises native controls offscreen, renders validation images, and publishes a framework-dependent build to `dist/Gitland-0.9.0`. The GitHub ZIP is self-contained:

```powershell
dotnet publish src/Gitland.App/Gitland.App.csproj -c Release -r win-x64 --self-contained true -o dist/Gitland-0.9.0-win-x64
```

The app project contains no sample data. Deterministic fixtures belong to `tools/Gitland.Preview` and are injected only by that validation host. GitHub command tests simulate hosted operations; Git integration tests use local temporary repositories and remotes.

## Current limits

Text review supports UTF-8, including BOM, up to 2 MiB per file. Binary conflicts, other encodings, symbolic links, submodules, and structural/delete-modify conflicts need another tool. New, deleted, and renamed files use whole-file staging. Interactive rebase plans, blame, LFS/submodule interfaces, GitHub Enterprise, and release-asset uploads inside the app are not included. Commit drafts last for the window session. Native dialog placement and Windows backdrop effects depend on the desktop environment.

## Source layout

- `src/Gitland.Core`: Git process boundary, diffs, conflict analysis, management, safe file replacement.
- `src/Gitland.App`: native window, source rendering, merge editor, preferences, integrations.
- `tests/Gitland.Tests`: core and real Git integration tests.
- `tools/Gitland.Preview`: offscreen rendering and native interaction validation.
- `LICENSES`: bundled font licenses.

[Design notes](docs/design.md) · [Git workflows](docs/git-client.md) · [Fonts](docs/fonts.md)
