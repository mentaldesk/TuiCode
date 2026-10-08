#!/usr/bin/env bash
# Puts a checksum-verified nfpm on a GitHub Actions runner's PATH for the steps after this one.
set -euo pipefail

version=2.47.0
case $(uname -m) in
  x86_64) asset=Linux_x86_64 sha=0660ca602b2d2d2ae4781a06c692b3eeb9d437ffea05b831d76e41f4a3188783 ;;
  aarch64) asset=Linux_arm64 sha=1c0f5f2999b9a974bfb04fdb0cc3306096de530ac5dbb25d739cc5f5219c919c ;;
  *) echo "install-nfpm.sh: no nfpm for $(uname -m)" >&2; exit 1 ;;
esac

archive="$RUNNER_TEMP/nfpm.tar.gz"
curl -fsSLo "$archive" "https://github.com/goreleaser/nfpm/releases/download/v$version/nfpm_${version}_$asset.tar.gz"
echo "$sha  $archive" | sha256sum -c -
mkdir -p "$RUNNER_TEMP/nfpm"
tar -xzf "$archive" -C "$RUNNER_TEMP/nfpm" nfpm
echo "$RUNNER_TEMP/nfpm" >> "$GITHUB_PATH"
