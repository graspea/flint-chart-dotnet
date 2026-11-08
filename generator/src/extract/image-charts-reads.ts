// The channels assembleImageCharts reads per chart type, found by behaviour instead of by reading its source (design
// part 9 §14.8.1; docs/generator.md "Failure policy"). The Image-Charts parts of the SDK (the curated
// imageChartsChannels, the validator's throw rules and undeclared-read exemptions) were written from src/image-charts,
// whose files extract fingerprints, but the assembler also calls src/core helpers (normalizeChartEncodingAliases,
// resolveChannelSemantics, the banded overflow filter) that the fingerprint does not cover. extract checks the reads
// against overrides.json imageChartsReads (registry.ts); the fixtures step records them per base in probes.json, which
// the C# cross-check (tests/Graspea.Flint.Tests/Parity/ImageChartsReadsTests.cs) reads.
import { ordinal } from '../ir.ts';

type Encodings = Record<string, { field: string }>;

/** A category and two measures for the bases, and the two fields a probed channel is bound to. */
const ROWS = [
  { category: 'A', measure: 3, measure2: 30, probe_text: 'k1', probe_number: 70 },
  { category: 'B', measure: 1, measure2: 10, probe_text: 'k2', probe_number: 50 },
  { category: 'C', measure: 2, measure2: 20, probe_text: 'k3', probe_number: 60 },
];

/**
 * The encodings each channel is toggled against. `x category, y measure` leaves color, size and theta without a field,
 * so a Pie or Donut Chart reads x as the slice and y as the value (assemble.ts:265-266); `x and y measures` is the one
 * a Scatter Plot assembles from (:290-293); `slice and value` binds the channels that shadow those fallbacks.
 */
export const IMAGE_CHARTS_READ_BASES: Record<string, Encodings> = {
  'x category, y measure': { x: { field: 'category' }, y: { field: 'measure' } },
  'x and y measures': { x: { field: 'measure' }, y: { field: 'measure2' } },
  'slice and value': { color: { field: 'category' }, size: { field: 'measure' } },
};

/** Text makes a channel a category or a series, a number a measure: a quantitative color is no series (assemble.ts:205-209). */
const PROBE_FIELDS = ['probe_text', 'probe_number'];

export interface ImageChartsReads {
  /** The bases, by name. */
  bases: Record<string, Encodings>;
  /**
   * Chart type -> each base it assembles from -> the channels whose binding (or, for a channel of the base, removal)
   * changes the URL or the throw, sorted.
   */
  reads: Record<string, Record<string, string[]>>;
}

/** The channels of IR catalog.channels (registry.ts): the core list and every channel a registry template declares. */
export function catalogChannels(flint: Record<string, unknown>): string[] {
  const declared = Object.keys(flint)
    .filter((k) => k.endsWith('AllTemplateDefs'))
    .flatMap((k) => (flint[k] as Array<{ channels?: string[] }>).flatMap((d) => d.channels ?? []));
  return [...new Set([...(flint.channels as readonly string[]), ...declared])];
}

/** Runs the probe against the installed package; silences upstream console warnings while it runs. */
export function probeImageChartsReads(flint: Record<string, unknown>): ImageChartsReads {
  const map = flint.IMAGE_CHARTS_TYPE_MAP as Record<string, unknown> | undefined;
  const assemble = flint.assembleImageCharts as ((input: unknown) => { url: string }) | undefined;
  if (map === null || typeof map !== 'object' || typeof assemble !== 'function') {
    throw new Error('flint-chart exports no IMAGE_CHARTS_TYPE_MAP or assembleImageCharts; update src/extract/image-charts-reads.ts.');
  }
  const channels = catalogChannels(flint);
  const outcome = (chartType: string, encodings: Encodings): string => {
    try {
      return `url ${assemble({ data: { values: ROWS.map((r) => ({ ...r })) }, chart_spec: { chartType, encodings } }).url}`;
    } catch (e) {
      return `throws ${e instanceof Error ? e.message : String(e)}`;
    }
  };
  const warn = console.warn;
  console.warn = () => {};
  try {
    const reads: ImageChartsReads['reads'] = {};
    for (const chartType of Object.keys(map).sort(ordinal)) {
      reads[chartType] = {};
      for (const [name, base] of Object.entries(IMAGE_CHARTS_READ_BASES)) {
        const before = outcome(chartType, base);
        if (before.startsWith('throws ')) continue;
        reads[chartType][name] = channels
          .filter((channel) =>
            (Object.hasOwn(base, channel)
              ? [Object.fromEntries(Object.entries(base).filter(([c]) => c !== channel))]
              : PROBE_FIELDS.map((field) => ({ ...base, [channel]: { field } }))
            ).some((variant) => outcome(chartType, variant) !== before),
          )
          .sort(ordinal);
      }
    }
    return { bases: IMAGE_CHARTS_READ_BASES, reads };
  } finally {
    console.warn = warn;
  }
}

/** Per chart type, the channels read under any base, sorted. */
export function readUnion(reads: ImageChartsReads['reads']): Record<string, string[]> {
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
      errors.push(`assembleImageCharts assembles "${name}" from none of the read probe's bases (src/extract/image-charts-reads.ts): add a base it assembles from`);
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
