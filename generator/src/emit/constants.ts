// constants emitter (design §7.4): partial files adding generated constants to the hand-written string-backed
// structs, and FlintUpstream.Version.
import { ordinal, type Ir, type IrType } from '../ir.ts';
import { pascal } from '../naming.ts';
import { emptyOverrides, OverrideUse, type Overrides } from '../overrides.ts';
import { csString, docComment, emptyResult, header, type EmitResult } from './common.ts';

interface Constant {
  member: string;
  value: string;
  doc: string;
  path: string;
  /** From a string-literal union: the struct also gets the internal `Literals` set the validator checks against. */
  literal?: true;
}

/** Members of the hand-written structs that a constant must not take. */
const STRUCT_MEMBERS = new Set(['Name', 'ToString', 'Equals', 'GetHashCode', 'GetType', 'IsRegistered', 'Deconstruct']);

function literalValues(ir: Ir, path: string, pick: (t: IrType) => IrType | undefined): string[] {
  const [typeName] = path.split('.');
  const t = ir.types[typeName];
  const found = t ? pick(t) : undefined;
  if (found?.kind !== 'literalUnion') throw new Error(`IR path ${path} is not a string-literal union.`);
  return found.values;
}

const member = (t: IrType, name: string): IrType | undefined =>
  t.kind === 'object' ? t.props.find((p) => p.name === name)?.type : undefined;

function literal(ir: Ir, path: string, pick: (t: IrType) => IrType | undefined): Constant[] {
  return literalValues(ir, path, pick).map((v) => ({ member: pascal(v), value: v, doc: `The \`${v}\` literal.`, path: `${path}[${v}]`, literal: true }));
}

/** The catalog structs (ChartType, Channel, SemanticType, ThemePreset) with their rule-1 member names, before renames. */
function catalogStructs(ir: Ir): Array<[string, Constant[]]> {
  const c = ir.catalog;
  return [
    [
      'ChartType',
      c.chartTypes.map((t) => ({
        member: pascal(t.name),
        value: t.name,
        doc: `The \`${t.name}\` template; backends: ${c.backends.filter((b) => b in t.backends).join(', ')}.`,
        path: `catalog.chartTypes[${t.name}]`,
      })),
    ],
    ['Channel', c.channels.map((ch) => ({ member: pascal(ch), value: ch, doc: `The \`${ch}\` channel.`, path: `catalog.channels[${ch}]` }))],
    [
      'SemanticType',
      c.semanticTypes.map((s) => ({
        member: s.name,
        value: s.name,
        doc: `The \`${s.name}\` semantic type (${s.t0}, ${s.t1})${s.doc ? `: ${s.doc}` : '.'}`,
        path: `catalog.semanticTypes[${s.name}]`,
      })),
    ],
    [
      'ThemePreset',
      c.themePresets.map((p) => ({
        member: pascal(p.id),
        value: p.id,
        doc: `The \`${p.id}\` preset (${p.label}): ${p.description}`,
        path: `catalog.themePresets[${p.id}]`,
      })),
    ],
  ];
}

/** Struct name -> wire value -> C# member of the catalog structs, with the overrides.json renames applied. */
export function catalogConstantNames(ir: Ir, overrides: Overrides): Map<string, Map<string, string>> {
  return new Map(catalogStructs(ir).map(([struct, constants]) => [struct, new Map(constants.map((c) => [c.value, overrides.renames[c.path] ?? c.member]))]));
}

/**
 * Struct name -> its constants in emission order (wire value and C# member, overrides.json renames applied), for every
 * string-backed struct: the catalog structs and the literal structs (EncodingType, Aggregate, SortOrder, SeriesCountAxis).
 */
export function constantNames(ir: Ir, overrides: Overrides): Map<string, Array<{ value: string; member: string }>> {
  return new Map(structs(ir).map(([struct, constants]) => [struct, constants.map((c) => ({ value: c.value, member: overrides.renames[c.path] ?? c.member }))]));
}

function structs(ir: Ir): Array<[string, Constant[]]> {
  return [
    ...catalogStructs(ir),
    ['EncodingType', literal(ir, 'ChartEncoding.type', (t) => member(t, 'type'))],
    ['Aggregate', literal(ir, 'ChartEncoding.aggregate', (t) => member(t, 'aggregate'))],
    ['SortOrder', literal(ir, 'ChartEncoding.sortOrder', (t) => member(t, 'sortOrder'))],
    [
      'SeriesCountAxis',
      literal(ir, 'ContinuousMarkCrossSection.seriesCountAxis', (t) =>
        t.kind === 'union' ? t.members.map((m) => member(m, 'seriesCountAxis')).find((m) => m) : undefined,
      ),
    ],
  ];
}

export function emitConstants(ir: Ir, overrides: Overrides = emptyOverrides()): EmitResult {
  const use = new OverrideUse(overrides);
  const result = emptyResult();
  const errors: string[] = [];
  const files: Array<{ path: string; content: string }> = [];

  for (const [struct, constants] of structs(ir)) {
    const owner = new Map<string, string>([[struct, `${struct} (struct name)`]]);
    for (const m of STRUCT_MEMBERS) owner.set(m, `${struct}.${m} (hand-written member)`);
    for (const c of constants) {
      c.member = use.rename(c.path) ?? c.member;
      if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(c.member)) errors.push(`${c.path}: "${c.member}" is not a C# identifier`);
      const clash = owner.get(c.member);
      if (clash !== undefined) errors.push(`constant name collision "${struct}.${c.member}": ${clash} and ${c.path}`);
      owner.set(c.member, c.path);
    }
    let body = constants
      .map((c) => `${docComment('    ', c.doc)}    public static readonly ${struct} ${c.member} = new(${csString(c.value)});\n`)
      .join('\n');
    if (constants.length > 0 && constants.every((c) => c.literal)) {
      const values = constants.map((c) => csString(c.value)).join(', ');
      body +=
        `\n    internal static readonly System.Collections.Frozen.FrozenSet<string> Literals = ` +
        `System.Collections.Frozen.FrozenSet.ToFrozenSet([${values}], StringComparer.Ordinal);\n`;
    }
    files.push({
      path: `Constants/${struct}.g.cs`,
      content: `${header(ir)}\nnamespace Graspea.Flint;\n\npublic readonly partial record struct ${struct}\n{\n${body}}\n`,
    });
    result.apiLines.push(...constants.map((c) => `static readonly Graspea.Flint.${struct}.${c.member} -> Graspea.Flint.${struct}`));
    result.ownedConstantStructs.push(`Graspea.Flint.${struct}`);
  }
  use.assertAllUsed((key) => key.startsWith('catalog.'));
  if (errors.length > 0) throw new Error(`Cannot emit constants:\n  ${errors.join('\n  ')}`);

  const version = ir.upstream.version;
  files.push({
    path: 'Constants/FlintUpstream.g.cs',
    content:
      `${header(ir)}\nnamespace Graspea.Flint;\n\n` +
      docComment('', `The upstream \`${ir.upstream.package}\` package this library was generated from and tested against.`) +
      'public static partial class FlintUpstream\n{\n' +
      docComment('    ', `The pinned \`${ir.upstream.package}\` version.`) +
      `    public const string Version = ${csString(version)};\n}\n`,
  });
  result.apiLines.push('Graspea.Flint.FlintUpstream', `const Graspea.Flint.FlintUpstream.Version = ${csString(version)} -> string!`);
  result.ownedTypes.push('Graspea.Flint.FlintUpstream');

  result.files = files.sort((a, b) => ordinal(a.path, b.path));
  return result;
}
