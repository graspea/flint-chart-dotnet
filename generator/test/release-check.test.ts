import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  changelogSection,
  checkRelease,
  isLatestStable,
  readFlintUpstreamVersion,
  readTagPrefix,
  releaseNotes,
} from '../scripts/release-check.mjs';
import { bumpKind, compareVersions, parseReleaseTag } from '../scripts/versions.mjs';

describe('versions', () => {
  // source: design §8.1 (FLINT.MAJOR.FLINT.MINOR.FLINT.PATCH.OURS[-preview.N]; OURS starts at 1; tag prefix v)
  it('parses a preview tag and a stable tag', () => {
    assert.deepEqual(parseReleaseTag('v0.5.1.1-preview.1', 'v'), {
      version: '0.5.1.1-preview.1',
      upstream: '0.5.1',
      ours: 1,
      preview: 1,
      prerelease: true,
    });
    assert.deepEqual(parseReleaseTag('v1.12.0.3', 'v'), {
      version: '1.12.0.3',
      upstream: '1.12.0',
      ours: 3,
      preview: null,
      prerelease: false,
    });
  });

  // source: design §8.1 (fourth part starts at 1, NuGet drops a trailing .0; no beta, no rc; -preview.N only) and
  // NuGet version normalisation (leading zeros are dropped, so 0.5.01.1 would pack as another version)
  for (const tag of [
    '0.5.1.1',
    'v0.5.1',
    'v0.5.1.0',
    'v0.5.1.0-preview.1',
    'v0.5.1.1-preview.0',
    'v0.5.1.1-preview',
    'v0.5.1.1-beta.1',
    'v0.5.1.1-rc.1',
    'v0.5.01.1',
    'v0.5.1.01',
    'v0.5.1.1-preview.01',
    'v0.5.1.1.1',
    'v0.5.1.1-preview.1+build',
    'v0.5.1.1 ',
  ]) {
    it(`rejects ${JSON.stringify(tag)}`, () => {
      assert.throws(() => parseReleaseTag(tag, 'v'), /does not match/);
    });
  }

  // source: companion part 3 (the prefix lives in one env line; a change of style is a one-line change)
  it('honours another tag prefix', () => {
    assert.equal(parseReleaseTag('0.5.1.2', '').version, '0.5.1.2');
    assert.throws(() => parseReleaseTag('v0.5.1.2', ''), /does not match/);
  });

  // source: semver precedence of X.Y.Z (numeric per part)
  it('compares upstream versions numerically', () => {
    assert.equal(compareVersions('0.5.1', '0.5.1'), 0);
    assert.ok(compareVersions('0.10.0', '0.9.9') > 0);
    assert.ok(compareVersions('0.4.2', '0.5.0') < 0);
    assert.ok(compareVersions('1.0.0', '0.99.99') > 0);
  });

  // source: companion part 1, Labels (upstream-major/minor/patch from the upstream semver bump)
  it('names the upstream bump', () => {
    assert.equal(bumpKind('0.5.1', '0.5.2'), 'patch');
    assert.equal(bumpKind('0.5.1', '0.6.0'), 'minor');
    assert.equal(bumpKind('0.5.1', '1.0.0'), 'major');
  });
});

describe('release-check', () => {
  const inputs = { prefix: 'v', flintUpstream: '0.5.1', generatorPin: '0.5.1' };

  // source: design §9.6 step 4 (the first three parts equal FlintUpstream.Version; the drift check keeps the
  // generator pin equal to it, and the release refuses either mismatch)
  it('accepts a tag whose first three parts equal FlintUpstream.Version and the generator pin', () => {
    assert.deepEqual(checkRelease({ ...inputs, tag: 'v0.5.1.1-preview.1' }), {
      version: '0.5.1.1-preview.1',
      upstream: '0.5.1',
      prerelease: true,
    });
  });

  it('rejects a tag for another upstream version', () => {
    assert.throws(
      () => checkRelease({ ...inputs, tag: 'v0.5.2.1' }),
      /tag v0\.5\.2\.1 is for flint-chart 0\.5\.2, but FlintUpstream\.Version is 0\.5\.1/,
    );
  });

  it('rejects a generator pin that differs from FlintUpstream.Version', () => {
    assert.throws(
      () => checkRelease({ ...inputs, generatorPin: '0.5.2', tag: 'v0.5.1.1' }),
      /generator\/package\.json pins flint-chart 0\.5\.2, but FlintUpstream\.Version is 0\.5\.1/,
    );
  });

  it('rejects a tag outside the grammar', () => {
    assert.throws(() => checkRelease({ ...inputs, tag: 'v0.5.1.0' }), /does not match/);
  });

  // source: design §9.6 step 4 (TAG_PREFIX is one env line of release.yml, read by the sync report too)
  it('reads the tag prefix from release.yml', () => {
    assert.equal(readTagPrefix('on:\n  push:\nenv:\n  TAG_PREFIX: v\n  OTHER: x\n'), 'v');
    assert.equal(readTagPrefix("env:\n  TAG_PREFIX: 'v'   # one line\n"), 'v');
    assert.throws(() => readTagPrefix('env:\n  OTHER: x\n'), /TAG_PREFIX/);
    assert.equal(readTagPrefix(readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8')), 'v');
  });

  // source: review D5-14 (TAG_PREFIX is the single source of the prefix: the on.push.tags filter must be <TAG_PREFIX>*, or
  // the tags the sync report suggests never start a release)
  it('triggers release.yml on tags with the TAG_PREFIX prefix', () => {
    const yml = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
    assert.match(yml, new RegExp(`tags: \\['${readTagPrefix(yml)}\\*'\\]`));
  });

  // source: GitHub REST "Create a release" (make_latest defaults to true for a published release) and design part 3 §9.6
  // step 5 (a release of an older line, tagged on an older commit of main, must not become Latest)
  it('tells whether a stable tag is the highest stable release', () => {
    const tags = ['v0.5.1.1-preview.1', 'v0.5.1.1', 'v0.5.1.2', 'v0.6.0.1', 'v0.6.0.2-preview.1', 'other'];
    assert.equal(isLatestStable('v0.5.1.3', [...tags, 'v0.5.1.3'], 'v'), false);
    assert.equal(isLatestStable('v0.6.0.2', [...tags, 'v0.6.0.2'], 'v'), true);
    assert.equal(isLatestStable('v0.6.0.1', tags, 'v'), true);
    assert.equal(isLatestStable('v0.10.0.1', [...tags, 'v0.10.0.1'], 'v'), true);
    assert.equal(isLatestStable('v0.7.0.1-preview.1', tags, 'v'), false);
  });

  // source: src/Graspea.Flint/Generated/Constants/FlintUpstream.g.cs (the generated constant)
  it('reads FlintUpstream.Version from the generated C#', () => {
    assert.equal(readFlintUpstreamVersion('public static partial class FlintUpstream\n{\n    public const string Version = "0.5.1";\n}\n'), '0.5.1');
    assert.throws(() => readFlintUpstreamVersion('class X {}'), /FlintUpstream\.Version/);
  });

  const changelog =
    '# Changelog\n\nIntro.\n\n## [Unreleased]\n\n- Next.\n\n## [0.5.1.1-preview.2] - 2026-11-01\n\n### Fixed\n\n- A fix.\n\n' +
    '## [0.5.1.1-preview.1] - 2026-10-20\n\n### Added\n\n- First.\n\n[0.5.1.1-preview.1]: https://example.invalid\n';

  // source: design §9.6 step 4 (the GitHub release body is the CHANGELOG entry; Keep a Changelog headings)
  it('extracts the CHANGELOG entry of a version', () => {
    assert.equal(changelogSection(changelog, '0.5.1.1-preview.2'), '### Fixed\n\n- A fix.');
    assert.equal(changelogSection(changelog, '0.5.1.1-preview.1'), '### Added\n\n- First.');
  });

  it('fails when the CHANGELOG has no entry for the version', () => {
    assert.throws(() => changelogSection(changelog, '0.5.1.1'), /CHANGELOG\.md has no "## \[0\.5\.1\.1\]" entry/);
    assert.throws(() => changelogSection('## [0.5.1.1]\n\n## [0.5.1.0]\n', '0.5.1.1'), /entry for 0\.5\.1\.1 is empty/);
  });

  // source: companion part 3, "Release title, body and tag format" (title "Flint.NET <ver> (flint-chart X.Y.Z)"; body
  // starts with "Tracks flint-chart X.Y.Z (npm)." linking the upstream release, or the gitHead commit when untagged)
  it('builds the release title and body', () => {
    assert.deepEqual(releaseNotes({ version: '0.5.1.1-preview.1', upstream: '0.5.1', upstreamRef: '0.5.1', entry: '### Added\n\n- First.' }), {
      title: 'Flint.NET 0.5.1.1-preview.1 (flint-chart 0.5.1)',
      body: 'Tracks [flint-chart 0.5.1](https://github.com/microsoft/flint-chart/releases/tag/0.5.1) (npm).\n\n### Added\n\n- First.\n',
    });
    const sha = '84a8e8d044f47440e08da2059fc5d48935600da0';
    assert.match(
      releaseNotes({ version: '0.5.0.1', upstream: '0.5.0', upstreamRef: sha, entry: '- x' }).body,
      new RegExp(`^Tracks \\[flint-chart 0\\.5\\.0\\]\\(https://github\\.com/microsoft/flint-chart/tree/${sha}\\) \\(npm\\)\\.\n`),
    );
  });
});
