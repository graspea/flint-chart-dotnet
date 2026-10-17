// options / theming emitter (design §7.4): one sealed partial DTO class per IR object type, members in IR order,
// [JsonPropertyName], WhenWritingNull, [JsonNumberHandling(Strict)], closed-generic VerbatimKeyDictionaryConverter<T>
// on map members, and a trailing [JsonExtensionData] Extensions. Unmapped constructs fail with their IR path.
// Also writes FlintMapJsonContext, which registers the map value types those converters resolve (design §5.1); it is a
// context of its own because System.Text.Json reads [JsonSerializable] from one partial declaration only.
import { ordinal, type Ir, type IrProp, type IrType } from '../ir.ts';
import { pascal } from '../naming.ts';
import { OverrideUse, type Overrides } from '../overrides.ts';
import { docComment, emptyResult, header, type EmitResult } from './common.ts';

/** Emission roots: every class reachable from a root is emitted into that root's namespace and folder. */
const ROOTS = [
  { type: 'AssembleOptions', namespace: 'Graspea.Flint', folder: 'Options' },
  { type: 'ThemeSpec', namespace: 'Graspea.Flint.Theming', folder: 'Theming' },
] as const;

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
  /** For maps: the closed generic argument of VerbatimKeyDictionaryConverter<T>. */
  converterArg?: string;
  remark?: string;
}

interface ClassPlan {
  name: string;
  namespace: string;
  folder: string;
  path: string;
  doc?: string;
  props: IrProp[];
}

const JSON_NODE: CsType = { cs: 'JsonNode', api: 'System.Text.Json.Nodes.JsonNode', value: false, node: true };

/** Element position: JsonNode stays nullable, other reference types are non-null. */
const inner = (t: CsType) => ({ cs: t.node ? `${t.cs}?` : t.cs, api: t.node || t.value ? `${t.api}${t.node ? '?' : ''}` : `${t.api}!` });

function andList(items: string[]): string {
  return items.length <= 1 ? items.join('') : `${items.slice(0, -1).join(', ')} and ${items.at(-1)}`;
}

export function emitOptions(ir: Ir, overrides: Overrides): EmitResult {
  const use = new OverrideUse(overrides);
  const errors: string[] = [];
  const classes = new Map<string, ClassPlan>();
  const queue: ClassPlan[] = [];
  /** Map value types (typeof form) that VerbatimKeyDictionaryConverter<T> resolves from FlintMapJsonContext. */
  const contextTypes = new Set<string>();

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
    const union = use.union(path);
    if (union !== undefined) return union === 'JsonNode' ? JSON_NODE : { cs: union, api: union, value: VALUE_TYPES.has(union) };
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
      case 'literalUnion':
        return { cs: 'string', api: 'string', value: false, remark: `One of: ${t.values.map((v) => `\`${v}\``).join(', ')}.` };
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
        const v = mapType(t.value, `${path}{*}`, owner, propName);
        const i = inner(v);
        // JsonNode values are written with WriteTo and never resolved from the context.
        if (!v.node) contextTypes.add(i.api.replace(/!/g, '').replace(/\?$/, ''));
        return { cs: `Dictionary<string, ${i.cs}>`, api: `System.Collections.Generic.Dictionary<string!, ${i.api}>`, value: false, converterArg: i.cs };
      }
      case 'object':
        return nested(t.props);
      case 'ref': {
        const target = ir.types[t.name];
        if (!target) return fail(path, `reference to ${t.name}, which the IR does not contain`);
        if (target.kind === 'record') return mapType(target, t.name, owner, propName);
        if (target.kind !== 'object') return fail(t.name, `named type of kind ${target.kind}`);
        const root = ROOTS.find((r) => r.type === t.name);
        plan({ name: t.name, namespace: root?.namespace ?? owner.namespace, folder: root?.folder ?? owner.folder, path: t.name, doc: target.doc, props: target.props });
        return { cs: t.name, api: `${root?.namespace ?? owner.namespace}.${t.name}`, value: false };
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
      default:
        return fail(path, `${t.kind}${'text' in t ? ` ${t.text}` : ''} (map it in overrides.json "unions")`);
    }
  }

  for (const root of ROOTS) {
    const t = ir.types[root.type];
    if (t?.kind === 'object') plan({ name: root.type, namespace: root.namespace, folder: root.folder, path: root.type, doc: t.doc, props: t.props });
  }

  const rendered = new Map<string, { content: string; api: string[] }>();
  while (queue.length > 0) {
    const c = queue.shift()!;
    const full = `${c.namespace}.${c.name}`;
    const api = [full, `${full}.${c.name}() -> void`];
    const members: string[] = [];
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
      const t = mapType(p.type, path, c, p.name);
      const ignoredBy = OPTION_CLASSES.has(c.name) ? ir.catalog.ignoredOptions[p.name] : undefined;
      const remarks = [
        t.remark,
        ignoredBy && `Overwritten by the ${andList(ignoredBy)} ${ignoredBy.length === 1 ? 'backend' : 'backends'}.`,
        use.docs(path),
      ].filter((r): r is string => !!r);
      const attrs = [`[JsonPropertyName("${p.name}")]`, '[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]'];
      if (t.converterArg) attrs.push(`[JsonConverter(typeof(VerbatimKeyDictionaryConverter<${t.converterArg}>))]`);
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
  result.ownedTypes = [...classes.values()].map((c) => `${c.namespace}.${c.name}`).sort(ordinal);
  return result;
}
