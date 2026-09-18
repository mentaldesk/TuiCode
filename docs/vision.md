# Vision

The yardstick for what TuiCode builds next. If a proposal doesn't serve this, it doesn't ship,
however good it is on its own.

## Who it's for

Developers who live in the terminal — locally or over SSH — and want the editor model they
already know from VS Code and JetBrains (an explorer, tabs, a command palette, rebindable
shortcuts, themes). They're keyboard-first, they switch between languages and repos, they 
spend most of they're time navigating code bases, reviewing PRs and trying to understand
existing/new code.

The first of those users is the maintainer. TuiCode should be good enough to be their daily
driver, and every rough edge they hit in daily use is a legitimate priority.

## What it's trying to be

**A terminal code editor you can use all day without reaching for another one.**

- **Familiar.** Borrow the concepts that make VS Code pleasant: commands, keybindings, themes,
  tabs, an explorer, find across files. A VS Code user should be productive in minutes.
- **Discoverable.** Every action is a command you can find (F1), trigger with a mnemonic, and
  rebind in Settings. No secret hand shakes.
- **Keyboard-first, mouse-tolerant.** Anything you can do, you can do from the keyboard. The mouse
  works where the terminal lets it.
- **Honest about the terminal.** Terminals eat keys and differ in what they support. Where we
  can't work around that in the app, we set the terminal up for the user (the iTerm2 and WezTerm
  integrations) or tell them plainly what won't work.
- **Fast and self-contained.** One native (AOT) binary, quick to start, comfortable over a slow
  SSH link. Installed through the package manager the user already has.
- **Batteries included.** Where possible, features ship with the editor, behind the parts + services
  architecture, not through an extension host.

## What it deliberately isn't

- **Not a modal editor.** Vim and Helix exist already.
- **Not a GUI, a web view, or Electron.** If it can't be drawn in a terminal, it doesn't fit.
- **Not a terminal multiplexer or shell.** The user's terminal already has tabs and splits;
  TuiCode doesn't need to reimplement them.
- **Not an extension marketplace.** Plugins, if they come, are for what we genuinely can't ship
  in-tree (see *Code intelligence* below), not a platform in their own right.
- **Not chasing Terminal.app.** Some terminals strip the modifiers a keyboard-driven editor needs. We
  recommend a better terminal instead of trying to support these atrocities.

## Next themes

Roughly in order. Each is a direction, not a commitment; pitches turn them into work.

1. **Daily-driver gaps.** The small things that send you back to another editor: editor settings
   like indent size and line endings (#14), document stats (#123), moving and trashing files in
   the explorer (#127, #128), and making Cmd+C/V/X just work across terminals (#40).
2. **Seeing change.** Diffs are the foundation for everything git-shaped: compare the buffer
   with the saved file, another file, or a revision (#61), which likely needs a second editor
   group (#60). With diffs in place, reviewing a PR without leaving the editor (#126) becomes
   reachable.
3. **Code intelligence.** Help that understands the code, not just the text: LLM-assisted
   editing (#62), and language-aware navigation such as find-usages, proven first for C# (#63).
   This is where the in-tree rule is most likely to bend.
4. **Reach.** Get TuiCode onto the machines of people who'd use it: winget (#75) and Linux
   package managers (#44), signed binaries on every platform (#70).

## How to judge a proposal

- Does it make TuiCode more usable as an all-day editor for the person above?
- Can a new user find it (F1, a mnemonic, Getting Started) without reading docs?
- Does it work over SSH and in every recommended terminal, or degrade gracefully where it can't?
- Does it keep the binary single, native and fast to start?
- Is it the smallest version that's actually useful?
