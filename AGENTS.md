# Gitland working and release flows

## Scope and defaults

This repository is Gitland, a native Git client for Windows built with Avalonia/.NET 10.
Preserve existing changes when working in a dirty checkout. Build scripts live at the
repository root; Forge is the sibling `../Forge` project. clockt and Hisashi are
reference implementations of this flow, not Gitland's release target.

## Commit messages

Use a normal commit title and body only. Do not add `Co-Authored-By` trailers or
AI/assistant attribution (including Claude or Codex) to commits or release notes.
For multiline messages or messages containing quotes, write a temporary UTF-8
message file and use `git commit -F <file>`. Check the exit code and verify the
resulting commit before tagging; PowerShell 5.1 can split inline quoted messages.

## RELEASE workflow

Only an explicit request to **RELEASE** triggers the complete publishing flow.
Ordinary fixes, builds, and installer requests do not imply a version bump,
commit, push, tag, or GitHub release. When RELEASE is requested, perform these
steps in order and stop/report any failure before proceeding:

1. Run `./version.ps1 -Bump patch` by default, or `-Set x.y.z` for a requested
   version. Clarify conflicting/ambiguous version instructions. The script keeps
   `src/Gitland.App/Gitland.App.csproj` `<Version>`, the Forge `[app]` version, and
   the README's release-notes link, installer filename, and `dist/` build paths
   synchronized. Forge's wizard strings and registry Version entry interpolate
   `app.version` and need no edit. Run `./version.ps1` to verify.
2. Write `docs/releases/<version>.md`; the README links to it and `version.ps1`
   warns for as long as it is missing. Follow the previous release note's shape:
   an `# Gitland <version>` title, then **Windows release**, **Changes**, and
   **Validation** sections. This file is also the GitHub release body verbatim, so
   write it once and reuse it in step 8.
3. Run `./installer.ps1`. It builds, runs the test suite and the offscreen UI
   checks, publishes the self-contained payload to `dist/win-x64`, and produces
   `dist/installer/Gitland-Setup-<version>.exe`. Do not skip tests for a release.
   Replacing the payload closes a Gitland running out of `dist/win-x64`; installed
   copies are untouched. Report this effect and respect authorization already
   given. Use `-SkipBuild` only to repackage an already-published payload.
4. Surface the exact Setup path for testing. During RELEASE, launch that new Setup
   executable for the user to install/test; an existing installed copy remains old
   until Setup is run. Keep the finish page's "Open Gitland" option enabled rather
   than restarting the old installed executable. Honor any requested test gate.
5. Review and commit the intended changes without attribution, using a message
   file. Verify the commit landed and record its hash before the next steps.
6. Push the commit to Gitland's configured remote/release branch (normally
   `origin main`). Inspect `git remote -v` and the branch first. Never use another
   project's remote, infer a missing remote, or force-push.
7. Create the matching `vX.Y.Z` tag on the verified commit, push it, and create a
   GitHub release with `gh release create vX.Y.Z`, attaching only
   `dist/installer/Gitland-Setup-X.Y.Z.exe`. Gitland ships the installer alone, as
   the other Fezcode apps do; do not build or attach a portable ZIP, even though
   releases up to 0.9.0 carried one. Title the release `Gitland X.Y.Z` with no leading `v`;
   that is what the published releases use, while the tag keeps the `v`. The body
   is `docs/releases/X.Y.Z.md` verbatim — pass it with `--notes-file`, which both
   reuses the written notes and avoids PowerShell multiline argument splitting.
   Verify the published assets.

## Build and installer maintenance

- Keep `forge.toml` on the Mica wizard theme, using Gitland's icon and identity.
  Keep `app.id` (`com.fezcode.gitland`) stable: the Apps & Features record under
  that id is the only thing Forge reads to detect an existing installation, so
  changing it turns every upgrade into a second parallel install.
- Keep every wizard version reference as `${app.version}`. A literal number would
  freeze while `[app] version` moved; `version.ps1` fails on one for that reason.
- Fonts are `AvaloniaResource` and the themes in `ThemeCatalog.cs` are compiled in,
  so the payload is the published output plus the `LICENSES` folder the app
  project copies beside the executable. There is no theme or asset directory to
  ship: adding one means teaching `ThemeCatalog` to load it and adding it to
  `forge.toml`, not dropping files next to the executable and hoping. The payload
  must stay self-contained: `installer.ps1` fails when
  `System.Private.CoreLib.dll` is absent, because a framework-dependent payload
  installs an app that needs a .NET runtime the user may not have.
- User settings live in `%LOCALAPPDATA%\Gitland\settings.json` (see
  `src/Gitland.App/Program.cs`, overridable with `GITLAND_SETTINGS_PATH`) and are
  removed only when the user ticks the uninstaller's remove-settings option.
  Gitland does not use `%APPDATA%\fezcode`; do not "correct" this path to match
  the other Fezcode apps, or uninstall will sweep a directory that never exists.
- Fail on inconsistent versions, failed tests, a missing or stale payload, or a
  non-GUI Setup executable. Never report an old installer as a new success.
- Check GUI Forge process exit codes with `Start-Process -Wait -PassThru`:
  `forge.exe` is a windowsgui binary, so `$LASTEXITCODE` is not propagated through
  the call operator. Quote arguments containing spaces and keep the build hidden.
- Forge needs sibling `../Forge/build/forge.exe` and `uninstall.exe`, produced by
  `gobake build` in Forge. Both are verified to be GUI-subsystem before packaging.
- Scope build cleanup and process shutdown to `dist/win-x64`; preserve other
  installations, release installers, and unrelated `dist` files.

These flows adapt clockt's `AGENTS.md` release and commit conventions to Gitland.
