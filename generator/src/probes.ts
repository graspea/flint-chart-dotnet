// Runtime probes behind the facts the design cites as probed (design §7.2): null handling and row shapes, the backends
// that apply the theme (by preset id or an object's `extends`), names of Object.prototype used as ids, the inference of the values .NET writes, and the Date.parse results the validator relies on.
// `npm run probes` records the output in verify/probes.json; a unit test that cites a probe fact names this file, and
// test/probes.test.ts keeps the record equal to a fresh run.
import { BACKENDS, type Backend, type Input } from './extract/fixtures.ts';

type Outcome = 'ok' | 'throws';

export interface ProbeResults {
  nullHandling: Record<string, Record<Backend, Outcome>>;
  prototypeKeys: Record<string, Record<Backend, Outcome>>;
  themeBackends: { unknownPresetThrows: Backend[]; unknownExtendsThrows: Backend[]; presetChangesOutput: Backend[] };
  valueInference: Record<string, string>;
  dateParse: Record<string, 'parses' | 'NaN'>;
}

type Assembler = (input: Input) => unknown;

const rows = () => [
  { city: 'Seattle', revenue: 501479, cost: 10 },
  { city: 'Boston', revenue: 300000, cost: 20 },
];

/** A Bar Chart over two rows; each probe changes one thing. */
const base = (): Record<string, any> => ({
  data: { values: rows() },
  chart_spec: { chartType: 'Bar Chart', encodings: { x: { field: 'city' }, y: { field: 'revenue' } } },
});

const nullProbes: Record<string, (i: Record<string, any>) => void> = {
  'data null': (i) => (i.data = null),
  'data values empty': (i) => (i.data = { values: [] }),
  'data values empty, typed encodings': (i) => {
    i.data = { values: [] };
    i.chart_spec.encodings = { x: { field: 'city', type: 'nominal' }, y: { field: 'revenue', type: 'quantitative' } };
  },
  'data url only': (i) => (i.data = { url: 'https://example.com/rows.json' }),
  'encodings missing': (i) => delete i.chart_spec.encodings,
  'encodings empty': (i) => (i.chart_spec.encodings = {}),
  'channel null': (i) => (i.chart_spec.encodings.color = null),
  'theme_spec null': (i) => (i.theme_spec = null),
  'chartType unknown': (i) => (i.chart_spec.chartType = 'Nope Chart'),
  'encoding field null': (i) => (i.chart_spec.encodings.x.field = null),
  'row null': (i) => (i.data.values[1] = null),
  'row null first': (i) => (i.data.values[0] = null),
  'row number, no temporal key': (i) => i.data.values.splice(1, 0, 7),
  'row number with temporal x': (i) => {
    i.data = { values: [{ month: '2024-01-15', revenue: 1 }, 7, { month: '2024-02-15', revenue: 2 }] };
    i.chart_spec = { chartType: 'Line Chart', encodings: { x: { field: 'month' }, y: { field: 'revenue' } } };
  },
  'row number with an unbound temporal key': (i) => {
    i.data.values = i.data.values.map((r: Record<string, unknown>, n: number) => ({ ...r, day: `2024-01-1${n}` }));
    i.data.values.splice(1, 0, 7);
  },
  'tolerated: semantic_types null': (i) => (i.semantic_types = null),
  'tolerated: semantic type value null': (i) => (i.semantic_types = { revenue: null }),
  'tolerated: options null': (i) => (i.options = null),
  'tolerated: field_display_names null': (i) => (i.field_display_names = null),
  'tolerated: chartProperties null': (i) => (i.chart_spec.chartProperties = null),
  'tolerated: title null': (i) => (i.chart_spec.title = null),
  'tolerated: baseSize null': (i) => (i.chart_spec.baseSize = null),
  'tolerated: canvasSize null': (i) => (i.chart_spec.canvasSize = null),
  'tolerated: encoding type null': (i) => (i.chart_spec.encodings.x.type = null),
  'tolerated: unknown keys at every level': (i) => {
    i.interaction_spec = { brush: true };
    i.chart_spec.zzz = 1;
    i.chart_spec.encodings.x.zzz = 1;
    i.options = { zzz: 1 };
    i.semantic_types = { revenue: { semanticType: 'Amount', zzz: 1 } };
  },
};

/** Names of Object.prototype where upstream looks ids up in plain objects (theme presets, the semantic type registry). */
const prototypeKeyProbes: Record<string, (i: Record<string, any>) => void> = {
  'theme_spec "constructor"': (i) => (i.theme_spec = 'constructor'),
  'theme_spec extends "toString"': (i) => (i.theme_spec = { extends: 'toString' }),
  'semantic type "constructor" on a bound field': (i) => (i.semantic_types = { city: 'constructor' }),
  'semantic type "toString" on an unbound key of the first row': (i) => (i.semantic_types = { cost: 'toString' }),
  'semantic type "valueOf" on no field': (i) => (i.semantic_types = { zz: 'valueOf' }),
};

/** Values as System.Text.Json writes the .NET types of design §5.3; two of each, as a column. */
const inferenceProbes: Record<string, unknown[]> = {
  'DateOnly "2024-01-15"': ['2024-01-15', '2024-02-01'],
  'Guid, digit-leading': ['3f2504e0-4f89-11d3-9a0c-0305e82c3301', '1a2504e0-4f89-11d3-9a0c-0305e82c3301'],
  'Guid, letter-leading': ['f2504e0a-4f89-11d3-9a0c-0305e82c3301', 'a2504e0a-4f89-11d3-9a0c-0305e82c3301'],
  'TimeOnly "10:00:00"': ['10:00:00', '11:30:00'],
  'TimeSpan "1.02:03:04"': ['1.02:03:04', '1.05:00:00'],
  'TimeSpan "2.00:00:00"': ['2.00:00:00', '3.00:00:00'],
  Uri: ['https://example.com/a', 'https://example.org/b'],
  bool: [true, false],
  'empty string': ['', ' '],
  'nested object': [{ a: 1 }, { a: 2 }],
  'single-number array': [[1], [2]],
};

/** ISO forms (parse), strings without a digit (never parse), and legacy forms V8 also reads ("Stage 1", "Jan 2024", "1"). */
const dateParseProbes = [
  '1', '2024', '2024-01-15', '2024-01-15T10:30:00Z', '2024-02-30', '2024-13', 'East', 'Jan 2024', 'Mar-Apr', 'May',
  'Monday January', 'Stage 1', 'Sat, Jan', 'now',
];

const sortedBackends = (set: Set<Backend>) => BACKENDS.filter((b) => set.has(b));

export async function runProbes(): Promise<ProbeResults> {
  const flint = (await import('flint-chart')) as unknown as Record<string, Assembler> & {
    inferVisCategory: (values: unknown[]) => string;
  };
  const assemblers: Record<Backend, Assembler> = {
    vegalite: flint.assembleVegaLite,
    echarts: flint.assembleECharts,
    chartjs: flint.assembleChartjs,
    plotly: flint.assemblePlotly,
    excel: flint.assembleExcel,
  };
  const outcome = (b: Backend, input: Input): Outcome => {
    try {
      assemblers[b](input);
      return 'ok';
    } catch {
      return 'throws';
    }
  };

  const warn = console.warn;
  console.warn = () => {};
  try {
    const record = (probes: Record<string, (i: Record<string, any>) => void>) =>
      Object.fromEntries(
        Object.entries(probes).map(([name, change]) => [
          name,
          Object.fromEntries(
            BACKENDS.map((b) => {
              const input = base();
              change(input);
              return [b, outcome(b, input)];
            }),
          ) as Record<Backend, Outcome>,
        ]),
      );
    const nullHandling = record(nullProbes);
    const prototypeKeys = record(prototypeKeyProbes);

    const unknownPresetThrows = new Set<Backend>();
    const unknownExtendsThrows = new Set<Backend>();
    const presetChangesOutput = new Set<Backend>();
    const text = (v: unknown) => JSON.stringify(v, (_k, x) => (typeof x === 'function' ? '[function]' : x));
    for (const b of BACKENDS) {
      if (outcome(b, { ...base(), theme_spec: 'no-such-preset' }) === 'throws') unknownPresetThrows.add(b);
      if (outcome(b, { ...base(), theme_spec: { extends: 'no-such-preset' } }) === 'throws') unknownExtendsThrows.add(b);
      if (text(assemblers[b](base())) !== text(assemblers[b]({ ...base(), theme_spec: 'economist' }))) presetChangesOutput.add(b);
    }

    const valueInference = Object.fromEntries(
      Object.entries(inferenceProbes).map(([name, values]) => [name, flint.inferVisCategory(values)]),
    );

    const dateParse = Object.fromEntries(dateParseProbes.map((s) => [s, Number.isNaN(Date.parse(s)) ? 'NaN' : 'parses'])) as ProbeResults['dateParse'];

    return {
      dateParse,
      nullHandling,
      prototypeKeys,
      themeBackends: {
        unknownPresetThrows: sortedBackends(unknownPresetThrows),
        unknownExtendsThrows: sortedBackends(unknownExtendsThrows),
        presetChangesOutput: sortedBackends(presetChangesOutput),
      },
      valueInference,
    };
  } finally {
    console.warn = warn;
  }
}
