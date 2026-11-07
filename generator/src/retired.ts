// The retirement ledger generator/retired.json (design §7.8, part 3 §8.4): which retired members the facades keep under
// `[Obsolete]` (FLINT0001), and the facade catalog that puts them back.
import type { Ir, IrCatalog, IrChartType, IrOption, IrProperty, IrSemanticType, IrThemePreset } from './ir.ts';

export interface RetiredEntry {
  path: string;
  lastSeenIn: string;
  retiredIn: string;
  /** The IR node as it was in `lastSeenIn`, from which the facades re-emit the member. */
  was?: unknown;
  /** Set by hand when a maintainer deleted the member in a breaking window before its lifetime ended. */
  deletedIn?: string;
}

const parts = (version: string) => version.split('.').map(Number);

/** The breaking line of a version (part 3 §8.4): `0.<minor>` before 1.0 (every upstream minor breaks), `<major>` after. */
export function breakingLine(version: string): string {
  const [major, minor] = parts(version);
  return major === 0 ? `0.${minor}` : `${major}`;
}

/**
 * `obsolete` while the pin is in the breaking line the member was retired in (it then lives at least the rest of that
 * upstream minor line), `removed` once the pin enters the next breaking line or a maintainer deleted it (`deletedIn`).
 */
export function retirementStatus(entry: RetiredEntry, pin: string): 'obsolete' | 'removed' {
  return entry.deletedIn === undefined && breakingLine(entry.retiredIn) === breakingLine(pin) ? 'obsolete' : 'removed';
}

const TEMPLATE = /^catalog\.chartTypes\[([^\]]+)\](?:\.backends\.([a-z]+)(?:\.channels\[([^\]]+)\]|\.properties\[([^\]]+)\](?:\.options\[(.+)\])?)?)?$/;
const CONSTANT = /^catalog\.(channels|semanticTypes|themePresets)\[([^\]]+)\]$/;

/** The id an IR option has in an IR path (`=<JSON value>`, or `omitted`), as `diffIr` writes it. */
export const optionId = (o: IrOption) => (o.omitted ? 'omitted' : `=${JSON.stringify(o.value)}`);

/**
 * The catalog the facades are built from: the IR catalog with every obsolete ledger entry of `ir.retired` put back at its
 * path, and the obsolete paths with the version that retired them. Without `ir.retired` it is the IR catalog.
 */
export function facadeCatalog(ir: Ir): { catalog: IrCatalog; obsolete: Map<string, string> } {
  const obsolete = new Map<string, string>();
  const entries = (ir.retired ?? []).filter((e) => retirementStatus(e, ir.upstream.version) === 'obsolete');
  if (entries.length === 0) return { catalog: ir.catalog, obsolete };
  const catalog = structuredClone(ir.catalog);
  // Outer nodes first, so a channel or property goes into a template that is itself put back.
  for (const e of [...entries].sort((a, b) => a.path.length - b.path.length)) {
    if (e.was === undefined) throw new Error(`generator/retired.json: ${e.path} is within its lifetime but has no "was" snapshot to emit it from.`);
    obsolete.set(e.path, e.retiredIn);
    const constant = CONSTANT.exec(e.path);
    if (constant) {
      const [, kind, name] = constant;
      if (kind === 'channels' && !catalog.channels.includes(name)) catalog.channels.push(name);
      if (kind === 'semanticTypes' && !catalog.semanticTypes.some((s) => s.name === name)) catalog.semanticTypes.push(e.was as IrSemanticType);
      if (kind === 'themePresets' && !catalog.themePresets.some((p) => p.id === name)) catalog.themePresets.push(e.was as IrThemePreset);
      continue;
    }
    const m = TEMPLATE.exec(e.path);
    if (!m) throw new Error(`generator/retired.json: ${e.path} is not a path the facades can keep.`);
    const [, chartType, backend, channel, key, option] = m;
    const template = catalog.chartTypes.find((t) => t.name === chartType);
    if (backend === undefined) {
      if (!template) catalog.chartTypes.push(e.was as IrChartType);
      continue;
    }
    if (!template) continue;
    const declared = template.backends[backend];
    if (channel === undefined && key === undefined) {
      if (!declared) template.backends[backend] = e.was as IrChartType['backends'][string];
      continue;
    }
    if (!declared) continue;
    if (channel !== undefined) {
      if (!declared.channels.includes(channel)) declared.channels.push(channel);
      continue;
    }
    const property = declared.properties.find((p) => p.key === key);
    if (option === undefined) {
      if (!property) declared.properties.push(e.was as IrProperty);
      continue;
    }
    if (property && !(property.options ?? []).some((o) => optionId(o) === option)) property.options = [...(property.options ?? []), e.was as IrOption];
  }
  return { catalog, obsolete };
}

/** The version that retired the node at `path`, or the template or backend it belongs to; undefined when none is obsolete. */
export function obsoleteIn(obsolete: ReadonlyMap<string, string>, path: string): string | undefined {
  const m = TEMPLATE.exec(path);
  if (!m) return obsolete.get(path);
  const [, chartType, backend, channel, key, option] = m;
  const chain = [`catalog.chartTypes[${chartType}]`];
  if (backend !== undefined) chain.push(`${chain[0]}.backends.${backend}`);
  if (channel !== undefined) chain.push(`${chain[1]}.channels[${channel}]`);
  if (key !== undefined) chain.push(`${chain[1]}.properties[${key}]`);
  if (option !== undefined) chain.push(`${chain[2]}.options[${option}]`);
  return chain.map((p) => obsolete.get(p)).find((v) => v !== undefined);
}

/** The `[Obsolete]` line of a member retired in `version` (part 3 §8.4). */
export function obsoleteAttribute(indent: string, version: string): string {
  return (
    `${indent}[Obsolete("Removed in flint-chart ${version}; ignored upstream. Deleted in the next breaking window.", ` +
    'DiagnosticId = "FLINT0001", UrlFormat = "https://github.com/graspea/flint-chart-dotnet/blob/main/docs/deprecations.md#{0}")]\n'
  );
}
