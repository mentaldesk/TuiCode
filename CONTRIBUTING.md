# Contributing

For code-level conventions (branch names, key handling, AOT gotchas, test framework), see [AGENTS.md](AGENTS.md). This doc covers the higher-level workflows — chiefly how releases happen.

## Branches and commits

- Features: `milestone-N-<slug>` (where `N` matches the milestone issue).
- Cleanup: `chore/<slug>`.
- Bug fixes: `fix/<slug>`.
- One branch per change, one worktree per branch (`git worktree add ../TuiCode-<slug> -b <branch> origin/main`).
- Open PRs as **drafts** until you've verified them locally; mark ready when you're confident.
- Commit subjects: short imperative ("Add X", not "Added X"). Body explains *why*, not *what*.

## Releases

A release is a `v*` git tag plus a GitHub Release attaching native single-file binaries for every supported runtime. The pipeline is tag-driven: pushing the tag triggers everything else.

### End-to-end flow

```mermaid
flowchart TD
    Tag["git tag v0.1.0<br/>git push --tags"]
    Tag -->|push event| WF["Release workflow<br/>.github/workflows/release.yml"]

    WF --> Matrix{{"matrix per RID"}}
    Matrix --> A["osx-arm64<br/>(macos-14)"]
    Matrix --> B["linux-x64<br/>(ubuntu-latest)"]
    Matrix --> C["linux-arm64<br/>(ubuntu-24.04-arm)"]
    Matrix --> D["win-x64<br/>(windows-latest)"]
    Matrix --> E["win-arm64<br/>(windows-11-arm)"]

    A --> Publish["dotnet publish<br/>-c Release<br/>--PublishAot"]
    B --> Publish
    C --> Publish
    D --> Publish
    E --> Publish

    Publish --> Sign["macOS: codesign + notarize<br/>(if APPLE_* secrets present)"]
    Sign --> Archive["tar.gz / zip<br/>+ sha256 sidecar"]
    Archive --> Upload["actions/upload-artifact"]

    Upload --> Release["Release job:<br/>gh release create v0.1.0 dist/* --draft"]
    Release --> Draft[("Draft release<br/>on GitHub")]

    Draft -.->|human review| Publish2["gh release edit v0.1.0<br/>--draft=false"]
    Publish2 --> Public[("Published release<br/>(assets downloadable)")]

    Public -.->|auto-bump| TapPR["PR to homebrew-tap<br/>updating url + sha256"]
    TapPR -.->|merge| Brew[("brew install<br/>mentaldesk/tap/tuicode")]

    style Draft fill:#fffae0
    style Public fill:#e0ffe0
    style Brew fill:#e0ffe0
    style TapPR stroke-dasharray: 5 5
    style Publish2 stroke-dasharray: 5 5
```

Dashed boxes are manual / pending automation (see [#41](https://github.com/mentaldesk/TuiCode/issues/41) for the formula bump step).

### Two ways to fire the workflow

```mermaid
flowchart LR
    subgraph "Normal release"
        T["git tag v0.1.0<br/>git push --tags"]
    end
    subgraph "Re-run an existing release"
        D["GitHub UI:<br/>Actions → Release →<br/>Run workflow → tag: v0.1.0"]
    end
    T -->|push event| WF["Release workflow runs<br/>against the tag's commit"]
    D -->|workflow_dispatch| WF
```

Use the dispatch path when a tag is already pushed but a build step failed (e.g. a flaky notarization upload) and you want to retry without bumping the version. Tags should be immutable; never delete and re-push a tag.

### Why a draft release?

`gh release create … --draft` gives you a chance to spot-check the artifacts before they go public:

- Five tarballs/zips with the expected names and sizes.
- `.sha256` sidecar for each.
- macOS tarball is signed (if you've added the `APPLE_*` secrets — see [AGENTS.md § Release workflow](AGENTS.md#release-workflow)).

Once you publish (`gh release edit <tag> --draft=false` or "Publish release" in the GitHub UI), the asset URLs become unauthenticated-downloadable. **This is what unblocks `brew install`** — draft assets 404 for unauthenticated clients.

### Cutting a release

```bash
# from main, with everything you want to ship merged in
git fetch origin && git checkout main && git pull --ff-only

# tag and push
git tag v0.1.0
git push origin v0.1.0

# wait ~3 min for the workflow, then sanity-check the draft
gh release view v0.1.0 --web

# publish
gh release edit v0.1.0 --draft=false
```

### Distribution

```mermaid
flowchart LR
    Release[("v0.1.0 release<br/>5× tarballs<br/>+ .sha256")]
    Release --> Tap["mentaldesk/homebrew-tap<br/>Formula/tuicode.rb"]
    Tap -->|brew install| User["User's machine<br/>/opt/homebrew/bin/tuicode"]

    Release -.->|future: #43| Winget["winget / scoop"]
    Release -.->|future: #44| Linux["apt / AUR / Flatpak"]
```

The Homebrew formula points at the release tarball URL with a pinned SHA256. When a new release lands, the formula needs to be bumped (today: manually; planned: an auto-bump PR job in this workflow — see [#41](https://github.com/mentaldesk/TuiCode/issues/41)).

For the iTerm2 dynamic profile that ships alongside `tuicode` on macOS, see the formula's `caveats` block and [#40](https://github.com/mentaldesk/TuiCode/issues/40) for the umbrella story across other terminal emulators.
