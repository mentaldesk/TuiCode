#!/bin/sh
# Runs install.sh against the latest release in a clean distro container: ci.yml's install job.
#   SETUP=<command that installs curl or wget> test.sh <install.sh>
set -eu

script=$1
sh -c "${SETUP:-true}"

shims=$(mktemp -d)
failures=0

check() {
    label=$1
    shift
    if "$@"; then echo "ok: $label"; else echo "FAIL: $label"; failures=$((failures + 1)); fi
}

run() {
    out=$(env "$@" sh <"$script" 2>&1) && status=0 || status=$?
    echo "$out" | sed 's/^/    /'
}

fresh_home() {
    HOME=$(mktemp -d)
    export HOME
    bin="$HOME/.local/bin/tuicode"
}

not() { ! "$@"; }
says() { case "$out" in *"$1"*) return 0 ;; *) return 1 ;; esac; }
succeeded() { [ "$status" -eq 0 ] && [ -x "$bin" ]; }
failed_cleanly() { [ "$status" -ne 0 ] && [ ! -e "$bin" ] && [ ! -e "$HOME/.local/bin/.tuicode.new" ]; }
binary_is() { [ "$(ls -li "$bin")" = "$1" ]; }

# Wraps the real curl or wget so a test can break the .sha256 download.
for tool in curl wget; do
    real=$(command -v "$tool") || continue
    cat >"$shims/$tool" <<EOF
#!/bin/sh
out= prev= url=
for a; do case "\$prev" in -o | -O) out=\$a ;; esac; prev=\$a; url=\$a; done
case "\$url:\${TAMPER:-}" in
    *.sha256:missing) exit 22 ;;
    *.sha256:bad) "$real" "\$@" || exit; echo "0000000000000000000000000000000000000000000000000000000000000000  x" >"\$out"; exit 0 ;;
esac
exec "$real" "\$@"
EOF
    chmod +x "$shims/$tool"
done
cat >"$shims/uname" <<EOF
#!/bin/sh
case "\$1" in
    -s) echo "\${FAKE_OS:-Linux}" ;;
    -m) echo "\${FAKE_ARCH:-\$($(command -v uname) -m)}" ;;
esac
EOF
chmod +x "$shims/uname"

fresh_home
run
check "installs" succeeded
check "verifies the checksum" says "sha256 verified"
# shellcheck disable=SC2016  # the literal line the user is told to add
check "shows the PATH line" says 'export PATH="$HOME/.local/bin:$PATH"'
check "tuicode --smoke" "$bin" --smoke

before=$(ls -li "$bin")
run PATH="$HOME/.local/bin:$PATH"
check "re-run leaves a current install alone" binary_is "$before"
check "re-run says it's up to date" says "already up to date"
check "no PATH line once it's on PATH" not says "export PATH"

echo 0.0.0 >"$HOME/.local/share/tuicode/version"
run
check "a newer release replaces the binary" succeeded
check "  ...with a new file" not binary_is "$before"

fresh_home
run TAMPER=bad PATH="$shims:$PATH"
check "a tampered checksum installs nothing" failed_cleanly
check "  ...and says the checksum didn't match" says "doesn't match"

fresh_home
run TAMPER=missing PATH="$shims:$PATH"
check "a missing checksum installs nothing" failed_cleanly

fresh_home
run FAKE_OS=Darwin PATH="$shims:$PATH"
check "macOS installs nothing" failed_cleanly
check "  ...and points at Homebrew" says "brew install"

fresh_home
run FAKE_ARCH=riscv64 PATH="$shims:$PATH"
check "an unsupported architecture installs nothing" failed_cleanly
check "  ...and says so" says "unsupported architecture"

[ "$failures" -eq 0 ] || { echo "$failures failed"; exit 1; }
