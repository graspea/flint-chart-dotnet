// The channels the assemblers read per chart type, found by behaviour instead of by reading their source (design part 9
// §14.8.1; docs/generator.md "Failure policy" and "Probes"). The validator's native rules and undeclared-read exemptions
// (Excel.ReadsUndeclared, ImageCharts.ReadsUndeclared and their mirror in src/Graspea.Flint.CSharp/Spec/CatalogCheck.cs)
// are hand-written from src/excel and src/image-charts, which also call src/core helpers (normalizeChartEncodingAliases,
// resolveChannelSemantics, the banded overflow filter). On the four layout backends they are the template's declared
// channels and LayoutReads.ReadsUndeclared (src/Graspea.Flint/Validation/Rules/LayoutReads.cs, mirrored in
// CatalogCheck.cs), hand-written from src/echarts, src/plotly and src/vegalite.
//
// A channel is read under a base when binding it to a text or a number field (or, for a channel of the base, removing
// it) changes the assembled artifact (JSON.stringify, so functions drop) or the throw message. Math.random is reseeded
// before every assembly, so equal inputs give equal outputs.
//
// Bases: per backend and chart type, from the fixture cases of the set (cases.jsonl and probe-cases.jsonl), one per
// encoding shape (the sorted channel set; every value an object): the smallest by JSON length (then id) that still
// assembles once the probe fields are added; then the SYNTHETIC_BASES that assemble. The shape ignores types, so the
// type-conditional reads (an Excel Line Chart over numeric x and y reads size and order) come from the synthetic bases.
// The extract gate (registry.ts) has no fixture set and uses the synthetic bases only; their Image-Charts reads equal
// the full record's (test/probes.test.ts).
//
// The native backends (assembleExcel, assembleImageCharts) are probed on every catalog channel (probes.json
// channelReads.reads). The four layout backends (assembleVegaLite, assembleECharts, assembleChartjs, assemblePlotly)
// are probed on the channels their template does not declare only (channelReads.layoutReads). Their shared layout
// (legend space, label fit, band steps, palettes, measure domains, facets, the _pivot/_options sidecars) reacts to some
// of them, about 1.2 per base against 1.0.1, nearly all of them side effects that draw nothing from the field's values,
// so the C# test asserts only the ignored ones there: an undeclared channel the probe sees ignored is
// unsupported_channel. Some artifacts format numbers and dates in the ICU default locale (toLocaleString,
// toLocaleDateString without a locale); cli.ts pins TZ, not the locale, and against 1.0.1 the reads are the same under
// en-US, en-IN and de-DE.
//
// extract checks the Image-Charts reads against overrides.json imageChartsReads (registry.ts); the fixtures step records
// the reads per base in probes.json channelReads, which tests/Graspea.Flint.Tests/Parity/ChannelReadsTests.cs checks
// the validator against.
import { ordinal } from '../ir.ts';
import { FIXTURE_SEED, installSeededRandom, type CaseLine, type Input } from './fixtures.ts';

type Encodings = Record<string, { field: string }>;

/** The native backends probed on every catalog channel, in the fixed backend order. */
export const READ_BACKENDS = ['excel', 'image-charts'] as const;
export type ReadBackend = (typeof READ_BACKENDS)[number];

/** The layout backends, probed on the channels their template does not declare: the assembler and the template list. */
export const LAYOUT_READ_BACKENDS = {
  vegalite: ['assembleVegaLite', 'vlAllTemplateDefs'],
  echarts: ['assembleECharts', 'ecAllTemplateDefs'],
  chartjs: ['assembleChartjs', 'cjsAllTemplateDefs'],
  plotly: ['assemblePlotly', 'plAllTemplateDefs'],
} as const;

/** The rows of the synthetic bases: a category and two measures. */
export const SYNTHETIC_ROWS = [
  { category: 'A', measure: 3, measure2: 30 },
  { category: 'B', measure: 1, measure2: 10 },
  { category: 'C', measure: 2, measure2: 20 },
];

/**
 * The synthetic bases, over SYNTHETIC_ROWS. `x category, y measure` leaves color, size and theta without a field, so a
 * Pie or Donut Chart reads x as the slice and y as the value (image-charts/assemble.ts:265-266,
 * excel/assemble.ts:277-281); `x and y measures` is the one a Scatter Plot assembles from, and an Excel Line Chart over
 * it reads size and order (excel/assemble.ts:294-295, :309-314); `slice and value` binds the channels that shadow those
 * fallbacks; `y measure` leaves an Excel Histogram its y fallback (excel/templates/histogram.ts:19, :25);
 * `y category, x measure` is a horizontal bar.
 */
export const SYNTHETIC_BASES: Record<string, Encodings> = {
  'x category, y measure': { x: { field: 'category' }, y: { field: 'measure' } },
  'x and y measures': { x: { field: 'measure' }, y: { field: 'measure2' } },
  'slice and value': { color: { field: 'category' }, size: { field: 'measure' } },
  'y measure': { y: { field: 'measure' } },
  'y category, x measure': { x: { field: 'measure' }, y: { field: 'category' } },
};

/**
 * The two fields a probed channel is bound to, added to every row of a base: row i takes entry i % length. Text makes a
 * channel a category or a series, a number a measure (a quantitative color is no series, image-charts/assemble.ts:205-209).
 * The cycles start out of order and have lengths 5 and 7, so that a sort by the probed field moves rows even when a
 * base's own series repeat every two or three rows.
 */
export const PROBE_VALUES = {
  probe_text: ['k3', 'k2', 'k1', 'k2', 'k3'],
  probe_number: [100, 63, 26, 90, 53, 16, 80],
};

export interface ReadBase {
  name: string;
  input: Input;
}

export interface ChannelReads {
  /** A fixture base names its case (`cases.jsonl` or `probe-cases.jsonl`); a synthetic base its encodings over `rows`. */
  bases: Record<string, { case: string } | { encodings: Encodings }>;
  probe: typeof PROBE_VALUES;
  /** Backend -> chart type -> base -> the channels whose binding (or removal) changes the artifact or the throw, sorted. */
  reads: Record<string, Record<string, Record<string, string[]>>>;
  /** Layout backend -> chart type -> base -> the channels the template does not declare whose binding (or removal)
   * changes the artifact or the throw, sorted; every other undeclared channel is ignored under that base. */
  layoutReads: Record<string, Record<string, Record<string, string[]>>>;
  rows: typeof SYNTHETIC_ROWS;
}

/** The channels of IR catalog.channels (registry.ts): the core list and every channel a registry template declares. */
export function catalogChannels(flint: Record<string, unknown>): string[] {
  const declared = Object.keys(flint)
    .filter((k) => k.endsWith('AllTemplateDefs'))
    .flatMap((k) => (flint[k] as Array<{ channels?: string[] }>).flatMap((d) => d.channels ?? []));
  return [...new Set([...(flint.channels as readonly string[]), ...declared])];
}

const isPlainObject = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);

/** The input with the probe fields added to every row, or null when the rows are not a non-empty array of objects. */
export function withProbeFields(input: Input): Input | null {
  const data = input.data as { values?: unknown } | undefined;
  const values = data?.values;
  if (!Array.isArray(values) || values.length === 0 || !values.every(isPlainObject)) return null;
  const rows = values.map((r, i) => ({
    ...r,
    probe_text: PROBE_VALUES.probe_text[i % PROBE_VALUES.probe_text.length],
    probe_number: PROBE_VALUES.probe_number[i % PROBE_VALUES.probe_number.length],
  }));
  return { ...input, data: { ...data, values: rows } };
}

/** The synthetic bases of a chart type, without the probe fields. */
export function syntheticBases(chartType: string): ReadBase[] {
  return Object.entries(SYNTHETIC_BASES).map(([name, encodings]) => ({
    name,
    input: { data: { values: SYNTHETIC_ROWS.map((r) => ({ ...r })) }, chart_spec: { chartType, encodings } },
  }));
}

type Assemble = (input: Input) => unknown;

/** The artifact, or the throw, as text; Math.random reseeded first. */
function outcome(assemble: Assemble, input: Input): string {
  installSeededRandom(FIXTURE_SEED);
  try {
    return `ok ${JSON.stringify(assemble(structuredClone(input)))}`;
  } catch (e) {
    return `throws ${e instanceof Error ? e.message : String(e)}`;
  }
}

const encodingsOf = (input: Input) => (input.chart_spec as { encodings?: unknown } | undefined)?.encodings;

/**
 * The fixture bases of a chart type on one assembler: the cases of that chart type whose encodings are an object of
 * objects, grouped by their sorted channel set; per group the smallest by JSON length, then id, that assembles with the
 * probe fields added.
 */
export function fixtureBases(assemble: Assemble, chartType: string, cases: readonly CaseLine[]): ReadBase[] {
  const shapes = new Map<string, { id: string; size: number; input: Input }[]>();
  for (const c of cases) {
    const encodings = encodingsOf(c.input);
    if (c.chartType !== chartType || !isPlainObject(encodings) || !Object.values(encodings).every(isPlainObject)) continue;
    const shape = Object.keys(encodings).sort(ordinal).join(',');
    shapes.set(shape, [...(shapes.get(shape) ?? []), { id: c.id, size: JSON.stringify(c.input).length, input: c.input }]);
  }
  const bases: ReadBase[] = [];
  for (const shape of [...shapes.keys()].sort(ordinal)) {
    const candidates = shapes.get(shape)!.sort((a, b) => a.size - b.size || ordinal(a.id, b.id));
    const first = candidates.find((c) => {
      const probed = withProbeFields(c.input);
      return probed !== null && !outcome(assemble, probed).startsWith('throws ');
    });
    if (first) bases.push({ name: first.id, input: first.input });
  }
  return bases;
}

/**
 * Per chart type, per base it assembles from (with the probe fields), the channels whose binding or removal changes the
 * outcome, sorted. A base that throws is left out; one whose outcome differs between two runs fails the probe.
 * Silences upstream console warnings and restores Math.random while it runs.
 */
export function probeReads(
  assemble: Assemble,
  chartTypes: readonly string[],
  basesOf: (chartType: string) => ReadBase[],
  channels: readonly string[],
): Record<string, Record<string, string[]>> {
  const warn = console.warn;
  const random = Math.random;
  console.warn = () => {};
  try {
    const reads: Record<string, Record<string, string[]>> = {};
    for (const chartType of [...chartTypes].sort(ordinal)) {
      reads[chartType] = {};
      for (const base of basesOf(chartType)) {
        const input = withProbeFields(base.input);
        if (input === null) continue;
        const before = outcome(assemble, input);
        if (before.startsWith('throws ')) continue;
        if (outcome(assemble, input) !== before) throw new Error(`The read probe's base "${base.name}" of "${chartType}" assembles differently on two runs.`);
        const encodings = encodingsOf(input) as Record<string, unknown>;
        const spec = input.chart_spec as Record<string, unknown>;
        const variants = (channel: string): Record<string, unknown>[] =>
          Object.hasOwn(encodings, channel)
            ? [Object.fromEntries(Object.entries(encodings).filter(([c]) => c !== channel))]
            : Object.keys(PROBE_VALUES).map((field) => ({ ...encodings, [channel]: { field } }));
        reads[chartType][base.name] = channels
          .filter((channel) => variants(channel).some((v) => outcome(assemble, { ...input, chart_spec: { ...spec, encodings: v } }) !== before))
          .sort(ordinal);
      }
    }
    return reads;
  } finally {
    console.warn = warn;
    Math.random = random;
  }
}

/** The assembler and the chart types of a native backend in the installed package. */
export function nativeAssembler(flint: Record<string, unknown>, backend: ReadBackend): { assemble: Assemble; chartTypes: string[] } {
  if (backend === 'excel') {
    const assemble = flint.assembleExcel;
    const defs = flint.excelAllTemplateDefs as Array<{ chart: string }> | undefined;
    if (typeof assemble !== 'function' || !Array.isArray(defs)) throw new Error('flint-chart exports no assembleExcel or excelAllTemplateDefs; update src/extract/channel-reads.ts.');
    return { assemble: assemble as Assemble, chartTypes: defs.map((d) => d.chart) };
  }
  const map = flint.IMAGE_CHARTS_TYPE_MAP as Record<string, unknown> | undefined;
  const assemble = flint.assembleImageCharts;
  if (map === null || typeof map !== 'object' || typeof assemble !== 'function') {
    throw new Error('flint-chart exports no IMAGE_CHARTS_TYPE_MAP or assembleImageCharts; update src/extract/channel-reads.ts.');
  }
  return { assemble: assemble as Assemble, chartTypes: Object.keys(map) };
}

/** The assembler and the templates (chart type, declared channels) of a layout backend in the installed package. */
export function layoutAssembler(flint: Record<string, unknown>, backend: keyof typeof LAYOUT_READ_BACKENDS): { assemble: Assemble; templates: { chart: string; channels: string[] }[] } {
  const [assembler, list] = LAYOUT_READ_BACKENDS[backend];
  const assemble = flint[assembler];
  const defs = flint[list] as Array<{ chart: string; channels?: string[] }> | undefined;
  if (typeof assemble !== 'function' || !Array.isArray(defs)) throw new Error(`flint-chart exports no ${assembler} or ${list}; update src/extract/channel-reads.ts.`);
  return { assemble: assemble as Assemble, templates: defs.map((d) => ({ chart: d.chart, channels: d.channels ?? [] })) };
}

/**
 * The record of probes.json channelReads: both native backends on every catalog channel and the four layout backends on
 * the channels each template does not declare, over the fixture bases of `cases` and the synthetic bases.
 */
export function probeChannelReads(flint: Record<string, unknown>, cases: readonly CaseLine[]): ChannelReads {
  const channels = catalogChannels(flint);
  const basesOf = (assemble: Assemble) => (chartType: string) => [...fixtureBases(assemble, chartType, cases), ...syntheticBases(chartType)];
  const reads: ChannelReads['reads'] = {};
  for (const backend of READ_BACKENDS) {
    const { assemble, chartTypes } = nativeAssembler(flint, backend);
    reads[backend] = probeReads(assemble, chartTypes, basesOf(assemble), channels);
  }
  const layoutReads: ChannelReads['layoutReads'] = {};
  for (const backend of Object.keys(LAYOUT_READ_BACKENDS) as (keyof typeof LAYOUT_READ_BACKENDS)[]) {
    const { assemble, templates } = layoutAssembler(flint, backend);
    layoutReads[backend] = {};
    for (const { chart, channels: declared } of templates) {
      Object.assign(layoutReads[backend], probeReads(assemble, [chart], basesOf(assemble), channels.filter((c) => !declared.includes(c))));
    }
  }
  const names = new Set(
    [reads, layoutReads].flatMap((record) => Object.values(record).flatMap((perType) => Object.values(perType).flatMap((perBase) => Object.keys(perBase)))),
  );
  const bases: ChannelReads['bases'] = {};
  for (const name of [...names].sort(ordinal)) bases[name] = Object.hasOwn(SYNTHETIC_BASES, name) ? { encodings: SYNTHETIC_BASES[name] } : { case: name };
  return { bases, layoutReads, probe: PROBE_VALUES, reads, rows: SYNTHETIC_ROWS };
}

/** Per chart type, the channels read under any base, sorted. */
export function readUnion(reads: Record<string, Record<string, string[]>>): Record<string, string[]> {
  return Object.fromEntries(Object.entries(reads).map(([t, bases]) => [t, [...new Set(Object.values(bases).flat())].sort(ordinal)]));
}

const REVIEW =
  'review the Image-Charts throw rules (src/Graspea.Flint/Validation/Rules/ImageCharts.cs), imageChartsChannels, the undeclared-read exemptions (ImageCharts.ReadsUndeclared and its mirror in src/Graspea.Flint.CSharp/Spec/CatalogCheck.cs) and docs/validation-codes.md against the new behaviour, then record the probed channels';

/**
 * Fails, naming every entry, when the probed reads (readUnion) differ from overrides.json imageChartsReads, a chart
 * type assembles from none of the bases, an entry names a chart type outside the map (unless pending lists it), or a
 * curated imageChartsChannels channel is not read.
 */
export function imageChartsReadErrors(
  probed: Record<string, string[]>,
  expected: Record<string, string[]>,
  curated: Record<string, string[]>,
  pending: ReadonlySet<string>,
): string[] {
  const errors: string[] = [];
  const list = (channels: string[]) => (channels.length > 0 ? channels.join(', ') : 'none');
  for (const [name, reads] of Object.entries(probed)) {
    if (reads.length === 0) {
      errors.push(`assembleImageCharts assembles "${name}" from none of the read probe's bases (src/extract/channel-reads.ts): add a base it assembles from`);
      continue;
    }
    if (!Object.hasOwn(expected, name)) {
      errors.push(`imageChartsReads["${name}"] is missing (the read probe finds ${list(reads)}): ${REVIEW}`);
    } else {
      const added = reads.filter((c) => !expected[name].includes(c));
      const removed = expected[name].filter((c) => !reads.includes(c));
      if (added.length > 0 || removed.length > 0) {
        errors.push(
          `imageChartsReads["${name}"] in overrides.json records ${list([...expected[name]].sort(ordinal))}, but the read probe finds ${list(reads)} (new: ${list(added)}; gone: ${list(removed)}): ${REVIEW}`,
        );
      }
    }
    for (const channel of Object.hasOwn(curated, name) ? curated[name] : []) {
      if (!reads.includes(channel)) errors.push(`imageChartsChannels["${name}"] lists ${channel}, which the read probe does not find read`);
    }
  }
  for (const name of Object.keys(expected)) {
    if (!Object.hasOwn(probed, name) && !pending.has(`catalog.chartTypes[${name}].backends.image-charts`)) {
      errors.push(`imageChartsReads["${name}"] matches no chart type of IMAGE_CHARTS_TYPE_MAP`);
    }
  }
  return errors;
}
