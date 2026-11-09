// Crafted contract cases (design §10.3): one small document per validator rule that the TEST_GENERATORS corpus never
// makes an assembler throw or warn on. The fixtures step records each one on every backend with recordOutcome, like the
// corpus, so the contract tests compare the validator with upstream on these rules too. Only documents the SDK model can
// express are listed (an absent `encodings` reads back as `{}`, a `null` theme_spec is not written).
import type { Input } from './fixtures.ts';

export interface ProbeCase {
  title: string;
  chartType: string;
  input: Input;
  /** Record every backend, not only the backends that have the template (a throw on a missing template). */
  allBackends?: true;
}

const rows = () => [
  { city: 'Seattle', revenue: 501479, cost: 10 },
  { city: 'Boston', revenue: 300000, cost: 20 },
];

type Doc = { data: any; chart_spec: any; [key: string]: any };

/** A Bar Chart of revenue by city; each case changes one thing. */
const bar = (change: (i: Doc) => void = () => {}): Doc => {
  const input: Doc = { data: { values: rows() }, chart_spec: { chartType: 'Bar Chart', encodings: { x: { field: 'city' }, y: { field: 'revenue' } } } };
  change(input);
  return input;
};

const line = (change: (i: Doc) => void = () => {}): Doc =>
  bar((i) => {
    i.chart_spec.chartType = 'Line Chart';
    change(i);
  });

/** The same document as another chart type. */
const chart = (chartType: string, change: (i: Doc) => void = () => {}): Doc =>
  bar((i) => {
    i.chart_spec.chartType = chartType;
    change(i);
  });

const map = (projectionCenter: unknown): Doc => ({
  data: { values: [{ lon: 10, lat: 50 }, { lon: 14, lat: 52 }] },
  chart_spec: { chartType: 'Map', encodings: { longitude: { field: 'lon' }, latitude: { field: 'lat' } }, chartProperties: { projectionCenter } },
});

const series = (...entries: unknown[]) => entries;

const sortBy = (value: string, change: (i: Doc) => void = () => {}): Doc =>
  bar((i) => {
    i.chart_spec.encodings.x.sortBy = value;
    change(i);
  });

const monthStarts = (count: number) => Array.from({ length: count }, (_, i) => new Date(Date.UTC(2024, i, 1)).toISOString().slice(0, 10));

/** A document of the given rows; Excel reads them after convertTemporalData, which writes every value of a temporal key as a string. */
const table = (chartType: string, values: unknown[], encodings: unknown, semantic_types?: Record<string, string>): Doc => ({
  data: { values },
  chart_spec: { chartType, encodings },
  ...(semantic_types ? { semantic_types } : {}),
});

const ohlc = (dates: unknown[]): Doc =>
  table('Candlestick Chart', dates.map((day, i) => ({ day, open: 10 + i, high: 12 + i, low: 9 + i, close: 11 + i })),
    { x: { field: 'day' }, open: { field: 'open' }, high: { field: 'high' }, low: { field: 'low' }, close: { field: 'close' } });

/** A Boxplot over dates on x (Excel needs a categorical x): snapshots read as ordinal, a run of time as temporal. */
const boxplot = (dates: unknown[], semanticType?: string): Doc => ({
  data: { values: dates.flatMap((d, i) => [{ day: d, revenue: 10 + i }, { day: d, revenue: 20 + i }]) },
  chart_spec: { chartType: 'Boxplot', encodings: { x: { field: 'day' }, y: { field: 'revenue' } } },
  ...(semanticType ? { semantic_types: { day: semanticType } } : {}),
});

const cases: Array<[string, Doc, true?]> = [
  ['static series on x and y', line((i) => (i.chart_spec.encodings = { x: series({ field: 'city' }, { field: 'cost' }), y: series({ field: 'revenue' }, { field: 'cost' }) }))],
  ['static series on color', line((i) => (i.chart_spec.encodings.color = series({ field: 'revenue' }, { field: 'cost' })))],
  ['static series with one entry', line((i) => (i.chart_spec.encodings.y = series({ field: 'revenue' })))],
  ['static series with a duplicate field', line((i) => (i.chart_spec.encodings.y = series({ field: 'revenue' }, { field: 'revenue' })))],
  ['static series entry without a field', line((i) => (i.chart_spec.encodings.y = series({ field: 'revenue' }, {})))],
  ['static series entry with an empty field', line((i) => (i.chart_spec.encodings.y = series({ field: '' }, { field: 'cost' })))],
  ['static series field not in the rows', line((i) => (i.chart_spec.encodings.y = series({ field: 'revenue' }, { field: 'profit' })))],
  ['static series with color bound', line((i) => {
    i.chart_spec.encodings.y = series({ field: 'revenue' }, { field: 'cost' });
    i.chart_spec.encodings.color = { field: 'city' };
  })],
  ['static series entry typed nominal', line((i) => (i.chart_spec.encodings.y = series({ field: 'revenue' }, { field: 'cost', type: 'nominal' })))],
  ['static series', line((i) => (i.chart_spec.encodings.y = series({ field: 'revenue' }, { field: 'cost' })))],
  ['data null', bar((i) => (i.data = null))],
  ['data values empty', bar((i) => (i.data = { values: [] }))],
  ['data values empty, typed encodings', bar((i) => {
    i.data = { values: [] };
    i.chart_spec.encodings = { x: { field: 'city', type: 'nominal' }, y: { field: 'revenue', type: 'quantitative' } };
  })],
  ['data url only', bar((i) => (i.data = { url: 'https://example.com/rows.json' }))],
  ['encodings null', bar((i) => (i.chart_spec.encodings = null))],
  ['encodings empty', bar((i) => (i.chart_spec.encodings = {}))],
  ['channel null', bar((i) => (i.chart_spec.encodings.color = null))],
  ['encoding field null', bar((i) => (i.chart_spec.encodings.x.field = null))],
  ['chart type unknown', bar((i) => (i.chart_spec.chartType = 'Nope Chart')), true],
  ['chart type without a template on some backends', map(undefined), true],
  ['theme preset unknown', bar((i) => (i.theme_spec = 'no-such-preset'))],
  ['theme extends unknown', bar((i) => (i.theme_spec = { extends: 'no-such-preset' }))],
  ['theme preset named like an Object.prototype member', bar((i) => (i.theme_spec = 'constructor'))],
  ['theme extends named like an Object.prototype member', bar((i) => (i.theme_spec = { extends: 'toString' }))],
  ['row null', bar((i) => (i.data.values[1] = null))],
  ['row null first', bar((i) => (i.data.values[0] = null))],
  ['row number, no temporal key', bar((i) => i.data.values.splice(1, 0, 7))],
  ['row string with a temporal x', line((i) => {
    i.data = { values: [{ month: '2024-01-15', revenue: 1 }, 'x', { month: '2024-02-15', revenue: 2 }] };
    i.chart_spec.encodings.x = { field: 'month' };
  })],
  ['row array', bar((i) => i.data.values.splice(1, 0, [1]))],
  ['option value invalid', line((i) => (i.chart_spec.chartProperties = { interpolate: 'zzz' }))],
  ['option value given as its label', line((i) => (i.chart_spec.chartProperties = { interpolate: ' monotone (SMOOTH) ' }))],
  ['option array value', map([10, 50])],
  ['option array value given as its label', map('Europe [10, 50]')],
  ['semantic type named like an Object.prototype member on a bound field', bar((i) => (i.semantic_types = { city: 'constructor' }))],
  ['semantic type named like an Object.prototype member on a key of the first row', bar((i) => (i.semantic_types = { cost: 'toString' }))],
  ['semantic type named like an Object.prototype member on no field', bar((i) => (i.semantic_types = { zz: 'valueOf' }))],
  ['semantic type unregistered', bar((i) => (i.semantic_types = { revenue: 'Revenue' }))],
  ['data absent', bar((i) => delete i.data)],
  ['sortBy x', sortBy('x')],
  ['sortBy size', sortBy('size')],
  ['sortBy blank', sortBy('  ')],
  ['sortBy a data field', sortBy('cost')],
  ['sortBy a field of a later row only', sortBy('cost', (i) => (i.data.values = [{ city: 'Seattle', revenue: 1 }, { city: 'Boston', revenue: 2, cost: 3 }]))],
  ['sortBy color, color unbound', sortBy('color')],
  ['sortBy color, color bound', sortBy('color', (i) => (i.chart_spec.encodings.color = { field: 'city' }))],
  ['sortBy y, y counted', sortBy('y', (i) => (i.chart_spec.encodings.y = { aggregate: 'count' }))],
  ['sortBy a JSON array of categories', sortBy('["Boston","Seattle"]')],
  ['sortBy an empty JSON array', sortBy('[]')],
  ['sortBy a JSON array with an invalid value', sortBy('[1,null]')],
  ['sortBy a JSON array of invalid values', sortBy('[null,{"a":1}]')],
  ['sortBy a JSON object', sortBy('{"a":1}')],
  ['sortBy a JSON number', sortBy('5')],
  ['sortOrder invalid', bar((i) => (i.chart_spec.encodings.x.sortOrder = 'zzz'))],
  ['sortOrder valid', bar((i) => (i.chart_spec.encodings.x.sortOrder = 'descending'))],
  ['boxplot x of 3 dates', boxplot(['2024-01-15', '2024-06-15', '2025-01-15'])],
  ['boxplot x of 12 consecutive months', boxplot(monthStarts(12))],
  ['boxplot x of 9 consecutive months and one far', boxplot([...monthStarts(9), '2026-01-01'])],
  ['boxplot x of 12 consecutive YearMonth numbers', boxplot(Array.from({ length: 12 }, (_, i) => 202401 + i), 'YearMonth')],
  ['boxplot x of 3 YearMonth numbers', boxplot([202501, 202503, 202507], 'YearMonth')],
  ['boxplot x of 12 YearMonth spellings', boxplot(['Jan 2024', 'Feb 2024', 'Mar 2024', 'Apr 2024', 'May 2024', 'Jun 2024', 'Jul 2024', 'Aug 2024', 'Sep 2024', 'Oct 2024', 'Nov 2024', 'Dec 2024'], 'YearMonth')],
  ['sparkline faceted on column', chart('Sparkline', (i) => (i.chart_spec.encodings.column = { field: 'city' }))],
  ['sparkline without y', chart('Sparkline', (i) => delete i.chart_spec.encodings.y)],
  ['pie from x and y', chart('Pie Chart')],
  ['pie with a slice field and no value', chart('Pie Chart', (i) => (i.chart_spec.encodings = { color: { field: 'city' } }))],
  ['pie with color unbound, x and size', chart('Pie Chart', (i) => (i.chart_spec.encodings = { color: { type: 'nominal' }, x: { field: 'city' }, size: { field: 'revenue' } }))],
  ['pie with an empty color field, x and size', chart('Pie Chart', (i) => (i.chart_spec.encodings = { color: { field: '' }, x: { field: 'city' }, size: { field: 'revenue' } }))],
  ['radar with two metrics', chart('Radar Chart')],
  ['scatter without rows', chart('Scatter Plot', (i) => {
    i.data = { values: [] };
    i.chart_spec.encodings = { x: { field: 'revenue' }, y: { field: 'cost' } };
  })],
  ['scatter with data url only', chart('Scatter Plot', (i) => {
    i.data = { url: 'https://example.com/rows.json' };
    i.chart_spec.encodings = { x: { field: 'revenue' }, y: { field: 'cost' } };
  })],
  ['facet bound with an empty field', bar((i) => (i.chart_spec.encodings.column = { field: '' }))],
  ['donut with a slice field and no value', chart('Donut Chart', (i) => (i.chart_spec.encodings = { color: { field: 'city' } }))],
  ['area without y', chart('Area Chart', (i) => delete i.chart_spec.encodings.y)],
  ['radar without x', chart('Radar Chart', (i) => delete i.chart_spec.encodings.x)],
  ['stacked bar from x and color', chart('Stacked Bar Chart', (i) => (i.chart_spec.encodings = { x: { field: 'city' }, color: { field: 'city' } }))],
  ['grouped bar from y and group', chart('Grouped Bar Chart', (i) => (i.chart_spec.encodings = { y: { field: 'revenue' }, group: { field: 'city' } }))],
  ['scatter without y', chart('Scatter Plot', (i) => (i.chart_spec.encodings = { x: { field: 'revenue' } }))],
  ['scatter without x', chart('Scatter Plot', (i) => (i.chart_spec.encodings = { y: { field: 'cost' } }))],
  ['pie faceted on row', chart('Pie Chart', (i) => (i.chart_spec.encodings = { color: { field: 'city' }, size: { field: 'revenue' }, row: { field: 'city' } }))],
  ['donut faceted on column', chart('Donut Chart', (i) => (i.chart_spec.encodings = { color: { field: 'city' }, size: { field: 'revenue' }, column: { field: 'city' } }))],
  ['stacked bar faceted on row', chart('Stacked Bar Chart', (i) => {
    i.chart_spec.encodings.color = { field: 'city' };
    i.chart_spec.encodings.row = { field: 'city' };
  })],
  ['grouped bar faceted on column', chart('Grouped Bar Chart', (i) => {
    i.chart_spec.encodings.group = { field: 'city' };
    i.chart_spec.encodings.column = { field: 'city' };
  })],
  ['pie with an empty size field and theta', chart('Pie Chart', (i) => (i.chart_spec.encodings = { color: { field: 'city' }, size: { field: '' }, theta: { field: 'revenue' } }))],
  ['pie with an empty theta field and y', chart('Pie Chart', (i) => (i.chart_spec.encodings = { color: { field: 'city' }, theta: { field: '' }, y: { field: 'revenue' } }))],
  ['grouped bar with a quantitative color and no group', chart('Grouped Bar Chart', (i) => (i.chart_spec.encodings = { x: { field: 'city' }, y: { field: 'revenue' }, color: { field: 'cost', type: 'quantitative' } }))],
  ['grouped bar with a nominal color and no group', chart('Grouped Bar Chart', (i) => (i.chart_spec.encodings = { x: { field: 'city' }, y: { field: 'revenue' }, color: { field: 'city', type: 'nominal' } }))],
  ['histogram with an empty x field and y', chart('Histogram', (i) => (i.chart_spec.encodings = { x: { field: '' }, y: { field: 'revenue' } }))],
  ['histogram with an empty x field and a numeric empty key', table('Histogram', [{ '': 202501, revenue: 10 }, { '': 202502, revenue: 20 }], { x: { field: '' }, y: { field: 'revenue' } })],
  ['histogram with an empty x field and a YearMonth empty key', table('Histogram', [{ '': 202501, revenue: 10 }, { '': 202502, revenue: 20 }], { x: { field: '' }, y: { field: 'revenue' } }, { '': 'YearMonth' })],
  ['histogram of a quantitative YearMonth field', table('Histogram', [{ month: 202501 }, { month: 202502 }], { x: { field: 'month', type: 'quantitative' } }, { month: 'YearMonth' })],
  ['radar with one YearMonth metric in three spellings', table('Radar Chart', [{ month: 202501, revenue: 10 }, { month: '2025-01', revenue: 11 }, { month: 'Jan 2025', revenue: 12 }], { x: { field: 'month', type: 'nominal' }, y: { field: 'revenue' } }, { month: 'YearMonth' })],
  ['pyramid with a date color and a null', table('Pyramid Chart', [{ age: 'a', wave: '2024-01-01', revenue: 1 }, { age: 'b', wave: '2025-01-01', revenue: 2 }, { age: 'a', wave: null, revenue: 3 }], { x: { field: 'revenue' }, y: { field: 'age' }, color: { field: 'wave' } })],
  ['pyramid with a Year color as numbers and strings', table('Pyramid Chart', [{ age: 'a', wave: 2024, revenue: 1 }, { age: 'b', wave: '2024', revenue: 2 }, { age: 'a', wave: 2025, revenue: 3 }], { x: { field: 'revenue' }, y: { field: 'age' }, color: { field: 'wave' } }, { wave: 'Year' })],
  ['waterfall of a quantitative YearMonth y', table('Waterfall Chart', [{ step: 'a', month: 202501 }, { step: 'b', month: 202502 }, { step: 'c', month: 405003 }], { x: { field: 'step' }, y: { field: 'month', type: 'quantitative' } }, { month: 'YearMonth' })],
  ['candlestick with a null first date', ohlc([null, '2025-01-02', '2025-01-03'])],
  ['candlestick with month-name dates out of order', ohlc(['Jan 2, 2025', 'Jan 1, 2025'])],
];

export function probeCases(): ProbeCase[] {
  return cases.map(([title, input, allBackends]) => ({ title, chartType: String(input.chart_spec.chartType), input, ...(allBackends ? { allBackends } : {}) }));
}
