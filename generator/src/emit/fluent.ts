// The fluent model shared by the builders, enums and entries emitters (design §4.2, §7.4, §7.6): per chart type the
// channel and property union across its backends with their C# names, and per discrete property its enum. Every
// collision and kind mismatch fails here, so the three emitters cannot disagree.
import { ordinal, type Ir, type IrProperty } from '../ir.ts';
import { builderName, catalogNames, enumMembers, optionKey, pascal, type EnumMember } from '../naming.ts';
import type { Overrides } from '../overrides.ts';

/** Members of the hand-written `Chart` class that an entry must not take. */
const RESERVED_ENTRY_NAMES: ReadonlySet<string> = new Set(['Of', 'Require']);

export interface FluentChannel {
  channel: string;
  method: string;
  /** The `Channel` constant. */
  constant: string;
  backends: string[];
}

export interface FluentEnumMember extends EnumMember {
  backends: string[];
}

export interface FluentEnum {
  name: string;
  chartType: string;
  key: string;
  label: string;
  /** The backends that declare the property. */
  backends: string[];
  members: FluentEnumMember[];
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
}

export function fluentModel(ir: Ir, overrides: Overrides): FluentTemplate[] {
  const names = catalogNames(ir.catalog, overrides.renames);
  const constant = (path: string, value: string) => overrides.renames[path] ?? pascal(value);
  const typeOwners = new Map<string, string>();
  const claimType = (name: string, path: string) => {
    const previous = typeOwners.get(name);
    if (previous !== undefined) throw new Error(`Type name collision "${name}": ${previous} and ${path}. Add a rename to generator/overrides.json.`);
    typeOwners.set(name, path);
  };

  const templates = ir.catalog.chartTypes.map((t): FluentTemplate => {
    const base = `catalog.chartTypes[${t.name}]`;
    const backends = ir.catalog.backends.filter((b) => b in t.backends);
    const members = names.members.get(t.name)!;
    const entry = names.entries.get(t.name)!;
    if (RESERVED_ENTRY_NAMES.has(entry)) {
      throw new Error(`Entry name collision "${entry}": ${base} and the hand-written Chart.${entry}. Add a rename to generator/overrides.json.`);
    }
    const builder = builderName(t.name);
    claimType(builder, base);

    const channelOrder = [...new Set(backends.flatMap((b) => t.backends[b].channels))];
    const channels = channelOrder.map((channel) => ({
      channel,
      method: members.channels.get(channel)!,
      constant: constant(`catalog.channels[${channel}]`, channel),
      backends: backends.filter((b) => t.backends[b].channels.includes(channel)),
    }));

    const keys = [...new Set(backends.flatMap((b) => t.backends[b].properties.map((p) => p.key)))].sort(ordinal);
    const properties = keys.map((key): FluentProperty => {
      const declarations = backends.flatMap((backend) => t.backends[backend].properties.filter((p) => p.key === key).map((property) => ({ backend, property })));
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
      };
      if (kind === 'discrete') {
        const path = `${base}.properties[${key}]`;
        const name = members.enums.get(key)!;
        claimType(name, path);
        claimType(`${name}Values`, path);
        const options = declarations.flatMap((d) => d.property.options ?? []);
        // Members are distinct values, so one option key identifies a member; Default is offered wherever the key is.
        const offered = (m: EnumMember) =>
          declarations.filter((d) => (d.property.options ?? []).some((o) => optionKey(o) === optionKey(m.options[0]))).map((d) => d.backend);
        property.enum = {
          name,
          chartType: t.name,
          key,
          label: first.property.label,
          backends: property.backends,
          members: enumMembers(path, options, (value) => overrides.renames[`${t.name}/enum/${key}/${value}`]).map((m) => ({ ...m, backends: m.name === 'Default' ? property.backends : offered(m) })),
        };
      }
      return property;
    });

    return { name: t.name, constant: constant(base, t.name), builder, entry, backends, channels, properties };
  });
  return templates;
}

/** "Declared by every backend of the template." or "Declared by a, b; not by c" (the caller ends the sentence). */
export function declaredBy(all: readonly string[], declaring: readonly string[]): { all: boolean; text: string; missing: string[] } {
  const missing = all.filter((b) => !declaring.includes(b));
  if (missing.length === 0) return { all: true, text: 'Declared by every backend of the template', missing };
  return { all: false, text: `Declared by ${declaring.join(', ')}; not by ${missing.join(', ')}`, missing };
}
