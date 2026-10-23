#!/usr/bin/env bash
# Usage: packaging/test-apt-repo.sh <image> <version>
# Follows the README's apt setup in a clean <image> container, waits for the repo to serve <version>,
# installs it and checks it starts. Fails on any apt warning. Needs docker.
set -euo pipefail

image=$1
version=$2
repo=https://mentaldesk.github.io/apt

container=$(docker run -d --rm "$image" sleep infinity)
trap 'docker rm -f "$container" >/dev/null' EXIT
run() { docker exec -e DEBIAN_FRONTEND=noninteractive "$container" sh -c "$1"; }
fail() { echo "::error::$image: $1"; exit 1; }

run 'apt-get update -qq && apt-get install -y -qq curl ca-certificates sudo >/dev/null'

run "curl -fsSL $repo/key.asc | sudo tee /etc/apt/keyrings/mentaldesk.asc >/dev/null"
run "echo 'deb [signed-by=/etc/apt/keyrings/mentaldesk.asc] $repo stable main' | sudo tee /etc/apt/sources.list.d/mentaldesk.list"

# GitHub Pages takes a few minutes to serve a fresh push.
for attempt in $(seq 30); do
  log=$(run 'sudo apt-get update 2>&1') || fail "apt update failed: $log"
  candidate=$(run "apt-cache policy tuicode | sed -n 's/^ *Candidate: //p'")
  [ "$candidate" = "$version" ] && break
  echo "Attempt $attempt: the repo offers '${candidate:-nothing}', waiting for $version"
  sleep 20
done
[ "$candidate" = "$version" ] || fail "the repo still offers '${candidate:-nothing}', expected $version"
if grep -E '^(W|E|Err):' <<<"$log"; then fail "apt update reported the warnings above"; fi

run 'sudo apt-get install -y -qq tuicode'
installed=$(run "dpkg-query -W -f='\${Version}' tuicode")
[ "$installed" = "$version" ] || fail "installed version is $installed, expected $version"
run 'tuicode --smoke-syntax'
echo "$image: installed tuicode $version from $repo"
