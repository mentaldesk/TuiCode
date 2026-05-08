# Agent guide

This file is for LLM coding assistants (Claude Code, Cursor, Aider, etc.). It captures project-specific gotchas that aren't obvious from reading the source — the stuff that would otherwise be rediscovered through trial and error.

`CLAUDE.md` at the repo root is a symlink to this file so the Claude Code CLI picks it up automatically.

**Keep this file in sync.** If you discover a new project convention, a TG quirk, a build/test setup detail, or a non-obvious pattern that future agents will need, add it here in the same PR as the change that motivated it. If a convention you read here is no longer accurate, fix it. Don't let this file rot — that's worse than not having it at all.

For high-level architecture and the v1 milestone plan, read [DESIGN.md](DESIGN.md). Don't duplicate that here.

## Quick start

```bash
dotnet build TuiCode.slnx                            # build everything
dotnet run --project src/TuiCode                     # run the app (needs a real terminal)
dotnet run --project tests/TuiCode.Tests             # run tests, no env setup needed
DOTNET_ROOT=$HOME/.dotnet dotnet test TuiCode.slnx   # alternative test invocation
```

The DOTNET_ROOT export is only needed for `dotnet test` on macOS — the test project uses Microsoft.Testing.Platform, which launches the test exe directly and needs DOTNET_ROOT to find the runtime. `dotnet run` works without it.

## Solution map

- `src/TuiCode/` — entry point + composition root. `Program.cs` registers services in MS.DI and wires the host.
- `src/TuiCode.Workbench/` — shell layout (`Workbench`, `WorkbenchHost`), parts (`SidebarPart`, `EditorPart`, `StatusBarPart`), service implementations (`CommandService`, `KeybindingService`), and `TerminalFlowControl`.
- `src/TuiCode.Editor/` — `EditorTab`, `EditorGroup`. Tabbed editor implementation.
- `src/TuiCode.Explorer/` — `FileExplorerView`. Tree-based file browser.
- `src/TuiCode.Abstractions/` — interfaces and command-id constants shared between assemblies. Workbench/Editor/Explorer all reference this; nothing else should reference each other directly except through these contracts.
- `tests/TuiCode.Tests/` — single test assembly covering everything.

## Test framework

- **xUnit v3 on Microsoft.Testing.Platform.** Do not switch to xUnit v2 + `Microsoft.NET.Test.Sdk`. That combination pulls in `Microsoft.TestPlatform.CoreUtilities` (15.x), which Terminal.Gui's `ConfigurationManager` chokes on during its assembly-scanning startup, and tests fail to even load.
- The test csproj has `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>` and `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>`. Don't change those.
- Test names use `Method_describes_what_should_happen` style (snake_case after the method name). Browse existing tests for the pattern.
- Use `MockFileSystem` from `System.IO.Abstractions.TestingHelpers` for any test touching files. Don't create temp directories.

## Terminal.Gui v2 conventions

- **Namespaces are split** in v2: `Terminal.Gui.App` (Application, IApplication), `.Views` (Window, FrameView, TextView, TreeView, Tabs, Label), `.ViewBase` (View, Pos, Dim), `.Drawing` (LineStyle), `.Input` (Key, KeyBinding, Command). These are set as `global using` in each assembly's `GlobalUsings.cs`.
- **The static `Application` class is `[Obsolete]`.** Use `Application.Create(timeProvider).Init(...).Run(runnable)` against an `IApplication` instance. `WorkbenchHost` owns the lifecycle.
- **`Tabs` uses standard `Add(view)`** to register tabs. Tab header text comes from the child view's `Title` property. The active tab is `Tabs.Value`.
- **`TextView.IsDirty` has no public setter.** Don't try to reset it. `EditorTab` keeps its own `_dirty` flag wired to `TextView.ContentsChanged` (subscribed *after* the initial load so the initial `Text =` doesn't mark it dirty).
- **TG `Application.RemoveDefaultKeyBinding(Command.Quit)` crashes `TextView.PopoverMenu` initialization.** It looks like TG assumes Quit always has at least one key bound. To stop Esc-as-Quit, reassign Quit to `Ctrl+Q` instead — see `WorkbenchHost.NeutralizeBuiltinQuitKey`.

## Key handling

- **App-level intercept**: `IApplication.Keyboard.KeyDown` fires before view dispatch. Set `Key.Handled = true` to consume. `WorkbenchHost.OnAppKeyDown` is the single subscription point for the whole app.
- **All keybindings go through `IKeybindingService`.** Don't add `KeyDown` handlers to individual views. To add a binding:
  1. Add a constant to `TuiCode.Abstractions.CommandIds`.
  2. Register the handler in `WorkbenchHost.RegisterDefaultCommands`.
  3. Bind the key sequence in `WorkbenchHost.RegisterDefaultKeybindings`.
- **Chord trie**: bindings are stored as a trie keyed on `Key`, so `"Ctrl+W X"` is a single first-class binding with arbitrary depth. Esc cancels in-flight chords. A stray key during a chord aborts and is consumed silently (matches VS Code).
- **Letter normalization**: bare letters (no Ctrl/Alt) drop the Shift flag and lowercase. So `"x"`, `"X"`, and `"shift+x"` all resolve to the same trie node. Modifier-stacked letters (e.g. `Ctrl+S`) are already canonicalized by TG itself. See `KeybindingService.Normalize`.
- **Chord wins over view bindings.** When a view (e.g. `TextView`) has a default binding for a key that is also a chord prefix at the workbench level (e.g. `Ctrl+W` is `TextView.Cut`), our handler intercepts and starts the chord. Use the unshadowed alternative (e.g. `Ctrl+X` for cut). This is intentional — matches VS Code.

## Filesystem and I/O

- **Always go through `IFileSystem`** from `System.IO.Abstractions`. Never call `System.IO.File` / `System.IO.Directory` directly. `IFileInfo.FileSystem` is plumbed through `FileExplorerView` → `EditorTab` so the same code path works against the real disk in production and `MockFileSystem` in tests.
- **`IFileSystem` is a singleton in DI.** `Program.cs` registers `new FileSystem()`; tests construct their own `MockFileSystem` per test.
- **Save appends a trailing newline** if the buffer is non-empty and doesn't already end with `\n`. Matches VS Code's `files.insertFinalNewline: true` default. Empty files stay empty.

## Terminal compatibility

- **`TerminalFlowControl`** (in `TuiCode.Workbench`) snapshots `stty -g` and runs `stty -ixon -ixoff` on Unix so `Ctrl+S` reaches the app instead of being eaten as XOFF flow control. Restored on `Dispose`. macOS Terminal.app and most Unix ttys swallow `Ctrl+S` by default; this fix is mandatory.
- **Three-modifier combos require a capable terminal.** TuiCode assumes the terminal passes full modifier combinations to the application. macOS Terminal.app silently strips most three-modifier combos (`Ctrl+Alt+Shift+letter`) and collapses `Ctrl+Shift+letter` onto `Ctrl+letter`. iTerm2 / Ghostty / WezTerm / Alacritty are recommended on macOS; modern Linux terminals (kitty, foot, GNOME Terminal with `modifyOtherKeys`) are fine.

## Composition and wiring

- `Program.cs` is the only place that talks to MS.DI directly. Resolve the entry-point `App`, ask the host for the workbench, plug in the working-directory root, then run.
- `Workbench` (the `Window`) wires events between parts in its constructor: `explorer.FileActivated → editor.Open + statusBar.SetMessage`, `editor.FileSaved → statusBar.SetMessage`. New cross-part wiring goes here.
- `WorkbenchHost` owns the `IApplication` lifecycle, the keybinding intercept, and the workbench-scoped command/keybinding registrations. New defaults go here.
- Parts (`SidebarPart` / `EditorPart` / `StatusBarPart`) are thin layout slots. They host feature views (`FileExplorerView`, `EditorGroup`) and re-expose a small API surface to the workbench.

## Conventions

- **Branch names**: `milestone-N-<slug>` for feature work, `chore/<slug>` for cleanup, `fix/<slug>` for bug fixes.
- **Commit messages**: short imperative subject, body explaining *why*. Look at `git log` for tone.
- **Don't add comments that restate what the code does.** Only add a comment when the WHY is non-obvious — a hidden constraint, a TG quirk, a workaround. The codebase deliberately runs comment-light; respect that.
- **Don't introduce abstractions ahead of need.** YAGNI applies — `TextBuffer` (mentioned in DESIGN.md) doesn't exist yet because `TextView.Text` is sufficient at v1. Add the wrapper when you actually need to swap implementations.
- **Don't add settings/config plumbing yet.** All defaults are hardcoded in code. JSON configuration (themes, keybindings, editor settings) lands in milestone 7. Resist the urge to add a half-finished settings layer earlier.
