// tables emitter (design §7.4): internal CatalogTables with FrozenDictionary rows, read by the validator and
// SemanticType.IsRegistered. Semantic types map to their candidate encodings; discrete properties carry their option
// labels, which upstream accepts in place of a value (normalize-properties.ts). The row types and the set/map helpers are hand-written (Model/CatalogTables.cs).
import type { Ir, IrBackendTemplate, IrProperty } from '../ir.ts';
import { backendMember, csNumber, csString, emptyResult, header, type EmitResult } from './common.ts';

const KIND: Readonly<Record<IrProperty['type'], string>> = {
  binary: 'PropertyKind.Binary',
  discrete: 'PropertyKind.Discrete',
  continuous: 'PropertyKind.Continuous',
};

const nameSet = (items: readonly string[]) => `NameSet(${items.map(csString).join(', ')})`;
const backendSet = (items: readonly string[]) => `BackendSet(${items.map(backendMember).join(', ')})`;

function propertyRow(p: IrProperty): string {
  const args = [csString(p.key), KIND[p.type]];
  if (p.options) {
    const values = p.options.map((o) => ('value' in o && o.value !== undefined ? csString(JSON.stringify(o.value)) : 'null'));
    args.push(`Options: [${values.join(', ')}]`);
    args.push(`Labels: [${p.options.map((o) => csString(o.label)).join(', ')}]`);
  }
  if (p.min !== undefined) args.push(`Min: ${csNumber(p.min)}`);
  if (p.max !== undefined) args.push(`Max: ${csNumber(p.max)}`);
  if (p.step !== undefined) args.push(`Step: ${csNumber(p.step)}`);
  return `new PropertyRow(${args.join(', ')})`;
}

function templateRow(t: IrBackendTemplate, indent: string): string {
  const inner = indent + '    ';
  const args = [`${inner}[${t.channels.map(csString).join(', ')}]`];
  args.push(
    t.properties.length === 0
      ? `${inner}[]`
      : `${inner}[\n${t.properties.map((p) => `${inner}    ${propertyRow(p)},\n`).join('')}${inner}]`,
  );
  if (t.supportsFacets !== undefined) args.push(`${inner}supportsFacets: ${t.supportsFacets}`);
  if (t.hasValidate !== undefined) args.push(`${inner}hasValidate: ${t.hasValidate}`);
  return `new TemplateRow(\n${args.join(',\n')})`;
}

/** `ByName(\n    (key, value),\n    ...)`; an empty map needs the explicit type argument. */
function byName(entries: Array<[string, string]>, valueType: string, indent: string): string {
  if (entries.length === 0) return `ByName<${valueType}>()`;
  return `ByName(\n${entries.map(([k, v]) => `${indent}    (${csString(k)}, ${v})`).join(',\n')})`;
}

export function emitTables(ir: Ir): EmitResult {
  const c = ir.catalog;
  const backends = c.backends.map((b) => backendMember(b));
  const member = 'internal static readonly';

  const chartTypes = c.chartTypes.map(({ name, backends: perBackend }): [string, string] => {
    const rows = c.backends
      .filter((b) => b in perBackend)
      .map((b) => `            (${backendMember(b)}, ${templateRow(perBackend[b], '            ')})`);
    return [name, `ByBackend(\n${rows.join(',\n')})`];
  });
  const unknownBackends = c.chartTypes.flatMap((t) => Object.keys(t.backends).filter((b) => !c.backends.includes(b)).map((b) => `${t.name}@${b}`));
  if (unknownBackends.length > 0) throw new Error(`Templates on undeclared backends: ${unknownBackends.join(', ')}.`);

  const body = [
    `    ${member} FlintBackend[] Backends = [${backends.join(', ')}];`,
    `    ${member} FrozenSet<FlintBackend> BackendsApplyingTheme = ${backendSet(c.backendsApplyingTheme)};`,
    `    ${member} FrozenSet<string> HostStateKeys = ${nameSet(c.hostStateKeys)};`,
    `    ${member} FrozenDictionary<string, FrozenSet<FlintBackend>> IgnoredOptions = ${byName(
      Object.entries(c.ignoredOptions).map(([k, v]) => [k, backendSet(v)]),
      'FrozenSet<FlintBackend>',
      '    ',
    )};`,
    `    ${member} FrozenSet<string> Channels = ${nameSet(c.channels)};`,
    `    ${member} FrozenDictionary<string, string[]> SemanticTypes = ${byName(
      c.semanticTypes.map((s) => [s.name, `Names(${s.visEncodings.map(csString).join(', ')})`]),
      'string[]',
      '    ',
    )};`,
    `    ${member} FrozenSet<string> ThemePresets = ${nameSet(c.themePresets.map((p) => p.id))};`,
    `    ${member} FrozenDictionary<string, FrozenDictionary<FlintBackend, TemplateRow>> ChartTypes = ${byName(
      chartTypes,
      'FrozenDictionary<FlintBackend, TemplateRow>',
      '    ',
    )};`,
  ];

  const content =
    `${header(ir)}\nusing System.Collections.Frozen;\n\nnamespace Graspea.Flint;\n\n` +
    `internal static partial class CatalogTables\n{\n${body.join('\n\n')}\n}\n`;
  const result = emptyResult();
  result.files.push({ path: 'Tables/CatalogTables.g.cs', content });
  return result;
}
