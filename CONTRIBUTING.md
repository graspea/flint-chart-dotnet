# Contributing

Thank you for helping. Bug reports, questions and pull requests are welcome; please open an issue before a large change. Security problems go through [SECURITY.md](SECURITY.md), not public issues.

Coding agents also read [AGENTS.md](AGENTS.md), which lists the same rules in short form.

## Setup

- .NET SDK 10 (pinned in `global.json`); the library targets `net8.0`, the tests `net10.0` and `net8.0`.
- Node.js 24 (`.nvmrc`), for the generator and the contract tests.

```
dotnet build -warnaserror
dotnet test -f net10.0
cd generator && npm ci --ignore-scripts && npm test
```

`dotnet test -f net10.0 --filter Category!=Contract` skips the fixture corpus and runs in seconds. CI also runs the `net8.0` tests, the samples, the Native AOT sample and the contract tests against the pinned `flint-chart`; [AGENTS.md](AGENTS.md) lists those commands.

## Rules

**Tests first.** Every change in behaviour starts with a test that fails for the stated reason; then the smallest change that makes it pass; then the refactoring. Each test names the source of its expected value in a one-line comment (`// source: d.ts T:2949`, `// source: ir/flint-ir.json`, `// source: design §5.2`); an expected value never comes from the code under test. A test is never weakened to make it pass. C# tests use NUnit, generator and contract tests use `node:test`.

**Generated code is never edited by hand.** These files are written by the generator:

- `src/Graspea.Flint/Generated/**`
- `generator/ir/flint-ir.json`
- `tests/fixtures/**`
- `docs/package/AGENTS.md`
- the sections of `README.md` between `<!-- flint:generated:<id> start -->` and `<!-- flint:generated:<id> end -->`
- the generated lines of `src/Graspea.Flint/PublicAPI.Unshipped.txt`

To change them, change the generator (`generator/src/**`, with a test in `generator/test/` first) or its one hand-maintained input, `generator/overrides.json` (a rename, a type mapping, a doc correction), and regenerate:

```
cd generator
npm run generate            # extract, emit, fixtures
npm run generate -- --check # what CI runs: fails when a committed file differs from a fresh run
```

`generate` never installs anything. Moving to a new `flint-chart` version is a separate step, [docs/generator.md](docs/generator.md) describes it, and a scheduled workflow normally does it.

**Public API.** Every public symbol is listed in `src/Graspea.Flint/PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt`; the build fails otherwise. Add new hand-written members to `PublicAPI.Unshipped.txt`. The package has no runtime dependency beyond the .NET base class library.

**Documentation.** The README and `docs/` are written for people and for AI coding agents. Every C# block in `README.md` is also a `#region readme:<name>` in `tests/Graspea.Flint.Tests/Readme/ReadmeExamplesTests.cs`, so a README example that stops compiling or changes its output fails the build. Keep code samples and their regions identical.

**npm.** Install packages with `--ignore-scripts` and pin every dependency to an exact version.

## Commits and pull requests

- English, a short imperative subject (`Add the docs emitter`), one topic per commit.
- No co-author trailers and no tool attribution.
- A pull request describes what changed and why, and passes CI: generated files in sync, `dotnet build -warnaserror`, all tests, the samples and the contract tests.

## Releases

Maintainers cut releases; [docs/maintainers.md](docs/maintainers.md) has the checklist and [docs/versioning.md](docs/versioning.md) the version rules.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).
