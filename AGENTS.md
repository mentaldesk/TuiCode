# Agent guide

Project-specific gotchas not obvious from the source. `CLAUDE.md` symlinks to this file. Update this and `README.md` in the same PR as the change that motivates it.

## Quick start

```bash
dotnet build TuiCode.slnx
dotnet run --project src/TuiCode                       # needs a real terminal
DOTNET_ROOT=$HOME/.dotnet dotnet test TuiCode.slnx     # DOTNET_ROOT only needed for `dotnet test` on macOS
```

## Solution map

- `src/TuiCode/` — entry point + composition root.
- `src/TuiCode.Workbench/` — shell, parts, services, settings UI.
- `src/TuiCode.Editor/` — `EditorGroup`, `EditorTab`.
- `src/TuiCode.Explorer/` — `FileExplorerView`.
- `src/TuiCode.Abstractions/` — interfaces + DTOs. Features depend only on this.
- `tests/TuiCode.Tests/` — single test assembly.

## Tests

- xUnit v3 on Microsoft.Testing.Platform. Don't switch to v2 + `Microsoft.NET.Test.Sdk` — pulls in `Microsoft.TestPlatform.CoreUtilities` 15.x, which TG's `ConfigurationManager` chokes on at startup.
- Test names: `Method_describes_what_should_happen` (snake_case after the method).
- File-touching tests: `MockFileSystem` from `System.IO.Abstractions.TestingHelpers`. No temp dirs.
- UI / focus / key bugs: drive via TG input injection (`host.App.InjectKey` from the `Iteration` event). See `WorkbenchHostTests.CtrlQ_quits_the_workbench` and TG's [testing docs](https://gui-cs.github.io/Terminal.Gui/docs/drivers.html#testing-and-input-injection). Faster than asking a human to retry manual steps.

## Terminal.Gui v2

- Namespaces split: `Terminal.Gui.App`, `.Views`, `.ViewBase`, `.Drawing`, `.Input`. `global using` per assembly.
- Static `Application` is `[Obsolete]`. Use `Application.Create(timeProvider).Init(...).Run(runnable)` against `IApplication`. `WorkbenchHost` owns it.
- `Tabs.Add(view)`; tab title = child's `Title`; active = `Tabs.Value`.
- `TextView.IsDirty` has no setter. `EditorTab` tracks `_dirty` via `TextView.ContentsChanged` (subscribed *after* the initial `Text =` so load doesn't dirty it).
- `Application.RemoveDefaultKeyBinding(Command.Quit)` crashes `TextView.PopoverMenu` init. To stop Esc-as-Quit, reassign Quit to `Ctrl+Q` — see `WorkbenchHost.NeutralizeBuiltinQuitKey`.
- `View.SetFocus()` returns false when any ancestor has `CanFocus = false`. Set `CanFocus = true` on container `View`s that should host focusable children.
- `ConfigurationManager` deserializes via source-generated `JsonTypeInfo` — only knows the types its built-in scopes use. Records, arrays, even `string[]` silently fail to load. Stick to primitives or persist to a dedicated file.
- `Terminal.Gui.Drawing.Attribute` collides with `System.Attribute`; fully qualify when constructing.

## Key handling

- `IApplication.Keyboard.KeyDown` fires before view dispatch. Single subscription: `WorkbenchHost.OnAppKeyDown`. Set `Key.Handled = true` to consume.
- All keybindings go through `IKeybindingService`; never wire `KeyDown` on individual views. To add a binding:
  1. Constant in `TuiCode.Abstractions.CommandIds`.
  2. Register handler in `WorkbenchHost.RegisterDefaultCommands` via the labelled `Register(id, label, handler)` overload.
  3. Bind in `WorkbenchHost.BindDefaults` (called by `ApplyKeybindings`).
- Chord trie keyed on `Key`; `"Ctrl+W X"` is a single binding. Esc cancels in-flight chords; stray keys abort and are consumed silently.
- Bare letters drop Shift and lowercase: `"x"`, `"X"`, `"shift+x"` collide. `KeybindingService.Bindings` emits the lowercased form. Casefold in UI if needed.
- Chord wins over view bindings: `Ctrl+W` shadows `TextView.Cut`. Use the unshadowed alternative (`Ctrl+X`).
- Input scopes are a stack (`IInputScopeStack`). Workbench scope is bottom, never popped. Modals push their own `KeybindingService` on open / pop on close — workbench shortcuts don't fire while a modal is up. New modal: instantiate `KeybindingService`, push, register bindings against it, pop on close.
- `KeyCaptureScope` is a third kind: absorbs every key, routes to a callback. Used while the picker records a chord. Always pop on commit/cancel.
- Keybindings picker only edits the workbench scope; modal-scope bindings deliberately aren't editable (rebinding them could trap the user).

## Filesystem

- All I/O through `IFileSystem` from `System.IO.Abstractions`; never call `System.IO.File` / `Directory` directly. `IFileInfo.FileSystem` plumbs the same instance through to `EditorTab` etc.
- DI registers `new FileSystem()` singleton; tests build their own `MockFileSystem`.
- `EditorTab.Save` appends `\n` if non-empty and not already terminated (VS Code `files.insertFinalNewline`).

## Terminal compatibility

- `TerminalFlowControl` runs `stty -ixon -ixoff` on Unix so `Ctrl+S` reaches the app. Restored on dispose. Mandatory.
- Three-modifier combos require a capable terminal: iTerm2 / Ghostty / WezTerm / Alacritty on macOS; kitty / foot / GNOME Terminal with `modifyOtherKeys` on Linux. macOS Terminal.app strips them and collapses `Ctrl+Shift+letter` onto `Ctrl+letter`.

## Settings & persistence

- `DefaultSettingsService` is a thin wrapper around TG's static `ConfigurationManager` / `ThemeManager`. `Theme` getter/setter delegate straight to `ThemeManager.Theme`; no backing field. `Load()` calls `ConfigurationManager.Enable(ConfigLocations.All)`; `Program.cs` invokes it on the resolved service before constructing the App, so `ThemeManager.Theme` is in place when `Application.Init()` paints. `ThemeManager.Theme` is a TG-native `[ConfigurationProperty(Scope = typeof(SettingsScope))]` and persists as `{"Theme": "Dark"}` at the JSON root of `~/.tui/TuiCode.config.json`. `Save()` writes that format manually (TG exposes no save API). Picker exposes only `Default` / `Dark` / `Light` (other TG built-ins look bad).
- Keybindings persist to a dedicated file `~/.tui/TuiCode.keybindings.json` (read at `DefaultSettingsService` construction, written on `Save()`) — TG's serializer can't round-trip them. Diff style: `{ Key, Command }`, prefix `Command` with `-` to remove. Boot: `BindDefaults` then `ApplyKeybindings(settings.KeybindingOverrides)`. Picker calls `WorkbenchHost.ApplyEditedBindings` on save (recomputes diff from defaults so the file stays minimal).
- Don't reach into `ConfigurationManager` / `ThemeManager` from feature code — go through `ISettingsService`. Tests use `InMemorySettingsService`.
- Tests that mutate `ThemeManager.Theme` (e.g. `DefaultSettingsServiceTests`) must use `[Collection("StaticConfiguration")]` plus a local `ThemeFixture` to snapshot+restore the static. Keybindings are instance state on the service, so keybinding-only tests need neither.

## Wiring

- `Program.cs` is the only DI consumer.
- `Workbench` (the root `Window`) wires cross-part events in its ctor: `explorer.FileActivated → editor.Open + tab.FocusContent + statusBar.SetMessage`, `editor.FileSaved → statusBar.SetMessage`. Add new cross-part wiring here.
- `WorkbenchHost` owns `IApplication`, the key intercept, and workbench-scoped command/keybinding registrations.
- Parts (`SidebarPart` / `EditorPart` / `StatusBarPart`) are thin layout slots over feature views.

## Navigation

- In-editor cursor navigation (Home/End/Ctrl+arrows + Shift selection variants) comes from TG's built-in `TextView` bindings — we don't bind these ourselves.
- `Ctrl+G` opens `GoToLineView` (1-based `line[:col]` input). `EditorTab.MoveCursor(row, col)` writes through `TextView.InsertionPoint`, which is a `Point` (Column, Row) — not an int offset. Out-of-range row/col clamp.
- `Alt+Left` / `Alt+Right` walk `INavigationHistoryService` (browser-style back/forward). The host calls `Record(leavingLocation)` automatically on `EditorGroup.ActiveTabChanged` and explicitly before a Go-to-line jump. Programmatic switches during back/forward navigation set `_suppressNextActiveTabRecord` so the hop doesn't get re-recorded.
- Default binding is `Alt+CursorLeft`/`Alt+CursorRight` rather than the originally-requested `Ctrl+-` because terminals frequently don't transmit `Ctrl+Minus` cleanly. Rebindable via the keybindings picker.

## Conventions

- Branches: `milestone-N-<slug>` (features), `chore/<slug>` (cleanup), `fix/<slug>` (bugs).
- Commit subject: short imperative; body explains *why*.
- Comment only for non-obvious WHY (hidden constraint, TG quirk, workaround). Codebase runs comment-light.
- No abstractions ahead of need.
- Don't add settings without a picker UI to drive them.
