// Assembles every emitted sample (samples/out/*.json, written by `dotnet run --project samples/Graspea.Flint.Samples`)
// with the pinned flint-chart on every backend that has the sample's template (design part 3 §10.2 and §10.3,
// "Contract-node: emitted samples"). The pinned package is the generator's: generator/package-lock.json is the only pin.
import { describe, it, before } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { assembleWarnings, backendsWith, root, version } from './flint.js';

const samplesDir = process.env.FLINT_SAMPLES_DIR ?? join(root, 'samples', 'out');

// The samples the Samples project writes; a sample that silently disappears fails here.
const EXPECTED = [
  'area-energy-mix.json',
  'bar-sales-by-region.json',
  'boxplot-temperatures.json',
  'bubble-cities.json',
  'bullet-sales-targets.json',
  'calendar-rainfall.json',
  'density-temperatures.json',
  'donut-device-share.json',
  'funnel-checkout.json',
  'grouped-bar-population.json',
  'heatmap-visits.json',
  'histogram-temperatures.json',
  'kpi-monthly-revenue.json',
  'line-temperature.json',
  'model-candlestick.json',
  'model-radar.json',
  'model-treemap.json',
  'model-waterfall.json',
  'options-bar.json',
  'pie-sales-channels.json',
  'scatter-cities.json',
  'stacked-bar-sales-by-quarter.json',
  'static-series-energy.json',
  'themed-line.json',
];

function sampleFiles() {
  if (!existsSync(samplesDir)) return [];
  return readdirSync(samplesDir).filter((f) => f.endsWith('.json')).sort();
}

const files = sampleFiles();

describe('samples', () => {
  before(() => {
    assert.ok(files.length > 0, `no samples in ${samplesDir}; run dotnet run --project samples/Graspea.Flint.Samples first`);
  });

  it('uses the flint-chart version the IR was extracted from', () => {
    const ir = JSON.parse(readFileSync(join(root, 'generator', 'ir', 'flint-ir.json'), 'utf8'));
    assert.equal(version, ir.upstream.version);
  });

  it('writes exactly the expected sample files', () => {
    assert.deepEqual(files, EXPECTED);
  });

  for (const file of files) {
    const input = JSON.parse(readFileSync(join(samplesDir, file), 'utf8'));
    const chartType = input.chart_spec.chartType;
    const backends = backendsWith(chartType);

    describe(file, () => {
      it('has a template on at least one backend', () => {
        assert.ok(backends.length > 0, `no backend has "${chartType}"`);
      });

      for (const backend of backends) {
        it(`assembles on ${backend} without warnings`, () => {
          assert.deepEqual(assembleWarnings(backend, input), []);
        });
      }
    });
  }
});
