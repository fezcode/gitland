# Fonts

Use **Settings → Fonts** for interface and source typefaces. Every menu entry previews its resolved font, and the field below shows the actual family with a Bundled or Installed label. Changes save automatically and apply to the existing window and open dialogs. Font changes retain the merge editor, its text, and its undo stack; source rendering and gutters refresh their metrics in place.

## Interface presets

| Preset | Family selection | Bundled fallback |
| --- | --- | --- |
| Geist | Geist | Geist |
| Bundled Inter | Inter | Inter |
| Modern Grotesque | Aptos, Segoe UI Variable Text, Segoe UI Variable, Segoe UI | Inter |
| Geometric Tech | Bahnschrift, DIN 1451, DIN Alternate | IBM Plex Sans |
| Windows UI | Segoe UI Variable Text, Segoe UI Variable, Segoe UI | Inter |
| Swiss Neo-Grotesque | Inter first; the screenshot’s SF Pro / Helvetica Neue alternatives are unnecessary when Inter is embedded | Inter |
| Editorial Serif | Georgia, Garamond, Palatino Linotype, Palatino | Source Serif 4 |
| Monospace / Code | Cascadia Code | Cascadia Code |

System presets choose the first installed family. Availability is checked by family name, and the selected family is resolved by Avalonia. Interface fonts can be proportional or monospaced. The code selector is limited to Geist Mono, Cascadia Code, and installed Consolas (falling back to Cascadia Code). Literal code font features disable contextual and standard ligature substitution, including conflict markers.

`InterfaceFont` and `CodeFont` are stored in the existing settings JSON. Older settings files receive the Geist defaults; invalid font IDs normalize to those defaults. Restore default fonts resets both choices. Code size remains in Settings → Editor.

## Embedded files and licenses

The existing Geist, Geist Mono, and IBM Plex Sans bundles remain. New files:

- **Inter 4.1:** original Regular, Medium, and SemiBold TTF faces from the [official release](https://github.com/rsms/inter/releases/tag/v4.1); `LICENSES/Inter-OFL.txt`.
- **Cascadia Code 2407.24:** original static Regular TTF from the [official release](https://github.com/microsoft/cascadia-code/releases/tag/v2407.24); `LICENSES/CascadiaCode-OFL.txt`.
- **Source Serif 4:** original static Regular TTF from [Adobe’s release branch](https://github.com/adobe-fonts/source-serif/tree/release/TTF); `LICENSES/SourceSerif4-OFL.txt`.

Static faces are used to ensure the intended regular weight in the native font renderer. The font files are not modified or globally installed. Their original SIL Open Font License notices ship in the published app’s LICENSES directory. Download sources and SHA-256 hashes are recorded in [bundled-fonts.json](bundled-fonts.json).

No Aptos, Segoe, Bahnschrift, DIN, SF Pro, Helvetica, Georgia, Garamond, Palatino, or Consolas files are redistributed. System availability is used instead. Microsoft explains the distinction between using installed fonts and redistributing them in its [font redistribution FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq).

## Verification

The native harness selects every interface and source preset, verifies persistence and the resolved embedded families, and checks fixed-width source cells. It changes fonts with an unsaved merge open, then verifies text retention and native undo. `--font-info` verifies that every bundled family resolves at regular weight and that Cascadia Code renders conflict separators as seven literal equals glyphs. Settings tests cover persistence, older settings files, and invalid font IDs.
