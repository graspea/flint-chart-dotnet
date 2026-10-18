// Naming and collision rules (design §7.6). Names depend only on the IR.
import { ordinal, type IrBackendTemplate, type IrCatalog, type IrChartType, type IrOption, type IrOptionValue } from './ir.ts';

/** Core method names of ChartBuilder<TSelf,TRow> (design §4.2). */
export const RESERVED_CORE_NAMES: ReadonlySet<string> = new Set([
  'Encode', 'Series', 'Encoding', 'Property', 'Title', 'Subtitle', 'BaseSize', 'CanvasSize', 'Annotate',
  'DisplayName', 'Theme', 'Options', 'Configure', 'Build', 'Validate', 'ToJson', 'ToTemplate',
]);

/**
 * Rule 1: split on non-alphanumeric runs and case changes; an all-upper token of length >= 2 is
 * capitalised (KPI -> Kpi); a leading digit gets `_`.
 */
export function pascal(s: string): string {
  const tokens = s
    .split(/[^A-Za-z0-9]+/)
    .flatMap((part) => part.split(/(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])/))
    .filter((t) => t.length > 0);
  const name = tokens
    .map((t) => (t.length >= 2 && /^[A-Z]+$/.test(t) ? t[0] + t.slice(1).toLowerCase() : t[0].toUpperCase() + t.slice(1)))
    .join('');
  if (name.length === 0) throw new Error(`Cannot derive a C# name from "${s}".`);
  return /^[0-9]/.test(name) ? `_${name}` : name;
}

/** Rule 2: the entry name is Pascal(name) with one trailing `Chart`/`Plot` token removed. */
export function entryName(chartType: string): string {
  const full = pascal(chartType);
  const lastToken = chartType.trim().split(/[^A-Za-z0-9]+/).filter((t) => t.length > 0).at(-1);
  if ((lastToken === 'Chart' || lastToken === 'Plot') && full.length > lastToken.length) {
    return full.slice(0, -lastToken.length);
  }
  return full;
}

/** Rule 2: the builder of a template. */
export function builderName(chartType: string): string {
  return `${pascal(chartType)}Builder`;
}

/**
 * Rule 3: the enum of a discrete template property, `{Pascal(chartType)}{Pascal(key)}`, or `renames["<Template>/enum/<key>"]`.
 * A key that repeats the template name (Regression `regressionMethod` -> RegressionRegressionMethod) fails generation
 * until the rename names it.
 */
export function enumName(chartType: string, key: string, renames: Readonly<Record<string, string>> = {}): string {
  const path = `${chartType}/enum/${key}`;
  const renamed = renames[path];
  if (renamed !== undefined) return renamed;
  const name = `${pascal(chartType)}${pascal(key)}`;
  if (pascal(key).startsWith(pascal(chartType))) {
    throw new Error(`Enum name "${name}" repeats the template name (catalog.chartTypes[${chartType}].properties[${key}]). Add renames["${path}"] to generator/overrides.json.`);
  }
  return name;
}

/** The IR path segment of an option: the raw string value, the JSON of another value, or `omitted`. */
export function optionKey(option: IrOption): string {
  if (option.omitted || option.value === undefined) return 'omitted';
  return typeof option.value === 'string' ? option.value : JSON.stringify(option.value);
}

/**
 * Rule 4: the enum member of an option. A string value names the member (`step-after` -> StepAfter; the literal
 * `default` -> DefaultLiteral); an omitted option is `Default`; any other value takes its label, without the trailing
 * `[…]` that repeats the value and without a trailing `(default)` marker (`Europe [10, 50]` -> Europe); a number
 * without a label is `N<value>`.
 */
export function enumMemberName(option: IrOption): string {
  if (option.omitted || option.value === undefined) return 'Default';
  if (typeof option.value === 'string') {
    const name = pascal(option.value);
    return name === 'Default' ? 'DefaultLiteral' : name;
  }
  const label = option.label.replace(/\s*\[[^\]]*\]\s*$/, '').replace(/\s*\(default\)\s*$/i, '');
  if (/[A-Za-z0-9]/.test(label)) {
    const name = pascal(label);
    return name === 'Default' ? 'DefaultLiteral' : name;
  }
  if (typeof option.value === 'number' && Number.isInteger(option.value) && option.value >= 0) return `N${option.value}`;
  throw new Error(`Cannot name the option ${optionKey(option)} (label "${option.label}") as an enum member.`);
}

export interface EnumMember {
  name: string;
  /** Absent for `Default`, which omits the key. */
  value?: IrOptionValue;
  options: IrOption[];
}

/**
 * Rules 4 and 6 for one discrete property: `Default` (the omitted key) first, then one member per distinct value in the
 * order given; two values with the same member name fail with both IR paths. `rename` returns the overrides.json name of a
 * value (`renames["<Template>/enum/<key>/<value>"]`), which replaces the rule-4 name; the wire value is unchanged.
 */
export function enumMembers(path: string, options: readonly IrOption[], rename: (optionKey: string) => string | undefined = () => undefined): EnumMember[] {
  const members: EnumMember[] = [{ name: 'Default', options: [] }];
  const byName = new Map<string, { member: EnumMember; key: string }>([['Default', { member: members[0], key: 'omitted' }]]);
  for (const option of options) {
    const key = optionKey(option);
    const name = (option.omitted ? undefined : rename(key)) ?? enumMemberName(option);
    const previous = byName.get(name);
    if (previous === undefined) {
      const member: EnumMember = { name, value: option.omitted ? undefined : option.value, options: [option] };
      members.push(member);
      byName.set(name, { member, key });
    } else if (previous.key === key) {
      previous.member.options.push(option);
    } else {
      throw new Error(`Enum member collision "${name}": ${path}.options[${previous.key}] and ${path}.options[${key}].`);
    }
  }
  return members;
}

/** Rule 6: entry names for every chart type; two names equal after stripping fail with both IR paths. */
export function entryNames(chartTypes: readonly string[]): Map<string, string> {
  const result = new Map<string, string>();
  const owner = new Map<string, string>();
  for (const chartType of chartTypes) {
    const name = entryName(chartType);
    const previous = owner.get(name);
    if (previous !== undefined) {
      throw new Error(
        `Entry name collision "${name}": catalog.chartTypes[${previous}] and catalog.chartTypes[${chartType}]. ` +
          'Add a rename to generator/overrides.json.',
      );
    }
    owner.set(name, chartType);
    result.set(chartType, name);
  }
  return result;
}

export interface TemplateMemberNames {
  channels: Map<string, string>;
  properties: Map<string, string>;
  /** Discrete property key -> enum name (rule 3). */
  enums: Map<string, string>;
}

/**
 * Rules 3 and 6 for one template: channel and property method names. A property sharing its name with a
 * channel gets `Value`; a reserved core name gets `Channel`/`Value`; any remaining collision fails.
 */
export function templateMemberNames(
  chartType: string,
  channels: readonly string[],
  properties: readonly string[],
  renames: Readonly<Record<string, string>> = {},
): TemplateMemberNames {
  const base = `catalog.chartTypes[${chartType}]`;
  const taken = new Map<string, string>();
  const claim = (name: string, path: string) => {
    const previous = taken.get(name);
    if (previous !== undefined) {
      throw new Error(`Member name collision "${name}": ${previous} and ${path}. Add a rename to generator/overrides.json.`);
    }
    taken.set(name, path);
  };

  const channelNames = new Map<string, string>();
  for (const channel of channels) {
    let name = renames[`${chartType}/channel/${channel}`] ?? pascal(channel);
    if (RESERVED_CORE_NAMES.has(name)) name += 'Channel';
    claim(name, `${base}.channels[${channel}]`);
    channelNames.set(channel, name);
  }

  const channelSet = new Set(channelNames.values());
  const propertyNames = new Map<string, string>();
  for (const key of properties) {
    let name = renames[`${chartType}/property/${key}`] ?? pascal(key);
    if (channelSet.has(name) || RESERVED_CORE_NAMES.has(name)) name += 'Value';
    claim(name, `${base}.properties[${key}]`);
    propertyNames.set(key, name);
  }
  return { channels: channelNames, properties: propertyNames, enums: new Map() };
}

export interface CatalogNames {
  /** Chart type name -> entry name. */
  entries: Map<string, string>;
  /** Chart type name -> its channel and property method names over the union of its backends. */
  members: Map<string, TemplateMemberNames>;
}

/**
 * Rules 2, 3 and 6 over the whole catalog; fails on any entry or member collision. Template renames
 * (`<Template>/channel/<ch>`, `<Template>/property/<key>`, `<Template>/enum/<key>` for a property that is discrete on
 * some backend, `<Template>/enum/<key>/<value>` for one of its option values) are applied; one that matches nothing fails.
 */
export function catalogNames(catalog: IrCatalog, renames: Readonly<Record<string, string>>): CatalogNames {
  const union = (t: IrChartType, pick: (b: IrBackendTemplate) => string[]) => [...new Set(Object.values(t.backends).flatMap(pick))].sort(ordinal);
  const used = new Set<string>();
  const members = new Map<string, TemplateMemberNames>();
  for (const t of catalog.chartTypes) {
    const channels = union(t, (b) => b.channels);
    const properties = union(t, (b) => b.properties.map((p) => p.key));
    const discrete = union(t, (b) => b.properties.filter((p) => p.type === 'discrete').map((p) => p.key));
    const values = union(t, (b) =>
      b.properties.filter((p) => p.type === 'discrete').flatMap((p) => (p.options ?? []).filter((o) => !o.omitted).map((o) => `${p.key}/${optionKey(o)}`)),
    );
    for (const key of [
      ...channels.map((c) => `${t.name}/channel/${c}`),
      ...properties.map((p) => `${t.name}/property/${p}`),
      ...discrete.map((p) => `${t.name}/enum/${p}`),
      ...values.map((v) => `${t.name}/enum/${v}`),
    ]) {
      if (key in renames) used.add(key);
    }
    const names = templateMemberNames(t.name, channels, properties, renames);
    for (const key of discrete) names.enums.set(key, enumName(t.name, key, renames));
    members.set(t.name, names);
  }
  const stale = Object.keys(renames).filter((k) => k.includes('/') && !used.has(k));
  if (stale.length > 0) throw new Error(`${stale.map((k) => `renames["${k}"]`).join(', ')}: template rename that matches nothing.`);
  return { entries: entryNames(catalog.chartTypes.map((t) => t.name)), members };
}
