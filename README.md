# TuiCode

A minimalist terminal code editor for working over SSH. Borrows VS Code's user-facing model (file explorer + tabbed editors + commands + keybindings + themes) without the Electron weight — runs as a single .NET process on top of [Terminal.Gui v2](https://gui-cs.github.io/Terminal.Gui/).

## Status

Pre-1.0. The v1 surface is up and usable: open files from a tree, edit and save, multi-tab editing, keyboard-only navigation, theme + keybinding pickers, persisted across launches. The [open issues](https://github.com/mentaldesk/TuiCode/issues) track what's next.

## Run

```bash
dotnet run --project src/TuiCode
```

Needs a real terminal — the app uses TG and won't render through a non-TTY pipe. On macOS, prefer iTerm2, Ghostty, WezTerm, or Alacritty over Terminal.app (which strips most three-modifier key combos and breaks some chord shortcuts).

## What works today

- **File explorer** in the sidebar — tree-style, navigates the working directory.
- **Tabbed editor** — multiple files open, dirty-state indicator (●), `Ctrl+S` to save.
- **Three-level keyboard navigation** — Sidebar / EditorTabStrip / EditorBody. `Ctrl+0` toggles the sidebar; `Ctrl+1..9` jumps directly to the Nth tab and into the editor body; `Esc` returns focus to the active editor; `Ctrl+Esc` lifts focus to the tab strip.
- **In-editor navigation** — TG's built-in word/line/document navigation (`Home`/`End`/`Ctrl+Home`/`Ctrl+End`/`Ctrl+←`/`Ctrl+→` and the `Shift+` selection variants). `Ctrl+G` opens a "Go to line:column" prompt.
- **Chord-aware command/keybinding system** with a modal scope stack — settings overlay (and any future modal) gets its own input scope so workbench shortcuts don't leak through.
- **Settings overlay** (`Ctrl+,`) — modal full-screen UI. Two categories so far:
  - **Theme** — picker with live preview, persisted to `~/.tui/TuiCode.config.json`.
  - **Keyboard Shortcuts** — Rider/VS-Code-style picker. Type to filter, Enter on a row to capture a key combination, Delete to remove. Conflicts (exact match or chord prefix collision) are flagged before the binding is accepted. Diff-style overrides persist to `~/.tui/TuiCode.keybindings.json`.
- **Three TG themes** — Default, Dark, Light. (TG's other built-ins are filtered; see [#11](https://github.com/mentaldesk/TuiCode/issues/11) for shipping our own.)
- **Actions overlay** (`F1`) — VS Code-style command palette listing every registered command with its current keybinding(s). Type to filter, Enter to run.

## What's next

Concrete v1 follow-ups have issues:

- [#13](https://github.com/mentaldesk/TuiCode/issues/13) — persist last-opened folder + open tabs.
- [#14](https://github.com/mentaldesk/TuiCode/issues/14) — editor settings (indent, line endings, …).
- [#11](https://github.com/mentaldesk/TuiCode/issues/11) — ship custom themes (incl. a faithful Turbo Pascal).

Post-v1 directions (editor splits, syntax highlighting, vim mode, git worktrees, LLM coding, plugins, terminal panel, async I/O) are tracked in [#16](https://github.com/mentaldesk/TuiCode/issues/16).

## Project layout

```
src/
├── TuiCode/                    entry point + composition root
├── TuiCode.Workbench/          shell layout, parts, services, settings UI
├── TuiCode.Editor/             EditorGroup + EditorTab
├── TuiCode.Explorer/           file tree
└── TuiCode.Abstractions/       interfaces / DTOs shared between assemblies
tests/
└── TuiCode.Tests/              single test assembly
```

The assemblies depend on `TuiCode.Abstractions` only — features (Editor, Explorer) never reference each other directly.

## Building and testing

```bash
dotnet build TuiCode.slnx
DOTNET_ROOT=$HOME/.dotnet dotnet test TuiCode.slnx
```

The `DOTNET_ROOT` export is needed for `dotnet test` on macOS — see [AGENTS.md](AGENTS.md#tests) for why.

Release publishing is Native AOT — a single native binary, no .NET runtime dependency:

```bash
dotnet publish src/TuiCode -c Release -r osx-arm64    # or linux-x64, linux-arm64, win-x64, …
```

## Releases

Pushing a `v*` tag triggers `.github/workflows/release.yml`, which builds a native single-file binary for each supported RID (`osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`), archives + checksums each, and attaches them to a draft GitHub Release. Bump the version, tag, and push:

```bash
git tag v0.1.0
git push origin v0.1.0
```

macOS builds are codesigned + notarized when the `APPLE_*` secrets are present (see [AGENTS.md](AGENTS.md#release-workflow)); without them the macOS tarballs ship unsigned and Gatekeeper will quarantine them on download.

## Contributing

Read [AGENTS.md](AGENTS.md) before opening a PR. It covers the worktree workflow, key handling, theming/configuration, the test framework gotcha, and the rest of the project conventions. It's written for AI coding agents but is just as useful for humans.

When you ship a PR that changes user-visible features or removes one of the gaps above, **update this README in the same PR** — keep "What works today" and "What's next" honest.
