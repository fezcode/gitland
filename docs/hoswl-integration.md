# Hisashi OS Window Layer

Gitland speaks version 1 of the Hisashi OS Window Layer (hoswl) protocol over a local named pipe.

## Enable

1. In Hisashi, enable **Settings → OS Window Layer**.
2. In Gitland, open **Settings → Integrations** and check **Enable hoswl integration**.
3. The status changes to **Connected to Hisashi** after its welcome message. Focus Gitland to see its menus in Hisashi’s menubar.

The integration defaults to off and persists with other preferences in `%LOCALAPPDATA%/Gitland/settings.json`. If Hisashi is absent, Gitland waits in the background and retries every two seconds. Turning the switch off sends `enable: false`; a previously established connection can stay open. Closing Gitland cancels pipe I/O and closes the client. Gitland’s title-bar menu works independently.

## Menus

- **File:** open/create a repository, compare local files, refresh, close.
- **View:** file filters, split/unified diff, color themes, full screen.
- **Git:** repository history, revision comparison, conflicts, stage/unstage, new branch, GitHub and releases.
- **Settings:** open preferences.

Commands use the same handlers as the local controls. Actions are disabled while an operation or modal dialog is active. The app rechecks availability when a click arrives; unknown, disabled, parent-menu and separator IDs are ignored. Repository writes keep their existing review, stale-state, backup, and conflict checks.

## Protocol implementation

`HoswlClient` connects asynchronously to the local `hoswl` named pipe. It sends UTF-8, newline-delimited JSON: `hello` first, followed by the current `menu` and `enable` state. The app ID is `com.fezcode.gitland`; the hello includes the process ID and application version. Reconnection resends the current menu tree and switch state.

Incoming messages are limited to 65,536 bytes per line. Unrecognized messages are ignored; malformed JSON is skipped. Pipe errors do not block the UI. Menu updates are coalesced and serialized by one writer. The UI dispatches clicks on Avalonia’s UI thread.

## Verification

Tests use a uniquely named local test pipe, verifying hello ordering, menu serialization, welcome/click handling, disabled and unknown clicks, the enable switch, host restart, reconnection and cancellation when the host is absent. Native UI checks exercise the settings screen. They do not change the user’s Hisashi configuration or click its live menubar.
