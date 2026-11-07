// options / theming emitter (design §7.4): one sealed partial DTO class per IR object type, members in IR order,
// [JsonPropertyName], WhenWritingNull, [JsonNumberHandling(Strict)], closed-generic VerbatimKeyDictionaryConverter<T>
// (IntKeyDictionaryConverter<T> for number keys) on map members, and a trailing [JsonExtensionData] Extensions. A record
// keyed by string literals is a class with one member per key; an open literal union (`'a' | string`) is a string-backed
// struct with its converter, one per literal set. Unmapped constructs fail with their IR path.
// Also writes FlintMapJsonContext, which registers the map value types those converters resolve (design §5.1); it is a
// context of its own because System.Text.Json reads [JsonSerializable] from one partial declaration only.
import { ordinal, type Ir, type IrProp, type IrType } from '../ir.ts';
import { pascal } from '../naming.ts';
import { OverrideUse, type Overrides } from '../overrides.ts';
import { csString, docComment, emptyResult, header, type EmitResult } from './common.ts';

/** Emission roots: every class reachable from one root only is emitted into that root's namespace and folder. */
const ROOTS = [
  { type: 'AssembleOptions', namespace: 'Graspea.Flint', folder: 'Options' },
  { type: 'ThemeSpec', namespace: 'Graspea.Flint.Theming', folder: 'Theming' },
] as const;

/** A named type reached from more than one root: shared, like the hand-written ContinuousMarkCrossSection. */
const SHARED = { namespace: 'Graspea.Flint', folder: 'Options' } as const;

/**
 * The namespace and folder of every named type reachable from a root: its root's, or SHARED when more than one root
 * reaches it. Decided from reachability alone, so it does not depend on the order the emitter meets the types.
 */
function placeNamedTypes(ir: Ir): Map<string, { namespace: string; folder: string }> {
  const reachedBy = new Map<string, Set<(typeof ROOTS)[number]>>();
  const refs = (node: unknown, out: string[]): void => {
    if (node === null || typeof node !== 'object') return;
    const n = node as { kind?: unknown; name?: unknown };
    if (n.kind === 'ref' && typeof n.name === 'string') out.push(n.name);
    for (const v of Object.values(node)) refs(v, out);
  };
  for (const root of ROOTS) {
    const queue: string[] = [root.type];
    const seen = new Set(queue);
    while (queue.length > 0) {
      const name = queue.shift()!;
      if (!reachedBy.has(name)) reachedBy.set(name, new Set());
      reachedBy.get(name)!.add(root);
      const next: string[] = [];
      refs(ir.types[name], next);
      for (const r of next) {
        // Another root keeps its own namespace and does not pass this root on.
        if (seen.has(r) || ROOTS.some((o) => o.type === r)) continue;
        seen.add(r);
        queue.push(r);
      }
    }
  }
  return new Map([...reachedBy].map(([name, roots]) => [name, roots.size === 1 ? [...roots][0] : SHARED]));
}

/** Classes whose members are assembler options (ThemeCompileDefaults extends Partial<AssembleOptions>). */
const OPTION_CLASSES: ReadonlySet<string> = new Set(['AssembleOptions', 'ThemeCompileDefaults']);

/** Hand-written value types that overrides may name (they take no `!` in PublicAPI lines). */
const VALUE_TYPES = new Set(['Graspea.Flint.ChartSize']);

/** A mapped C# type: `cs` as written in source, `api` as PublicAPI.txt writes it, both without nullability. */
interface CsType {
  cs: string;
  api: string;
  value: boolean;
  /** JsonNode: nullable even as an element, because JSON null is a value. */
  node?: boolean;
  /** For maps: the converter and its closed generic argument. */
  converter?: 'VerbatimKeyDictionaryConverter' | 'IntKeyDictionaryConverter';
  converterArg?: string;
  remark?: string;
}

interface ClassPlan {
  name: string;
  namespace: string;
  folder: string;
  path: string;
  doc?: string;
  /** `mapped`: the member type, already mapped (the keys of a literal-keyed record share one value type). */
  props: Array<IrProp & { mapped?: CsType }>;
}

/** An open literal union (`'a' | 'b' | string`) and the members typed by it; identical literal sets share one struct. */
interface OpenUnion {
  values: string[];
  /** Per member: its IR path, the name it proposes (owner's last path segment + member) and the owner's namespace and folder. */
  uses: Array<{ path: string; name: string; namespace: string; folder: string }>;
}

/** The struct of an open literal union: its name, namespace and folder. */
type OpenName = { name: string; namespace: string; folder: string };

/** Members a string-backed struct declares itself, so no literal may take them. */
const STRUCT_MEMBERS = new Set(['Value', 'ToString', 'Equals', 'GetHashCode', 'GetType', 'EqualityContract']);

const JSON_NODE: CsType = { cs: 'JsonNode', api: 'System.Text.Json.Nodes.JsonNode', value: false, node: true };

/** Element position: JsonNode stays nullable, other reference types are non-null. */
const inner = (t: CsType) => ({ cs: t.node ? `${t.cs}?` : t.cs, api: t.node || t.value ? `${t.api}${t.node ? '?' : ''}` : `${t.api}!` });

function andList(items: string[]): string {
  return items.length <= 1 ? items.join('') : `${items.slice(0, -1).join(', ')} and ${items.at(-1)}`;
}

/** A generated options or theming class as the spec-to-C# name tables see it (design §12.7). */
export interface OptionClass {
  /** Namespace-qualified name, such as `Graspea.Flint.Theming.ThemeInk`. */
  name: string;
  /** Members in IR order: wire name, C# property and the C# type as the generated class declares it, without `?`. */
  members: Array<{ wire: string; name: string; type: string }>;
}

/** A generated open literal union struct as the spec-to-C# name tables see it: its literals and their properties. */
export interface OpenStruct {
  /** Namespace-qualified name, such as `Graspea.Flint.Theming.PointInk`. */
  name: string;
  literals: Array<{ wire: string; member: string }>;
}

export function emitOptions(ir: Ir, overrides: Overrides): EmitResult {
  return buildOptions(ir, overrides).result;
}

/** Every generated options and theming class with its members, in the order the classes are emitted. */
export function optionClasses(ir: Ir, overrides: Overrides): OptionClass[] {
  return buildOptions(ir, overrides).classes;
}

/** Every generated open literal union struct, by name. */
export function openStructs(ir: Ir, overrides: Overrides): OpenStruct[] {
  return buildOptions(ir, overrides).structs;
}

/** Two passes: the first finds every member of each open literal union, so the struct name can be chosen from all of them. */
type Built = { result: EmitResult; classes: OptionClass[]; structs: OpenStruct[] };

function buildOptions(ir: Ir, overrides: Overrides): Built {
  const opens = new Map<string, OpenUnion>();
  buildPass(ir, overrides, opens, undefined);
  const names = new Map<string, OpenName>();
  for (const [key, u] of opens) {
    const best = [...u.uses].sort((a, b) => a.name.length - b.name.length || ordinal(a.name, b.name))[0];
    names.set(key, { name: best.name, namespace: best.namespace, folder: best.folder });
  }
  return buildPass(ir, overrides, new Map(), names);
}

const openKey = (values: string[]) => values.join('\n');

/** The last segment of an IR path, without `{*}` and `[]`: `ThemeMarks.point` -> `point`. */
const lastSegment = (path: string) => path.split('.').at(-1)!.replace(/\{\*\}|\[\d*\]/g, '');

function buildPass(
  ir: Ir,
  overrides: Overrides,
  opens: Map<string, OpenUnion>,
  openNames: Map<string, OpenName> | undefined,
): Built {
  const use = new OverrideUse(overrides);
  const errors: string[] = [];
  const classes = new Map<string, ClassPlan>();
  const queue: ClassPlan[] = [];
  /** Map value types (typeof form) that VerbatimKeyDictionaryConverter<T> resolves from FlintMapJsonContext. */
  const contextTypes = new Set<string>();
  const placed = placeNamedTypes(ir);

  const plan = (p: ClassPlan) => {
    const existing = classes.get(p.name);
    if (existing) {
      if (existing.path !== p.path) errors.push(`class name collision "${p.name}": ${existing.path} and ${p.path}`);
      return;
    }
    classes.set(p.name, p);
    queue.push(p);
  };

  const fail = (path: string, what: string): CsType => {
    errors.push(`${path}: ${what}`);
    return { cs: 'object', api: 'object', value: false };
  };

  function mapType(t: IrType, path: string, owner: ClassPlan, propName: string): CsType {
    const dedup = use.dedup(path);
    if (dedup !== undefined) {
      const shape = t.kind === 'object' ? t.props.map((p) => `${p.name}${p.optional ? '?' : ''}:${p.type.kind}`).join(',') : t.kind;
      if (shape !== 'height:number,width:number') return fail(path, `dedup to ${dedup} needs {width: number, height: number}, found ${shape}`);
      return { cs: dedup, api: dedup, value: VALUE_TYPES.has(dedup) };
    }
    const nested = (props: IrProp[], doc?: string): CsType => {
      const name = owner.name + pascal(propName);
      plan({ name, namespace: owner.namespace, folder: owner.folder, path, doc, props });
      return { cs: name, api: `${owner.namespace}.${name}`, value: false };
    };

    switch (t.kind) {
      case 'string':
        return { cs: 'string', api: 'string', value: false };
      case 'number':
        return { cs: 'double', api: 'double', value: true };
      case 'boolean':
        return { cs: 'bool', api: 'bool', value: true };
      case 'literal':
        if (typeof t.value === 'string') return { cs: 'string', api: 'string', value: false, remark: `Value: \`${t.value}\`.` };
        return fail(path, `non-string literal ${JSON.stringify(t.value)}`);
      case 'literalUnion': {
        if (!t.open) return { cs: 'string', api: 'string', value: false, remark: `One of: ${t.values.map((v) => `\`${v}\``).join(', ')}.` };
        const key = openKey(t.values);
        const u = opens.get(key) ?? { values: t.values, uses: [] };
        opens.set(key, u);
        u.uses.push({ path, name: pascal(lastSegment(owner.path)) + pascal(propName), namespace: owner.namespace, folder: owner.folder });
        const named = openNames?.get(key);
        if (!named) return { cs: 'string', api: 'string', value: false }; // first pass
        const known = t.values.map((v) => `\`${v}\` (\`${named.name}.${literalMember(v) ?? '?'}\`)`);
        return {
          cs: named.name,
          api: `${named.namespace}.${named.name}`,
          value: true,
          remark: `${known.length > 1 ? `${known.slice(0, -1).join(', ')}, ${known.at(-1)}` : known[0]} or any other string.`,
        };
      }
      case 'array': {
        const el = mapType(t.element, `${path}[]`, owner, propName);
        if (el.node) return fail(`${path}[]`, 'JsonNode array elements would be written with the caller\'s options');
        const i = inner(el);
        return { cs: `${i.cs}[]`, api: `${i.api}[]`, value: false, remark: el.remark && `Each item: ${el.remark.charAt(0).toLowerCase()}${el.remark.slice(1)}` };
      }
      case 'tuple':
        if (t.elements.every((e) => e.kind === 'number')) return { cs: 'double[]', api: 'double[]', value: false };
        return fail(path, 'tuple with non-number elements');
      case 'record': {
        if (t.keys) {
          // One member per key, all of one value type, mapped once (so an object value is one class, `<Record>Value`).
          const keyed: ClassPlan = { name: owner.name + pascal(propName), namespace: owner.namespace, folder: owner.folder, path, props: [] };
          const v = mapType(t.value, `${path}{*}`, keyed, 'value');
          keyed.props = t.keys.map((k) => ({ name: k, optional: true, doc: `The entry for the \`${k}\` key.`, type: t.value, mapped: v }));
          plan(keyed);
          return { cs: keyed.name, api: `${keyed.namespace}.${keyed.name}`, value: false };
        }
        const v = mapType(t.value, `${path}{*}`, owner, propName);
        const i = inner(v);
        // JsonNode values are written with WriteTo and never resolved from the context.
        if (!v.node) contextTypes.add(i.api.replace(/!/g, '').replace(/\?$/, ''));
        const [key, keyApi] = t.key === 'number' ? ['int', 'int'] : ['string', 'string!'];
        return {
          cs: `Dictionary<${key}, ${i.cs}>`,
          api: `System.Collections.Generic.Dictionary<${keyApi}, ${i.api}>`,
          value: false,
          converter: t.key === 'number' ? 'IntKeyDictionaryConverter' : 'VerbatimKeyDictionaryConverter',
          converterArg: i.cs,
        };
      }
      case 'object':
        return nested(t.props);
      case 'ref': {
        const target = ir.types[t.name];
        if (!target) return fail(path, `reference to ${t.name}, which the IR does not contain`);
        if (target.kind === 'record') return mapType(target, t.name, owner, propName);
        if (target.kind !== 'object') return fail(t.name, `named type of kind ${target.kind}`);
        const place = placed.get(t.name) ?? owner;
        plan({ name: t.name, namespace: place.namespace, folder: place.folder, path: t.name, doc: target.doc, props: target.props });
        return { cs: t.name, api: `${place.namespace}.${t.name}`, value: false };
      }
      case 'partial':
        return mapType(t.of, path, owner, propName);
      case 'mapped':
        if (use.flatten(path)) return nested(t.props);
        return fail(path, `mapped type ${t.text} (accept it with overrides.json "flatten")`);
      case 'intersection': {
        const props: IrProp[] = [];
        for (const m of t.members) {
          const resolved = m.kind === 'partial' ? m.of : m;
          const obj = resolved.kind === 'ref' ? ir.types[resolved.name] : resolved;
          if (obj?.kind !== 'object') return fail(path, 'intersection with a non-object member');
          props.push(...obj.props);
        }
        return nested(props.sort((a, b) => ordinal(a.name, b.name)));
      }
      default: {
        // Only here: a member the mapping maps keeps its type, so an entry written ahead for a wider upstream type
        // (a pending path) leaves the pinned version's member as it is.
        const union = use.union(path);
        if (union !== undefined) return union === 'JsonNode' ? JSON_NODE : { cs: union, api: union, value: VALUE_TYPES.has(union) };
        return fail(path, `${t.kind}${'text' in t ? ` ${t.text}` : ''} (map it in overrides.json "unions")`);
      }
    }
  }

  for (const root of ROOTS) {
    const t = ir.types[root.type];
    if (t?.kind === 'object') plan({ name: root.type, namespace: root.namespace, folder: root.folder, path: root.type, doc: t.doc, props: t.props });
  }

  const rendered = new Map<string, { content: string; api: string[] }>();
  const shapes: OptionClass[] = [];
  while (queue.length > 0) {
    const c = queue.shift()!;
    const full = `${c.namespace}.${c.name}`;
    const api = [full, `${full}.${c.name}() -> void`];
    const members: string[] = [];
    const shape: OptionClass = { name: full, members: [] };
    shapes.push(shape);
    const names = new Map<string, string>([['Extensions', `${c.path} (Extensions)`], [c.name, `${c.path} (class name)`]]);
    for (const p of c.props) {
      const path = `${c.path}.${p.name}`;
      const name = use.rename(path) ?? pascal(p.name);
      const clash = names.get(name);
      if (clash !== undefined) {
        errors.push(`member name collision "${name}": ${clash} and ${path}`);
        continue;
      }
      names.set(name, path);
      const t = p.mapped ?? mapType(p.type, path, c, p.name);
      shape.members.push({ wire: p.name, name, type: t.cs });
      const ignoredBy = OPTION_CLASSES.has(c.name) ? ir.catalog.ignoredOptions[p.name] : undefined;
      const remarks = [
        t.remark,
        ignoredBy && `Overwritten by the ${andList(ignoredBy)} ${ignoredBy.length === 1 ? 'backend' : 'backends'}.`,
        use.docs(path),
      ].filter((r): r is string => !!r);
      const attrs = [`[JsonPropertyName("${p.name}")]`, '[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]'];
      if (t.converterArg) attrs.push(`[JsonConverter(typeof(${t.converter}<${t.converterArg}>))]`);
      if (t.node) attrs.push('[JsonConverter(typeof(JsonNodePassThroughConverter))]');
      members.push(
        docComment('    ', p.doc ?? `The \`${p.name}\` member.`, remarks.join('\n\n') || undefined) +
          attrs.map((a) => `    ${a}\n`).join('') +
          `    public ${t.cs}? ${name} { get; set; }\n`,
      );
      api.push(`${full}.${name}.get -> ${t.api}?`, `${full}.${name}.set -> void`);
    }
    members.push(
      docComment('    ', 'Members this version does not model, kept for round-trips; written after the known members.') +
        '    [JsonExtensionData]\n    public Dictionary<string, JsonElement>? Extensions { get; set; }\n',
    );
    api.push(
      `${full}.Extensions.get -> System.Collections.Generic.Dictionary<string!, System.Text.Json.JsonElement>?`,
      `${full}.Extensions.set -> void`,
    );
    const content =
      `${header(ir)}\n` +
      'using System.Collections.Generic;\nusing System.Text.Json;\nusing System.Text.Json.Nodes;\nusing System.Text.Json.Serialization;\n' +
      'using Graspea.Flint.Serialization;\n\n' +
      `namespace ${c.namespace};\n\n` +
      docComment('', c.doc ?? `The \`${c.path}\` object.`, use.docs(c.path)) +
      '[JsonNumberHandling(JsonNumberHandling.Strict)]\n' +
      `public sealed partial class ${c.name}\n{\n${members.join('\n')}}\n`;
    rendered.set(`${c.folder}/${c.name}.g.cs`, { content, api });
  }

  const structs: string[] = [];
  const structShapes: OpenStruct[] = [];
  for (const [key, named] of openNames ?? []) {
    const u = opens.get(key);
    if (!u) continue;
    const full = `${named.namespace}.${named.name}`;
    const paths = [...new Set(u.uses.map((x) => x.path))];
    const clash = classes.get(named.name)?.path ?? (structs.includes(full) ? '(another open literal union)' : undefined);
    if (clash !== undefined) {
      errors.push(`type name collision "${named.name}": ${clash} and ${paths.join(', ')}`);
      continue;
    }
    const members = new Map<string, string>();
    for (const v of u.values) {
      const member = literalMember(v);
      if (member === undefined) {
        errors.push(`${paths[0]}: literal "${v}" gives no C# member name`);
        continue;
      }
      if (STRUCT_MEMBERS.has(member) || member === named.name || members.has(member)) {
        errors.push(`${paths[0]}: literal "${v}" gives the member name ${member}, which ${members.has(member) ? `"${members.get(member)}" also gives` : `the struct ${named.name} declares`}`);
        continue;
      }
      members.set(member, v);
    }
    structs.push(full);
    structShapes.push({ name: full, literals: [...members].map(([member, wire]) => ({ wire, member })) });
    const struct = openStruct(ir, named, paths, members);
    rendered.set(`${named.folder}/${named.name}.g.cs`, struct.type);
    rendered.set(`Json/${named.name}Converter.g.cs`, struct.converter);
  }

  // string keeps the context non-empty (the converter compiles against it) and is the commonest map value.
  contextTypes.add('string');
  rendered.set('Json/FlintMapJsonContext.g.cs', {
    content:
      `${header(ir)}\nusing System.Text.Json.Serialization;\n\nnamespace Graspea.Flint.Serialization;\n\n` +
      '/// <summary>Map value types of the generated members, resolved by <see cref="VerbatimKeyDictionaryConverter{T}"/>.</summary>\n' +
      '[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.Strict)]\n' +
      [...contextTypes].sort(ordinal).map((t) => `[JsonSerializable(typeof(${t}))]\n`).join('') +
      'internal sealed partial class FlintMapJsonContext : JsonSerializerContext;\n',
    api: [],
  });

  use.assertAllUsed((key) => !key.includes('/') && !key.startsWith('catalog.'));
  if (errors.length > 0) throw new Error(`Cannot map these IR constructs:\n  ${errors.join('\n  ')}`);

  const result = emptyResult();
  for (const path of [...rendered.keys()].sort(ordinal)) {
    const { content, api } = rendered.get(path)!;
    result.files.push({ path, content });
    result.apiLines.push(...api);
  }
  result.ownedTypes = [
    ...[...classes.values()].map((c) => `${c.namespace}.${c.name}`),
    ...structs,
    ...structs.map((t) => `Graspea.Flint.Serialization.${t.slice(t.lastIndexOf('.') + 1)}Converter`),
  ].sort(ordinal);
  return { result, classes: shapes.sort((a, b) => ordinal(a.name, b.name)), structs: structShapes.sort((a, b) => ordinal(a.name, b.name)) };
}

/** The C# member of an open union literal: Pascal(literal), or undefined when the literal gives no name. */
function literalMember(value: string): string | undefined {
  try {
    return pascal(value);
  } catch {
    return undefined;
  }
}

/** The string-backed struct of an open literal union and its converter (the pattern of the constant structs, design §4.1). */
function openStruct(
  ir: Ir,
  named: OpenName,
  paths: string[],
  members: Map<string, string>,
): { type: { content: string; api: string[] }; converter: { content: string; api: string[] } } {
  const { name, namespace } = named;
  const full = `${namespace}.${name}`;
  const statics = [...members].map(
    ([member, value]) => `${docComment('    ', `The \`${value}\` value.`)}    public static ${name} ${member} { get; } = new(${csString(value)});\n`,
  );
  const type =
    `${header(ir)}\nusing System.Text.Json.Serialization;\nusing Graspea.Flint.Serialization;\n\nnamespace ${namespace};\n\n` +
    docComment(
      '',
      `A value of ${andList(paths.map((p) => `\`${p}\``))}: one of the values below or any other string, written verbatim.`,
      'Any string converts implicitly; a null string gives null.',
    ) +
    `[JsonConverter(typeof(${name}Converter))]\npublic readonly partial record struct ${name}\n{\n` +
    docComment('    ', 'Creates a value from its wire string.') +
    `    /// <param name="value">The string, written verbatim.</param>\n` +
    `    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>\n` +
    `    public ${name}(string value)\n    {\n        ArgumentNullException.ThrowIfNull(value);\n        Value = value;\n    }\n\n` +
    statics.join('\n') +
    '\n' +
    docComment('    ', `The wire string; null only for \`default(${name})\`, which cannot be serialised.`) +
    '    public string Value { get; }\n\n' +
    docComment('    ', 'A value from its wire string.') +
    '    /// <param name="value">The string, written verbatim.</param>\n' +
    `    public static implicit operator ${name}(string value) => new(value);\n\n` +
    docComment('    ', 'A value from its wire string; null for a null string.') +
    '    /// <param name="value">The string, written verbatim, or null.</param>\n' +
    `    public static implicit operator ${name}?(string? value) => value is null ? default(${name}?) : new ${name}(value);\n\n` +
    docComment('    ', 'The wire string.') +
    `    /// <returns><see cref="Value"/>, or an empty string for <c>default(${name})</c>.</returns>\n` +
    '    public override string ToString() => Value ?? string.Empty;\n}\n';
  const typeApi = [
    full,
    `${full}.${name}() -> void`,
    `${full}.${name}(string! value) -> void`,
    `${full}.Equals(${full} other) -> bool`,
    `${full}.Value.get -> string!`,
    `override ${full}.GetHashCode() -> int`,
    `override ${full}.ToString() -> string!`,
    `static ${full}.operator !=(${full} left, ${full} right) -> bool`,
    `static ${full}.operator ==(${full} left, ${full} right) -> bool`,
    `static ${full}.implicit operator ${full}(string! value) -> ${full}`,
    `static ${full}.implicit operator ${full}?(string? value) -> ${full}?`,
    ...[...members.keys()].map((m) => `static ${full}.${m}.get -> ${full}`),
    `~override ${full}.Equals(object obj) -> bool`,
  ];
  const conv = `Graspea.Flint.Serialization.${name}Converter`;
  const converter =
    `${header(ir)}\nusing System.ComponentModel;\nusing System.Text.Json;\nusing System.Text.Json.Serialization;\nusing ${namespace};\n\n` +
    'namespace Graspea.Flint.Serialization;\n\n' +
    `/// <summary>Writes <see cref="${name}"/> as its string, verbatim. Infrastructure: public only so that a host\n` +
    '/// source-generated <see cref="JsonSerializerContext"/> can instantiate it.</summary>\n' +
    `[EditorBrowsable(EditorBrowsableState.Never)]\npublic sealed class ${name}Converter : JsonConverter<${name}>\n{\n` +
    '    /// <inheritdoc/>\n' +
    `    public override ${name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>\n` +
    `        StringBacked.Read(ref reader, static value => new ${name}(value));\n\n` +
    '    /// <inheritdoc/>\n' +
    `    public override void Write(Utf8JsonWriter writer, ${name} value, JsonSerializerOptions options) =>\n` +
    `        StringBacked.Write<${name}>(writer, value.Value);\n}\n`;
  const converterApi = [
    conv,
    `${conv}.${name}Converter() -> void`,
    `override ${conv}.Read(ref System.Text.Json.Utf8JsonReader reader, System.Type! typeToConvert, System.Text.Json.JsonSerializerOptions! options) -> ${full}`,
    `override ${conv}.Write(System.Text.Json.Utf8JsonWriter! writer, ${full} value, System.Text.Json.JsonSerializerOptions! options) -> void`,
  ];
  return { type: { content: type, api: typeApi }, converter: { content: converter, api: converterApi } };
}
