import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { outsideAllowlist, parseAllowlist } from '../scripts/check-sync-paths.mjs';

const allowlist = parseAllowlist('generator/package.json\ngenerator/ir/**\nsrc/**\ntests/fixtures/**\nREADME.md\n\n');

describe('check-sync-paths', () => {
  // source: review D5-1 (create-pull-request commits the whole index, so the patch is checked against the add-paths
  // allowlist before the action runs: an exact path, or a folder ending in /**)
  it('lists the changed paths outside the sync allowlist', () => {
    assert.deepEqual(allowlist, ['generator/package.json', 'generator/ir/**', 'src/**', 'tests/fixtures/**', 'README.md']);
    assert.deepEqual(
      outsideAllowlist(
        [
          'generator/package.json',
          'generator/ir/flint-ir.json',
          'src/Graspea.Flint/Generated/Enums/A.g.cs',
          'tests/fixtures/flint-chart@0.6.0/cases.jsonl',
          'README.md',
          'scripts/check-nupkg.sh',
          'generator/scripts/release-check.mjs',
          'tests/Graspea.Flint.Tests/X.cs',
          'README.md.bak',
          'srcx/a',
          'generator/package-lock.json',
        ],
        allowlist,
      ),
      ['scripts/check-nupkg.sh', 'generator/scripts/release-check.mjs', 'tests/Graspea.Flint.Tests/X.cs', 'README.md.bak', 'srcx/a', 'generator/package-lock.json'],
    );
  });

  it('accepts only exact paths and folder/** entries', () => {
    assert.throws(() => parseAllowlist('src/*.cs'), /src\/\*\.cs/);
  });
});
