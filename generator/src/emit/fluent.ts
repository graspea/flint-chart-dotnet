// The fluent model shared by the builders, enums and entries emitters (design §4.2, §7.4, §7.6): per chart type the
// channel and property union across its backends with their C# names, and per discrete property its enum. Every
// collision and kind mismatch fails here, so the three emitters cannot disagree.
import { ordinal, type Ir, type IrProperty } from '../ir.ts';
import { builderName, catalogNames, enumMembers, optionKey, pascal, type EnumMember } from '../naming.ts';
import type { Overrides } from '../overrides.ts';
import { facadeCatalog, obsoleteIn, optionId } from '../retired.ts';

/** Members of the hand-written `Chart` class that an entry must not take. */
const RESERVED_ENTRY_NAMES: ReadonlySet<string> = new Set(['Of', 'Require']);

export interface FluentChannel {
  channel: string;
  method: string;
  /** The `Channel` constant. */
  constant: string;
  backends: string[];
  /** The flint-chart version that retired it on every backend that declared it (design §7.8): emitted with `[Obsolete]`. */
  obsoleteIn?: string;
}

export interface FluentEnumMember extends EnumMember {
  backends: string[];
  obsoleteIn?: string;
}

export interface FluentEnum {
  name: string;
  chartType: string;
  key: string;
  label: string;
  /** The backends that declare the property. */
  backends: string[];
  members: FluentEnumMember[];
  obsoleteIn?: string;
}

export interface FluentProperty {
  key: string;
  method: string;
  label: string;
  /** `json` when the kind differs across backends and overrides.json maps the key to JsonNode. */
  kind: IrProperty['type'] | 'json';
  backends: string[];
  /** The declaration on each backend, in backend order. */
  declarations: Array<{ backend: string; property: IrProperty }>;
  enum?: FluentEnum;
  obsoleteIn?: string;
}

export interface FluentTemplate {
  name: string;
  /** The `ChartType` constant. */
  constant: string;
  builder: string;
  entry: string;
  backends: string[];
  channels: FluentChannel[];
  properties: FluentProperty[];
  obsoleteIn?: string;
}

/**
 * The backends that declare a member, without those that retired it, unless every one did; then all of them, with the
 * version that retired it (the latest), so the member is kept under `[Obsolete]`.
 */
function live<T>(items: T[], path: (item: T) => string, obsolete: ReadonlyMap<string, string>): { items: T[]; obsoleteIn?: string } {
  const versions = items.map((item) => obsoleteIn(obsolete, path(item)));
  if (versions.some((v) => v === undefined)) return { items: items.filter((_, i) => versions[i] === undefined) };
  return { items, obsoleteIn: versions.sort().at(-1) };
}

export function fluentModel(ir: Ir, overrides: Overrides): FluentTemplate[] {
  const { catalog, obsolete } = facadeCatalog(ir);
  const names = catalogNames(catalog, overrides.renames);
  const constant = (path: string, value: string) => overrides.renames[path] ?? pascal(value);
  const typeOwners = new Map<string, string>();
  const claimType = (name: string, path: string) => {
    const previous = typeOwners.get(name);
    if (previous !== undefined) throw new Error(`Type name collision "${name}": ${previous} and ${path}. Add a rename to generator/overrides.json.`);
    typeOwners.set(name, path);
  };

  const templates = catalog.chartTypes.map((t): FluentTemplate => {
    const base = `catalog.chartTypes[${t.name}]`;
    const backends = catalog.backends.filter((b) => b in t.backends);
    const members = names.members.get(t.name)!;
    const entry = names.entries.get(t.name)!;
    if (RESERVED_ENTRY_NAMES.has(entry)) {
      throw new Error(`Entry name collision "${entry}": ${base} and the hand-written Chart.${entry}. Add a rename to generator/overrides.json.`);
    }
    const builder = builderName(t.name);
    claimType(builder, base);

    const channelOrder = [...new Set(backends.flatMap((b) => t.backends[b].channels))];
    const channels = channelOrder.map((channel): FluentChannel => {
      const declaring = live(backends.filter((b) => t.backends[b].channels.includes(channel)), (b) => `${base}.backends.${b}.channels[${channel}]`, obsolete);
      return {
        channel,
        method: members.channels.get(channel)!,
        constant: constant(`catalog.channels[${channel}]`, channel),
        backends: declaring.items,
        ...(declaring.obsoleteIn ? { obsoleteIn: declaring.obsoleteIn } : {}),
      };
    });

    const keys = [...new Set(backends.flatMap((b) => t.backends[b].properties.map((p) => p.key)))].sort(ordinal);
    const properties = keys.map((key): FluentProperty => {
      const declaring = live(
        backends.flatMap((backend) => t.backends[backend].properties.filter((p) => p.key === key).map((property) => ({ backend, property }))),
        (d) => `${base}.backends.${d.backend}.properties[${key}]`,
        obsolete,
      );
      const declarations = declaring.items;
      const first = declarations[0];
      let kind: FluentProperty['kind'] = first.property.type;
      const other = declarations.find((d) => d.property.type !== first.property.type);
      if (other) {
        const path = (d: typeof first) => `${base}.backends.${d.backend}.properties[${key}]`;
        const mapped = overrides.unions[`${t.name}/property/${key}`];
        if (mapped === undefined) {
          throw new Error(`Property kind mismatch: ${path(first)} is ${first.property.type} but ${path(other)} is ${other.property.type}. Map "${t.name}/property/${key}" in overrides.json unions.`);
        }
        if (mapped !== 'JsonNode') throw new Error(`overrides.json unions["${t.name}/property/${key}"] = ${mapped}: a template property maps only to JsonNode.`);
        kind = 'json';
      }
      const property: FluentProperty = {
        key,
        method: members.properties.get(key)!,
        label: first.property.label,
        kind,
        backends: declarations.map((d) => d.backend),
        declarations,
        ...(declaring.obsoleteIn ? { obsoleteIn: declaring.obsoleteIn } : {}),
      };
      if (kind === 'discrete') {
        const path = `${base}.properties[${key}]`;
        const name = members.enums.get(key)!;
        claimType(name, path);
        claimType(`${name}Values`, path);
        const options = declarations.flatMap((d) => d.property.options ?? []);
        const optionPath = (d: (typeof declarations)[number], m: EnumMember) =>
          `${base}.backends.${d.backend}.properties[${key}].options[${optionId(m.options[0])}]`;
        // Members are distinct values, so one option key identifies a member; Default is offered wherever the key is.
        const offering = (m: EnumMember) =>
          live(declarations.filter((d) => (d.property.options ?? []).some((o) => optionKey(o) === optionKey(m.options[0]))), (d) => optionPath(d, m), obsolete);
        property.enum = {
          name,
          chartType: t.name,
          key,
          label: first.property.label,
          backends: property.backends,
          ...(declaring.obsoleteIn ? { obsoleteIn: declaring.obsoleteIn } : {}),
          members: enumMembers(path, options, (value) => overrides.renames[`${t.name}/enum/${key}/${value}`]).map((m): FluentEnumMember => {
            if (m.name === 'Default') return { ...m, backends: property.backends };
            const offered = offering(m);
            return { ...m, backends: offered.items.map((d) => d.backend), ...(offered.obsoleteIn ? { obsoleteIn: offered.obsoleteIn } : {}) };
          }),
        };
      }
      return property;
    });

    const templateObsolete = obsolete.get(base);
    return { name: t.name, constant: constant(base, t.name), builder, entry, backends, channels, properties, ...(templateObsolete ? { obsoleteIn: templateObsolete } : {}) };
  });
  return templates;
}

/** "Declared by every backend of the template." or "Declared by a, b; not by c" (the caller ends the sentence). */
export function declaredBy(all: readonly string[], declaring: readonly string[]): { all: boolean; text: string; missing: string[] } {
  const missing = all.filter((b) => !declaring.includes(b));
  if (missing.length === 0) return { all: true, text: 'Declared by every backend of the template', missing };
  return { all: false, text: `Declared by ${declaring.join(', ')}; not by ${missing.join(', ')}`, missing };
}
