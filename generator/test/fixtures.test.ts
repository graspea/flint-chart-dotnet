import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const generatorDir = new URL('..', import.meta.url).pathname;

const fixturesArgs = (out: string, ...extra: string[]) => ['--import', 'tsx', 'src/cli.ts', 'fixtures', '--out', out, ...extra];
const childOptions = { cwd: generatorDir, env: { ...process.env, TZ: 'Europe/Prague' } };

/** Runs `npm run fixtures` into a fresh directory (a fresh process each time: the seed and TZ are process-wide). */
function runFixtures(): string {
  const out = mkdtempSync(join(tmpdir(), 'flint-fixtures-'));
  execFileSync(process.execPath, fixturesArgs(out), { ...childOptions, stdio: 'pipe' });
  return out;
}

function folderHash(dir: string): string {
  const hash = createHash('sha256');
  for (const name of readdirSync(dir, { recursive: true, withFileTypes: true })
    .filter((e) => e.isFile())
    .map((e) => join(e.parentPath, e.name))
    .sort()) {
    hash.update(name.slice(dir.length)).update(readFileSync(name));
  }
  return hash.digest('hex');
}

// Runs against the installed flint-chart@0.5.1.
describe('extract/fixtures', () => {
  let first: string;
  let second: string;
  let folder: string;
  const lines = (file: string) =>
    readFileSync(join(folder, file), 'utf8')
      .trimEnd()
      .split('\n')
      .map((l) => JSON.parse(l));

  before(() => {
    first = runFixtures();
    second = runFixtures();
    folder = join(first, 'flint-chart@0.5.1');
  });

  after(() => {
    for (const dir of [first, second]) if (dir) rmSync(dir, { recursive: true, force: true });
  });

  // source: design §10.3 generator determinism (two fixture runs byte-identical, even from a non-UTC shell)
  it('writes byte-identical output on two runs', () => {
    assert.equal(folderHash(first), folderHash(second));
  });

  // source: design §7.7 (--check writes nothing and fails on drift)
  it('passes --check on its own output and fails it on an empty folder without writing', () => {
    assert.equal(spawnSync(process.execPath, fixturesArgs(first, '--check'), childOptions).status, 0);
    const empty = mkdtempSync(join(tmpdir(), 'flint-fixtures-'));
    try {
      const run = spawnSync(process.execPath, fixturesArgs(empty, '--check'), { ...childOptions, encoding: 'utf8' });
      assert.equal(run.status, 1);
      assert.match(run.stderr, /cases\.jsonl/);
      assert.equal(existsSync(join(empty, 'flint-chart@0.5.1')), false);
    } finally {
      rmSync(empty, { recursive: true });
    }
  });

  // source: design §10.2 (cases.jsonl, expectations.jsonl, manifest.json, known-failures.txt) and review D2-3 (the
  // crafted cases in probe-cases.jsonl and probe-expectations.jsonl)
  it('writes the six files of the version folder', () => {
    assert.deepEqual(readdirSync(folder).sort(), [
      'cases.jsonl',
      'expectations.jsonl',
      'known-failures.txt',
      'manifest.json',
      'probe-cases.jsonl',
      'probe-expectations.jsonl',
    ]);
    assert.equal(readFileSync(join(folder, 'known-failures.txt'), 'utf8'), '');
  });

  // source: design §10.3 fixture formats (manifest) and part 01 §2 (934 TestCases)
  it('records the manifest', () => {
    const manifest = JSON.parse(readFileSync(join(folder, 'manifest.json'), 'utf8'));
    const lock = JSON.parse(readFileSync(join(generatorDir, 'package-lock.json'), 'utf8'));
    assert.deepEqual(manifest, {
      cases: 934,
      excludedNondeterministic: [],
      integrity: lock.packages['node_modules/flint-chart'].integrity,
      probeCases: 38,
      seed: 42,
      tz: 'UTC',
      upstream: '0.5.1',
    });
  });

  // source: design §10.4 test 6 (Semantic Context #0 is a Bar Chart over city and revenue), §10.3 (id format) and §10.1
  // rule 5 (fixture tests are named <group>|<title>, so each case carries its title)
  it('maps Semantic Context #0', () => {
    const c = lines('cases.jsonl').find((l) => l.id === 'Semantic Context|#0');
    assert.equal(c.chartType, 'Bar Chart');
    assert.equal(typeof c.title, 'string');
    assert.equal(c.defaultBackend, 'vegalite');
    assert.deepEqual(Object.keys(c.input.data.values[0]), ['city', 'revenue']);
    assert.equal(c.input.chart_spec.encodings.x.field, 'city');
    assert.equal(c.input.chart_spec.encodings.y.field, 'revenue');
  });

  // source: design §10.1 rule 5 (<group>|<title> names a fixture test, so it must be unique)
  it('gives every case a unique group and title', () => {
    const names = lines('cases.jsonl').map((l) => `${l.id.slice(0, l.id.lastIndexOf('|#'))}|${l.title}`);
    assert.equal(new Set(names).size, names.length);
  });

  // source: part 01 §2 Fixtures (no throw on vegalite 886, echarts 866, chartjs 743, plotly 894; Excel throws on 219 of 695)
  it('records the per-backend outcomes', () => {
    const expectations = lines('expectations.jsonl');
    assert.equal(expectations.length, 934);
    const count = (b: string, pick: (o: { throws: boolean }) => boolean) =>
      expectations.filter((e) => e.backends[b] && pick(e.backends[b])).length;
    const applicable = Object.fromEntries(['vegalite', 'echarts', 'chartjs', 'plotly', 'excel'].map((b) => [b, count(b, () => true)]));
    const throws = Object.fromEntries(['vegalite', 'echarts', 'chartjs', 'plotly', 'excel'].map((b) => [b, count(b, (o) => o.throws)]));
    assert.deepEqual(applicable, { vegalite: 886, echarts: 866, chartjs: 743, plotly: 894, excel: 695 });
    assert.deepEqual(throws, { vegalite: 0, echarts: 0, chartjs: 0, plotly: 0, excel: 219 });
  });

  // source: src/extract/probe-cases.ts (one case per rule; a case without the template on a backend is recorded there
  // too) and verify/probes.json (the layout backends throw on a static-series rule, Excel on every static series)
  it('records the crafted cases on every backend that has the template', () => {
    const cases = lines('probe-cases.jsonl');
    const expectations = lines('probe-expectations.jsonl');
    assert.equal(cases.length, 38);
    assert.deepEqual(expectations.map((e) => e.id), cases.map((c) => c.id));
    assert.equal(new Set(cases.map((c) => c.title)).size, cases.length);
    assert.ok(cases.every((c, i) => c.id === `Probe|#${i}`));
    const outcome = (title: string) => expectations[cases.findIndex((c) => c.title === title)].backends;
    assert.deepEqual(Object.keys(outcome('chart type unknown')), ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel']);
    assert.deepEqual(
      Object.entries(outcome('static series with one entry')).filter(([, o]) => (o as { throws: boolean }).throws).map(([b]) => b),
      ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel'],
    );
    assert.deepEqual(outcome('option array value').vegalite, { throws: false, warnings: ['invalid-option-value'] });
  });

  // source: design §13.3.1 (an absent data is expected to throw like data: null, from input.data.values on undefined;
  // the case backs data_missing for a document without data)
  it('records an absent data as a throw on every backend', () => {
    const cases = lines('probe-cases.jsonl');
    const expectations = lines('probe-expectations.jsonl');
    const index = cases.findIndex((c) => c.title === 'data absent');
    assert.equal('data' in cases[index].input, false);
    const backends = expectations[index].backends as Record<string, { throws: boolean; error?: string }>;
    assert.deepEqual(Object.keys(backends), ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel']);
    for (const [backend, outcome] of Object.entries(backends)) {
      assert.equal(outcome.throws, true, backend);
      assert.match(outcome.error ?? '', /^Cannot read properties of undefined \(reading 'values'\)/, backend);
    }
  });
});
