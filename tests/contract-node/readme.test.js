// Assembles the document the README quick start shows with the pinned flint-chart, on every backend that has its
// template. Graspea.Flint.Tests (ReadmeExamplesTests) proves that the quick-start code writes exactly this document.
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { assembleWarnings, backendsWith, root } from './flint.js';

const readme = readFileSync(join(root, 'README.md'), 'utf8').replace(/\r\n/g, '\n');
const quickStart = readme.slice(readme.indexOf('\n## Quick start\n'));
const block = /^```json\n([\s\S]*?)^```$/m.exec(quickStart);

describe('README quick start', () => {
  it('shows a JSON document', () => {
    assert.ok(block, 'README.md has no ```json block after "## Quick start"');
  });

  const input = block ? JSON.parse(block[1]) : { chart_spec: { chartType: '' } };
  const backends = backendsWith(input.chart_spec.chartType);

  it('uses a template that every backend has', () => {
    assert.deepEqual(backends, ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel']);
  });

  for (const backend of backends) {
    it(`assembles on ${backend} without warnings`, () => {
      assert.deepEqual(assembleWarnings(backend, input), []);
    });
  }
});
