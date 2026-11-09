import { before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { canonicalJson, type Ir } from '../src/ir.ts';
import { readUnion } from '../src/extract/channel-reads.ts';
import { extractFixtures } from '../src/extract/fixtures.ts';
import { loadOverrides } from '../src/overrides.ts';
import { runProbes, type ProbeResults } from '../src/probes.ts';

const all = ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel'];
const layout = ['vegalite', 'echarts', 'chartjs', 'plotly'];

const record = (version: string) => new URL(`../../tests/fixtures/flint-chart@${version}/probes.json`, import.meta.url);
const pinned = (JSON.parse(readFileSync(new URL('../ir/flint-ir.json', import.meta.url), 'utf8')) as Ir).upstream.version;

/**
 * The flint-chart version whose facts the design, docs/validation-codes.md and the validator describe. Each expectation
 * below is the documented fact the probe backs, checked against that version's record; when a sync changes a fact
 * (the report lists it as a behavioural row), the maintainer moves this version, the expectations and the docs together.
 */
const DOCUMENTED = '1.0.0';

// A fresh run against the installed flint-chart (the pinned version) equals its record, which `npm run generate` writes.
describe('probes: record of the pinned version', () => {
  // source: tests/fixtures/flint-chart@<pinned version>/probes.json (written by `npm run generate`, docs/generator.md "Probes")
  // (a fresh fixture set first: the read probe takes its fixture bases from it, and extractFixtures must be the first
  // flint-chart import of the process)
  it('matches a fresh run', async () => {
    const set = await extractFixtures({ version: pinned, integrity: '' });
    assert.equal(canonicalJson(await runProbes([...set.cases, ...set.probeCases])), readFileSync(record(pinned), 'utf8'));
  });
});

describe(`probes: documented facts (flint-chart ${DOCUMENTED})`, () => {
  let results: ProbeResults;
  // The record is canonical JSON (keys sorted), so backends are listed in the fixed order.
  const throwingIn = (outcomes: Record<string, string>) => all.filter((b) => outcomes[b] === 'throws');
  const throwingBackends = (probe: string) => throwingIn(results.nullHandling[probe]!);

  before(() => {
    results = JSON.parse(readFileSync(record(DOCUMENTED), 'utf8')) as ProbeResults;
  });

  // source: part 01 §2 Null handling (throws on all five: data null, missing encodings, unknown chartType)
  it('finds the throws on every backend', () => {
    for (const probe of ['data null', 'encodings missing', 'chartType unknown']) assert.deepEqual(throwingBackends(probe), all, probe);
  });

  // source: part 01 §2 Null handling (backend-specific throws)
  it('finds the backend-specific throws', () => {
    assert.deepEqual(throwingBackends('channel null'), layout);
    assert.deepEqual(throwingBackends('theme_spec null'), ['vegalite', 'plotly']);
    assert.deepEqual(throwingBackends('encodings empty'), ['excel']);
    assert.deepEqual(throwingBackends('data values empty'), ['excel']);
    assert.deepEqual(throwingBackends('data url only'), ['excel']);
    assert.deepEqual(throwingBackends('encoding field null'), ['excel']);
  });

  // source: core convertTemporalData (Object.keys(data[0]) and row[key] read every row, and a temporal key of the first
  // row is written back into every row, which strict mode refuses on a primitive) (review D2-2)
  it('finds the row shapes that throw on every backend', () => {
    for (const probe of ['row null', 'row null first', 'row number with temporal x', 'row number with an unbound temporal key']) {
      assert.deepEqual(throwingBackends(probe), all, probe);
    }
    assert.deepEqual(throwingBackends('row number, no temporal key'), []);
  });

  // source: core/theme/presets.ts (THEME_PRESETS[id] is a plain-object lookup) and core registry isRegistered
  // (`semanticType in TYPE_REGISTRY`, so a name of Object.prototype counts as registered and has no visEncodings) (review D2-5)
  it('finds how names of Object.prototype resolve', () => {
    const throwing = (probe: string) => throwingIn(results.prototypeKeys[probe]!);
    assert.deepEqual(throwing('theme_spec "constructor"'), []);
    assert.deepEqual(throwing('theme_spec extends "toString"'), []);
    assert.deepEqual(throwing('semantic type "constructor" on a bound field'), all);
    assert.deepEqual(throwing('semantic type "toString" on an unbound key of the first row'), all);
    assert.deepEqual(throwing('semantic type "valueOf" on no field'), []);
  });

  // source: tests/fixtures/flint-chart@1.0.0/probes.json (Excel's empty-data throw comes from a type requirement that untyped fields cannot meet
  // without rows; with explicit types the Bar Chart assembles on Excel too; see docs/validation-codes.md data_empty)
  it('finds no throw for empty data with explicit encoding types', () => {
    assert.deepEqual(throwingBackends('data values empty, typed encodings'), []);
  });

  // source: part 01 §2 Null handling (null tolerated as absent everywhere else; unknown keys accepted at every level)
  it('finds no throw for tolerated nulls and unknown keys', () => {
    for (const probe of Object.keys(results.nullHandling).filter((p) => p.startsWith('tolerated:'))) {
      assert.deepEqual(throwingBackends(probe), [], probe);
    }
  });

  // source: design §7.5 (theme applied by Vega-Lite and Plotly: an unknown preset throws there, output changes there)
  it('finds the backends that apply the theme', () => {
    assert.deepEqual(results.themeBackends.unknownPresetThrows, ['vegalite', 'plotly']);
    assert.deepEqual(results.themeBackends.presetChangesOutput, ['vegalite', 'plotly']);
  });

  // source: core/theme/presets.ts:67-84 (an object's `extends` goes through presetSpec, which throws on an unknown id)
  it('finds the backends where an unknown extends throws', () => {
    assert.deepEqual(results.themeBackends.unknownExtendsThrows, ['vegalite', 'plotly']);
  });

  // source: design §5.3 table (TimeOnly, Guid, Uri, nested object nominal; single-number array quantitative) and the
  // flint-chart 1.0.0 record (TimeSpan nominal; 0.5.1 inferred it temporal, docs/row-data.md)
  it('infers the documented categories for the values .NET writes', () => {
    assert.deepEqual(results.valueInference, {
      'DateOnly "2024-01-15"': 'temporal',
      'Guid, digit-leading': 'nominal',
      'Guid, letter-leading': 'nominal',
      'TimeOnly "10:00:00"': 'nominal',
      'TimeSpan "1.02:03:04"': 'nominal',
      'TimeSpan "2.00:00:00"': 'nominal',
      'Uri': 'nominal',
      'bool': 'nominal',
      'empty string': 'quantitative',
      'nested object': 'nominal',
      'single-number array': 'quantitative',
    });
  });

  // source: ECMA-262 §21.4.1.32 (the ISO forms parse) and V8 (its legacy parser also reads "Stage 1" and bare numbers,
  // and reads no string without a digit); the validator decides Date.parse only for these cases (docs/validation-codes.md)
  it('records what Date.parse accepts', () => {
    assert.deepEqual(results.dateParse, {
      '1': 'parses',
      '2024': 'parses',
      '2024-01-15': 'parses',
      '2024-01-15T10:30:00Z': 'parses',
      '2024-02-30': 'parses',
      '2024-13': 'NaN',
      'East': 'NaN',
      'Jan 2024': 'parses',
      'Mar-Apr': 'NaN',
      'May': 'NaN',
      'Monday January': 'NaN',
      'Stage 1': 'parses',
      'Sat, Jan': 'NaN',
      'now': 'NaN',
      '2024-00': 'NaN',
      '2024-01-00': 'NaN',
      '2024-01-32': 'NaN',
      '2024-01-15T24:00': 'parses',
      '2024-01-15T25:00': 'NaN',
      '2024-01-15T10:60': 'NaN',
      '2024-01-15T10:00:60': 'NaN',
      '2024-01-15T10:00+0200': 'parses',
      '2024-01-15T10:00:00.123456Z': 'parses',
      '2024-01-15 10:00': 'parses',
      '2024-01-15 10:00+0200': 'parses',
      '2024-01-15 25:00': 'NaN',
      '2024-01-15 10:60': 'NaN',
      '1/15/2025': 'parses',
      '2/30/2025': 'parses',
      '12/31/2025': 'parses',
      '0/15/2025': 'NaN',
      '1/0/2025': 'NaN',
      '13/1/2025': 'NaN',
      '12/32/2025': 'NaN',
      '13/45/2025': 'NaN',
    });
  });

  // source: src/core/decisions.ts readsAsSnapshots (at most 8 dates; 9 to 50 calendar dates filling under half their
  // periods), src/core/temporal-parse.ts parseTemporalByType (the YearMonth and Date spellings) and
  // src/core/semantic-types.ts isUnambiguousDateString of flint-chart 1.0.0 (docs/validation-codes.md, "How a rule reads a
  // field's type")
  it('records the channel types the validator ports', () => {
    assert.deepEqual(results.channelTypes, {
      'banded x, 3 dates': 'ordinal snapshots',
      'line x, 3 dates': 'temporal',
      'banded y, 3 dates': 'ordinal snapshots',
      'banded color, 3 dates': 'ordinal',
      'banded x, 12 consecutive months': 'temporal',
      'banded x, 10 quarterly months': 'temporal',
      'banded x, 9 consecutive months and one far': 'ordinal snapshots',
      'banded x, 10 weekly dates': 'temporal',
      'banded x, 9 consecutive days and one far': 'ordinal snapshots',
      'banded x, 60 days': 'temporal',
      'banded x, 10 hours UTC': 'temporal',
      'banded x, 10 dates with an offset, 2 days apart': 'temporal',
      'banded x, Year 2000 to 2008 and 2024': 'ordinal snapshots',
      'banded x, YearMonth numbers 202501, 202503, 202507': 'ordinal snapshots',
      'banded x, YearMonth numbers, 12 consecutive': 'temporal',
      'line x, YearMonth numbers': 'temporal',
      'line x, YearMonth spellings': 'temporal',
      'line x, Date spellings': 'temporal',
      'line x, Date spellings with an invalid day': 'temporal',
      'line x, untyped "Jan 2025"': 'nominal',
      'line x, untyped "Jan 15, 2025"': 'temporal',
      'line x, untyped "2025/01/15"': 'temporal',
      'line x, untyped "2024-01-15 10:00"': 'temporal',
      'line x, untyped US slash dates': 'temporal',
      'line x, untyped "13/45/2025"': 'nominal',
      'line x, untyped "Stage 1"': 'nominal',
    });
  });
});

// Image-Charts joined the SDK on flint-chart 1.0.1 (design part 9 §14.8.1); its facts are pinned to that record, which
// docs/validation-codes.md cites.
describe('probes: Image-Charts facts (flint-chart 1.0.1)', () => {
  let results: ProbeResults;
  const imageCharts = (probe: string) => results.nullHandling[probe]!['image-charts'];

  before(() => {
    results = JSON.parse(readFileSync(record('1.0.1'), 'utf8')) as ProbeResults;
  });

  // source: src/image-charts/assemble.ts of flint-chart 1.0.1 (column and row throw, :192-194; group, else a categorical
  // color, is the series, :205-210; x is the slice when color has no field and y the value when neither size nor theta
  // has one, :265-266; a Scatter Plot assembles only from quantitative x and y, :290-293), probed by
  // src/extract/channel-reads.ts under its synthetic bases
  it('records the channels assembleImageCharts reads per chart type under the synthetic bases', () => {
    const axes = ['color', 'column', 'group', 'row', 'x', 'y'];
    const partToWhole = ['color', 'column', 'row', 'size', 'theta', 'x', 'y'];
    const both = { 'x and y measures': axes, 'x category, y measure': axes, 'y category, x measure': axes };
    const slices = { 'slice and value': ['color', 'column', 'row', 'size'], 'x and y measures': partToWhole, 'x category, y measure': partToWhole, 'y category, x measure': partToWhole };
    const synthetic = Object.fromEntries(
      Object.entries(results.channelReads.reads['image-charts']).map(([t, bases]) => [t, Object.fromEntries(Object.entries(bases).filter(([b]) => 'encodings' in results.channelReads.bases[b]))]),
    );
    assert.deepEqual(synthetic, {
      'Area Chart': both, 'Bar Chart': both, 'Donut Chart': slices, 'Grouped Bar Chart': both, 'Line Chart': both,
      'Pie Chart': slices, 'Radar Chart': both, 'Scatter Plot': { 'x and y measures': axes }, Sparkline: both, 'Stacked Bar Chart': both,
    });
  });

  // source: docs/generator.md "Failure policy" (extract checks the reads under the synthetic bases against overrides.json
  // imageChartsReads; the fixture bases find no other channel)
  it('records the Image-Charts reads of overrides.json imageChartsReads under every base', () => {
    const { imageChartsReads } = loadOverrides(new URL('../overrides.json', import.meta.url).pathname);
    const expected = Object.fromEntries(Object.entries(imageChartsReads).map(([t, c]) => [t, [...c].sort()]));
    assert.deepEqual(readUnion(results.channelReads.reads['image-charts']), expected);
  });

  // source: src/excel/assemble.ts of flint-chart 1.0.1 (group, else a categorical color, is the series off Pie and Donut,
  // :270-275; color, else x, is the slice and size, else theta, else y, the value, :277-281; the dash field splits the
  // series, :293, :403-448; size and order are read on the XY path, :294-295, :309-314, :320-377), the stock chart
  // throws on color, group or detail (src/excel/templates/candlestick.ts:31-33) and a Histogram reads y without x
  // (src/excel/templates/histogram.ts:19, :25), probed by src/extract/channel-reads.ts
  it('records the channels assembleExcel reads', () => {
    const excel = results.channelReads.reads.excel;
    assert.deepEqual(excel['Line Chart']['x category, y measure'], ['color', 'column', 'group', 'row', 'strokeDash', 'x', 'y']);
    assert.deepEqual(excel['Line Chart']['x and y measures'], ['color', 'column', 'group', 'order', 'row', 'size', 'strokeDash', 'x', 'y']);
    assert.deepEqual(excel['Pie Chart']['x category, y measure'], ['color', 'column', 'row', 'size', 'strokeDash', 'theta', 'x', 'y']);
    assert.deepEqual(excel['Pie Chart']['slice and value'], ['color', 'column', 'row', 'size', 'strokeDash']);
    assert.deepEqual(excel.Histogram['y measure'], ['color', 'column', 'row', 'x', 'y']);
    assert.deepEqual(excel.Histogram['x and y measures'], ['color', 'column', 'row', 'x']);
    assert.deepEqual(excel['Candlestick Chart']['Candlestick Chart|#4'], ['close', 'color', 'column', 'detail', 'group', 'high', 'low', 'open', 'row', 'x']);
  });

  // source: src/image-charts/assemble.ts (no category or measure resolves without encodings; a null channel is skipped;
  // only data.values is read, never data.url) and core convertTemporalData (the row shapes)
  it('finds the Image-Charts throws and what it tolerates', () => {
    for (const probe of ['data null', 'encodings missing', 'encodings empty', 'encoding field null', 'chartType unknown', 'row null', 'row null first', 'row number with temporal x', 'row number with an unbound temporal key']) {
      assert.equal(imageCharts(probe), 'throws', probe);
    }
    for (const probe of ['channel null', 'data values empty', 'data url only', 'theme_spec null', 'row number, no temporal key']) {
      assert.equal(imageCharts(probe), 'ok', probe);
    }
  });

  // source: src/image-charts/assemble.ts (theme_spec is never read)
  it('finds that Image-Charts applies no theme', () => {
    for (const [fact, backends] of Object.entries(results.themeBackends)) assert.ok(!backends.includes('image-charts'), fact);
  });
});
