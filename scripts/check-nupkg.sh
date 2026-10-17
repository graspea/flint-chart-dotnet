#!/usr/bin/env sh
# Fails unless every given .nupkg carries the files agents and nuget.org read: README.md, icon.png, AGENTS.md and
# catalog/catalog.json (design part 3 §9.6, companion part 2 §2), plus THIRD-PARTY-NOTICES.md (the flint-chart MIT
# notice for the material derived from it), and declares no package dependencies (the package references only the BCL). Usage: scripts/check-nupkg.sh <file.nupkg>...
set -eu
[ "$#" -gt 0 ] || { echo "usage: $0 <file.nupkg>..." >&2; exit 2; }
for pkg in "$@"; do
  entries=$(unzip -Z1 "$pkg")
  for entry in README.md icon.png AGENTS.md catalog/catalog.json THIRD-PARTY-NOTICES.md; do
    printf '%s\n' "$entries" | grep -qx "$entry" || { echo "$pkg: missing $entry" >&2; exit 1; }
  done
  if unzip -p "$pkg" '*.nuspec' | grep -q '<dependency '; then
    echo "$pkg: declares package dependencies:" >&2
    unzip -p "$pkg" '*.nuspec' | grep '<dependency ' >&2
    exit 1
  fi
  echo "$pkg: README.md, icon.png, AGENTS.md, catalog/catalog.json and THIRD-PARTY-NOTICES.md present; no dependencies"
done
