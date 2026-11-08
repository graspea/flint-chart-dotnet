// The pinned flint-chart and its assemblers, shared by the contract-node tests. The pinned package is the generator's:
// generator/package-lock.json is the only pin.
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

process.env.TZ = 'UTC';

export const root = fileURLToPath(new URL('../../', import.meta.url));
const generatorRequire = createRequire(join(root, 'generator', 'package.json'));

export async function loadFlint() {
  // The package exports neither package.json nor a resolvable ESM path to require.resolve; both sit next to the
  // CommonJS entry (dist/index.cjs).
  const cjs = generatorRequire.resolve('flint-chart');
  const pkg = JSON.parse(readFileSync(join(dirname(cjs), '..', 'package.json'), 'utf8'));
  return { version: pkg.version, flint: await import(pathToFileURL(join(dirname(cjs), 'index.js')).href) };
}

export const { version, flint } = await loadFlint();

export const BACKENDS = {
  vegalite: [flint.assembleVegaLite, flint.vlGetTemplateDef],
  echarts: [flint.assembleECharts, flint.ecGetTemplateDef],
  chartjs: [flint.assembleChartjs, flint.cjsGetTemplateDef],
  plotly: [flint.assemblePlotly, flint.plGetTemplateDef],
  excel: [flint.assembleExcel, flint.excelGetTemplateDef],
  // No template registry: the chart-type map is the gate (src/image-charts/chart-types.ts), read as an own key, because
  // isImageChartsSupported uses `in`, which also finds Object.prototype names such as "constructor".
  'image-charts': [flint.assembleImageCharts, (chartType) => Object.hasOwn(flint.IMAGE_CHARTS_TYPE_MAP, chartType)],
};

/** The backends that have the chart type: a template registry entry, or a key of the Image-Charts chart-type map. */
export const backendsWith = (chartType) => Object.keys(BACKENDS).filter((b) => BACKENDS[b][1](chartType));

/** Assembles on one backend and returns the warnings as `code: message` lines. */
export function assembleWarnings(backend, input) {
  const result = BACKENDS[backend][0](structuredClone(input));
  if (backend === 'image-charts' && !(result.type === 'image-charts' && result.url?.startsWith('https://image-charts.com/chart?'))) {
    throw new Error(`image-charts returned no Image-Charts URL: ${JSON.stringify(result).slice(0, 200)}`);
  }
  // The four layout backends report on _warnings, the Excel artifact on its top-level warnings array. The Image-Charts
  // artifact ({ type, url }) has none; a top-level array a later flint-chart adds is read as Excel's is.
  const warnings = (backend === 'excel' || backend === 'image-charts' ? result.warnings : result._warnings) ?? [];
  return warnings.map((w) => `${w.code}: ${w.message}`);
}
