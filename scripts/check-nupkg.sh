#!/usr/bin/env sh
# Fails unless every given .nupkg carries the files agents and nuget.org read: README.md, icon.png, AGENTS.md and
# catalog/catalog.json (design part 3 §9.6, companion part 2 §2), plus THIRD-PARTY-NOTICES.md (the flint-chart MIT
# notice for the material derived from it), and declares no package dependencies (the package references only the BCL).
# With --tool, checks the flint-cs tool package instead (design §12.9 step 1): the DotnetTool package type, the
# flint-cs command, rollForward Major, Graspea.Flint.dll of the package's own version and no other assembly but
# flint-cs.dll, README.md, icon.png, the license and THIRD-PARTY-NOTICES.md, and no package dependencies.
# With --generators, checks the source generator package (design §12.9 step 1): the analyzer under
# analyzers/dotnet/roslyn4.14/cs/ and nothing else under analyzers/, no Microsoft.CodeAnalysis or System.Text.Json file,
# build/Graspea.Flint.Generators.targets and no buildTransitive/, lib/net8.0/_._ and nothing else under lib/ (no run-time
# assembly reaches a consumer), exactly one dependency, Graspea.Flint
# at [<version>], not a development dependency, README.md, icon.png, the license and THIRD-PARTY-NOTICES.md.
# Usage: scripts/check-nupkg.sh [--tool | --generators] <file.nupkg>...
set -eu
kind=library
case "${1:-}" in
  --tool) kind=tool; shift ;;
  --generators) kind=generators; shift ;;
esac
[ "$#" -gt 0 ] || { echo "usage: $0 [--tool | --generators] <file.nupkg>..." >&2; exit 2; }
for pkg in "$@"; do
  entries=$(unzip -Z1 "$pkg")
  nuspec=$(unzip -p "$pkg" '*.nuspec')
  case "$kind" in
    library) required="README.md icon.png AGENTS.md catalog/catalog.json THIRD-PARTY-NOTICES.md" ;;
    tool) required="README.md icon.png THIRD-PARTY-NOTICES.md tools/net8.0/any/DotnetToolSettings.xml tools/net8.0/any/flint-cs.dll tools/net8.0/any/flint-cs.runtimeconfig.json tools/net8.0/any/Graspea.Flint.dll" ;;
    *) required="README.md icon.png THIRD-PARTY-NOTICES.md analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll build/Graspea.Flint.Generators.targets lib/net8.0/_._" ;;
  esac
  for entry in $required; do
    printf '%s\n' "$entries" | grep -qx "$entry" || { echo "$pkg: missing $entry" >&2; exit 1; }
  done
  if [ "$kind" = generators ]; then
    version=$(printf '%s\n' "$nuspec" | sed -n 's:.*<version>\(.*\)</version>.*:\1:p')
    analyzers=$(printf '%s\n' "$entries" | grep '^analyzers/' | grep -vx 'analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll' || true)
    [ -z "$analyzers" ] || { echo "$pkg: other files under analyzers/:" >&2; echo "$analyzers" >&2; exit 1; }
    others=$(printf '%s\n' "$entries" | grep '^lib/' | grep -vx 'lib/net8.0/_._' || true)
    [ -z "$others" ] || { echo "$pkg: other files under lib/:" >&2; echo "$others" >&2; exit 1; }
    leaked=$(printf '%s\n' "$entries" | grep -E '(^|/)(Microsoft\.CodeAnalysis|System\.Text\.Json)[^/]*$' || true)
    [ -z "$leaked" ] || { echo "$pkg: compiler or System.Text.Json files:" >&2; echo "$leaked" >&2; exit 1; }
    if printf '%s\n' "$entries" | grep -q '^buildTransitive/'; then
      echo "$pkg: has a buildTransitive/ folder" >&2
      exit 1
    fi
    dependencies=$(printf '%s\n' "$nuspec" | grep -c '<dependency ' || true)
    [ "$dependencies" = 1 ] || { echo "$pkg: expected one dependency, found $dependencies" >&2; exit 1; }
    printf '%s\n' "$nuspec" | grep -qF "<dependency id=\"Graspea.Flint\" version=\"[$version]\"" || { echo "$pkg: the dependency is not Graspea.Flint [$version]" >&2; exit 1; }
    if printf '%s\n' "$nuspec" | grep -q '<developmentDependency>'; then
      echo "$pkg: is a development dependency" >&2
      exit 1
    fi
    printf '%s\n' "$nuspec" | grep -q '<license type="expression">MIT</license>' || { echo "$pkg: no MIT license expression" >&2; exit 1; }
    echo "$pkg: analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll only, build/ targets, lib/net8.0/_._, Graspea.Flint [$version]; README.md, icon.png, license and THIRD-PARTY-NOTICES.md present"
    continue
  fi
  if printf '%s\n' "$nuspec" | grep -q '<dependency '; then
    echo "$pkg: declares package dependencies:" >&2
    printf '%s\n' "$nuspec" | grep '<dependency ' >&2
    exit 1
  fi
  if [ "$kind" = library ]; then
    echo "$pkg: README.md, icon.png, AGENTS.md, catalog/catalog.json and THIRD-PARTY-NOTICES.md present; no dependencies"
    continue
  fi
  printf '%s\n' "$nuspec" | grep -q '<packageType name="DotnetTool" />' || { echo "$pkg: the package type is not DotnetTool" >&2; exit 1; }
  printf '%s\n' "$nuspec" | grep -q '<license type="expression">MIT</license>' || { echo "$pkg: no MIT license expression" >&2; exit 1; }
  unzip -p "$pkg" tools/net8.0/any/DotnetToolSettings.xml | grep -q 'Command Name="flint-cs"' || { echo "$pkg: the command is not flint-cs" >&2; exit 1; }
  unzip -p "$pkg" tools/net8.0/any/flint-cs.runtimeconfig.json | grep -q '"rollForward": "Major"' || { echo "$pkg: rollForward is not Major" >&2; exit 1; }
  others=$(printf '%s\n' "$entries" | grep '\.dll$' | grep -vx 'tools/net8.0/any/flint-cs.dll' | grep -vx 'tools/net8.0/any/Graspea.Flint.dll' || true)
  [ -z "$others" ] || { echo "$pkg: assemblies other than flint-cs.dll and Graspea.Flint.dll:" >&2; echo "$others" >&2; exit 1; }
  version=$(printf '%s\n' "$nuspec" | sed -n 's:.*<version>\(.*\)</version>.*:\1:p')
  literal=$(printf '%s' "$version" | sed 's/[].[^$*+?(){}|\\]/\\&/g')
  unzip -p "$pkg" tools/net8.0/any/Graspea.Flint.dll | strings | grep -Eq "$literal(\+[0-9a-f]+)?$" || { echo "$pkg: Graspea.Flint.dll is not version $version" >&2; exit 1; }
  echo "$pkg: DotnetTool flint-cs, rollForward Major, Graspea.Flint.dll $version and no other assembly; README.md, icon.png, license and THIRD-PARTY-NOTICES.md present; no dependencies"
done
