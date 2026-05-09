# Agent guide

This file is for LLM coding assistants (Claude Code, Cursor, Aider, etc.). It captures project-specific gotchas that aren't obvious from reading the source — the stuff that would otherwise be rediscovered through trial and error.

`CLAUDE.md` at the repo root is a symlink to this file so the Claude Code CLI picks it up automatically.

**Keep this file in sync.** If you discover a new project convention, a TG quirk, a build/test setup detail, or a non-obvious pattern that future agents will need, add it here in the same PR as the change that motivated it. If a convention you read here is no longer accurate, fix it. Don't let this file rot — that's worse than not having it at all.

**Also keep [README.md](README.md) in sync** when a PR changes user-visible behaviour, ships or closes one of the tracked gaps, or otherwise affects what the "What works today" / "What's next" sections claim. Same PR as the change.

## Quick start

```bash
dotnet build TuiCode.slnx                            # build everything
dotnet run --project src/TuiCode                     # run the app (needs a real terminal)
dotnet run --project tests/TuiCode.Tests             # run tests, no env setup needed
DOTNET_ROOT=$HOME/.dotnet dotnet test TuiCode.slnx   # alternative test invocation
```

The DOTNET_ROOT export is only needed for `dotnet test` on macOS — the test project uses Microsoft.Testing.Platform, which launches the test exe directly and needs DOTNET_ROOT to find the runtime. `dotnet run` works without it.

## Worktrees

This repo uses git worktrees for parallel work. **Never modify files in the main checkout (`/Users/justice/code/TuiCode`) directly** — that path may belong to another agent's in-flight work, and uncommitted changes there would collide. One worktree per task.

Layout: worktrees are **siblings** of the main checkout, named `<repo>-<branch-slug>`. Branch name and directory suffix match. (Worktrees cannot live inside the main checkout — git rejects that.)

```
/Users/justice/code/
├── TuiCode/                              # main checkout
├── TuiCode-milestone-7-settings/         # worktree on milestone-7-settings
└── TuiCode-fix-explorer-resize/          # worktree on fix/explorer-resize
```

Create a new worktree off `origin/main` (always start from a fresh remote main, not whatever HEAD happens to be):

```bash
git fetch origin
git worktree add ../TuiCode-<slug> -b <branch-name> origin/main
cd ../TuiCode-<slug>
```

For chore branches with a `/` in the name (e.g. `chore/foo`), use a flat directory suffix: `../TuiCode-chore-foo` with `-b chore/foo`. Don't put slashes in the directory name.

Lifecycle:
- **Work** in your worktree. No stashing, no branch switching — each worktree is its own checkout.
- **Open the PR as draft** from your worktree: `gh pr create --draft`. The user marks it ready for review.
- **After merge**, remove the worktree: `git worktree remove ../TuiCode-<slug>` (add `-f` if there are submodules or untracked files you've already saved elsewhere).

If you discover you accidentally started work in the main checkout, stop, stash, create a worktree, pop the stash there, and continue. Don't keep going in the main checkout.

Inspired by <https://www.jamescrosswell.dev/posts/switching-to-git-worktrees/>.

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
- **UI tests use TG's input injection** (`host.App.InjectKey(...)` driven from the `Iteration` event), not manual unit-style focus/keypress assertions. See `WorkbenchHostTests.CtrlQ_quits_the_workbench` and `SettingsFocusTransitionTests` for the pattern, and TG's own [drivers / testing docs](https://gui-cs.github.io/Terminal.Gui/docs/drivers.html#testing-and-input-injection). Reach for this whenever a bug involves focus routing, key dispatch, or scope-stack interaction — much faster than asking a human to re-run manual steps.

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
  2. Register the handler in `WorkbenchHost.RegisterDefaultCommands` — use the labelled overload (`Register(id, label, handler)`) so the keybindings picker and help dialog have a human-readable label.
  3. Bind the key sequence in `WorkbenchHost.BindDefaults` (the static helper called by `ApplyKeybindings`).
- **Chord trie**: bindings are stored as a trie keyed on `Key`, so `"Ctrl+W X"` is a single first-class binding with arbitrary depth. Esc cancels in-flight chords. A stray key during a chord aborts and is consumed silently (matches VS Code).
- **Letter normalization**: bare letters (no Ctrl/Alt) drop the Shift flag and lowercase. So `"x"`, `"X"`, and `"shift+x"` all resolve to the same trie node. Modifier-stacked letters (e.g. `Ctrl+S`) are already canonicalized by TG itself. See `KeybindingService.Normalize`.
- **Chord wins over view bindings.** When a view (e.g. `TextView`) has a default binding for a key that is also a chord prefix at the workbench level (e.g. `Ctrl+W` is `TextView.Cut`), our handler intercepts and starts the chord. Use the unshadowed alternative (e.g. `Ctrl+X` for cut). This is intentional — matches VS Code.
- **Input scopes are a stack.** `IInputScopeStack` (singleton) holds a stack of `IKeybindingService` frames. Workbench keybindings are pushed at startup and never popped. A modal (e.g. `SettingsView`) pushes its own scope on open and pops on close — its `IKeybindingService` is the *only* one that handles keys until popped. Workbench shortcuts (`Ctrl+Q`, `Ctrl+1..9`, …) deliberately do not fire while a modal is up. To add a new modal, instantiate your own `KeybindingService`, push it on the stack, register your bindings against it, and pop it when the modal closes.
- **Workbench-scope only for user keybinding overrides.** The settings keybindings picker reads/writes the *workbench* scope's bindings — modal-scope bindings (settings overlay's `Esc` / `Ctrl+Enter` / etc.) are not editable in v1. Don't add a path that lets users rebind those without thinking through the trap-the-user case (rebinding the only escape from a modal).
- **`KeyCaptureScope`** is a third kind of scope: it doesn't bind anything, it just absorbs every keystroke and routes it to a callback. Used by the keybindings picker while the user is recording a chord. Push it on top of the modal scope; pop on commit/cancel. Never leave one pushed.
- **Bare-letter chord steps store as lowercase.** `KeybindingService.Bindings` emits e.g. `"Ctrl+W x"` (the letter is lowercased by `Normalize`), even if the user originally bound `"Ctrl+W X"`. Both forms hit the same trie node. UI rendering can casefold for display.

## Filesystem and I/O

- **Always go through `IFileSystem`** from `System.IO.Abstractions`. Never call `System.IO.File` / `System.IO.Directory` directly. `IFileInfo.FileSystem` is plumbed through `FileExplorerView` → `EditorTab` so the same code path works against the real disk in production and `MockFileSystem` in tests.
- **`IFileSystem` is a singleton in DI.** `Program.cs` registers `new FileSystem()`; tests construct their own `MockFileSystem` per test.
- **Save appends a trailing newline** if the buffer is non-empty and doesn't already end with `\n`. Matches VS Code's `files.insertFinalNewline: true` default. Empty files stay empty.

## Terminal compatibility

- **`TerminalFlowControl`** (in `TuiCode.Workbench`) snapshots `stty -g` and runs `stty -ixon -ixoff` on Unix so `Ctrl+S` reaches the app instead of being eaten as XOFF flow control. Restored on `Dispose`. macOS Terminal.app and most Unix ttys swallow `Ctrl+S` by default; this fix is mandatory.
- **Three-modifier combos require a capable terminal.** TuiCode assumes the terminal passes full modifier combinations to the application. macOS Terminal.app silently strips most three-modifier combos (`Ctrl+Alt+Shift+letter`) and collapses `Ctrl+Shift+letter` onto `Ctrl+letter`. iTerm2 / Ghostty / WezTerm / Alacritty are recommended on macOS; modern Linux terminals (kitty, foot, GNOME Terminal with `modifyOtherKeys`) are fine.

## Theming and configuration

- **TG's `ThemeManager` owns themes.** v1 exposes TG's built-ins (`Default`, `Dark`, `Light`, `TurboPascal 5`); we don't ship our own theme JSONs. Switch via `ThemeManager.Theme = "Dark"; ConfigurationManager.Apply();` or — in app code — through `ISettingsService.Theme`.
- **TG's `ConfigurationManager` owns persistence.** `Program.cs` calls `ConfigurationManager.Enable(ConfigLocations.All)` *before* DI builds, so the layered JSON hierarchy (library → app → `~/.tui/TuiCode.config.json` → cwd → env → runtime) is already loaded by the time services come up.
- **Theme persists via TG's `ConfigurationManager`** as a static `[ConfigurationProperty] string` on `TuiCodeSettings` — auto-prefixed JSON key `TuiCodeSettings.Theme` under `AppSettings` in `~/.tui/TuiCode.config.json`.
- **Keybinding overrides do NOT use TG's `ConfigurationManager`.** TG deserializes via source-generated `JsonTypeInfo` and only knows the types its built-in scopes use; anything else (records, even `string[]`) silently fails to deserialize on load. So keybindings persist to a sibling file `~/.tui/TuiCode.keybindings.json` that `DefaultSettingsService` reads at construction time and writes on `Save()`. Don't try to add other complex/typed settings to `TuiCodeSettings` without confirming TG can round-trip them — default to a dedicated file when in doubt.
- **Keybinding overrides are diff-style.** Each entry is `{ Key, Command }`; commands prefixed with `-` mean "remove the binding for this key". Boot order: `WorkbenchHost.BindDefaults` registers the hardcoded defaults, then `WorkbenchHost.ApplyKeybindings(settings.KeybindingOverrides)` layers the user's diff on top. The settings picker mutates a local copy and calls `WorkbenchHost.ApplyEditedBindings(picker.CurrentBindings)` on save — that recomputes the diff against defaults so the persisted file stays minimal.
- **Don't read or write `TuiCodeSettings.*` directly** from feature code. Go through `ISettingsService` — `DefaultSettingsService` wraps the static surface so DI-driven code stays uniform and tests can sub in `InMemorySettingsService`.
- **`ISettingsService.Save()` is bespoke.** TG has no public API for "write current state to disk", so `DefaultSettingsService` serializes the diff for theme to `~/.tui/TuiCode.config.json` and writes the keybindings array to `~/.tui/TuiCode.keybindings.json` itself.
- **Static state leaks between tests.** `TuiCodeSettings.Theme` is process-global. Tests that mutate it use the `[Collection("StaticConfiguration")]` attribute so they serialize, and a fixture (e.g. `ThemeFixture` in `KeybindingOverrideTests`) snapshots/restores per test. Keybindings live on the service instance now, so they don't need fixturing.
- **`Terminal.Gui.Drawing.Attribute` collides with `System.Attribute`.** Fully qualify it (`Terminal.Gui.Drawing.Attribute(...)`) when constructing one.

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
- **Settings layer scope (milestone 7).** Theme is the only persisted setting today; the picker UI (`Ctrl+,` → `SettingsView`) only renders categories that are wired. Keybinding overrides via `Application.DefaultKeyBindings` and editor settings can layer on top once they have UI to drive them — don't add settings entries that have no picker.
