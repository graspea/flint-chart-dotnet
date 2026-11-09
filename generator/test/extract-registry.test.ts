import { before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import * as flint from 'flint-chart';
import { extractCatalog } from '../src/extract/registry.ts';
import { flintPackageDir } from '../src/extract/package.ts';
import { loadOverrides } from '../src/overrides.ts';
import { catalogChannels } from '../src/extract/channel-reads.ts';
import { ordinal, type Ir, type IrCatalog } from '../src/ir.ts';

// Runs against the installed flint-chart. Facts cited from docs/design part 01 §2 and §10.4 test 3 were probed against
// the 0.5.1 tarball; a count or list that changes between upstream versions is read from the committed IR of the pinned
// version (ir/flint-ir.json), with the 0.5.1 value named in the source comment.
describe('extract/registry', () => {
  let catalog: IrCatalog;
  const committed = (JSON.parse(readFileSync(new URL('../ir/flint-ir.json', import.meta.url), 'utf8')) as Ir).catalog;
  const backendsOf = (name: string) => Object.keys(catalog.chartTypes.find((t) => t.name === name)!.backends).sort();
  const perBackend = (c: IrCatalog) =>
    Object.fromEntries(c.backends.map((b) => [b, c.chartTypes.filter((t) => b in t.backends).length]));
  const allProperties = (c: IrCatalog) =>
    c.chartTypes.flatMap((t) => Object.values(t.backends).flatMap((b) => b.properties.map((p) => ({ t, p }))));
  const discretePairs = (c: IrCatalog) =>
    new Set(allProperties(c).filter(({ p }) => p.type === 'discrete').map(({ t, p }) => `${t.name}/${p.key}`));

  const { imageChartsChannels, imageChartsReads, imageChartsSource } = loadOverrides(new URL('../overrides.json', import.meta.url).pathname);
  const overrides = { backendsApplyingTheme: ['vegalite', 'plotly'], hostStateKeys: ['sort'], ignoredOptions: {}, imageChartsChannels, imageChartsReads, imageChartsSource };
  const extract = (module: Record<string, unknown>, change: Partial<typeof overrides> & { pending?: string[] } = {}) =>
    extractCatalog(module, flintPackageDir(), { ...overrides, ...change });
  const map = (flint as unknown as { IMAGE_CHARTS_TYPE_MAP: Record<string, Record<string, string | boolean>> }).IMAGE_CHARTS_TYPE_MAP;

  before(() => {
    catalog = extract(flint as unknown as Record<string, unknown>);
  });

  // source: registries (vl/ec/cjs/pl/excelAllTemplateDefs), design §2, and IMAGE_CHARTS_TYPE_MAP (src/image-charts/chart-types.ts),
  // appended last (design part 9 §14.8.1)
  it('lists the six backends in the fixed order, Image-Charts last', () => {
    assert.deepEqual(catalog.backends, ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel', 'image-charts']);
  });

  // source: src/image-charts/chart-types.ts (the ten own keys of IMAGE_CHARTS_TYPE_MAP), src/image-charts/assemble.ts
  // (no chartProperties read; a bound column or row throws) and overrides.json imageChartsChannels
  it('derives the Image-Charts templates from the chart-type map and the curated channels', () => {
    const withImageCharts = catalog.chartTypes.filter((t) => 'image-charts' in t.backends).map((t) => t.name);
    assert.deepEqual(withImageCharts, Object.keys(map).sort());
    for (const name of withImageCharts) {
      const t = catalog.chartTypes.find((c) => c.name === name)!;
      const others = Object.entries(t.backends).filter(([b]) => b !== 'image-charts').flatMap(([, b]) => b.channels);
      const template = t.backends['image-charts'];
      assert.deepEqual(template, { channels: [...new Set(others)].filter((c) => imageChartsChannels[name].includes(c)), properties: [], typeMapping: map[name], supportsFacets: false }, name);
      assert.ok(template.channels.every((c) => others.includes(c)), `${name}: no builder gains a member`);
    }
  });

  // source: docs/generator.md "Failure policy" (an Image-Charts entry that disagrees with the chart-type map, or that
  // would give a builder a member, fails generation; pending lists an entry written ahead)
  it('fails on Image-Charts channels that disagree with the chart-type map', () => {
    const module = flint as unknown as Record<string, unknown>;
    const without = (name: string) => Object.fromEntries(Object.entries(imageChartsChannels).filter(([k]) => k !== name));
    assert.throws(() => extract(module, { imageChartsChannels: without('Sparkline') }), /imageChartsChannels\["Sparkline"\] is missing/);
    assert.throws(() => extract(module, { imageChartsChannels: { ...imageChartsChannels, Heatmap: ['x'] } }), /imageChartsChannels\["Heatmap"\] matches no chart type/);
    assert.doesNotThrow(() => extract(module, { imageChartsChannels: { ...imageChartsChannels, Heatmap: ['x'] }, pending: ['catalog.chartTypes[Heatmap].backends.image-charts'] }));
    assert.throws(() => extract(module, { imageChartsChannels: { ...imageChartsChannels, toString: ['x'] } }), /imageChartsChannels\["toString"\] matches no chart type/);
    assert.throws(() => extract(module, { imageChartsChannels: { ...imageChartsChannels, 'Bar Chart': ['x', 'y', 'size'] } }), /lists size, which no other backend/);
    assert.throws(() => extract(module, { imageChartsChannels: { ...imageChartsChannels, 'Bar Chart': ['x', 'y', 'column'] } }), /lists column, on which assembleImageCharts throws/);
  });

  // source: docs/generator.md "Failure policy" and src/extract/channel-reads.ts (the probe's candidate channels are
  // the catalog's: flint.channels and every channel a registry template declares)
  it('probes the channels of the catalog', () => {
    assert.deepEqual([...catalogChannels(flint as unknown as Record<string, unknown>)].sort(ordinal), [...committed.channels].sort(ordinal));
  });

  // source: docs/generator.md "Failure policy" and src/extract/channel-reads.ts (the channels the probe finds
  // assembleImageCharts reading are recorded in overrides.json imageChartsReads)
  it('fails when the probed Image-Charts reads differ from imageChartsReads', () => {
    const module = flint as unknown as Record<string, unknown>;
    const bar = { ...imageChartsReads, 'Bar Chart': ['color', 'column', 'row', 'x', 'y'] };
    assert.throws(
      () => extract(module, { imageChartsReads: bar }),
      /imageChartsReads\["Bar Chart"\] in overrides\.json records color, column, row, x, y, but the read probe finds color, column, group, row, x, y \(new: group; gone: none\): review the Image-Charts throw rules/,
    );
    const without = Object.fromEntries(Object.entries(imageChartsReads).filter(([k]) => k !== 'Sparkline'));
    assert.throws(() => extract(module, { imageChartsReads: without }), /imageChartsReads\["Sparkline"\] is missing/);
    assert.throws(() => extract(module, { imageChartsReads: { ...imageChartsReads, Heatmap: ['x'] } }), /imageChartsReads\["Heatmap"\] matches no chart type/);
    assert.doesNotThrow(() => extract(module, { imageChartsReads: { ...imageChartsReads, Heatmap: ['x'] }, pending: ['catalog.chartTypes[Heatmap].backends.image-charts'] }));
  });

  // source: docs/generator.md "Failure policy" and src/extract/channel-reads.ts (a change in a src/core helper the
  // fingerprinted files call is seen by behaviour, the fingerprint unchanged)
  it('fails when a src/core change makes Image-Charts read a new channel, the fingerprint unchanged', () => {
    const real = (flint as unknown as { assembleImageCharts: (i: any) => { type: string; url: string } }).assembleImageCharts;
    const readsOpacity = {
      ...(flint as unknown as Record<string, unknown>),
      assembleImageCharts: (input: any) => {
        const out = real(input);
        return input.chart_spec.encodings?.opacity?.field ? { ...out, url: `${out.url}&chls=2` } : out;
      },
    };
    assert.throws(
      () => extract(readsOpacity),
      (e: Error) => /imageChartsReads\["Bar Chart"\].*\(new: opacity; gone: none\)/.test(e.message) && !/has sha256/.test(e.message),
    );
  });

  // source: docs/generator.md "Failure policy" and src/extract/channel-reads.ts (a curated channel the assembler
  // ignores would hide an unsupported_channel)
  it('fails on a curated Image-Charts channel the probe does not find read', () => {
    const real = (flint as unknown as { assembleImageCharts: (i: any) => unknown }).assembleImageCharts;
    const ignoresColor = {
      ...(flint as unknown as Record<string, unknown>),
      assembleImageCharts: (input: any) => {
        if (input.chart_spec.chartType !== 'Bar Chart') return real(input);
        const { color: _color, ...encodings } = input.chart_spec.encodings ?? {};
        return real({ ...input, chart_spec: { ...input.chart_spec, encodings } });
      },
    };
    assert.throws(() => extract(ignoresColor), /imageChartsChannels\["Bar Chart"\] lists color, which the read probe does not find read/);
  });

  // source: docs/generator.md "Failure policy" and src/extract/channel-reads.ts (a chart type that assembles from
  // none of the bases cannot be probed)
  it('fails on a chart type that assembles from none of the bases', () => {
    const real = (flint as unknown as { assembleImageCharts: (i: any) => unknown }).assembleImageCharts;
    const throwsOnRadar = {
      ...(flint as unknown as Record<string, unknown>),
      assembleImageCharts: (input: any) => {
        if (input.chart_spec.chartType === 'Radar Chart') throw new Error('no');
        return real(input);
      },
    };
    assert.throws(() => extract(throwsOnRadar), /assembleImageCharts assembles "Radar Chart" from none of the read probe's bases/);
  });

  // source: docs/generator.md "Failure policy" (an Image-Charts source file that differs from its reviewed sha256 fails
  // extraction until the rules, imageChartsChannels and the consumed-channel exemptions are reviewed) and the installed
  // src/image-charts/assemble.ts and chart-types.ts
  it('fails when an Image-Charts source differs from the reviewed fingerprint', () => {
    const module = flint as unknown as Record<string, unknown>;
    for (const file of ['src/image-charts/assemble.ts', 'src/image-charts/chart-types.ts']) {
      assert.equal(imageChartsSource[file], createHash('sha256').update(readFileSync(join(flintPackageDir(), file))).digest('hex'), file);
    }
    const changed = { ...imageChartsSource, 'src/image-charts/assemble.ts': '0'.repeat(64) };
    assert.throws(
      () => extract(module, { imageChartsSource: changed }),
      /src\/image-charts\/assemble\.ts has sha256 [0-9a-f]{64}, but imageChartsSource in overrides\.json records 0{64}: review src\/Graspea\.Flint\/Validation\/Rules\/ImageCharts\.cs, imageChartsChannels and the consumed-channel exemptions .*then update the fingerprint/,
    );
    const missing = Object.fromEntries(Object.entries(imageChartsSource).filter(([k]) => k !== 'src/image-charts/chart-types.ts'));
    assert.throws(() => extract(module, { imageChartsSource: missing }), /src\/image-charts\/chart-types\.ts has sha256 [0-9a-f]{64}, but imageChartsSource in overrides\.json records none/);
  });

  // source: src/image-charts/assemble.ts of flint-chart 1.0.1: what assembleImageCharts reads (x, y, color and group on
  // the axes and Scatter families, :187-210 and :258-380; color, x, size, theta and y on Pie and Donut, :265-266; column
  // and row only to throw, :192-194), intersected with what another backend of the template declares
  // (docs/generator.md imageChartsChannels); read but left out: group outside Grouped Bar Chart, x and y on Pie and Donut
  it('pins the curated Image-Charts channels of flint-chart 1.0.1', () => {
    const expected: Record<string, string[]> = {
      'Area Chart': ['x', 'y', 'color'],
      'Bar Chart': ['x', 'y', 'color'],
      'Donut Chart': ['size', 'color', 'theta'],
      'Grouped Bar Chart': ['x', 'y', 'group', 'color'],
      'Line Chart': ['x', 'y', 'color'],
      'Pie Chart': ['size', 'color', 'theta'],
      'Radar Chart': ['x', 'y', 'color'],
      'Scatter Plot': ['x', 'y', 'color'],
      Sparkline: ['x', 'y', 'color'],
      'Stacked Bar Chart': ['x', 'y', 'color'],
    };
    assert.deepEqual(Object.keys(map).sort(), Object.keys(expected).sort());
    for (const [name, channels] of Object.entries(expected)) {
      const template = catalog.chartTypes.find((t) => t.name === name)!.backends['image-charts'];
      assert.deepEqual([...template.channels].sort(), [...channels].sort(), name);
    }
  });

  // source: the assemble* exports of the pinned flint-chart (a backend without a template registry fails loudly)
  it('fails on an assembler export the catalog does not cover', () => {
    assert.throws(() => extract({ ...(flint as unknown as Record<string, unknown>), assembleNewBackend: () => ({}) }), /Unknown assemblers: assembleNewBackend/);
  });

  // source: ir/flint-ir.json catalog.chartTypes (the chart types of the pinned version; design §2 on 0.5.1: 48, Vega-Lite
  // 36, ECharts 37, Chart.js 22, Plotly 38, Excel 18)
  it('extracts the chart types of the committed IR with its per-backend counts', () => {
    assert.deepEqual(catalog.chartTypes.map((t) => t.name), committed.chartTypes.map((t) => t.name));
    assert.deepEqual(perBackend(catalog), perBackend(committed));
    assert.ok(Object.values(perBackend(catalog)).every((n) => n > 0));
  });

  // source: design §7.3 (chart types sorted by name)
  it('sorts chart types by name', () => {
    const names = catalog.chartTypes.map((t) => t.name);
    assert.deepEqual(names, [...names].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)));
  });

  // source: ir/flint-ir.json catalog.semanticTypes and catalog.themePresets (the pinned version; design §2 on 0.5.1: 44
  // semantic types, ten presets from cartoon to swiss)
  it('extracts the semantic types and theme presets of the committed IR', () => {
    assert.deepEqual(catalog.semanticTypes.map((t) => t.name), committed.semanticTypes.map((t) => t.name));
    assert.deepEqual(catalog.themePresets.map((p) => p.id), committed.themePresets.map((p) => p.id));
    assert.ok(catalog.themePresets.some((p) => p.id === 'swiss'));
  });

  // source: ir/flint-ir.json catalog.chartTypes[*].backends[*].properties (the pinned version; design §2 on 0.5.1: 296
  // ChartPropertyDefs, 46 distinct (template, key) discrete pairs)
  it('extracts the properties and distinct discrete pairs of the committed IR', () => {
    assert.equal(allProperties(catalog).length, allProperties(committed).length);
    assert.deepEqual([...discretePairs(catalog)].sort(), [...discretePairs(committed)].sort());
    assert.ok(discretePairs(catalog).size > 0);
  });

  // source: design §2 Channels (constant 26 + theta = 27; text only in channelGroups)
  it('derives a channel union of 27 names that contains theta and not text', () => {
    assert.equal(catalog.channels.length, 27);
    assert.ok(catalog.channels.includes('theta'));
    assert.ok(!catalog.channels.includes('text'));
  });

  // source: design §10.4 test 3 (registry facts: channels are recorded per backend; on 0.5.1 Line Chart has strokeDash on
  // vegalite, plotly and excel and detail on vegalite only) and ir/flint-ir.json catalog.chartTypes[Line Chart]
  it('records the Line Chart strokeDash and detail channels per backend', () => {
    const having = (c: IrCatalog, ch: string) =>
      Object.entries(c.chartTypes.find((t) => t.name === 'Line Chart')!.backends).filter(([, b]) => b.channels.includes(ch)).map(([n]) => n).sort();
    for (const ch of ['strokeDash', 'detail']) {
      assert.deepEqual(having(catalog, ch), having(committed, ch), ch);
      assert.ok(having(catalog, ch).includes('vegalite'), ch);
    }
    assert.ok(having(catalog, 'detail').length < Object.keys(catalog.chartTypes.find((t) => t.name === 'Line Chart')!.backends).length);
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
