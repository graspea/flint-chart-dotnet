import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fluentModel } from '../src/emit/fluent.ts';
import { wireValue } from '../src/emit/enums.ts';
import { BACKENDS, recordOutcome, type Backend, type CaseLine, type ExpectationLine, type Input } from '../src/extract/fixtures.ts';
import { loadOverrides } from '../src/overrides.ts';
import type { Ir } from '../src/ir.ts';

// Runs every emitted enum value through the installed flint-chart, on each backend that offers it.
describe('enum wire values against flint-chart', () => {
  const ir = JSON.parse(readFileSync(new URL('../ir/flint-ir.json', import.meta.url), 'utf8')) as Ir;
  const folder = new URL(`../../tests/fixtures/flint-chart@${ir.upstream.version}/`, import.meta.url);
  const jsonl = <T>(file: string) =>
    readFileSync(new URL(file, folder), 'utf8').split('\n').filter((l) => l.length > 0).map((l) => JSON.parse(l) as T);
  const cases = jsonl<CaseLine>('cases.jsonl');
  const expectations = new Map(jsonl<ExpectationLine>('expectations.jsonl').map((e) => [e.id, e]));

  // source: core/normalize-properties.ts (a value is accepted when === an option value or when it is a string equal to an
  // option label, case-insensitive; anything else is dropped with invalid-option-value)
  it('writes no value that flint-chart drops as invalid-option-value', async () => {
    const flint = (await import('flint-chart')) as unknown as Record<string, (input: Input) => unknown>;
    const assemblers: Record<Backend, (input: Input) => unknown> = {
      vegalite: flint.assembleVegaLite,
      echarts: flint.assembleECharts,
      chartjs: flint.assembleChartjs,
      plotly: flint.assemblePlotly,
      excel: flint.assembleExcel,
    };
    const enums = fluentModel(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname)).flatMap((t) =>
      t.properties.flatMap((p) => (p.enum ? [p.enum] : [])),
    );
    const dropped: string[] = [];
    let checked = 0;
    const warn = console.warn;
    console.warn = () => {};
    try {
      for (const e of enums) {
        for (const m of e.members) {
          const value = wireValue(m);
          if (value === undefined) continue;
          for (const b of BACKENDS.filter((x) => m.backends.includes(x))) {
            const base = cases.find((c) => c.chartType === e.chartType && expectations.get(c.id)?.backends[b]?.throws === false);
            assert.ok(base, `no assembling fixture of ${e.chartType} on ${b}`);
            const input = structuredClone(base.input) as { chart_spec: Record<string, unknown> };
            input.chart_spec.chartProperties = { [e.key]: value };
            const outcome = recordOutcome(assemblers[b], input);
            checked++;
            if (outcome.throws || outcome.warnings.includes('invalid-option-value')) dropped.push(`${e.name}.${m.name} on ${b}`);
          }
        }
      }
    } finally {
      console.warn = warn;
    }
    assert.ok(checked > 0);
    assert.deepEqual(dropped, []);
  });
});
