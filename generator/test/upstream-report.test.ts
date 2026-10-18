import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { diffIr } from '../src/diff.ts';
import type { Ir } from '../src/ir.ts';
import { classifyIrDiff, gitBlobSha, labelsFor, renderReport, suggestTag, trxCounts } from '../scripts/upstream-report.mjs';
import { syntheticIr } from './support/synthetic-ir.ts';

function pair() {
  const before = syntheticIr();
  before.types = {
    AssembleOptions: {
      kind: 'object',
      props: [
        { name: 'addTooltips', optional: true, type: { kind: 'boolean' } },
        { name: 'gapStyle', optional: true, type: { kind: 'literalUnion', values: ['rule', 'pad'] }, doc: 'Gap.' },
      ],
    },
  } as unknown as Ir['types'];
  const after = structuredClone(before);
  after.upstream.version = '0.6.0';
  return { before, after };
}

const severities = (before: Ir, after: Ir) =>
  Object.fromEntries(classifyIrDiff(before, after).map((r: { path: string; severity: string }) => [r.path, r.severity]));

describe('upstream-report: severity', () => {
  // source: companion part 1, Severity (BREAKING: template, channel, property, option value or semantic type removed;
  // an enum value removed; additive: anything new and optional; cosmetic: description or documentation text only)
  it('classifies catalog removals as BREAKING, additions as additive and labels as cosmetic', () => {
    const { before, after } = pair();
    const line = after.catalog.chartTypes[0].backends.vegalite!;
    line.channels = ['x', 'y', 'size'];
    line.properties[0].options!.pop();
    line.properties[0].label = 'Interpolation';
    after.catalog.semanticTypes = [];
    after.catalog.themePresets.push({ id: 'nyt', label: 'NYT', description: 'News.' });
    assert.deepEqual(severities(before, after), {
      'catalog.chartTypes[Line Chart].backends.vegalite.channels[color]': 'BREAKING',
      'catalog.chartTypes[Line Chart].backends.vegalite.channels[size]': 'additive',
      'catalog.chartTypes[Line Chart].backends.vegalite.properties[interpolate].label': 'cosmetic',
      'catalog.chartTypes[Line Chart].backends.vegalite.properties[interpolate].options[="monotone"]': 'BREAKING',
      'catalog.semanticTypes[YearMonth]': 'BREAKING',
      'catalog.themePresets[nyt]': 'additive',
    });
  });

  // source: companion part 1, Severity (behavioural: validation tightened without a type change; a property kind change
  // is a narrowed input type)
  it('classifies range changes as behavioural and a property kind change as BREAKING', () => {
    const { before, after } = pair();
    const props = after.catalog.chartTypes[0].backends.vegalite!.properties;
    props[2].max = 4;
    props[1].type = 'discrete';
    assert.deepEqual(severities(before, after), {
      'catalog.chartTypes[Line Chart].backends.vegalite.properties[showPoints].type': 'BREAKING',
      'catalog.chartTypes[Line Chart].backends.vegalite.properties[strokeWidth].max': 'behavioural',
    });
  });

  // source: companion part 1, Severity (input type narrowed; a property made required; an enum value removed are
  // BREAKING; a type widened and a new optional member are additive; documentation text is cosmetic)
  it('classifies input type changes', () => {
    const { before, after } = pair();
    const props = (after.types as unknown as Record<string, { props: { name: string; optional: boolean; type: { kind: string; values?: string[] }; doc?: string }[] }>)
      .AssembleOptions.props;
    props[0].optional = false;
    props[1].type.values = ['rule', 'gap'];
    props[1].doc = 'Gap style.';
    props.push({ name: 'legend', optional: true, type: { kind: 'boolean' } }, { name: 'mode', optional: false, type: { kind: 'string' } });
    assert.deepEqual(severities(before, after), {
      'types.AssembleOptions.props[addTooltips].optional': 'BREAKING',
      'types.AssembleOptions.props[gapStyle].doc': 'cosmetic',
      'types.AssembleOptions.props[gapStyle].type.values[gap]': 'additive',
      'types.AssembleOptions.props[gapStyle].type.values[pad]': 'BREAKING',
      'types.AssembleOptions.props[legend]': 'additive',
      'types.AssembleOptions.props[mode]': 'BREAKING',
    });
  });

  // source: companion part 1, Severity (a widened type is additive; a changed kind is a narrowed or different type)
  it('classifies an optional member made optional as additive and a kind change as BREAKING', () => {
    const { before, after } = pair();
    const types = after.types as unknown as Record<string, { props: { optional: boolean; type: { kind: string } }[] }>;
    (before.types as unknown as typeof types).AssembleOptions.props[0].optional = false;
    types.AssembleOptions.props[1].type.kind = 'string';
    delete (types.AssembleOptions.props[1].type as { values?: string[] }).values;
    const s = severities(before, after);
    assert.equal(s['types.AssembleOptions.props[addTooltips].optional'], 'additive');
    assert.equal(s['types.AssembleOptions.props[gapStyle].type.kind'], 'BREAKING');
  });

  // source: design §7.2 (ir-diff rows; the upstream block carries the version and integrity, not catalog changes)
  it('leaves the upstream version and integrity out', () => {
    const { before, after } = pair();
    after.upstream.integrity = 'sha512-other';
    assert.deepEqual(classifyIrDiff(before, after), []);
    assert.equal(diffIr(before, after).length, 2);
  });
});

describe('upstream-report: suggested tag', () => {
  const base = { prefix: 'v', version: '0.6.0', stableExists: false, breaking: false, m2Done: true, cleanSyncStreak: 0 };

  // source: companion part 1, "Suggested SDK tag" (X.Y.Z.1 when stable_exists && !breaking, or when !stable_exists &&
  // m2_done && clean_sync_streak >= 2; otherwise X.Y.Z.1-preview.1)
  it('suggests a preview until M2 is done and two syncs were clean', () => {
    assert.equal(suggestTag({ ...base, cleanSyncStreak: 1 }), 'v0.6.0.1-preview.1');
    assert.equal(suggestTag({ ...base, m2Done: false, cleanSyncStreak: 5 }), 'v0.6.0.1-preview.1');
    assert.equal(suggestTag({ ...base, cleanSyncStreak: 2 }), 'v0.6.0.1');
  });

  it('suggests stable after a stable release unless the sync is breaking', () => {
    assert.equal(suggestTag({ ...base, stableExists: true }), 'v0.6.0.1');
    assert.equal(suggestTag({ ...base, stableExists: true, breaking: true }), 'v0.6.0.1-preview.1');
    assert.equal(suggestTag({ ...base, stableExists: true, prefix: '' }), '0.6.0.1');
  });
});

describe('upstream-report: labels and inputs', () => {
  // source: companion part 1, Labels (upstream always; breaking; needs-attention when tests or generation failed or
  // flint-chart-mcp@<ver> is missing; upstream-major/minor/patch from the semver bump)
  it('computes the PR labels', () => {
    assert.deepEqual(labelsFor({ breaking: false, tests: 'success', generation: 'ok', mcpFallback: false, bump: 'minor' }), ['upstream', 'upstream-minor']);
    assert.deepEqual(labelsFor({ breaking: true, tests: 'failure', generation: 'ok', mcpFallback: false, bump: 'patch' }), [
      'upstream',
      'breaking',
      'needs-attention',
      'upstream-patch',
    ]);
    assert.deepEqual(labelsFor({ breaking: false, tests: 'success', generation: 'failed', mcpFallback: false, bump: 'major' }), ['upstream', 'needs-attention', 'upstream-major']);
    assert.deepEqual(labelsFor({ breaking: false, tests: 'success', generation: 'ok', mcpFallback: true, bump: 'patch' }), ['upstream', 'needs-attention', 'upstream-patch']);
  });

  // source: VSTest TRX format (ResultSummary/Counters attributes)
  it('reads the counters of a TRX file', () => {
    assert.deepEqual(trxCounts('<ResultSummary outcome="Failed"><Counters total="942" executed="942" passed="940" failed="2" error="0" /></ResultSummary>'), {
      total: 942,
      passed: 940,
      failed: 2,
    });
    assert.equal(trxCounts('<TestRun />'), null);
  });

  // source: git object format (sha1 of "blob <bytes>\0<content>"), the SKILL.md comparison of companion part 1
  it('computes the git blob SHA of a file', () => {
    assert.equal(gitBlobSha(''), 'e69de29bb2d1d6434b8b29ae775ad8c2e48c5391');
    assert.equal(gitBlobSha('hé\n'), createHash('sha1').update(Buffer.concat([Buffer.from('blob 4\0'), Buffer.from('hé\n')])).digest('hex'));
  });
});

describe('upstream-report: body', () => {
  const sha = (c: string) => c.repeat(40);
  const model = () => ({
    pin: '0.5.1',
    version: '0.6.0',
    published: '2026-11-02T10:14:00.000Z',
    gitHeadOld: sha('a'),
    gitHeadNew: sha('b'),
    upstreamRef: '0.6.0',
    mcp: { version: '0.6.0' },
    releaseNotes: true,
    skill: { old: sha('1'), new: sha('2') },
    generation: 'ok',
    generateLog: '',
    forward: { outcome: 'success', counts: { total: 942, passed: 940, failed: 2 } as { total: number; passed: number; failed: number } | null },
    tests: { dotnet: 'success', generator: 'success' },
    queued: ['0.6.1'],
    suggestedTag: 'v0.6.0.1-preview.1',
    stableTagForPin: null as string | null,
    rows: [
      { change: 'removed' as const, path: 'catalog.chartTypes[Line Chart].backends.vegalite.channels[color]', before: '"color"', severity: 'BREAKING' as const },
      { change: 'added' as const, path: 'catalog.themePresets[nyt]', after: '{…}', severity: 'additive' as const },
    ],
    retired: [{ path: 'catalog.chartTypes[Line Chart].backends.vegalite.channels[color]', lastSeenIn: '0.5.1', retiredIn: '0.6.0' }],
    stats: 'tests/fixtures: 3 files changed, 950 insertions(+)',
    changelog: '## 0.6.0\n\n- Thanks @someone for `x` and ```fenced``` text.' as string | undefined,
  });

  // source: companion part 1, "PR body template"
  it('renders the summary table, the severity summary, the rows and the after-merge commands', () => {
    const body = renderReport(model());
    assert.ok(body.startsWith('## flint-chart 0.5.1 -> 0.6.0\n'));
    for (const line of [
      '| Published | 2026-11-02 10:14 UTC |',
      `| Upstream compare | https://github.com/microsoft/flint-chart/compare/${sha('a')}...${sha('b')} |`,
      '| Provenance | verified by the gate: `npm audit signatures`, then repository microsoft/flint-chart, workflow `.github/workflows/npm-publish.yml`, gitCommit = gitHead |',
      '| flint-chart-mcp | 0.6.0 published; `mcp-commands` pins 0.6.0 |',
      '| Release notes | https://github.com/microsoft/flint-chart/blob/' + sha('b') + '/.github/release-notes/0.6.0.md |',
      `| Agent skill | changed upstream (${sha('1').slice(0, 8)} -> ${sha('2').slice(0, 8)}), https://github.com/microsoft/flint-chart/blob/0.6.0/agent-skills/flint-chart-author/SKILL.md |`,
      '| Generation | OK |',
      '| Forward-compat round-trip (old model, all stored fixture sets) | 940 / 942 |',
      '| Tests | dotnet test: PASS, generator node:test: PASS |',
      '| Queued after this | 0.6.1 |',
      '| Suggested SDK tag | `v0.6.0.1-preview.1` (suggestion only; the maintainer may force preview or stable at tag time; rule: design part 3 §8.1) |',
      '### Severity summary\nBREAKING 1 · behavioural 0 · additive 1 · cosmetic 0\n',
      '### Channels\n| Change | IR path | Before | After | Severity |\n|---|---|---|---|---|\n| removed | `catalog.chartTypes[Line Chart].backends.vegalite.channels[color]` | `"color"` |  | BREAKING |\n',
      '### Theme presets\n',
      '| `catalog.chartTypes[Line Chart].backends.vegalite.channels[color]` | 0.6.0 | 0.5.1 |',
      '### Fixtures and generated files\ntests/fixtures: 3 files changed, 950 insertions(+)\n',
      '1. Open the release pull request `release: v0.6.0.1-preview.1`',
      '2. `git tag v0.6.0.1-preview.1 <release PR merge sha> && git push origin v0.6.0.1-preview.1`',
    ]) {
      assert.ok(body.includes(line), `missing: ${line}\n---\n${body}`);
    }
    assert.ok(!body.includes('### Generation log'));
    assert.ok(!body.includes('### Input types'));
  });

  // source: GitHub Flavored Markdown code spans (a backtick inside needs a longer delimiter; a pipe needs escaping);
  // long documentation values are cut so the table stays readable
  it('quotes IR values with backticks or pipes and shortens long values', () => {
    const long = `"${'x'.repeat(200)}"`;
    const body = renderReport({
      ...model(),
      rows: [
        { change: 'changed' as const, path: 'types.A.props[b].doc', before: '"Use `flat` | `none`."', after: long, severity: 'cosmetic' as const },
      ],
    });
    assert.ok(body.includes('| changed | `types.A.props[b].doc` | `` "Use `flat` \\| `none`." `` | `"' + 'x'.repeat(99) + '…` | cosmetic |'), body);
  });

  // source: companion part 1, Security (registry and CHANGELOG text are written to the file as-is, never through a
  // shell expression); quoting keeps upstream text from mentioning people or closing the block
  it('quotes the upstream CHANGELOG and the generation log in fences longer than any backtick run inside', () => {
    const body = renderReport({ ...model(), generation: 'failed', generateLog: 'Error: collision at catalog.x\n```' });
    assert.ok(body.includes('| Generation | failed, see the log below |'));
    assert.ok(body.includes('### Generation log\n````text\nError: collision at catalog.x\n```\n````\n'));
    assert.ok(body.includes('### Upstream CHANGELOG 0.6.0\n````markdown\n## 0.6.0\n\n- Thanks @someone'));
  });

  // source: create-pull-request v8.1.1 src/main.ts (a body longer than 65536 characters is cut without notice in the pull
  // request), so the log is capped to its tail and comes after the after-merge steps (review D5-9)
  it('caps the generation log and keeps the after-merge steps', () => {
    const log = Array.from({ length: 5000 }, (_, i) => `line ${i} ${'x'.repeat(80)}`).join('\n');
    const body = renderReport({ ...model(), generation: 'failed', generateLog: log });
    assert.ok(body.length < 65536, `${body.length}`);
    assert.ok(body.includes('### After merge'));
    assert.ok(body.indexOf('### After merge') < body.indexOf('### Generation log'));
    assert.ok(body.includes('line 4999 '));
    assert.ok(!body.includes('line 0 '));
    assert.ok(body.includes('truncated; full log in artifact upstream-0.6.0/generate.log'));
    const short = renderReport({ ...model(), generation: 'failed', generateLog: 'one line' });
    assert.ok(!short.includes('truncated'));
  });

  // source: companion part 1 (untagged versions link the gitHead commit; flint-chart-mcp fallback is needs-attention;
  // the report prints the stable tag of a clean restarted line)
  it('renders the fallbacks and the restarted-line stable tag', () => {
    const body = renderReport({
      ...model(),
      upstreamRef: sha('b'),
      mcp: { version: '0.5.1' },
      releaseNotes: false,
      skill: { old: sha('1'), new: sha('1') },
      forward: { outcome: 'failure', counts: null },
      tests: { dotnet: 'failure', generator: 'success' },
      queued: [],
      stableTagForPin: 'v0.5.1.1',
      changelog: undefined,
    });
    for (const line of [
      '| flint-chart-mcp | missing; `mcp-commands` pins 0.5.1 (needs-attention) |',
      '| Release notes | none |',
      `| Agent skill | unchanged (${sha('1').slice(0, 8)}), https://github.com/microsoft/flint-chart/blob/${sha('b')}/agent-skills/flint-chart-author/SKILL.md |`,
      '| Forward-compat round-trip (old model, all stored fixture sets) | failed (no counts) |',
      '| Tests | dotnet test: FAIL, generator node:test: PASS |',
      '| Queued after this | none |',
      '| Stable tag for the current line | `v0.5.1.1` (the restarted 0.5.1 line had no generator fix since its sync) |',
      '### Upstream CHANGELOG 0.6.0\nNot found at the new gitHead.\n',
    ]) {
      assert.ok(body.includes(line), `missing: ${line}\n---\n${body}`);
    }
  });
});
