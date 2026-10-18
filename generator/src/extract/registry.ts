// Runtime registries of flint-chart -> IR.catalog (design §7.1, §7.3). Counts are derived, never assumed.
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

/** Backend id -> registry exports, in the fixed backend order. */
const BACKENDS: ReadonlyArray<{ id: string; all: string; byCategory?: string }> = [
  { id: 'vegalite', all: 'vlAllTemplateDefs', byCategory: 'vlTemplateDefs' },
  { id: 'echarts', all: 'ecAllTemplateDefs', byCategory: 'ecTemplateDefs' },
  { id: 'chartjs', all: 'cjsAllTemplateDefs', byCategory: 'cjsTemplateDefs' },
  { id: 'plotly', all: 'plAllTemplateDefs', byCategory: 'plTemplateDefs' },
  { id: 'excel', all: 'excelAllTemplateDefs' },
];

/** Catalog facts that come from overrides.json rather than from the package (design §7.3). */
export interface CatalogOverrides {
  backendsApplyingTheme: string[];
  hostStateKeys: string[];
  ignoredOptions: Record<string, string[]>;
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
    backends: BACKENDS.map((b) => b.id),
    backendsApplyingTheme: [...overrides.backendsApplyingTheme],
    hostStateKeys: [...overrides.hostStateKeys],
    ignoredOptions: { ...overrides.ignoredOptions },
    channels,
    chartTypes: [...chartTypes.values()].sort((a, b) => ordinal(a.name, b.name)),
    semanticTypes,
    themePresets,
  };
}
