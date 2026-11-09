// Runtime registries of flint-chart -> IR.catalog (design §7.1, §7.3). Counts are derived, never assumed.
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  ordinal,
  type IrBackendTemplate,
  type IrCatalog,
  type IrChartType,
  type IrOption,
  type IrProperty,
  type IrSemanticType,
  type IrThemePreset,
} from '../ir.ts';
import { catalogChannels, imageChartsReadErrors, nativeAssembler, probeReads, readUnion, syntheticBases } from './channel-reads.ts';

/** Backend id -> registry exports, in the fixed backend order. */
const BACKENDS: ReadonlyArray<{ id: string; all: string; byCategory?: string }> = [
  { id: 'vegalite', all: 'vlAllTemplateDefs', byCategory: 'vlTemplateDefs' },
  { id: 'echarts', all: 'ecAllTemplateDefs', byCategory: 'ecTemplateDefs' },
  { id: 'chartjs', all: 'cjsAllTemplateDefs', byCategory: 'cjsTemplateDefs' },
  { id: 'plotly', all: 'plAllTemplateDefs', byCategory: 'plTemplateDefs' },
  { id: 'excel', all: 'excelAllTemplateDefs' },
];

/** Image-Charts has no template registry: its chart types are the own keys of IMAGE_CHARTS_TYPE_MAP, its channels the
 * curated `imageChartsChannels` of overrides.json (docs/generator.md). It follows the registry backends. */
const IMAGE_CHARTS = 'image-charts';
/** The Image-Charts sources whose reviewed sha256 overrides.json records (`imageChartsSource`). */
const IMAGE_CHARTS_SOURCES = ['src/image-charts/assemble.ts', 'src/image-charts/chart-types.ts'];

/** The assembler exports the catalog covers: one per backend above, Image-Charts, and the dispatcher. */
const KNOWN_ASSEMBLERS = new Set([
  'assembleVegaLite',
  'assembleECharts',
  'assembleChartjs',
  'assemblePlotly',
  'assembleExcel',
  'assembleImageCharts',
  'assembleForBackend',
]);

/** Catalog facts that come from overrides.json rather than from the package (design §7.3). */
export interface CatalogOverrides {
  backendsApplyingTheme: string[];
  hostStateKeys: string[];
  ignoredOptions: Record<string, string[]>;
  /** Chart type of IMAGE_CHARTS_TYPE_MAP -> the channels its Image-Charts template declares. */
  imageChartsChannels: Record<string, string[]>;
  /** Chart type of IMAGE_CHARTS_TYPE_MAP -> the channels assembleImageCharts reads, as the read probe finds them. */
  imageChartsReads: Record<string, string[]>;
  /** Package-relative path -> the reviewed sha256 of an Image-Charts source file. */
  imageChartsSource: Record<string, string>;
  /** IR paths of entries written ahead for an upstream version that is not pinned yet. */
  pending?: string[];
}

interface TemplateDef {
  chart: string;
  channels?: string[];
  properties?: PropertyDef[];
  typeMapping?: Record<string, string | boolean>;
  validate?: unknown;
}

interface PropertyDef {
  key: string;
  label: string;
  type: IrProperty['type'];
  options?: Array<{ label: string; value?: IrOption['value'] }>;
  min?: number;
  max?: number;
  step?: number;
}

function property(p: PropertyDef): IrProperty {
  if (!['binary', 'discrete', 'continuous'].includes(p.type)) {
    throw new Error(`Unknown ChartPropertyDef type "${p.type}" for key "${p.key}".`);
  }
  const out: IrProperty = { key: p.key, label: p.label, type: p.type };
  if (p.type === 'discrete') {
    out.options = (p.options ?? []).map((o) => (o.value === undefined ? { label: o.label, omitted: true } : { label: o.label, value: o.value }));
  }
  for (const k of ['min', 'max', 'step'] as const) if (typeof p[k] === 'number') out[k] = p[k];
  return out;
}

/** Trailing `// comment` of each `Name: 'Name',` line in src/core/semantic-types.ts (not available at runtime). */
function semanticTypeDocs(packageDir: string): Map<string, string> {
  const source = readFileSync(join(packageDir, 'src/core/semantic-types.ts'), 'utf8');
  const docs = new Map<string, string>();
  for (const m of source.matchAll(/^\s+(\w+): '(\w+)',\s*\/\/\s*(.+)$/gm)) docs.set(m[2], m[3].trim());
  return docs;
}

export function extractCatalog(flint: Record<string, unknown>, packageDir: string, overrides: CatalogOverrides): IrCatalog {
  const known = new Set(BACKENDS.map((b) => b.all));
  const unknown = Object.keys(flint).filter((k) => k.endsWith('AllTemplateDefs') && !known.has(k));
  if (unknown.length > 0) throw new Error(`Unknown backend registries: ${unknown.join(', ')}. Add them to src/extract/registry.ts.`);
  const assemblers = Object.keys(flint).filter((k) => /^assemble[A-Z]/.test(k) && !KNOWN_ASSEMBLERS.has(k));
  if (assemblers.length > 0) throw new Error(`Unknown assemblers: ${assemblers.join(', ')}. Add their backend to src/extract/registry.ts.`);

  const chartTypes = new Map<string, IrChartType>();
  for (const backend of BACKENDS) {
    const defs = flint[backend.all] as TemplateDef[];
    const categoryOf = new Map<string, string>();
    if (backend.byCategory) {
      for (const [category, list] of Object.entries(flint[backend.byCategory] as Record<string, TemplateDef[]>)) {
        for (const d of list) if (!categoryOf.has(d.chart)) categoryOf.set(d.chart, category);
      }
    }
    for (const def of defs) {
      const entry: IrBackendTemplate = {
        channels: [...(def.channels ?? [])],
        properties: (def.properties ?? []).map(property).sort((a, b) => ordinal(a.key, b.key)),
      };
      const category = categoryOf.get(def.chart);
      if (category !== undefined) entry.category = category;
      if (backend.id === 'excel') {
        entry.typeMapping = def.typeMapping ?? {};
        entry.hasValidate = typeof def.validate === 'function';
        entry.supportsFacets = entry.channels.includes('column') || entry.channels.includes('row');
      }
      const chartType = chartTypes.get(def.chart) ?? { name: def.chart, backends: {} };
      if (backend.id in chartType.backends) throw new Error(`Duplicate template "${def.chart}" on ${backend.id}.`);
      chartType.backends[backend.id] = entry;
      chartTypes.set(def.chart, chartType);
    }
  }

  imageChartsTemplates(flint, packageDir, chartTypes, overrides);

  const coreChannels = flint.channels as readonly string[];
  const declared = new Set([...chartTypes.values()].flatMap((t) => Object.values(t.backends).flatMap((b) => b.channels)));
  const channels = [...coreChannels, ...[...declared].filter((c) => !coreChannels.includes(c)).sort(ordinal)];

  const docs = semanticTypeDocs(packageDir);
  const getRegistryEntry = flint.getRegistryEntry as (name: string) => { t0: string; t1: string; visEncodings: string[] };
  const semanticTypes: IrSemanticType[] = Object.values(flint.SemanticTypes as Record<string, string>)
    .sort(ordinal)
    .map((name) => {
      const entry = getRegistryEntry(name);
      const out: IrSemanticType = { name, t0: entry.t0, t1: entry.t1, visEncodings: [...entry.visEncodings] };
      const doc = docs.get(name);
      if (doc !== undefined) out.doc = doc;
      return out;
    });

  const themePresets: IrThemePreset[] = (flint.listThemePresets as () => IrThemePreset[])()
    .map((p) => ({ id: p.id, label: p.label, description: p.description }))
    .sort((a, b) => ordinal(a.id, b.id));

  return {
    backends: [...BACKENDS.map((b) => b.id), IMAGE_CHARTS],
    backendsApplyingTheme: [...overrides.backendsApplyingTheme],
    hostStateKeys: [...overrides.hostStateKeys],
    ignoredOptions: { ...overrides.ignoredOptions },
    channels,
    chartTypes: [...chartTypes.values()].sort((a, b) => ordinal(a.name, b.name)),
    semanticTypes,
    themePresets,
  };
}

/**
 * The Image-Charts templates: one per own key of IMAGE_CHARTS_TYPE_MAP (never an `in` lookup, which finds
 * Object.prototype members), with the curated channels in the order the other backends declare them, no properties
 * (assembleImageCharts reads no chartProperties) and no facets (it throws on a bound column or row). Fails, naming every
 * entry, when the map and the overrides disagree, a curated
 * channel would give a builder a member, an Image-Charts source file differs from its reviewed fingerprint, or the
 * channels the read probe finds assembleImageCharts reading differ from `imageChartsReads`.
 */
function imageChartsTemplates(flint: Record<string, unknown>, packageDir: string, chartTypes: Map<string, IrChartType>, overrides: CatalogOverrides): void {
  const map = flint.IMAGE_CHARTS_TYPE_MAP as Record<string, Record<string, string | boolean>> | undefined;
  if (map === null || typeof map !== 'object') throw new Error('flint-chart exports no IMAGE_CHARTS_TYPE_MAP; update src/extract/registry.ts.');
  const curated = overrides.imageChartsChannels;
  const pending = new Set(overrides.pending ?? []);
  const errors: string[] = [];
  for (const name of Object.keys(map)) {
    if (!Object.hasOwn(curated, name)) {
      errors.push(`imageChartsChannels["${name}"] is missing (IMAGE_CHARTS_TYPE_MAP has the chart type; its channels are curated)`);
      continue;
    }
    const chartType = chartTypes.get(name);
    if (!chartType) {
      errors.push(`catalog.chartTypes[${name}].backends.${IMAGE_CHARTS}: no other backend has the chart type, so a builder would be added`);
      continue;
    }
    const union = new Set(Object.values(chartType.backends).flatMap((b) => b.channels));
    for (const channel of curated[name]) {
      if (channel === 'column' || channel === 'row') {
        errors.push(`imageChartsChannels["${name}"] lists ${channel}, on which assembleImageCharts throws`);
      } else if (!union.has(channel)) {
        errors.push(`imageChartsChannels["${name}"] lists ${channel}, which no other backend of the template declares, so a builder would gain a member`);
      }
    }
  }
  for (const name of Object.keys(curated)) {
    if (!Object.hasOwn(map, name) && !pending.has(`catalog.chartTypes[${name}].backends.${IMAGE_CHARTS}`)) {
      errors.push(`imageChartsChannels["${name}"] matches no chart type of IMAGE_CHARTS_TYPE_MAP`);
    }
  }
  const imageCharts = nativeAssembler(flint, IMAGE_CHARTS);
  const probed = readUnion(probeReads(imageCharts.assemble, imageCharts.chartTypes, syntheticBases, catalogChannels(flint)));
  errors.push(...imageChartsReadErrors(probed, overrides.imageChartsReads, curated, pending));
  // ponytail: the fingerprint catches an edit of these two files, the read probe above a change in a src/core helper
  // they call that adds or drops a read. Here the probe toggles one catalog channel at a time on three rows under the
  // synthetic bases of src/extract/channel-reads.ts (the fixture bases need the fixture set, which extract has not
  // built); the fixtures step probes the fixture bases too and records them in probes.json channelReads, which the C#
  // tests read. A read that needs two more channels bound together, many rows (overflow filtering) or other values
  // passes unseen; widen SYNTHETIC_BASES if a sync ever slips through.
  for (const file of IMAGE_CHARTS_SOURCES) {
    const actual = createHash('sha256').update(readFileSync(join(packageDir, file))).digest('hex');
    const reviewed = overrides.imageChartsSource[file];
    if (actual !== reviewed) {
      errors.push(
        `${file} has sha256 ${actual}, but imageChartsSource in overrides.json records ${reviewed ?? 'none'}: review src/Graspea.Flint/Validation/Rules/ImageCharts.cs, imageChartsChannels and the consumed-channel exemptions (ImageCharts.ReadsUndeclared and its mirror in src/Graspea.Flint.CSharp/Spec/CatalogCheck.cs) against the new file, then update the fingerprint`,
      );
    }
  }
  if (errors.length > 0) throw new Error(`Image-Charts templates: ${errors.join('; ')}.`);

  for (const name of Object.keys(map)) {
    const chartType = chartTypes.get(name)!;
    const union = [...new Set(Object.values(chartType.backends).flatMap((b) => b.channels))];
    const channels = union.filter((c) => curated[name].includes(c));
    chartType.backends[IMAGE_CHARTS] = { channels, properties: [], typeMapping: { ...map[name] }, supportsFacets: false };
  }
}
