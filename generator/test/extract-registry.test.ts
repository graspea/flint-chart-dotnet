import { before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import * as flint from 'flint-chart';
import { extractCatalog } from '../src/extract/registry.ts';
import { flintPackageDir } from '../src/extract/package.ts';
import type { IrCatalog } from '../src/ir.ts';

// Runs against the installed flint-chart@0.5.1. Expected counts: docs/design part 01 §2 and §10.4 test 3,
// all probed against the 0.5.1 tarball.
describe('extract/registry', () => {
  let catalog: IrCatalog;
  const backendsOf = (name: string) => Object.keys(catalog.chartTypes.find((t) => t.name === name)!.backends).sort();

  before(() => {
    catalog = extractCatalog(flint as unknown as Record<string, unknown>, flintPackageDir(), {
      backendsApplyingTheme: ['vegalite', 'plotly'],
      hostStateKeys: ['sort'],
      ignoredOptions: {},
    });
  });

  // source: registries (vl/ec/cjs/pl/excelAllTemplateDefs), design §2
  it('lists the five backends in the fixed order', () => {
    assert.deepEqual(catalog.backends, ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel']);
  });

  // source: design §2 (48 chart types; Vega-Lite 36, ECharts 37, Chart.js 22, Plotly 38, Excel 18)
  it('extracts 48 chart types with the per-backend counts', () => {
    assert.equal(catalog.chartTypes.length, 48);
    const perBackend = Object.fromEntries(
      catalog.backends.map((b) => [b, catalog.chartTypes.filter((t) => b in t.backends).length]),
    );
    assert.deepEqual(perBackend, { vegalite: 36, echarts: 37, chartjs: 22, plotly: 38, excel: 18 });
  });

  // source: design §7.3 (chart types sorted by name)
  it('sorts chart types by name', () => {
    const names = catalog.chartTypes.map((t) => t.name);
    assert.deepEqual(names, [...names].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)));
  });

  // source: design §2 (44-entry SemanticTypes registry; ten theme presets)
  it('extracts 44 semantic types and 10 theme presets', () => {
    assert.equal(catalog.semanticTypes.length, 44);
    assert.deepEqual(
      catalog.themePresets.map((p) => p.id),
      ['cartoon', 'datawrapper', 'economist', 'mckinsey', 'nature', 'nyt', 'pop', 'powerbi', 'powerbi-light', 'swiss'],
    );
  });

  // source: design §2 (296 ChartPropertyDefs; 46 distinct (template, key) discrete pairs)
  it('extracts 296 properties and 46 distinct discrete pairs', () => {
    const all = catalog.chartTypes.flatMap((t) => Object.values(t.backends).flatMap((b) => b.properties.map((p) => ({ t, p }))));
    assert.equal(all.length, 296);
    const pairs = new Set(all.filter(({ p }) => p.type === 'discrete').map(({ t, p }) => `${t.name}/${p.key}`));
    assert.equal(pairs.size, 46);
  });

  // source: design §2 Channels (constant 26 + theta = 27; text only in channelGroups)
  it('derives a channel union of 27 names that contains theta and not text', () => {
    assert.equal(catalog.channels.length, 27);
    assert.ok(catalog.channels.includes('theta'));
    assert.ok(!catalog.channels.includes('text'));
  });

  // source: design §10.4 test 3 (registry facts)
  it('records Line Chart strokeDash on vegalite, plotly and excel and detail on vegalite only', () => {
    const line = catalog.chartTypes.find((t) => t.name === 'Line Chart')!;
    const having = (ch: string) => Object.entries(line.backends).filter(([, b]) => b.channels.includes(ch)).map(([n]) => n).sort();
    assert.deepEqual(having('strokeDash'), ['excel', 'plotly', 'vegalite']);
    assert.deepEqual(having('detail'), ['vegalite']);
  });

  // source: design §10.4 test 3 (registry facts)
  it('declares goal only on Bullet Chart and KPI Card', () => {
    const withGoal = catalog.chartTypes
      .filter((t) => Object.values(t.backends).some((b) => b.channels.includes('goal')))
      .map((t) => t.name);
    assert.deepEqual(withGoal, ['Bullet Chart', 'KPI Card']);
  });

  // source: design §10.4 test 3 (Doughnut Chart is chartjs-only)
  it('records Doughnut Chart on chartjs only', () => {
    assert.deepEqual(backendsOf('Doughnut Chart'), ['chartjs']);
  });

  // source: design §2 Registries (16 of 18 Excel templates have validate(); Pie and Donut do not)
  it('marks hasValidate false for Excel Pie and Donut only', () => {
    const without = catalog.chartTypes.filter((t) => t.backends.excel && !t.backends.excel.hasValidate).map((t) => t.name);
    assert.deepEqual(without, ['Donut Chart', 'Pie Chart']);
  });

  // source: design §2 Registries (Excel templates carry typeMapping and no properties)
  it('records typeMapping and no properties on every Excel template', () => {
    for (const t of catalog.chartTypes.filter((c) => c.backends.excel)) {
      assert.ok(t.backends.excel.typeMapping, t.name);
      assert.equal(t.backends.excel.properties.length, 0, t.name);
    }
  });

  // source: registry (getRegistryEntry(name).visEncodings: the candidate encodings upstream resolves a field's type from)
  it('records the encoding candidates of each semantic type', () => {
    const encodings = (name: string) => catalog.semanticTypes.find((s) => s.name === name)!.visEncodings;
    assert.deepEqual(encodings('Amount'), ['quantitative']);
    assert.deepEqual(encodings('Year'), ['temporal', 'ordinal']);
    assert.deepEqual(encodings('Score'), ['quantitative', 'ordinal']);
    assert.deepEqual(encodings('Latitude'), ['quantitative', 'geographic']);
    assert.deepEqual(encodings('Category'), ['nominal']);
  });

  // source: registry (Line Chart interpolate: omitted default first, then string values in declaration order)
  it('keeps option declaration order and marks the omitted default', () => {
    const interpolate = catalog.chartTypes
      .find((t) => t.name === 'Line Chart')!
      .backends.vegalite.properties.find((p) => p.key === 'interpolate')!;
    assert.equal(interpolate.type, 'discrete');
    assert.deepEqual(interpolate.options![0], { label: 'Default (linear)', omitted: true });
    assert.deepEqual(
      interpolate.options!.slice(1).map((o) => o.value),
      ['linear', 'monotone', 'step', 'step-before', 'step-after', 'basis', 'cardinal', 'catmull-rom'],
    );
  });

  // source: design §7.3 (backendsApplyingTheme and hostStateKeys come from overrides.json)
  it('copies the override-backed catalog facts', () => {
    assert.deepEqual(catalog.backendsApplyingTheme, ['vegalite', 'plotly']);
    assert.deepEqual(catalog.hostStateKeys, ['sort']);
  });

  // source: registry getRegistryEntry('YearMonth') = { t0: 'Temporal', t1: 'DateGranule', ... }
  it('records the registry hierarchy of each semantic type', () => {
    const ym = catalog.semanticTypes.find((s) => s.name === 'YearMonth')!;
    assert.equal(ym.t0, 'Temporal');
    assert.equal(ym.t1, 'DateGranule');
  });
});
