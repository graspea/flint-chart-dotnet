import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// The package set of release.yml and ci.yml (design part 7 §12.9): the three packages are packed with the tag's version
// only in the approved publish job, each is checked by its kind, attested and pushed through Trusted Publishing.
const release = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
const ci = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');

const packages = [
  { id: 'Graspea.Flint', project: 'src/Graspea.Flint', kind: '', symbols: true },
  { id: 'Graspea.Flint.Tool', project: 'src/Graspea.Flint.Tool', kind: '--tool ', symbols: true },
  { id: 'Graspea.Flint.Generators', project: 'src/Graspea.Flint.Generators', kind: '--generators ', symbols: false },
];

/** The text of one job: from its `  <name>:` line to the next job, or to the end. */
function job(yml: string, name: string): string {
  const start = yml.indexOf(`\n  ${name}:\n`);
  assert.ok(start >= 0, `no job ${name}`);
  const next = yml.slice(start + 1).search(/\n {2}[a-z-]+:\n/);
  return next < 0 ? yml.slice(start) : yml.slice(start, start + 1 + next);
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** Positions of the matches, in the order of `packages`; fails when one is missing. */
function positions(text: string, pattern: (p: (typeof packages)[number]) => RegExp): number[] {
  return packages.map((p) => {
    const match = pattern(p).exec(text);
    assert.ok(match, `${p.id}: no match for ${pattern(p)}`);
    return match.index;
  });
}

function ascending(values: number[]): boolean {
  return values.every((v, i) => i === 0 || values[i - 1] < v);
}

describe('release.yml: package set', () => {
  const publish = job(release, 'publish');

  // source: design §12.9 step 1 (the publish job packs the three projects with the tag's version, the library first)
  it('packs every package with the tag version, the library first', () => {
    const packs = positions(publish, (p) => new RegExp(`dotnet pack ${escape(p.project)} -c Release -p:Version="\\$VERSION" -o artifacts/pack\\n`));
    assert.ok(ascending(packs), 'pack order: library, tool, generators');
  });

  // source: design part 3 §9.6 (only the job that waits for the nuget approval produces packages; the tests in job ci
  // run without secrets and their output is never published)
  it('packs only in the approved publish job', () => {
    assert.match(publish, /\n {4}environment: nuget\n/);
    assert.match(publish, /\n {6}id-token: write\n/);
    const outside = release.replace(publish, '');
    assert.doesNotMatch(outside, /dotnet pack/);
    assert.doesNotMatch(outside, /dotnet nuget push/);
    assert.doesNotMatch(outside, /attest-build-provenance/);
  });

  // source: design §12.9 step 1 (scripts/check-nupkg.sh per package kind, on the files of the tag's version)
  it('checks each package by its kind, after packing', () => {
    const lastPack = publish.lastIndexOf('dotnet pack');
    const checks = positions(publish, (p) => new RegExp(`scripts/check-nupkg\\.sh ${escape(p.kind)}"artifacts/pack/${escape(p.id)}\\.\\$VERSION\\.nupkg"`));
    assert.ok(checks.every((c) => c > lastPack));
    for (const p of packages) {
      assert.match(publish, new RegExp(`test -f "artifacts/pack/${escape(p.id)}\\.\\$VERSION\\.nupkg"`), p.id);
      if (p.symbols) assert.match(publish, new RegExp(`test -f "artifacts/pack/${escape(p.id)}\\.\\$VERSION\\.snupkg"`), p.id);
    }
  });

  // source: design §12.9 step 2 (build provenance for every .nupkg and .snupkg of the three packages, before the push)
  it('attests every package file before the first push', () => {
    const attest = publish.indexOf('actions/attest-build-provenance');
    assert.ok(attest > 0);
    const subjects = publish.slice(attest, publish.indexOf('\n      - ', attest + 1) + 1);
    for (const p of packages) {
      assert.match(subjects, new RegExp(`artifacts/pack/${escape(p.id)}\\.\\$\\{\\{ env\\.VERSION \\}\\}\\.nupkg\\n`), p.id);
      const snupkg = new RegExp(`artifacts/pack/${escape(p.id)}\\.\\$\\{\\{ env\\.VERSION \\}\\}\\.snupkg\\n`);
      if (p.symbols) assert.match(subjects, snupkg, p.id);
      else assert.doesNotMatch(subjects, snupkg, p.id);
    }
    assert.ok(attest < publish.indexOf('dotnet nuget push'));
  });

  // source: design §12.9 steps 3 and 4 (push order library, tool, generators, each with --skip-duplicate, through the
  // Trusted Publishing key of NuGet/login)
  it('pushes every package through Trusted Publishing, library first, each with --skip-duplicate', () => {
    const login = publish.indexOf('NuGet/login@');
    assert.ok(login > 0);
    const pushes = positions(
      publish,
      (p) =>
        new RegExp(
          `dotnet nuget push "artifacts/pack/${escape(p.id)}\\.\\$VERSION\\.nupkg" --api-key "\\$NUGET_API_KEY" --source https://api\\.nuget\\.org/v3/index\\.json --skip-duplicate\\n`,
        ),
    );
    assert.ok(ascending(pushes), 'push order: library, tool, generators');
    assert.ok(pushes[0] > login);
    assert.doesNotMatch(release, /secrets\./);
  });

  // source: design §12.9 step 2 (the GitHub release attaches the attested files, as uploaded by the publish job) and
  // docs/maintainers.md (a re-run packs and attests again: each attempt uploads an artifact of its own, as an artifact
  // name of a run cannot be uploaded twice, and the release attaches the files of the attempt whose publish job ran)
  it('attaches every attested file of the same attempt to the GitHub release', () => {
    assert.match(publish, /\n {4}outputs:\n {6}artifact: package-\$\{\{ github\.run_attempt \}\}\n/);
    assert.match(publish, /name: package-\$\{\{ github\.run_attempt \}\}\n {10}path: artifacts\/pack\/\n/);
    const githubRelease = job(release, 'github-release');
    assert.match(githubRelease, /name: \$\{\{ needs\.publish\.outputs\.artifact \}\}\n {10}path: package\n/);
    assert.match(githubRelease, /package\/\*\.nupkg package\/\*\.snupkg/);
  });
});

describe('ci.yml: package set', () => {
  // source: design §12.9 step 6 (CI builds, tests and packs the three packages and checks each by its kind)
  it('packs and checks every package', () => {
    const dotnet = job(ci, 'dotnet');
    assert.match(dotnet, /\n {6}- run: dotnet build -warnaserror\n/);
    assert.match(dotnet, /\n {6}- run: dotnet test --no-build\n/);
    for (const p of packages) {
      const folder = p.kind ? `artifacts/pack-${p.kind.slice(2, -1)}` : 'artifacts/pack';
      assert.match(dotnet, new RegExp(`dotnet pack ${escape(p.project)} -c Release -o ${escape(folder)}\\n`), p.id);
      assert.match(dotnet, new RegExp(`scripts/check-nupkg\\.sh ${escape(p.kind)}${escape(folder)}/\\*\\.nupkg\\n`), p.id);
    }
  });
});
