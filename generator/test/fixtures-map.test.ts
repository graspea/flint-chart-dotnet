import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { defaultBackend, mapTestCase, recordOutcome, type TestCaseLike } from '../src/extract/fixtures.ts';

const testCase = (over: Partial<TestCaseLike> = {}): TestCaseLike => ({
  title: 'T×Q (2 pts)',
  description: 'dropped',
  tags: ['dropped'],
  chartType: 'Line Chart',
  data: [
    { Month: '2024-01', Revenue: 10, Region: 'East' },
    { Month: '2024-02', Revenue: 12, Region: 'West' },
  ],
  fields: [
    { id: 'f1', name: 'Month', source: 'original', tableRef: 't' },
    { id: 'f2', name: 'Revenue', source: 'original', tableRef: 't' },
    { id: 'f3', name: 'Region', source: 'original', tableRef: 't' },
  ],
  metadata: {
    Month: { type: 'date', semanticType: 'YearMonth', levels: ['2024-01', '2024-02'] },
    Revenue: { type: 'number', semanticType: 'Amount', levels: [] },
    Region: { type: 'string', semanticType: '', levels: ['East', 'West'] },
  },
  encodingMap: {
    x: { fieldID: 'f1', dtype: 'temporal' },
    y: { fieldID: 'f2', dtype: 'quantitative' },
  },
  ...over,
});

describe('extract/fixtures mapping', () => {
  // source: design §7.9 (data = {values}; encodings by field name with dtype; title, description, tags, metadata.type and levels dropped)
  it('maps a TestCase to a ChartAssemblyInput', () => {
    assert.deepEqual(mapTestCase(testCase()), {
      data: {
        values: [
          { Month: '2024-01', Revenue: 10, Region: 'East' },
          { Month: '2024-02', Revenue: 12, Region: 'West' },
        ],
      },
      semantic_types: { Month: 'YearMonth', Revenue: 'Amount' },
      chart_spec: {
        chartType: 'Line Chart',
        encodings: { x: { field: 'Month', type: 'temporal' }, y: { field: 'Revenue', type: 'quantitative' } },
      },
    });
  });

  // source: design §7.9 (semantic_types[f] = semanticAnnotations?.[f] ?? metadata[f].semanticType)
  it('prefers the enriched semantic annotation over the metadata string', () => {
    const input = mapTestCase(testCase({ semanticAnnotations: { Revenue: { semanticType: 'Amount', unit: 'USD' } } }));
    assert.deepEqual(input.semantic_types, { Month: 'YearMonth', Revenue: { semanticType: 'Amount', unit: 'USD' } });
  });

  // source: design §7.9 (aggregate, sortOrder, sortBy, scheme mapped when present; empty values dropped; entries without fieldID dropped)
  it('maps the optional encoding members and drops empty values and unbound entries', () => {
    const input = mapTestCase(
      testCase({
        encodingMap: {
          x: { fieldID: 'f1', dtype: 'temporal', sortOrder: 'descending', sortBy: '', scheme: 'default' },
          y: { fieldID: 'f2', aggregate: 'sum' },
          color: { dtype: 'nominal' },
        },
      }),
    ) as { chart_spec: { encodings: unknown } };
    assert.deepEqual(input.chart_spec.encodings, {
      x: { field: 'Month', type: 'temporal', sortOrder: 'descending', scheme: 'default' },
      y: { field: 'Revenue', aggregate: 'sum' },
    });
  });

  // source: design §7.9 (chartProperties as given; options = assembleOptions)
  it('carries chartProperties and assembleOptions', () => {
    const input = mapTestCase(testCase({ chartProperties: { interpolate: 'monotone' }, assembleOptions: { addTooltips: true } })) as {
      chart_spec: Record<string, unknown>;
      options: unknown;
    };
    assert.deepEqual(input.chart_spec.chartProperties, { interpolate: 'monotone' });
    assert.deepEqual(input.options, { addTooltips: true });
  });

  // source: design §7.9 (no semantic types -> nothing to write; the key is omitted rather than written empty)
  it('omits semantic_types when no field has one', () => {
    const input = mapTestCase(testCase({ metadata: {} }));
    assert.equal('semantic_types' in input, false);
  });
});

describe('extract/fixtures default backend', () => {
  const all = ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel'];

  // source: docs/fixtures.md (the generator group prefix names the gallery backend; the rest are Vega-Lite groups)
  it('follows the group prefix, else Vega-Lite', () => {
    assert.equal(defaultBackend('Semantic Context', all), 'vegalite');
    assert.equal(defaultBackend('ECharts: Bar', all), 'echarts');
    assert.equal(defaultBackend('Chart.js: Bar', all), 'chartjs');
    assert.equal(defaultBackend('Plotly: Core Templates', all), 'plotly');
  });

  // source: docs/fixtures.md (falls back to the first backend in the fixed order that has the template)
  it('falls back to the first backend that has the template', () => {
    assert.equal(defaultBackend('Omni: Sunburst', ['echarts', 'plotly', 'excel']), 'echarts');
    assert.equal(defaultBackend('Chart.js: Doughnut *', ['chartjs']), 'chartjs');
  });
});

describe('extract/fixtures outcome', () => {
  // source: design §10.3 fixture formats (throws plus warning codes from _warnings)
  it('records the warning codes of a layout backend', () => {
    const outcome = recordOutcome(() => ({ _warnings: [{ code: 'overflow' }, { code: 'invalid-option-value' }, { code: 'overflow' }] }), {});
    assert.deepEqual(outcome, { throws: false, warnings: ['invalid-option-value', 'overflow'] });
  });

  // source: design §7.9 (Excel reports the top-level warnings array, same shape)
  it('records the top-level warnings array of the Excel artifact', () => {
    const outcome = recordOutcome(() => ({ warnings: [{ code: 'excel-radar-fill-unsupported' }] }), {});
    assert.deepEqual(outcome, { throws: false, warnings: ['excel-radar-fill-unsupported'] });
  });

  // source: design §10.3 fixture formats (a throw is recorded with its message)
  it('records a throw and its message', () => {
    const outcome = recordOutcome(() => {
      throw new Error('Excel backend does not support faceting');
    }, {});
    assert.deepEqual(outcome, { throws: true, error: 'Excel backend does not support faceting' });
  });

  // source: design §7.9 (each backend assembles its own copy; an assembler may mutate its input)
  it('passes a copy of the input', () => {
    const input = { a: [1] };
    recordOutcome((i) => {
      (i as { a: number[] }).a.push(2);
      return {};
    }, input);
    assert.deepEqual(input, { a: [1] });
  });
});
