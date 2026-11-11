# Maber

**A clean, simple, lightweight Markdown reader — built to replace Notepad.**

Maber opens one Markdown file at a time, renders it beautifully, and stays out of the way.
There is no vault, no workspace, no database, and no import step. A native WinUI 3 shell
handles the window and file access, while a WebView2 renderer provides the typography and
Markdown ecosystem.

![version](https://img.shields.io/badge/version-v0.3.0-blue)
![platform](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey)
![license](https://img.shields.io/badge/license-MIT-green)

## Highlights

- Instant single-file reading: double-click a Markdown file and it is on screen.
- Native, small, fast: no Electron, no bundled browser.
- Obsidian-compatible syntax without requiring an Obsidian vault.
- A settings system designed for daily use, including custom shortcuts and local fonts.

## Features

### Reading

- CommonMark + GFM: tables, task lists, strikethrough, autolinks, and code highlighting.
- Obsidian dialect: `[[wikilinks]]`, `![[embeds]]`, callouts, `==highlight==`, `%%comments%%`,
  and `#tags`.
- KaTeX math (`$...$` and `$$...$$`) and Mermaid diagrams, both toggleable.
- Footnotes, heading anchors (`#标题` and `[[#标题]]`), and in-page jumps.
- Frontmatter is shown as a metadata strip instead of being mixed into the body.
- Light theme by default, with optional dark theme or system theme.

### Settings

- Dedicated settings window with Appearance, Reading, Shortcuts, Files, and About pages.
- Body font: built-in presets, any installed system font (searchable), or imported
  `.ttf` / `.otf` / `.ttc` files.
- Separate code font and code size, plus font size, line height, and reading width.
- Custom keyboard shortcuts with recording, conflict detection, and reset.
- Sidebar width and default visibility, remembered window size and position.
- Recent-file limit, startup behavior, external-change policy, and rendering toggles.
- Live typography preview.

### Opening files

- `Ctrl+P` quick open: fuzzy-search recent files, scan the current file's folder (two levels),
  or paste an absolute path.
- Drag and drop a `.md`, `.markdown`, or `.txt` file into the window.
- Reading positions are remembered per file.
- Recent files are de-duplicated, validated, and capped at a configurable limit.

### Files

- Non-exclusive file access: Maber can read a file while Obsidian or VS Code edits it.
- External changes reload automatically, or prompt first, depending on settings.
- Atomic saves with UTF-8, UTF-8 BOM, and GBK detection and write-back.
- `[[wikilinks]]` and `[text](path.md)` links open in a new window; web links open in the
  default browser.
- Local links decode Chinese and space-containing paths; `#标题` opens the matching heading.
- Relative images resolve against the document's folder, its vault root (`.obsidian`), or
  up to six parent folders for `../` attachments.
- Binary files are rejected before rendering instead of being decoded as text.

### Interface

- **Total Mode** (launched directly): sidebar with outline, recent files, and syntax reference;
  `Ctrl+B` collapses it.
- **Slim Mode** (launched with a file): no sidebar, just the document.
- Paper & Ink visual language: warm paper surfaces, ink text, a single terracotta accent,
  and 1px borders in both themes.
- Parchment document icon for associated Markdown files.

## Install

Run `Maber-Setup-0.3.0.exe`.

- Default location: `C:\Program Files\Maber`; the destination page is editable.
- Requires administrator privileges (UAC).
- Checks for the .NET 10 Desktop Runtime and warns before installing if it is missing.
- Creates a Start Menu shortcut and an optional desktop shortcut.
- Registers `.md` and `.markdown` so Maber appears in **Open with** and in
  **Settings → Default apps**. The installer does not silently take over the current default;
  choose **Always use Maber** once to make it the default.
- Can be removed from **Settings → Apps → Installed apps**.
- Requires Windows 10 1809+ (x64) and the .NET 10 Desktop Runtime.

User data is stored separately from the installation:

- `%LOCALAPPDATA%\Maber\settings.json` — settings, shortcuts, recent files
- `%LOCALAPPDATA%\Maber\reading-state.json` — per-file reading positions
- `%LOCALAPPDATA%\Maber\fonts\` — imported fonts
- `%LOCALAPPDATA%\Maber\WebView2\` — WebView2 profile and cache

Uninstalling Maber keeps these files.

A portable build can be produced with `dotnet publish`; see **Build** below.

## Default shortcuts

All shortcuts can be changed in Settings → Shortcuts.

| Shortcut | Action |
|---|---|
| `Ctrl+O` | Open file |
| `Ctrl+S` | Save |
| `Ctrl+E` | Toggle reading / source mode |
| `Ctrl+K` | Outline |
| `Ctrl+F` | Find in document |
| `Ctrl+B` | Toggle sidebar (Total Mode) |
| `Ctrl+P` | Quick open |
| `Ctrl+,` | Settings |

## Build

Requirements: .NET 10 SDK, Node.js 24+ (only for the renderer), and Windows 10 1809+.
Building the installer additionally requires Inno Setup 6.

```bash
# Renderer (output is copied into src/Maber.App/wwwroot)
cd src/Maber.Web
npm install
npm run build

# Application
dotnet build
dotnet run --project src/Maber.App

# Tests
dotnet test
cd src/Maber.Web
npm run typecheck
npm test

# Installer (output goes to the repository root)
pwsh installer/build-exe.ps1    # Inno Setup 6 required
```

## Architecture

```text
Maber.App   WinUI 3 shell: windows, title bar, files, watcher, shortcuts
    ↕       JSON message protocol (BridgeMessages.cs / types.ts)
Maber.Web   TypeScript renderer: markdown-it, KaTeX, Mermaid, CodeMirror, esbuild
Maber.Core  UI-free services: encoding detection, non-exclusive file I/O,
            file watching, settings, shortcuts, reading state
```

## Limitations

- No vault or workspace concept. Wikilinks are resolved relative to the current file by
  walking up its parent folders.
- Source mode is a plain code editor, not a WYSIWYG editor.
- Wikilinks are not validated, and embeds support images only.
- No tabs, cross-file wikilink indexing, or MSIX packaging yet.

## License

Maber is released under the **MIT License**. See [LICENSE](LICENSE).

Maber uses third-party software under its respective licenses. The main components are:

| Component | License |
|---|---|
| Microsoft Windows App SDK | Microsoft Software License Terms |
| Microsoft Windows SDK BuildTools | Microsoft Windows SDK License |
| Microsoft WebView2 SDK | BSD-3-Clause |
| Microsoft Edge WebView2 Runtime | Microsoft license (system component, not bundled) |
| .NET 10 Desktop Runtime | MIT (required, not bundled) |
| markdown-it | MIT |
| KaTeX | MIT (bundled math fonts under SIL OFL 1.1) |
| Mermaid | MIT |
| CodeMirror 6 | MIT |
| DOMPurify | MPL-2.0 OR Apache-2.0 |
| Inno Setup (installer) | Inno Setup License |

Optional Chinese fonts (LXGW WenKai GB Screen R and Smiley Sans) are not bundled; they are
used only when installed on the system and are distributed upstream under the
SIL Open Font License 1.1.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the complete runtime dependency list
and license identifiers.
