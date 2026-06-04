# TuiCode

A minimalist terminal code editor inspired by VS Code.

## Installing

macOS / Linux via [Homebrew](https://brew.sh):

```bash
brew install mentaldesk/tap/tuicode
```

Windows via [Scoop](https://scoop.sh):

```powershell
scoop bucket add mentaldesk https://github.com/mentaldesk/scoop-bucket
scoop install tuicode
```

Other channels (winget, Linux packagers) are tracked in [#43](https://github.com/mentaldesk/TuiCode/issues/43) and [#44](https://github.com/mentaldesk/TuiCode/issues/44). In the meantime, pre-built single-file binaries for every supported RID are attached to each [GitHub Release](https://github.com/mentaldesk/TuiCode/releases).

## Running

```bash
tuicode
```

Needs a real terminal — the app won't render through a non-TTY pipe. On macOS, prefer iTerm2, Ghostty, WezTerm, or Alacritty over Terminal.app, which strips most three-modifier key combos and breaks some chord shortcuts. On Linux, kitty / foot / GNOME Terminal with `modifyOtherKeys` all work.

## Getting help

- Bug reports and feature requests: [open an issue](https://github.com/mentaldesk/TuiCode/issues).
- Questions and ideas: [GitHub Discussions](https://github.com/mentaldesk/TuiCode/discussions).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branch/PR/release workflow and [AGENTS.md](AGENTS.md) for code-level conventions (key handling, theming, AOT, the test framework). Written for AI coding agents but just as useful for humans.
