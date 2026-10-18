// Fixtures and expectations (design §7.9, §10.3): flint-chart/test-data TestCases mapped to ChartAssemblyInput
// documents, the crafted cases of probe-cases.ts, and the outcome of each of the five assemblers on each of them.
//
// Determinism: TZ=UTC and a Park-Miller generator (seed 42) installed in place of Math.random before
// flint-chart/test-data is imported. Both are process-wide, so extractFixtures() must run in a fresh process
// (npm run fixtures); cli.ts never imports flint-chart before calling it.
import { canonicalJson } from '../ir.ts';
import { probeCases } from './probe-cases.ts';

export const FIXTURE_SEED = 42;
export const FIXTURE_TZ = 'UTC';

/** The fixed backend order of the IR (design §7.3). */
export const BACKENDS = ['vegalite', 'echarts', 'chartjs', 'plotly', 'excel'] as const;
export type Backend = (typeof BACKENDS)[number];

/** The parts of the upstream `TestCase` (dist/types-2j8bDxoT.d.ts) the mapping reads or drops. */
export interface TestCaseLike {
  title: string;
  description?: string;
  tags?: string[];
  chartType: string;
  data: Record<string, unknown>[];
  fields: { id: string; name: string; source?: string; tableRef?: string }[];
  metadata?: Record<string, { type?: string; semanticType?: string; levels?: unknown[] } | undefined>;
  encodingMap?: Record<string, EncodingItemLike | undefined>;
  chartProperties?: Record<string, unknown>;
  assembleOptions?: Record<string, unknown>;
  semanticAnnotations?: Record<string, unknown>;
}

interface EncodingItemLike {
  fieldID?: string;
  dtype?: string;
  aggregate?: string;
  sortOrder?: string;
  sortBy?: string;
  scheme?: string;
}

export type Input = Record<string, unknown>;

export type Outcome = { throws: true; error: string } | { throws: false; warnings: string[] };

export interface CaseLine {
  id: string;
  title: string;
  chartType: string;
  defaultBackend: Backend;
  input: Input;
}

export interface ExpectationLine {
  id: string;
  backends: Partial<Record<Backend, Outcome>>;
}

export interface FixtureManifest {
  upstream: string;
  integrity: string;
  cases: number;
  probeCases: number;
  tz: string;
  seed: number;
  excludedNondeterministic: string[];
}

export interface FixtureSet {
  cases: CaseLine[];
  expectations: ExpectationLine[];
  probeCases: CaseLine[];
  probeExpectations: ExpectationLine[];
  manifest: FixtureManifest;
}

const isEmpty = (v: unknown) => v === undefined || v === null || v === '';

/** The TestCase -> ChartAssemblyInput mapping of design §7.9. Upstream's own converters are not used. */
export function mapTestCase(tc: TestCaseLike): Input {
  const semanticTypes: Record<string, unknown> = {};
  for (const field of Object.keys(tc.metadata ?? {})) {
    const annotation = tc.semanticAnnotations?.[field] ?? tc.metadata?.[field]?.semanticType;
    if (!isEmpty(annotation)) semanticTypes[field] = annotation;
  }

  const encodings: Record<string, Record<string, unknown>> = {};
  for (const [channel, item] of Object.entries(tc.encodingMap ?? {})) {
    if (!item?.fieldID) continue;
    const encoding: Record<string, unknown> = {
      field: tc.fields.find((f) => f.id === item.fieldID)?.name,
      type: item.dtype,
      aggregate: item.aggregate,
      sortOrder: item.sortOrder,
      sortBy: item.sortBy,
      scheme: item.scheme,
    };
    for (const key of Object.keys(encoding)) if (isEmpty(encoding[key])) delete encoding[key];
    encodings[channel] = encoding;
  }

  const chartSpec: Record<string, unknown> = { chartType: tc.chartType, encodings };
  if (tc.chartProperties !== undefined) chartSpec.chartProperties = tc.chartProperties;

  const input: Input = { data: { values: tc.data } };
  if (Object.keys(semanticTypes).length > 0) input.semantic_types = semanticTypes;
  input.chart_spec = chartSpec;
  if (tc.assembleOptions !== undefined) input.options = tc.assembleOptions;
  return input;
}

const GROUP_PREFIX: Record<string, Backend> = { 'ECharts: ': 'echarts', 'Chart.js: ': 'chartjs', 'Plotly: ': 'plotly' };

/**
 * The backend the upstream gallery shows a group on: the group prefix (`ECharts: `, `Chart.js: `, `Plotly: `), else
 * Vega-Lite; when that backend lacks the template, the first backend in the fixed order that has it.
 */
export function defaultBackend(group: string, backendsWithTemplate: readonly string[]): Backend {
  const prefix = Object.keys(GROUP_PREFIX).find((p) => group.startsWith(p));
  const preferred: Backend = prefix ? GROUP_PREFIX[prefix] : 'vegalite';
  if (backendsWithTemplate.includes(preferred)) return preferred;
  const first = BACKENDS.find((b) => backendsWithTemplate.includes(b));
  if (!first) throw new Error(`No backend has the template of group "${group}".`);
  return first;
}

/**
 * Runs one assembler on a copy of the input: a throw with its message, or the sorted distinct warning codes
 * (`_warnings` on the four layout backends, the top-level `warnings` array of the Excel artifact).
 */
export function recordOutcome(assemble: (input: Input) => unknown, input: Input): Outcome {
  let result: unknown;
  try {
    result = assemble(structuredClone(input));
  } catch (e) {
    return { throws: true, error: e instanceof Error ? e.message : String(e) };
  }
  const r = result as { _warnings?: { code: string }[]; warnings?: { code: string }[] };
  const codes = (r?._warnings ?? r?.warnings ?? []).map((w) => w.code);
  return { throws: false, warnings: [...new Set(codes)].sort() };
}

/** Replaces Math.random with the Park-Miller minimal standard generator (design §10.3 generator determinism). */
export function installSeededRandom(seed: number = FIXTURE_SEED): void {
  let state = seed;
  Math.random = () => (state = (state * 16807) % 2147483647) / 2147483647;
}

type Assembler = (input: Input) => unknown;
type TemplateLookup = (chartType: string) => unknown;

/**
 * Builds the fixture set from the installed flint-chart. Sets TZ, seeds Math.random and only then imports
 * flint-chart and flint-chart/test-data, so it must be the first use of either in the process.
 */
export async function extractFixtures(upstream: { version: string; integrity: string }): Promise<FixtureSet> {
  process.env.TZ = FIXTURE_TZ;
  installSeededRandom();
  const flint = (await import('flint-chart')) as unknown as Record<string, Assembler & TemplateLookup>;
  const { TEST_GENERATORS } = (await import('flint-chart/test-data')) as unknown as {
    TEST_GENERATORS: Record<string, () => TestCaseLike[]>;
  };
  const assemblers: Record<Backend, [Assembler, TemplateLookup]> = {
    vegalite: [flint.assembleVegaLite, flint.vlGetTemplateDef],
    echarts: [flint.assembleECharts, flint.ecGetTemplateDef],
    chartjs: [flint.assembleChartjs, flint.cjsGetTemplateDef],
    plotly: [flint.assemblePlotly, flint.plGetTemplateDef],
    excel: [flint.assembleExcel, flint.excelGetTemplateDef],
  };

  const cases: CaseLine[] = [];
  const expectations: ExpectationLine[] = [];
  const probeLines: CaseLine[] = [];
  const probeExpectations: ExpectationLine[] = [];
  const warn = console.warn;
  console.warn = () => {}; // Vega-Lite warns on an invalid sortBy; the outcome records nothing from the console.
  try {
    for (const [group, generate] of Object.entries(TEST_GENERATORS)) {
      generate().forEach((tc, index) => {
        const id = `${group}|#${index}`;
        const input = mapTestCase(tc);
        const backends = BACKENDS.filter((b) => assemblers[b][1](tc.chartType));
        cases.push({ id, title: tc.title, chartType: tc.chartType, defaultBackend: defaultBackend(group, backends), input });
        const outcomes: Partial<Record<Backend, Outcome>> = {};
        for (const b of backends) outcomes[b] = recordOutcome(assemblers[b][0], input);
        expectations.push({ id, backends: outcomes });
      });
    }
    probeCases().forEach((pc, index) => {
      const id = `Probe|#${index}`;
      const withTemplate = BACKENDS.filter((b) => assemblers[b][1](pc.chartType));
      const backends = pc.allBackends || withTemplate.length === 0 ? [...BACKENDS] : withTemplate;
      probeLines.push({ id, title: pc.title, chartType: pc.chartType, defaultBackend: backends[0], input: pc.input });
      const outcomes: Partial<Record<Backend, Outcome>> = {};
      for (const b of backends) outcomes[b] = recordOutcome(assemblers[b][0], pc.input);
      probeExpectations.push({ id, backends: outcomes });
    });
  } finally {
    console.warn = warn;
  }

  return {
    cases,
    expectations,
    probeCases: probeLines,
    probeExpectations,
    manifest: {
      upstream: upstream.version,
      integrity: upstream.integrity,
      cases: cases.length,
      probeCases: probeLines.length,
      tz: FIXTURE_TZ,
      seed: FIXTURE_SEED,
      excludedNondeterministic: [],
    },
  };
}

/** The fixture folder of one upstream version (design §10.2): tests/fixtures/flint-chart@<version>/. */
export const fixtureFolder = (version: string) => `flint-chart@${version}`;

/**
 * The files of the fixture folder, relative to it. `known-failures.txt` is written empty only when it does not exist
 * yet: its ids come from the .NET round-trip, not from this step.
 */
export function fixtureFiles(set: FixtureSet): { path: string; content: string }[] {
  const jsonl = (lines: unknown[]) => lines.map((l) => JSON.stringify(l)).join('\n') + '\n';
  return [
    { path: 'cases.jsonl', content: jsonl(set.cases) },
    { path: 'expectations.jsonl', content: jsonl(set.expectations) },
    { path: 'manifest.json', content: canonicalJson(set.manifest) },
    { path: 'probe-cases.jsonl', content: jsonl(set.probeCases) },
    { path: 'probe-expectations.jsonl', content: jsonl(set.probeExpectations) },
  ];
}

export const KNOWN_FAILURES_FILE = 'known-failures.txt';
