#!/bin/sh
# Installs or updates tuicode in ~/.local/bin:
#   curl -fsSL https://raw.githubusercontent.com/mentaldesk/TuiCode/main/install.sh | sh
set -eu

REPO_URL=https://github.com/mentaldesk/TuiCode
BIN_DIR="$HOME/.local/bin"
SHARE_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/tuicode"

fail() {
    echo "tuicode install: $*" >&2
    exit 1
}

pretty() {
    case "$1" in
        "$HOME"/*) echo "~${1#"$HOME"}" ;;
        *) echo "$1" ;;
    esac
}

detect_rid() {
    os=$(uname -s)
    case "$os" in
        Linux) ;;
        Darwin) fail "this script is for Linux. On macOS, run: brew install mentaldesk/tap/tuicode" ;;
        *) fail "unsupported OS '$os'. Download a build from $REPO_URL/releases" ;;
    esac
    machine=$(uname -m)
    case "$machine" in
        x86_64 | amd64) echo linux-x64 ;;
        aarch64 | arm64) echo linux-arm64 ;;
        *) fail "unsupported architecture '$machine'. TuiCode runs on x86_64 and aarch64" ;;
    esac
}

latest_tag() {
    if command -v curl >/dev/null 2>&1; then
        url=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$REPO_URL/releases/latest") || return 1
    else
        url=$(wget -S --spider "$REPO_URL/releases/latest" 2>&1 | tr -d '\r' | sed -n 's/^ *[Ll]ocation: *\([^ ]*\).*/\1/p' | tail -n 1)
    fi
    case "$url" in
        */releases/tag/v*) echo "${url##*/}" ;;
        *) return 1 ;;
    esac
}

download() {
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL -o "$2" "$1"
    else
        wget -q -O "$2" "$1"
    fi
}

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

main() {
    rid=$(detect_rid)
    command -v curl >/dev/null 2>&1 || command -v wget >/dev/null 2>&1 || fail "needs curl or wget"
    command -v tar >/dev/null 2>&1 || fail "needs tar"
    command -v sha256sum >/dev/null 2>&1 || command -v shasum >/dev/null 2>&1 || fail "needs sha256sum or shasum"

    tag=$(latest_tag) || fail "couldn't reach github.com to find the latest release. Check your network connection"
    version=${tag#v}
    echo "tuicode $version ($rid)"

    target="$BIN_DIR/tuicode"
    if [ -x "$target" ] && [ "$(cat "$SHARE_DIR/version" 2>/dev/null)" = "$version" ]; then
        echo "  ✓ already up to date in $(pretty "$target")"
        return
    fi

    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    trap 'exit 1' HUP INT TERM

    archive="tuicode-$version-$rid.tar.gz"
    url="$REPO_URL/releases/download/$tag/$archive"
    download "$url" "$tmp/$archive" || fail "couldn't download $url"
    echo "  ✓ downloaded $archive"

    download "$url.sha256" "$tmp/$archive.sha256" || fail "couldn't download the checksum $url.sha256"
    expected=$(cut -d ' ' -f 1 "$tmp/$archive.sha256")
    if [ -z "$expected" ] || [ "$(sha256 "$tmp/$archive")" != "$expected" ]; then
        fail "sha256 of $archive doesn't match $archive.sha256. Nothing was installed"
    fi
    echo "  ✓ sha256 verified"

    tar -xzf "$tmp/$archive" -C "$tmp" || fail "couldn't unpack $archive"
    mkdir -p "$BIN_DIR" "$SHARE_DIR" || fail "couldn't create $(pretty "$BIN_DIR")"
    # Rename over the old binary, so a running tuicode keeps its copy and a failed copy leaves it alone.
    if ! { cp "$tmp/TuiCode" "$BIN_DIR/.tuicode.new" && chmod 755 "$BIN_DIR/.tuicode.new" && mv -f "$BIN_DIR/.tuicode.new" "$target"; }; then
        rm -f "$BIN_DIR/.tuicode.new"
        fail "couldn't write $(pretty "$target")"
    fi
    cp "$tmp/THIRD-PARTY-NOTICES.md" "$tmp/DOTNET-THIRD-PARTY-NOTICES.TXT" "$SHARE_DIR/"
    echo "$version" >"$SHARE_DIR/version"
    echo "  ✓ installed to $(pretty "$target")"

    case ":$PATH:" in
        *":$BIN_DIR:"*) ;;
        *)
            echo "$(pretty "$BIN_DIR") isn't on your PATH. Add this line to ~/.profile (or your shell's rc file):"
            # shellcheck disable=SC2016  # printed for the user to paste, not expanded
            echo '  export PATH="$HOME/.local/bin:$PATH"'
            ;;
    esac
    echo "Run \`tuicode\` to start. Re-run this script to update."
}

main "$@"
