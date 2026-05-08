# TuiCode — Design

A minimalist terminal-based code editor, intended to work well over SSH. Inspired by VS Code's user-facing model (file explorer + tabbed editors + command/keybinding/theme systems), but built as a single-process .NET TUI rather than an Electron application.

## Intent

Build a code editor that:

- Runs entirely in the terminal — no GUI, no web view, no Electron.
- Is comfortable to use over an SSH connection (low bandwidth, no client-side rendering tricks).
- Borrows the *concepts* from VS Code that make it pleasant to use (commands, keybindings, themes, tabs) without inheriting its plugin/extension-host complexity.
- Starts as a tight, opinionated subset and grows from there.

Eventually TuiCode should support git worktrees and integrated LLM-assisted coding, but those are explicitly post-v1.

## Non-goals (for v1)

- Plugin / extension loading. No `AssemblyLoadContext`, no extension host, no contribution registry. If a feature exists, it ships in-tree.
- Multiple editor groups / split panes. One group of tabs is enough.
- Syntax highlighting. Plain text only at v1.
- Modal (Vim-style) editing. Non-modal at v1; a "vim mode" can come later as an opt-in.
- File operations beyond open/save (no rename/move/delete from the explorer at v1).
- Settings UI. Config is JSON files, edited by the user.

## Stack

- **.NET 10**, single executable, single solution.
- **Terminal.Gui v2** (`Terminal.Gui` 2.1.0+ on NuGet). v1 is in maintenance; v2 has the layout system, `TabView`, and theming hooks we need.
- **Microsoft.Extensions.DependencyInjection** for service composition.
- **System.Text.Json** for config files (themes, keybindings, settings).
- Plain `TextView` from Terminal.Gui for the editor at v1. The buffer abstraction is wrapped in `TermCode.Editor.TextBuffer` so it can be replaced with a piece-table / gap-buffer later without touching call sites.

## Architecture

The single architectural idea borrowed from VS Code on day one is **parts + services**:

- The shell (the **workbench**) lays out a small fixed set of **parts**: sidebar, editor area, status bar.
- Parts never reach into each other directly. They communicate through services exposed on `TermCode.Abstractions`.
- Features live in their own assemblies (`TermCode.Explorer`, `TermCode.Editor`) and only depend on `TermCode.Abstractions`.

This is enforced by the assembly split — even though it's overkill at file-count scale, it pays for itself the moment a second feature wants to react to "active editor changed" or similar.

### Core services (in `TermCode.Abstractions`)

- `ICommandService` — registry of named commands (`workbench.action.closeActiveEditor`, etc.). Every action — menu items, keybindings, future command palette — dispatches through this single registry.
- `IKeybindingService` — loads a JSON map of chord → command ID, with user overrides merged on top of defaults. The keybinding system never invokes handlers directly; it only resolves command IDs and asks `ICommandService` to execute them.
- `IThemeService` — semantic color tokens (`editor.background`, `tab.activeBorder`, `sideBar.foreground`, …). Views never hardcode colors; they read tokens.
- `IConfigurationService` — everything else: editor settings (indent size, line endings), recently-opened folders, last-window state.
- `IFileSystemService` — thin async wrapper over `System.IO`. Exists so the explorer and editor share one cancellable, async-by-default file API, and so it can be faked in tests.

### DI composition

Services are registered in `App.cs` via `Microsoft.Extensions.DependencyInjection`. Lifetimes:

- Singleton: all the services above.
- Transient: views, editor tabs, explorer nodes.

No service locator pattern — constructors take what they need, and the workbench constructs parts via the container.

## Project layout

```
TuiCode.sln
├── src/
│   ├── TuiCode/                    # entry point, composition root
│   │   ├── Program.cs
│   │   ├── App.cs                  # top-level Toplevel, owns the workbench
│   │   └── appsettings.json        # default config
│   ├── TuiCode.Workbench/          # the "shell" — layout, parts, service implementations
│   │   ├── Workbench.cs            # root view: sidebar | editor area | statusbar
│   │   ├── Parts/
│   │   │   ├── SidebarPart.cs
│   │   │   ├── EditorPart.cs       # hosts the editor group(s)
│   │   │   └── StatusBarPart.cs
│   │   └── Services/
│   │       ├── CommandService.cs
│   │       ├── KeybindingService.cs
│   │       ├── ThemeService.cs
│   │       ├── ConfigurationService.cs
│   │       └── FileSystemService.cs
│   ├── TuiCode.Explorer/           # file tree feature
│   │   ├── FileExplorerView.cs
│   │   └── FileSystemTreeNode.cs
│   ├── TuiCode.Editor/             # tabbed editor feature
│   │   ├── EditorGroup.cs          # one TabView's worth of editors
│   │   ├── EditorTab.cs            # one open file (buffer + view)
│   │   └── TextBuffer.cs
│   └── TuiCode.Abstractions/       # interfaces + DTOs shared between assemblies
└── tests/
    └── TuiCode.Tests/
```

## Theming strategy

Terminal.Gui v2 ships its own theme/config system (`ConfigurationManager`, `ThemeScope`, `ColorScheme`). It is good infrastructure — JSON loading, defaults → app → user merge, hot-reload — but it operates at a coarser grain than a code editor needs: a `ColorScheme` is five `Attribute` pairs (`Normal`, `Focus`, `HotNormal`, `HotFocus`, `Disabled`) per scope (`Base`, `Menu`, `Dialog`, `Toplevel`, `Error`).

A code editor needs *semantic* tokens — `editor.background`, `editor.lineHighlightBackground`, `tab.activeBorder`, `sideBar.background`, etc. — at a much finer grain.

The strategy:

- **TuiCode's token map is the source of truth.** Themes ship as `themes/<name>.json`, a flat token → color map. `IThemeService.GetColor(token)` is the API every view uses.
- **Terminal.Gui's `ColorScheme` is the rendering bridge.** A `TerminalGuiThemeAdapter` (in `TuiCode.Workbench`) reads the token map and constructs `ColorScheme` instances per part. Views assign these to their `ColorScheme` property. Theme switch → adapter rebuilds the schemes → `Application.Refresh()`.
- **Reuse `ConfigurationManager` for loading.** Themes live in TG's config scope so we get the defaults/app/user merge and file-watch reload without rewriting that machinery.
- **Keep theme separate from general settings.** `IThemeService` is colors only. `IConfigurationService` is everything else. They have different lifetimes and reload semantics, and conflating them in v1 makes the v2 split painful.

Worth reading before implementing: `Terminal.Gui/Configuration/ConfigurationManager.cs` and `ThemeScope.cs` in the TG repo, to confirm the public surface and avoid duplicating its merge logic.

## v1 milestones

1. **Skeleton** — App boots, three regions (sidebar / editor area / status bar) with placeholders. Quit via `Ctrl+Q`. ~1 day.
2. **File explorer** — `FileExplorerView` opens a directory tree from the working directory. Arrows navigate, Enter opens. Read-only (no rename/delete). ~2 days.
3. **Single editor** — Enter on a file in the tree opens it in `EditorPart` as one `TextView`. Edit, `Ctrl+S` to save. ~1 day.
4. **Tabs** — Promote `EditorPart` to host an `EditorGroup` (`TabView`). Second open file → new tab; `Ctrl+W` closes; `Ctrl+Tab` cycles. Dirty-state indicator (`●`) on the tab title. ~2 days.
5. **Command + keybinding service** — Extract hardcoded shortcuts into a command registry and a `keybindings.json` config file with user-override merging. ~1–2 days.
6. **Theme service** — Extract colors into `themes/dark.json` + `themes/light.json`, wire `IThemeService` and the TG adapter through every view. ~1 day.
7. **Config + persistence** — Load `~/.tuicode/settings.json`, persist last-opened folder, restore open tabs on relaunch. ~1 day.

Roughly 1.5–2 weeks of focused evenings to a usable v1.

## Decisions to revisit later (post-v1)

- **Editor splits / multiple groups.** Touches focus management; deferred deliberately.
- **Syntax highlighting.** Likely TextMate grammars via a .NET port of `vscode-oniguruma`.
- **Modal (vim) input mode.** As an opt-in setting, not a separate executable.
- **Git worktrees integration.** New assembly (`TuiCode.Git`) consuming the same command/keybinding services.
- **LLM-assisted coding.** New assembly (`TuiCode.Assistant`) consuming the same services. Likely talks to a local model server or a remote API behind a configured endpoint.
- **Plugin loading.** Only if and when a clear use case demands extensibility we can't ship in-tree.
