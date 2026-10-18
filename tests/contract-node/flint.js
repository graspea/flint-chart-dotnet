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
};

/** The backends whose template registry has the chart type. */
export const backendsWith = (chartType) => Object.keys(BACKENDS).filter((b) => BACKENDS[b][1](chartType));

/** Assembles on one backend and returns the warnings as `code: message` lines. */
export function assembleWarnings(backend, input) {
  const result = BACKENDS[backend][0](structuredClone(input));
  // The four layout backends report on _warnings, the Excel artifact on its top-level warnings array.
  const warnings = (backend === 'excel' ? result.warnings : result._warnings) ?? [];
  return warnings.map((w) => `${w.code}: ${w.message}`);
}
