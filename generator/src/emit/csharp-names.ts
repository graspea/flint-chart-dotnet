// csharp-names emitter (design §12.7): the C# spelling of every catalog name for the spec-to-C# conversion core in
// src/Graspea.Flint.CSharp/. It reads the shared fluent model and the constants and options emitters, so its names cannot
// differ from the compiled facades. It also writes the catalog the core's checks need (Catalog.g.cs), with the property
// rows of the library's tables emitter. The output is plain arrays (netstandard2.0 and net8.0); the core builds its
// dictionaries from them on first use.
import type { Ir, IrProperty } from '../ir.ts';
import type { Overrides } from '../overrides.ts';
import { csNumber, csString, header, type EmittedFile } from './common.ts';
import { constantNames } from './constants.ts';
import { wireValue } from './enums.ts';
import { fluentModel, type FluentProperty } from './fluent.ts';
import { optionClasses } from './options.ts';

const KINDS: Readonly<Record<FluentProperty['kind'], string>> = {
  binary: 'PropertyKind.Bool',
  continuous: 'PropertyKind.Double',
  discrete: 'PropertyKind.Enum',
  json: 'PropertyKind.Json',
};

function enumMembers(p: FluentProperty): string {
  const members = (p.enum?.members ?? []).flatMap((m) => {
    const value = wireValue(m);
    if (value === undefined) return [];
    const [kind, text] =
      typeof value === 'string' ? ['String', csString(value)] : typeof value === 'number' ? ['Number', csString(csNumber(value))] : ['Boolean', csString(String(value))];
    return [`new(WireKind.${kind}, ${text}, ${csString(m.name)})`];
  });
  return `[${members.join(', ')}]`;
}

const list = (name: string, type: string, items: string[]) =>
  `    internal static readonly ${type}[] ${name} =\n    [\n${items.map((i) => `        ${i},\n`).join('')}    ];\n`;

const CATALOG_KINDS: Readonly<Record<IrProperty['type'], string>> = {
  binary: 'CatalogPropertyKind.Binary',
  discrete: 'CatalogPropertyKind.Discrete',
  continuous: 'CatalogPropertyKind.Continuous',
};

const strings = (items: readonly string[]) => `[${items.map(csString).join(', ')}]`;

/** A property row as the library's tables emitter writes it: options as the JSON text of each value, null when omitted. */
function catalogProperty(p: IrProperty): string {
  const options = p.options
    ? `[${p.options.map((o) => ('value' in o && o.value !== undefined ? csString(JSON.stringify(o.value)) : 'null')).join(', ')}]`
    : 'null';
  const labels = p.options ? strings(p.options.map((o) => o.label)) : 'null';
  const bound = (n: number | undefined) => (n === undefined ? 'null' : csNumber(n));
  return `new(${csString(p.key)}, ${CATALOG_KINDS[p.type]}, ${options}, ${labels}, ${bound(p.min)}, ${bound(p.max)})`;
}

function emitCatalog(ir: Ir): EmittedFile {
  const c = ir.catalog;
  const chartTypes = c.chartTypes.map(({ name, backends }) => {
    const rows = c.backends
      .filter((b) => b in backends)
      .map((b) => {
        const t = backends[b];
        const properties = t.properties.length === 0 ? '[]' : `\n            [\n${t.properties.map((p) => `                ${catalogProperty(p)},\n`).join('')}            ]`;
        return `            new(${csString(b)}, ${strings(t.channels)},${properties === '[]' ? ' []' : properties}),\n`;
      });
    return `new(${csString(name)},\n        [\n${rows.join('')}        ])`;
  });
  const content =
    `${header(ir)}\nnamespace Graspea.Flint.CSharp;\n\ninternal static partial class Catalog\n{\n` +
    [
      `    internal static readonly string[] Backends = ${strings(c.backends)};\n`,
      `    internal static readonly string[] BackendsApplyingTheme = ${strings(c.backendsApplyingTheme)};\n`,
      `    internal static readonly string[] HostStateKeys = ${strings(c.hostStateKeys)};\n`,
      list('ChartTypes', 'ChartTypeRow', chartTypes),
    ].join('\n') +
    '}\n';
  return { path: 'Catalog.g.cs', content };
}

const pair = (wire: string, member: string) => `new(${csString(wire)}, ${csString(member)})`;

export function emitCSharpNames(ir: Ir, overrides: Overrides): EmittedFile[] {
  const constants = constantNames(ir, overrides);
  const struct = (name: string) => (constants.get(name) ?? []).map((c) => pair(c.value, c.member));
  const chartTypes = new Map((constants.get('ChartType') ?? []).map((c) => [c.value, c.member]));

  const templates = fluentModel(ir, overrides).map((t) => {
    const channels = t.channels.map((c) => pair(c.channel, c.method)).join(', ');
    const properties = t.properties
      .map((p) => `                new(${csString(p.key)}, ${csString(p.method)}, ${KINDS[p.kind]}, ${p.enum ? csString(p.enum.name) : 'null'}, ${enumMembers(p)}),\n`)
      .join('');
    const props = properties ? `[\n${properties}            ]` : '[]';
    return `new(${csString(t.name)}, ${csString(chartTypes.get(t.name) ?? t.constant)}, ${csString(t.entry)},\n            [${channels}],\n            ${props})`;
  });

  const semanticTypes = ir.catalog.semanticTypes.map((s) => {
    const member = (constants.get('SemanticType') ?? []).find((c) => c.value === s.name)?.member ?? s.name;
    return `new(${csString(s.name)}, ${csString(member)}, ${csString(s.visEncodings[0] ?? '')})`;
  });

  const classes = optionClasses(ir, overrides).map(
    (c) => `new(${csString(c.name)},\n        [\n${c.members.map((m) => `            new(${csString(m.wire)}, ${csString(m.name)}, ${csString(m.type)}),\n`).join('')}        ])`,
  );

  const content =
    `${header(ir)}\nnamespace Graspea.Flint.CSharp;\n\ninternal static partial class NameTables\n{\n` +
    [
      list('Templates', 'TemplateNames', templates),
      list('Channels', 'ConstantName', struct('Channel')),
      list('SemanticTypes', 'SemanticTypeName', semanticTypes),
      list('ThemePresets', 'ConstantName', struct('ThemePreset')),
      list('EncodingTypes', 'ConstantName', struct('EncodingType')),
      list('Aggregates', 'ConstantName', struct('Aggregate')),
      list('SortOrders', 'ConstantName', struct('SortOrder')),
      list('SeriesCountAxes', 'ConstantName', struct('SeriesCountAxis')),
      list('Classes', 'ClassNames', classes),
    ].join('\n') +
    '}\n';
  return [{ path: 'NameTables.g.cs', content }, emitCatalog(ir)];
}
