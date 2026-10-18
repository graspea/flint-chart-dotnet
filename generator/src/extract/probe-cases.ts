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

const map = (projectionCenter: unknown): Doc => ({
  data: { values: [{ lon: 10, lat: 50 }, { lon: 14, lat: 52 }] },
  chart_spec: { chartType: 'Map', encodings: { longitude: { field: 'lon' }, latitude: { field: 'lat' } }, chartProperties: { projectionCenter } },
});

const series = (...entries: unknown[]) => entries;

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
];

export function probeCases(): ProbeCase[] {
  return cases.map(([title, input, allBackends]) => ({ title, chartType: String(input.chart_spec.chartType), input, ...(allBackends ? { allBackends } : {}) }));
}
