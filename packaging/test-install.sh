#!/usr/bin/env bash
# Usage: packaging/test-install.sh <image> <version> <package> [<version> <package>...]
# Installs each package in turn in a clean <image> container, upgrading in place, and checks the
# version, that tuicode starts, and that removing it leaves nothing behind. Needs docker and script.
set -euo pipefail

image=$1
shift
case $image in
  fedora*) format=rpm ;;
  *) format=deb ;;
esac

dir=$(cd "$(dirname "$2")" && pwd)
container=$(docker run -d --rm -v "$dir:/packages:ro" "$image" sleep infinity)
trap 'docker rm -f "$container" >/dev/null' EXIT
run() { docker exec "$container" sh -c "$1"; }
fail() { echo "::error::$image: $1"; exit 1; }

if [ "$format" = deb ]; then
  run 'apt-get update -qq'
  owned=/usr/share/doc/tuicode
else
  owned=/usr/share/licenses/tuicode
fi

while [ $# -gt 0 ]; do
  version=$1
  package=/packages/$(basename "$2")
  shift 2
  echo "Installing $package"
  if [ "$format" = deb ]; then
    run "DEBIAN_FRONTEND=noninteractive apt-get install -y -qq $package"
    installed=$(run "dpkg-query -W -f='\${Version}' tuicode")
  else
    run "dnf install -y -q $package"
    installed=$(run "rpm -q --qf '%{VERSION}' tuicode")
  fi
  [ "$installed" = "$version" ] || fail "installed version is $installed, expected $version"
  # Resolved, because Fedora's /usr/sbin is a symlink to bin and comes first on PATH.
  # shellcheck disable=SC2016  # expands in the container
  found=$(run 'readlink -f "$(command -v tuicode)"')
  [ "$found" = /usr/bin/tuicode ] || fail "tuicode on PATH is '$found', expected /usr/bin/tuicode"
done

for notice in THIRD-PARTY-NOTICES.md DOTNET-THIRD-PARTY-NOTICES.TXT; do
  if [ "$format" = deb ]; then list='dpkg -L tuicode'; else list='rpm -ql tuicode'; fi
  run "$list" | grep -qx "$owned/$notice" || fail "package doesn't carry $notice"
done

run 'tuicode --smoke-syntax'
timeout 30s script -qfec "docker exec -t -e TERM=xterm-256color $container tuicode --smoke" /dev/null

if [ "$format" = deb ]; then
  run 'dpkg -L tuicode > /tmp/files && apt-get remove -y -qq tuicode'
else
  run 'rpm -ql tuicode > /tmp/files && dnf remove -y -q tuicode'
fi
# shellcheck disable=SC2016  # expands in the container
left=$(run 'while read -r f; do if [ -f "$f" ] || [ -L "$f" ]; then echo "$f"; fi; done < /tmp/files')
[ -z "$left" ] || fail "removing tuicode left $left"
run "[ ! -e $owned ]" || fail "removing tuicode left $owned"
echo "$image: installed, upgraded, ran and removed cleanly"
