# Gitland workbench design

## Direction

The interface should feel like a precise desktop tool. Navigation establishes location, the next row contains view controls, and the source area takes the remaining space. Visible decoration must clarify a state or an action.

Version 0.6 replaces the horizontal tab strip with a navigation rail and gives file review and committing separate, permanent spaces. The merge workspace retains bounded source previews above a full-width editable result.

## Selection treatment

Selected navigation, files, settings pages, and conflict choices share a neutral background. There are no accent-edge selection indicators. Hover is quieter than selection; a full outline is reserved for keyboard focus. File-state headings use ordinary title case without decorative dots. Semantic colors remain on diff lines, merge alternatives, and file-state indicators.

## Changes and committing

Working changes owns the entire review-to-commit sequence. Separate Unstaged, Staged, and Conflicts groups expose file state without relying on status letters. A partially staged file has one entry for each version. Review staged filters to the index. The composer keeps the message, optional description, staged count, destination branch, action, and feedback together. A summary-length hint turns amber above 72 characters without imposing a hard limit.

The composer is a stable control: refreshing or staging does not reconstruct its text editors. Drafts are retained separately per repository for the lifetime of the window, and cleared only after a successful commit. Invalid or stale commits show feedback beside the retained message. Commits use the existing HEAD/index checks and Git hooks; no remote push is implicit.

## Decisions

- **Hierarchy:** a 48 px repository title bar, a compact vertical navigation rail, a file-and-commit panel, and an inset source review. The commit draft uses a raised surface; source text uses the darkest surface. Each area has one purpose.
- **Navigation:** Changes, Compare, 3-way, Resolve, History, and GitHub occupy the left rail, with Settings anchored below. The active item uses a neutral selected surface and brighter text, with no colored edge. All / Changed / Conflicts remain explicit file scopes. All and Changed activate Working changes; Conflicts activates Resolve conflicts. Opening an individual merge preserves the file scope.
- **Typography:** Geist and Geist Mono are the defaults. Settings → Fonts provides previewed interface presets and separate source fonts; the resolved family is shown explicitly. See [font details](fonts.md). Source text defaults to 13 px, adjustable from 11–18 px. Page headings use 22 px, file names 12 px, and file-state captions 10 px. Rail labels are compact with full tooltips and accessible names.
- **Color:** Clockt’s Xcode Dark is the default. Graphite, Midnight, and Paper offer neutral, cool, and light palettes. Shared brush objects update live across editors, syntax, semantic backgrounds, controls, and forms, preserving open editors and their undo stacks. Ours is blue, theirs violet, base gray, conflict markers amber, and accepted results green.
- **Structure:** whitespace and alignment separate related controls. Fine rules distinguish the inspector, source panes, and history rows. Small radii are used for interactive controls.
- **Repository:** dedicated tabs separate History, Branches, Tags, Remotes, Stashes, Worktrees, and Recovery. History lanes follow actual parent IDs, with an all-branches scope and search. Write a commit navigates to the main changes composer. One shared commit action handles keyboard, button, and Hisashi menu dispatch.
- **Merge:** numbered conflict chips sit above two source previews. Blue and violet headers identify the read-only alternatives. Resolution controls sit immediately below those previews; the full-width result is labeled Editable. Its gutters show line numbers, markers, and the active block. A progress footer distinguishes unresolved, resolved, and unsaved state. Source snippets wrap; full files, the common ancestor, and an expanded result remain available.
- **GitHub:** account, repository, and release controls use standard rows and lists. Forms show their actual targets and defaults.
- **Small windows:** the rail and file panel narrow, search moves to a second toolbar row, and the commit description becomes expandable. The summary and commit action stay visible. Rendering checks cover 980 × 640, retaining a source viewport over 650 px wide and at least 250 px high.

**Identity:** the logo is a Git graph rooted in a layered island. A vector mark is shared by the title bar and About page; the Windows ICO includes 16–256 px sizes.

## Online references reviewed

- [JetBrains — Resolve Git conflicts](https://www.jetbrains.com/help/idea/resolve-conflicts.html): explicit source choices, editable output, and per-conflict navigation.
- [GitKraken — Visual merge editor](https://gitkraken.com/features/merge-conflict-resolution-tool): separate alternatives and a live output area.
- [Linear — A calmer interface for a product in motion, March 12, 2026](https://linear.app/now/behind-the-latest-design-refresh): consistent headers, dimmer supporting navigation, reduced icon treatments, and softer structural separation.
- [Zed — Split Diffs are Here, February 18, 2026](https://zed.dev/blog/split-diffs): source-centered split views, alignment, and synchronized scrolling.
- [Tower — Inspecting Changes for Windows](https://www.git-tower.com/help/guides/working-copy/inspect-changes/windows): clear file state, changed-file navigation, and direct access to diff inspection.

- [Fork — Git client](https://git-fork.com/): direct access to staging, file review, and commit messages.

These references informed the hierarchy and workflow. Gitland's controls, native layout, and implementation remain its own.

## Verification

The offscreen native renderer captures the diff, merge, repository, GitHub, and forms at desktop and compact sizes. The harness operates file and hunk staging, both entries of partially staged files, multi-paragraph commit messages, Ctrl+Enter, failed stale-index commits, repository-specific drafts, merge choice/undo/save, and tag controls against temporary repositories, and verifies GitHub form defaults without publishing remote resources.

## Windows and settings

Maximized custom-chrome content uses Avalonia’s `OffScreenMargin`, expressed in logical pixels, instead of a hard-coded resize-frame width. Normal and true-fullscreen windows have no compensating inset. F11 remembers the prior normal/maximized state; Escape restores it. The native harness simulates nonuniform and DPI-changed insets; physical multi-monitor placement has not been automated.

Settings is a separate window with Appearance, Editor, Integrations, and About pages. Theme tiles use small previews of their actual palette. Settings save atomically in the user’s local application data. Hisashi is optional; its menus use the same command registry as the local menu, with action availability checked again when invoked.

Implementation reference: [Avalonia Window.OffScreenMargin](https://reference.avaloniaui.net/api/Avalonia.Controls/Window/130EE154).

Background status uses `--no-optional-locks` so reading the worktree does not race the parallel index inventory. See [Git status: background refresh](https://git-scm.com/docs/git-status#_background_refresh).

## Three-way review (0.8)

Three equal-width source columns share a baseline-aligned row model and synchronized scrolling. LEFT, BASE · ANCESTOR, and RIGHT remain visible above the sources. Green indicates a changed side, red an affected ancestor, and gray cells indicate an absent line. Diff colors describe changes; the three columns and pinned revision labels describe ownership. Search, context folding, and divergent-change navigation use the same aligned rows. Short sources start at the top of the viewport. See [workflow details](git-client.md).

## Sidebar and expanded commit editor (0.8.2)

The file sidebar uses a native, keyboard-accessible splitter. Dragged width is saved, while layout clamps it when space is constrained; double-click restores automatic compact/desktop sizing. Diff search responds to the remaining workspace width.

The New commit expand control opens a separate resizable writing window. The subject and long description share the sidebar draft. A distinct right-hand panel lists the staged file versions and line counts; unstaged changes are explicitly excluded. File statistics come from the pinned index tree. Inserting the report appends it to the body, and committing validates the reviewed index, HEAD, and branch. Closing keeps the draft for the current window session.

## Unified merge workspace (0.9.0)

The rail has one Merge entry. Its contextual switch selects conflict resolution or three-way revision review. Both share the same navigation selection; switching away from an edited merge retains the unsaved-work check. Source arrows have accessible action names and tooltips. The wand marks deterministic Magic resolve. Tags has a dedicated rail entry and search by name, annotation, or commit.

Production opens an empty welcome page. Sample repository content lives exclusively in the validation project and never enters the shipped assembly.
