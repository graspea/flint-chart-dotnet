# Contributor notes for coding agents

This file is for agents working on this repository. Agents using the package read the README "For AI agents"
section, which the generator also writes to `docs/package/AGENTS.md` and packs at the nupkg root.

## Commands

- Build: `dotnet build -warnaserror`
- Test: `dotnet test -f net10.0` (CI also runs `net8.0`); fast loop without fixtures: `dotnet test -f net10.0 --filter Category!=Contract`
- `net8.0` locally: install the 8.0 SDK to a private folder (`dotnet-install.sh --channel 8.0 --install-dir <dir>`;
  `vstest` needs the SDK, not only the runtime), build with `dotnet build -f net8.0`, then run `DOTNET_ROOT=<dir> <dir>/dotnet vstest <absolute paths
  of the net8.0 test dlls>` from a directory without a `global.json` (such as `/tmp`); from the repository the 8.0 host
  honours `global.json` and stops with "A compatible .NET SDK was not found".
- Generator: `cd generator && npm ci --ignore-scripts`, then `npm test` (node:test), `npm run typecheck` and
  `npm run generate`; drift check: `npm run generate -- --check`
- Samples: `dotnet run --project samples/Graspea.Flint.Samples` writes `samples/out/*.json` (git-ignored) and exits 1 when a
  sample has an Error or a Warning; `cd tests/contract-node && npm test` then assembles every file, and the README
  quick-start JSON, with the generator's pinned `flint-chart` on each backend that has the template (needs the
  generator's `npm ci`).
- AOT: `dotnet publish samples/Graspea.Flint.Samples.Aot -c Release -p:PublishAot=true`, then run the binary from
  `bin/Release/net8.0/<rid>/publish/`; a trimming or AOT warning fails the publish.
- Pack: `dotnet pack src/Graspea.Flint -c Release -o artifacts/pack`, then `scripts/check-nupkg.sh artifacts/pack/*.nupkg`
  (fails unless the package has `README.md`, `icon.png`, `AGENTS.md`, `catalog/catalog.json` and `THIRD-PARTY-NOTICES.md`).
- Workflows (`.github/workflows/`): lint with `actionlint` (with `shellcheck` on the PATH) after any change; every
  action is pinned to a full commit SHA with its tag in a comment; no secrets in `ci.yml`; a new variable, secret or
  environment goes into `docs/maintainers.md` ("What the workflows need").
- Pipeline logic (version and tag checks, the upstream picker, the gate, the sync report, the sync path check) lives in
  `generator/scripts/*.mjs`, tested with `node:test` in `generator/test/`; workflow YAML only calls it.
- Install npm packages only with `--ignore-scripts`; pin every dependency exactly.

## Rules

- TDD: write the failing test first and see it fail, then implement, then refactor. Each test states the
  source of its expected value in a one-line comment (`// source: ...`). Never weaken a test to make it pass.
- Generated output is never edited by hand: `src/Graspea.Flint/Generated/**`, `generator/ir/flint-ir.json`,
  `tests/fixtures/**`, `docs/package/AGENTS.md` and the sections of `README.md` between
  `<!-- flint:generated:<id> start -->` and `<!-- flint:generated:<id> end -->`. Change the generator (test first in
  `generator/test/`) or `generator/overrides.json` and run `npm run generate`. Names are changed only through
  `overrides.json` `renames` (an enum member: `<Template>/enum/<key>/<value>`; a catalog constant such as a theme
  preset: `catalog.themePresets[<id>]`; see `docs/generator.md` "renames"; the wire value never changes).
- `generator/mcp-pin.json` holds the `flint-chart-mcp` version for the pinned `flint-chart`; it changes only together
  with the pin (`docs/generator.md`, "Documentation").
- `README.md` outside the markers is hand-written. The NuGet badge and the two `dotnet add package` lines are set in
  the release pull request (`docs/maintainers.md`), never by the generator.
- Every C# block in `README.md` is a `#region readme:<name>` in
  `tests/Graspea.Flint.Tests/Readme/ReadmeExamplesTests.cs`; change both together, or the test fails.
- Every public symbol is listed in `src/Graspea.Flint/PublicAPI.*.txt` (RS0016/RS0017 are errors).
- The shipped package has no runtime dependencies beyond the BCL.
- Versioning: `FLINT.MAJOR.FLINT.MINOR.FLINT.PATCH.OURS[-preview.N]`; the first three parts equal the pinned
  `flint-chart` version (`docs/versioning.md`). Maintainers push release tags by hand; agents never tag or publish.
- Design: `docs/design/00-index.md`; match its names exactly.

## Commits

- English, short imperative subject (`Add IR canonical writer`); one topic per commit.
- No co-author trailers or tool attribution.
