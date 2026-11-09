// Runtime probes behind the facts the design cites as probed (design §7.2): null handling and row shapes, the backends
// that apply the theme (by preset id or an object's `extends`), names of Object.prototype used as ids, the inference of the values .NET writes, the Date.parse results the validator relies on, the channel types it ports, and the channels the Excel and Image-Charts assemblers read per chart type.
// `npm run generate` (its fixtures step) records the output per version in tests/fixtures/flint-chart@<version>/probes.json;
// a unit test that cites a probe fact names that file, and test/probes.test.ts keeps the pinned version's record equal to
// a fresh run.
import { probeChannelReads, type ChannelReads } from './extract/channel-reads.ts';
import { BACKENDS, type Backend, type CaseLine, type Input } from './extract/fixtures.ts';

/** The record's file name in the fixture folder of a version. */
export const PROBES_FILE = 'probes.json';

type Outcome = 'ok' | 'throws';

export interface ProbeResults {
  nullHandling: Record<string, Record<Backend, Outcome>>;
  prototypeKeys: Record<string, Record<Backend, Outcome>>;
  themeBackends: { unknownPresetThrows: Backend[]; unknownExtendsThrows: Backend[]; presetChangesOutput: Backend[] };
  valueInference: Record<string, string>;
  dateParse: Record<string, 'parses' | 'NaN'>;
  /** The type `resolveChannelSemantics` gives the probe's channel, with ` snapshots` when it reads the dates as snapshots. */
  channelTypes: Record<string, string>;
  /** The channels `assembleExcel` and `assembleImageCharts` read per chart type and base (src/extract/channel-reads.ts). */
  channelReads: ChannelReads;
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

/**
 * ISO forms (parse), strings without a digit (never parse), and legacy forms V8 also reads ("Stage 1", "Jan 2024", "1");
 * the wider ISO forms of upstream's `ISO_DATE` (a space for `T`, an offset without a colon, a longer fraction) and its
 * US slash dates, each with components in and out of range.
 */
const dateParseProbes = [
  '1', '2024', '2024-01-15', '2024-01-15T10:30:00Z', '2024-02-30', '2024-13', 'East', 'Jan 2024', 'Mar-Apr', 'May',
  'Monday January', 'Stage 1', 'Sat, Jan', 'now',
  '2024-00', '2024-01-00', '2024-01-32', '2024-01-15T24:00', '2024-01-15T25:00', '2024-01-15T10:60', '2024-01-15T10:00:60',
  '2024-01-15T10:00+0200', '2024-01-15T10:00:00.123456Z', '2024-01-15 10:00', '2024-01-15 10:00+0200', '2024-01-15 25:00',
  '2024-01-15 10:60', '1/15/2025', '2/30/2025', '12/31/2025', '0/15/2025', '1/0/2025', '13/1/2025', '12/32/2025', '13/45/2025',
];

const months = (from: number, count: number, step = 1) =>
  Array.from({ length: count }, (_, i) => new Date(Date.UTC(2024, from + i * step, 1)).toISOString().slice(0, 10));
const days = (count: number, step = 1) => Array.from({ length: count }, (_, i) => new Date(Date.UTC(2024, 0, 1 + i * step)).toISOString().slice(0, 10));

/**
 * One channel of a chart over one field (`resolveChannelSemantics` with the chart type, as every backend calls it): dates
 * on a banded axis read as snapshots or a time scale (`readsAsSnapshots`), the spellings a YearMonth or Date semantic type
 * rewrites (`parseTemporalByType`), and the strings untyped inference reads as dates (`isUnambiguousDateString`).
 */
const channelTypeProbes: Record<string, { chartType: string; values: unknown[]; semanticType?: string; channel?: string }> = {
  'banded x, 3 dates': { chartType: 'Bar Chart', values: ['2024-01-15', '2024-06-15', '2025-01-15'] },
  'line x, 3 dates': { chartType: 'Line Chart', values: ['2024-01-15', '2024-06-15', '2025-01-15'] },
  'banded y, 3 dates': { chartType: 'Bar Chart', values: ['2024-01-15', '2024-06-15', '2025-01-15'], channel: 'y' },
  'banded color, 3 dates': { chartType: 'Bar Chart', values: ['2024-01-15', '2024-06-15', '2025-01-15'], channel: 'color' },
  'banded x, 12 consecutive months': { chartType: 'Bar Chart', values: months(0, 12) },
  'banded x, 10 quarterly months': { chartType: 'Bar Chart', values: months(0, 10, 3) },
  'banded x, 9 consecutive months and one far': { chartType: 'Bar Chart', values: [...months(0, 9), '2026-01-01'] },
  'banded x, 10 weekly dates': { chartType: 'Bar Chart', values: days(10, 7) },
  'banded x, 9 consecutive days and one far': { chartType: 'Bar Chart', values: [...days(9), '2024-02-01'] },
  'banded x, 60 days': { chartType: 'Bar Chart', values: days(60) },
  'banded x, 10 hours UTC': { chartType: 'Bar Chart', values: Array.from({ length: 10 }, (_, i) => `2024-01-01T${String(i).padStart(2, '0')}:00:00Z`) },
  'banded x, 10 dates with an offset, 2 days apart': {
    chartType: 'Bar Chart',
    values: Array.from({ length: 10 }, (_, i) => `2024-01-${String(1 + i * 2).padStart(2, '0')}T00:00:00+00:00`),
  },
  'banded x, Year 2000 to 2008 and 2024': { chartType: 'Bar Chart', values: [...Array.from({ length: 9 }, (_, i) => 2000 + i), 2024], semanticType: 'Year' },
  'banded x, YearMonth numbers 202501, 202503, 202507': { chartType: 'Bar Chart', values: [202501, 202503, 202507], semanticType: 'YearMonth' },
  'banded x, YearMonth numbers, 12 consecutive': {
    chartType: 'Bar Chart',
    values: Array.from({ length: 12 }, (_, i) => 202401 + i),
    semanticType: 'YearMonth',
  },
  'line x, YearMonth numbers': { chartType: 'Line Chart', values: [202501, 202502, 202503], semanticType: 'YearMonth' },
  'line x, YearMonth spellings': { chartType: 'Line Chart', values: ['2025M01', 'Feb 2025', '03/2025', '2025-Apr'], semanticType: 'YearMonth' },
  'line x, Date spellings': { chartType: 'Line Chart', values: ['20250115', '2025/01/16', 'Jan 17, 2025', '18 January 2025'], semanticType: 'Date' },
  'line x, Date spellings with an invalid day': { chartType: 'Line Chart', values: ['2025/02/30', '2025/02/31'], semanticType: 'Date' },
  'line x, untyped "Jan 2025"': { chartType: 'Line Chart', values: ['Jan 2025', 'Feb 2025'] },
  'line x, untyped "Jan 15, 2025"': { chartType: 'Line Chart', values: ['Jan 15, 2025', 'Feb 15, 2025'] },
  'line x, untyped "2025/01/15"': { chartType: 'Line Chart', values: ['2025/01/15', '2025/02/15'] },
  'line x, untyped "2024-01-15 10:00"': { chartType: 'Line Chart', values: ['2024-01-15 10:00', '2024-01-16 10:00'] },
  'line x, untyped US slash dates': { chartType: 'Line Chart', values: ['1/15/2025', '2/30/2025'] },
  'line x, untyped "13/45/2025"': { chartType: 'Line Chart', values: ['13/45/2025', '1/15/2025'] },
  'line x, untyped "Stage 1"': { chartType: 'Line Chart', values: ['Stage 1', 'Stage 2'] },
};

const sortedBackends = (set: Set<Backend>) => BACKENDS.filter((b) => set.has(b));

/** Runs the probes; `cases` (the fixture and crafted cases of the set just extracted) supply the read probe's fixture bases. */
export async function runProbes(cases: readonly CaseLine[]): Promise<ProbeResults> {
  const flint = (await import('flint-chart')) as unknown as Record<string, Assembler> & {
    inferVisCategory: (values: unknown[]) => string;
    resolveChannelSemantics: (
      encodings: Record<string, unknown>,
      data: unknown[],
      semanticTypes: Record<string, string>,
      converted: undefined,
      context: { chartType: string },
    ) => Record<string, { type: string; snapshots?: boolean }>;
  };
  const assemblers: Record<Backend, Assembler> = {
    vegalite: flint.assembleVegaLite,
    echarts: flint.assembleECharts,
    chartjs: flint.assembleChartjs,
    plotly: flint.assemblePlotly,
    excel: flint.assembleExcel,
    'image-charts': flint.assembleImageCharts,
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

    const channelTypes = Object.fromEntries(
      Object.entries(channelTypeProbes).map(([name, { chartType, values, semanticType, channel = 'x' }]) => {
        const data = values.map((v, i) => ({ f: v, n: i + 1 }));
        const encodings = channel === 'x' ? { x: { field: 'f' }, y: { field: 'n' } } : { x: { field: 'n' }, [channel]: { field: 'f' } };
        const resolved = flint.resolveChannelSemantics(encodings, data, semanticType ? { f: semanticType } : {}, undefined, { chartType })[channel];
        return [name, `${resolved.type}${resolved.snapshots ? ' snapshots' : ''}`];
      }),
    );

    const channelReads = probeChannelReads(flint as unknown as Record<string, unknown>, cases);

    return {
      channelReads,
      channelTypes,
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
