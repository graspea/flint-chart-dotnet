import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { compareInputs, summarise } from '../src/extract/crosscheck.ts';

const ours = () => ({
  data: { values: [{ a: 'x', b: 1 }] },
  semantic_types: { a: 'Category', b: { semanticType: 'Amount', unit: 'USD' } },
  chart_spec: { chartType: 'Bar Chart', encodings: { x: { field: 'a', type: 'nominal' }, y: { field: 'b', type: 'quantitative' } } },
});

describe('extract/crosscheck', () => {
  // source: design §7.9 (compare by chartType and data shape; the rest is reported, not judged)
  it('reports nothing for identical inputs', () => {
    assert.deepEqual(compareInputs(ours(), ours()), []);
  });

  // source: design §7.9 (the mapped fixtures carry the TestCase dtype; upstream's converter may not)
  it('separates encoding types from encoding fields', () => {
    const theirs = ours();
    theirs.chart_spec.encodings = { x: { field: 'a' }, y: { field: 'b' } } as never;
    assert.deepEqual(compareInputs(ours(), theirs), ['encoding types']);
  });

  // source: design §7.9 (chartType and data shape)
  it('reports chart type, data and field differences', () => {
    const theirs = ours();
    theirs.chart_spec.chartType = 'Line Chart';
    theirs.data.values = [{ a: 'y', b: 2 }];
    theirs.chart_spec.encodings.y.field = 'a';
    assert.deepEqual(compareInputs(ours(), theirs), ['chartType', 'data values', 'encoding fields']);
  });

  // source: design §7.9 (data shape: row count and first-row keys, apart from the values)
  it('reports a data shape difference', () => {
    const theirs = ours();
    theirs.data.values = [{ b: 1, a: 'x' }, { a: 'z', b: 3 }] as never;
    assert.deepEqual(compareInputs(ours(), theirs), ['data shape', 'data values']);
  });

  // source: design §7.9 (a string annotation equals the object form with only semanticType)
  it('compares semantic types by meaning and lists keys only one side has', () => {
    const theirs = ours() as Record<string, any>;
    theirs.semantic_types = { a: { semanticType: 'Category' }, b: { semanticType: 'Amount', unit: 'USD' } };
    theirs.options = { addTooltips: true };
    theirs.chart_spec.baseSize = { width: 400, height: 300 };
    assert.deepEqual(compareInputs(ours(), theirs), ['chart_spec.baseSize only upstream', 'options only upstream']);
  });

  // source: design §7.9 (the result recorded in docs/fixtures.md is a count per difference)
  it('summarises the differences as counts', () => {
    assert.deepEqual(summarise([[], ['data values'], ['data values', 'chartType']]), { chartType: 1, 'data values': 2, identical: 1 });
  });
});
