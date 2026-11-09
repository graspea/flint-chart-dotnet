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

// Runs against the installed flint-chart, the version package.json pins. A count that changes between upstream versions
// is read from the committed fixture set of that version, tests/fixtures/flint-chart@<version>/, with the 0.5.1 value
// named in the source comment.
describe('extract/fixtures', () => {
  const version: string = JSON.parse(readFileSync(join(generatorDir, 'package.json'), 'utf8')).dependencies['flint-chart'];
  const versionFolder = `flint-chart@${version}`;
  const committedFolder = join(generatorDir, '../tests/fixtures', versionFolder);
  let first: string;
  let second: string;
  let folder: string;
  const linesIn = (dir: string, file: string) =>
    readFileSync(join(dir, file), 'utf8')
      .trimEnd()
      .split('\n')
      .map((l) => JSON.parse(l));
  const lines = (file: string) => linesIn(folder, file);

  before(() => {
    first = runFixtures();
    second = runFixtures();
    folder = join(first, versionFolder);
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
      assert.equal(existsSync(join(empty, versionFolder)), false);
    } finally {
      rmSync(empty, { recursive: true });
    }
  });

  // source: design §10.2 (cases.jsonl, expectations.jsonl, manifest.json, known-failures.txt) and review D2-3 (the
  // crafted cases in probe-cases.jsonl and probe-expectations.jsonl); docs/generator.md "Probes" (the runtime probe
  // record, probes.json, is written per version)
  it('writes the seven files of the version folder', () => {
    assert.deepEqual(readdirSync(folder).sort(), [
      'cases.jsonl',
      'expectations.jsonl',
      'known-failures.txt',
      'manifest.json',
      'probe-cases.jsonl',
      'probe-expectations.jsonl',
      'probes.json',
    ]);
    assert.equal(readFileSync(join(folder, 'known-failures.txt'), 'utf8'), '');
  });

  // source: design §10.3 fixture formats (manifest: case counts, integrity, seed 42, UTC, the upstream version),
  // package.json (the pin), package-lock.json, src/extract/probe-cases.ts (85 crafted cases) and the committed
  // tests/fixtures/flint-chart@<version>/manifest.json (the TestCases of the installed version; 934 on 0.5.1, part 01 §2)
  it('records the manifest', () => {
    const manifest = JSON.parse(readFileSync(join(folder, 'manifest.json'), 'utf8'));
    const lock = JSON.parse(readFileSync(join(generatorDir, 'package-lock.json'), 'utf8'));
    const committedManifest = JSON.parse(readFileSync(join(committedFolder, 'manifest.json'), 'utf8'));
    assert.deepEqual(manifest, {
      cases: committedManifest.cases,
      excludedNondeterministic: [],
      integrity: lock.packages['node_modules/flint-chart'].integrity,
      probeCases: 85,
      seed: 42,
      tz: 'UTC',
      upstream: version,
    });
    assert.equal(manifest.cases, lines('cases.jsonl').length);
    assert.equal(manifest.probeCases, lines('probe-cases.jsonl').length);
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

  // source: part 01 §2 Fixtures (the layout backends throw on no test case; Excel throws on some; on 0.5.1 vegalite 886,
  // echarts 866, chartjs 743, plotly 894 and Excel 219 of 695) and the committed
  // tests/fixtures/flint-chart@<version>/expectations.jsonl (the counts of the installed version)
  it('records the per-backend outcomes', () => {
    const backends = ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel', 'image-charts'];
    const tally = (expectations: { backends: Record<string, { throws: boolean }> }[]) => {
      const count = (b: string, pick: (o: { throws: boolean }) => boolean) =>
        expectations.filter((e) => e.backends[b] && pick(e.backends[b])).length;
      return {
        applicable: Object.fromEntries(backends.map((b) => [b, count(b, () => true)])),
        throws: Object.fromEntries(backends.map((b) => [b, count(b, (o) => o.throws)])),
      };
    };
    const expectations = lines('expectations.jsonl');
    assert.equal(expectations.length, lines('cases.jsonl').length);
    const fresh = tally(expectations);
    assert.deepEqual(fresh, tally(linesIn(committedFolder, 'expectations.jsonl')));
    for (const b of backends.slice(0, 4)) assert.equal(fresh.throws[b], 0, b);
    assert.ok(fresh.throws.excel > 0 && fresh.throws.excel < fresh.applicable.excel);
    assert.ok(fresh.throws['image-charts'] > 0 && fresh.throws['image-charts'] < fresh.applicable['image-charts']);
  });

  // source: src/extract/probe-cases.ts (one case per rule; a case without the template on a backend is recorded there
  // too), tests/fixtures/flint-chart@0.5.1/probes.json (the layout backends throw on a static-series rule, Excel on every
  // static series) and flint-chart 1.0.0 src/core/normalize-properties.ts sameOptionValue (an array value equal entry by
  // entry to an array option is kept, with no warning)
  it('records the crafted cases on every backend that has the template', () => {
    const cases = lines('probe-cases.jsonl');
    const expectations = lines('probe-expectations.jsonl');
    assert.equal(cases.length, 85);
    assert.deepEqual(expectations.map((e) => e.id), cases.map((c) => c.id));
    assert.equal(new Set(cases.map((c) => c.title)).size, cases.length);
    assert.ok(cases.every((c, i) => c.id === `Probe|#${i}`));
    const outcome = (title: string) => expectations[cases.findIndex((c) => c.title === title)].backends;
    const all = ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel', 'image-charts'];
    const throwing = (title: string) => Object.entries(outcome(title)).filter(([, o]) => (o as { throws: boolean }).throws).map(([b]) => b);
    assert.deepEqual(Object.keys(outcome('chart type unknown')), all);
    assert.deepEqual(throwing('static series with one entry'), all);
    assert.deepEqual(outcome('option array value').vegalite, { throws: false, warnings: [] });
    // source: src/image-charts/assemble.ts (faceting and the category/measure throw on every chart type; the slice and
    // value fall through color, else x, and size, else theta, else y; no radar metric count)
    assert.deepEqual(throwing('sparkline faceted on column'), ['image-charts']);
    assert.deepEqual(throwing('sparkline without y'), ['image-charts']);
    assert.deepEqual(throwing('pie with color unbound, x and size'), ['excel']);
    assert.deepEqual(throwing('radar with two metrics'), ['excel']);
    assert.deepEqual(throwing('scatter without rows'), ['excel', 'image-charts']);
    assert.deepEqual(throwing('facet bound with an empty field'), []);
    // source: the same rules on the other chart families: the axes rule on Area, Radar, Stacked and Grouped Bar Chart,
    // the unbound Scatter axes, faceting on Pie, Donut, Stacked and Grouped Bar Chart, the Donut slice and value, and the
    // value order size, then theta, then y (an empty field does not fall through); Excel throws on each too
    for (const title of [
      'donut with a slice field and no value',
      'area without y',
      'radar without x',
      'stacked bar from x and color',
      'grouped bar from y and group',
      'scatter without y',
      'scatter without x',
      'pie faceted on row',
      'donut faceted on column',
      'stacked bar faceted on row',
      'grouped bar faceted on column',
      'pie with an empty size field and theta',
      'pie with an empty theta field and y',
    ]) {
      assert.deepEqual(throwing(title), ['excel', 'image-charts'], title);
    }
  });

  // source: design §13.3.1 (an absent data is expected to throw like data: null, from input.data.values on undefined;
  // the case backs data_missing for a document without data)
  it('records an absent data as a throw on every backend', () => {
    const cases = lines('probe-cases.jsonl');
    const expectations = lines('probe-expectations.jsonl');
    const index = cases.findIndex((c) => c.title === 'data absent');
    assert.equal('data' in cases[index].input, false);
    const backends = expectations[index].backends as Record<string, { throws: boolean; error?: string }>;
    assert.deepEqual(Object.keys(backends), ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel', 'image-charts']);
    for (const [backend, outcome] of Object.entries(backends)) {
      assert.equal(outcome.throws, true, backend);
      assert.match(outcome.error ?? '', /^Cannot read properties of undefined \(reading 'values'\)/, backend);
    }
  });
});
