# Agent guide

Project-specific gotchas not obvious from the source. `CLAUDE.md` symlinks to this file.

## Audience and scope

This file is for **contributors** (human or AI). `README.md` is for **users**. Keep them disjoint:

> When a PR adds a user-visible feature (a CLI flag, a new Settings section, a keybinding), update `README.md` only if it changes one of: what the app is, how to install it, how to run it, or where to get help. Otherwise the change belongs in `AGENTS.md`.

Per-feature catalogues ("what works today") don't go in the README — they belong in the issue tracker and release notes. Workflow-level docs (branches, PRs, releases) live in `CONTRIBUTING.md`.

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
- Any test that boots a TG `Application` (news up a `WorkbenchHost`, renders a View) or mutates `ThemeManager`/`ConfigurationManager` must derive from `StaticConfigurationTest` (issue #77). Those TG statics are process-global; under xUnit's default parallelism a theme mutation in one test makes TG's render path throw `KeyNotFoundException` in another. The base joins the serialised `StaticConfiguration` collection and snapshot/restores the theme. Most tests don't touch TG statics and stay parallel — only opt the ones that do into the base. A reentrancy guard in the base throws if the serialisation ever breaks, so a forgotten `[CollectionDefinition]` rename fails loudly instead of flaking.
- Tests that boot a `WorkbenchHost` must also pass `driverName: DriverRegistry.Names.ANSI` (the headless, deterministic CI driver). With the default (`null`) TG auto-selects the platform driver; on a headless **Windows** runner that's `WindowsDriver`, which blocks on console input and hangs the run forever — `RunAsync` doesn't observe the cancellation token while blocked. Linux auto-selects ANSI already, which is why this only bit the Windows CI leg. Production passes `null` so a real terminal still gets the best driver.

## Terminal.Gui v2

- Namespaces split: `Terminal.Gui.App`, `.Views`, `.ViewBase`, `.Drawing`, `.Input`. `global using` per assembly.
- Static `Application` is `[Obsolete]`. Use `Application.Create(timeProvider).Init(...).Run(runnable)` against `IApplication`. `WorkbenchHost` owns it.
- `Tabs.Add(view)`; tab title = child's `Title`; active = `Tabs.Value`.
- `TextView.IsDirty` has no setter. `EditorTab` tracks `_dirty` via `TextView.ContentsChanged` (subscribed *after* the initial `Text =` so load doesn't dirty it).
- `Application.RemoveDefaultKeyBinding(Command.Quit)` crashes `TextView.PopoverMenu` init. To stop Esc-as-Quit, reassign Quit to `Ctrl+Q` — see `WorkbenchHost.NeutralizeBuiltinQuitKey`.
- `View.SetFocus()` returns false when any ancestor has `CanFocus = false`. Set `CanFocus = true` on container `View`s that should host focusable children.
- `ConfigurationManager` deserializes via source-generated `JsonTypeInfo` — only knows the types its built-in scopes use. Records, arrays, even `string[]` silently fail to load. Stick to primitives or persist to a dedicated file.
- `Terminal.Gui.Drawing.Attribute` collides with `System.Attribute`; fully qualify when constructing.

## AOT

- Release builds are Native AOT (`PublishAot=true` on `src/TuiCode`). `dotnet publish -c Release -r <rid>` emits a single native binary; `dotnet build`/`dotnet run` still JIT.
- All `src/` projects set `IsAotCompatible=true`, so trim/AOT analyzers run on every Debug build. Don't silence warnings — fix the call site.
- `JsonArray.Add(JsonNode)` is AOT-safe; the generic `JsonArray.Add<T>(T)` overload is not. When appending a `JsonObject`/`JsonArray`, cast to `JsonNode` to pick the right overload (see `DefaultSettingsService.SaveKeybindings`).
- `dotnet test` runs JIT, so AOT-only failures (missing metadata, trim-stripped paths) won't surface there. CI's `AOT smoke` step publishes the binary and runs `./TuiCode --smoke` under a pty — boots through `Application.Init`, renders one iteration, exits 0. Add anything reflection-heavy with that smoke in mind; an AOT-compatible test framework is tracked separately.

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
- `KeyCaptureScope` (`Services/`) is a third kind: absorbs every key, routes to a callback. Used by the keybindings picker (recording a chord) and the mnemonic dialog (reading the typed mnemonic). Always pop on commit/cancel. Note it consumes *everything* — while it's the top scope even `Ctrl+Q` won't quit, so a host test that opens such a modal must Esc it shut (or let it auto-execute and close) before injecting `Ctrl+Q`, else `RunAsync` times out.
- Keybindings picker only edits the workbench scope; modal-scope bindings deliberately aren't editable (rebinding them could trap the user).

## Mnemonics (leader key, #50)

- A leader key (`Ctrl+Space` by default, rebindable like any shortcut → `CommandIds.ShowMnemonics`) opens `MnemonicView`: a modal launcher that lists every command's mnemonic and fires the command the instant the typed prefix is unambiguous — no Enter. Esc cancels, Backspace edits.
- Mnemonics are **hard-coded design decisions, not user-configurable** — only the leader key is rebindable. The table lives in `CommandMnemonics` (Abstractions), keyed by command id, next to `CommandIds`. Commands with no entry (`ShowActions`, `ShowMnemonics` itself) simply don't appear.
- **Invariant: no complete mnemonic is a prefix of another** (guarded by `CommandMnemonicsTests.No_mnemonic_is_a_prefix_of_another`). That's what lets the auto-execute rule work without a terminating key. Mnemonics come in families under a shared first key with room to grow (`cf` close-file reserves `co`/`ca`/`cs`; `tn`/`tp`/`ts` tab/toggle; `fe`/`ft`/`f1`…`f9` focus). `q`/`?` are the only single-key ones.
- The matching logic is the pure, TG-free `MnemonicResolver` (`Matching` for the hint list, `ResolveExact` for the fire-now decision) so it's unit-tested directly. `MnemonicView` is a thin capture-scope shell over it — it reads keys via `KeyCaptureScope` rather than a focused TextField precisely so a command that disposes the view executes on the app key-dispatch path, not inside a TextChanged callback.

## Filesystem

- All I/O through `IFileSystem` from `System.IO.Abstractions`; never call `System.IO.File` / `Directory` directly. `IFileInfo.FileSystem` plumbs the same instance through to `EditorTab` etc.
- DI registers `new FileSystem()` singleton; tests build their own `MockFileSystem`.
- `EditorTab.Save` appends a final line break if non-empty and not already terminated (VS Code `files.insertFinalNewline`). It preserves the file's line-ending style, mirroring VS Code: `DetectEol` fixes the EOL on load and `Save` re-applies it. A non-empty file keeps its own style (first line break wins; LF if it has none); an **empty buffer is treated as a new/blank file and takes the OS default** (`Environment.NewLine` — CRLF on Windows), which is what a `Ctrl+N` file gets. `Normalize` is needed because `TextView.Text` re-joins lines with `Environment.NewLine`, so a CRLF file would otherwise become LF on Linux. In tests: assert exact bytes (`\n` / `\r\n`) for *existing-file* preservation, but assert `Environment.NewLine` for *new/empty-file* output (it's intentionally OS-dependent).

## Terminal compatibility

- `TerminalFlowControl` runs `stty -ixon -ixoff` on Unix so `Ctrl+S` reaches the app. Restored on dispose. Mandatory.
- Three-modifier combos require a capable terminal: iTerm2 / Ghostty / WezTerm / Alacritty on macOS; kitty / foot / GNOME Terminal with `modifyOtherKeys` on Linux. macOS Terminal.app strips them and collapses `Ctrl+Shift+letter` onto `Ctrl+letter`.
- Driver selection (#82): `DriverSelection.Resolve` in `Program.cs` picks the TG driver and plumbs it into `Application.Init(driverName)`. TG 2.1.0's `DriverRegistry` registers exactly three names — `windows` (Win32 Console API), `dotnet` (.NET `System.Console`), `ansi` (escape-sequence/kitty) — and TG's auto-select resolves to `ansi` on Windows, which mis-decodes kitty key events there (Backspace / Shift+letter / Ctrl+digit drop) despite kitty being fully negotiated. So **on Windows we default to `windows`** (`IEnvironment.IsWindows`), which reads keys via the Win32 Console API and decodes correctly in Windows Terminal; off Windows we pass `null` (TG auto). Precedence: `--driver <name>` / `--driver=<name>` flag > `TUICODE_DRIVER` env var > per-OS default; a blank override falls through to the default. The name is pass-through — TG validates it (throws `ArgumentException` on unknown, e.g. the `v2win`/`v2net` names from TG 2.2+ don't exist here). F12 Diagnostics shows the *active* driver (`_app.Driver.GetName()`). Known gaps the driver can't fix: `Ctrl+0` is eaten by the terminal's own zoom-reset ([#81](https://github.com/mentaldesk/TuiCode/issues/81)); WezTerm-on-Windows decodes poorly under every driver (tracked for the TG 2.2.x upgrade). Tests force `ansi` via the same `driverName` ctor param (the real WindowsDriver blocks on console input headless), so the `windows` default only ever runs through `Program.cs`, never in CI/tests.

## Terminal integration

- Each supported emulator (iTerm2, WezTerm) implements `ITerminalIntegration` and is registered as a singleton in `Program.cs`. Consumers (Settings UI + CLI) inject `IEnumerable<ITerminalIntegration>` straight from DI — no separate registry.
- `Iterm2Integration` writes `~/Library/Application Support/iTerm2/DynamicProfiles/tuicode.json` via `IFileSystem`; tests pass `MockFileSystem` + `FakeEnvironment`. Stable GUID; staleness detection via a `TuiCodeIntegrationVersion` marker inside the JSON. `Bound Hosts` lists both `&TuiCode*` and `&tuicode*` so the brew-renamed binary still matches (iTerm2's matcher is case-sensitive).
- `WezTermIntegration` writes `~/.config/wezterm/tuicode.lua` only — it deliberately doesn't touch `wezterm.lua` (WezTerm users treat that file as personal config). The user pastes a one-liner (`require 'tuicode'.apply(config)`) themselves; the snippet is surfaced via `ITerminalIntegration.PostInstallInstructions`, rendered in the Settings panel and printed by the CLI installer. The module registers a `tuicode` key table and a `user-var-changed` handler. `WorkbenchHost` emits OSC 1337 `SetUserVar=TUICODE_ACTIVE=1` on startup (after `_app.Init`) and `=0` on shutdown (post-`_app.Dispose`, pre-`_flowControl.Dispose`) to push/pop the key table — unconditional, since terminals that don't grok OSC 1337 strip it silently. Staleness marker (`-- TuiCodeIntegrationVersion: N`) lives in the module. tmux passthrough caveat: nested sessions need `set -g allow-passthrough on` for the OSC to reach WezTerm.
- CLI surface in `TerminalIntegrationCli` — `--install-/--uninstall-/--list-/--check-terminal-integration[=id]`. `Program.cs` runs it before TG init and exits on hit; the `--check` flag returns 0/1/2 for installed/stale/not-installed.
- Settings UI: `TerminalIntegrationPickerView` shows only the *detected* terminal (per #59). Buttons act on `ITerminalIntegration` directly — no staging via `ISettingsService.Save`, because the write is to an external app's config, not a TuiCode setting. Rendering decisions are split into the pure `TerminalIntegrationPanelState.Build` so unit tests don't need TG.

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
- macOS gotcha: by default Mission Control's "Move left/right a space" eats `Ctrl+Left`/`Ctrl+Right` before iTerm2 sees them. Disable in System Settings → Keyboard → Keyboard Shortcuts → Mission Control. Cursor location history (back/forward) is tracked separately in [#35](https://github.com/mentaldesk/TuiCode/issues/35).

## Release workflow

- `.github/workflows/release.yml` fires on `v*` tag push (or `workflow_dispatch` with an existing tag). Matrix builds AOT single-file binaries on native runners for each RID (Apple Silicon only on macOS — Intel Macs are EOL), archives them (`.tar.gz` on Unix, `.zip` on Windows) with a `.sha256` sidecar, and uploads to a *draft* GitHub Release — review/publish manually.
- macOS signing + notarization auto-enables when these secrets exist; without them the macOS tarballs ship unsigned (Gatekeeper quarantines on download):
  - `APPLE_CERT_BASE64` — Developer ID Application `.p12`, base64-encoded.
  - `APPLE_CERT_PASSWORD` — password for the `.p12`.
  - `APPLE_SIGNING_IDENTITY` — identity string, e.g. `Developer ID Application: Name (TEAMID)`.
  - `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` — for `notarytool submit`. App-specific password from appleid.apple.com.
- A bare Mach-O can't carry a stapled notarization ticket, so we notarize the tarball; users pick up the ticket via the Gatekeeper cache on first launch.
- Linux/Windows arm64 use the public `ubuntu-24.04-arm` / `windows-11-arm` runners — native, no cross-compile.
- `.github/workflows/bump-tap.yml` listens for `release: published` and opens a PR against [mentaldesk/homebrew-tap](https://github.com/mentaldesk/homebrew-tap) bumping `version` + the three `sha256` lines in `Formula/tuicode.rb`. Uses `HOMEBREW_TAP_TOKEN` (a fine-grained PAT scoped to the tap with Contents + Pull requests write). Prereleases skipped. Re-runs idempotently force-push the same `bump-tuicode-X.Y.Z` branch.

## Conventions

- **Work in a dedicated worktree, never on `main` or the primary checkout directly** — `git worktree add ../TuiCode-<slug> -b <branch> origin/main`, one branch per change. This is mandatory for agents: it's what lets multiple sessions/agents work the repo concurrently without colliding. (Human contributors are free to use their own workflow.)
- Branches: `milestone-N-<slug>` (features), `chore/<slug>` (cleanup), `fix/<slug>` (bugs).
- Commit subject: short imperative; body explains *why*.
- Comment only for non-obvious WHY (hidden constraint, TG quirk, workaround). Codebase runs comment-light.
- No abstractions ahead of need.
- Don't add settings without a picker UI to drive them.
