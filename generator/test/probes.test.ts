import { before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { canonicalJson, type Ir } from '../src/ir.ts';
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
  it('matches a fresh run', async () => {
    assert.equal(canonicalJson(await runProbes()), readFileSync(record(pinned), 'utf8'));
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
    });
  });
});
