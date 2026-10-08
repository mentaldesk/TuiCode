#!/usr/bin/env bash
# Usage: packaging/build.sh <version> <rid> <publish dir> <out dir>
# Packages a published Linux binary as the .deb and .rpm a release attaches. Needs nfpm.
set -euo pipefail

version=$1
rid=$2
publish=$3
out=$(mkdir -p "$4" && cd "$4" && pwd)
repo=$(cd "$(dirname "$0")/.." && pwd)

case $rid in
  linux-x64) deb_arch=amd64 rpm_arch=x86_64 ;;
  linux-arm64) deb_arch=arm64 rpm_arch=aarch64 ;;
  *) echo "build.sh: no Linux package for $rid" >&2; exit 1 ;;
esac

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
cp "$publish/TuiCode" "$publish/THIRD-PARTY-NOTICES.md" "$publish/DOTNET-THIRD-PARTY-NOTICES.TXT" "$repo/LICENSE" "$stage"
# nfpm keeps the source mode, and the runtime pack ships the .NET notices as 0744.
chmod 644 "$stage/THIRD-PARTY-NOTICES.md" "$stage/DOTNET-THIRD-PARTY-NOTICES.TXT" "$stage/LICENSE"

cd "$stage"
export PACKAGE_ARCH=$deb_arch PACKAGE_VERSION=$version
nfpm package -f "$repo/packaging/nfpm.yaml" -p deb -t "$out/tuicode_${version}_${deb_arch}.deb"
nfpm package -f "$repo/packaging/nfpm.yaml" -p rpm -t "$out/tuicode-${version}-1.${rpm_arch}.rpm"
