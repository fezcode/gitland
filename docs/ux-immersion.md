# History immersion review

## Journey reviewed

An offscreen native Avalonia session exercised opening History, following branches and merges, selecting commits with the mouse and keyboard, reading a changed file, changing merge parents, and moving quickly between commits. Visual review covered a regular 1440 × 920 window, a compact 980 × 640 window, and the Paper theme. The fixture uses real local Git commits, branches, tags, and merges. This is an implementation review, not a user study.

## Changes

- Commit selection and hover span the entire row, including the graph, author, and date. Changed-file selection also spans its navigator. Rows have square edges so selections read as list rows.
- Branch lanes keep their color when columns shift. Curved connections follow actual commit parents; merge nodes have a distinct center. Branch and tag badges sit beside the abbreviated commit ID.
- The graph and diff have a resizable divider. Author and date labels clarify the table; the selected commit and its parent remain visible above the diff. Short diffs align with the top of the review pane.
- Up/Down and Home/End select visible commits. Rapid selection keeps the final requested commit's diff; earlier asynchronous reads cannot replace it.
- Button backgrounds transition in 120 ms, graph emphasis in 170 ms, and new diff content fades from 78% to full opacity in 150 ms. Sidebar tab hover changes immediately so moving between tabs cannot leave a second fading highlight. Source text does not slide or scale. A thin progress indicator runs only while Git content is loading.
- Reduce motion applies immediately to open controls and is saved in user settings. Loading remains visible without an indeterminate animation.

## Validation

The native `--history-review` harness checks full-width hit targets, clicking the date area, keyboard navigation, rapid selection, live reduced-motion behavior, merge-parent review, and compact diff space. It captures `history-immersive.png`, `history-immersive-compact.png`, and `history-immersive-paper.png` in the chosen output directory.

Core tests check lane identity through changing graph columns, commit/parent/file comparisons, and settings persistence and equality. The existing broader UI harness covers themes, merge tools, repository actions, and workspace flows.
