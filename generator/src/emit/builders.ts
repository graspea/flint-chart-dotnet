// builders emitter (design §4.2, §7.4): one sealed partial {Name}Builder<TRow> per chart type over the hand-written CRTP
// core. Channel methods cover the union across the template's backends (string, Expression and ChartEncoding overloads);
// property methods map discrete -> the per-template enum, binary -> bool, continuous -> double, and a kind that differs
// across backends -> JsonNode (overrides.json). Every method is a one-line delegation to a core method.
import { csNumber, csString, docComment, emptyResult, header, xmlText, type EmitResult } from './common.ts';
import { declaredBy, fluentModel, type FluentProperty, type FluentTemplate } from './fluent.ts';
import type { Ir } from '../ir.ts';
import type { Overrides } from '../overrides.ts';

const INDENT = '    ';
const param = (name: string, text: string) => `${INDENT}/// <param name="${name}">${xmlText(text)}</param>\n`;
const RETURNS = `${INDENT}/// <returns>This builder.</returns>\n`;
const SEMANTIC_TYPE = param('semanticType', 'When set, also annotates the field in `semantic_types`.');

/** "0.5 to 5, step 0.5", or one such range per group of backends when they differ; empty when nothing is declared. */
function ranges(p: FluentProperty): string {
  const range = ({ min, max, step }: { min?: number; max?: number; step?: number }) => {
    const parts: string[] = [];
    if (min !== undefined && max !== undefined) parts.push(`${csNumber(min)} to ${csNumber(max)}`);
    else if (min !== undefined) parts.push(`at least ${csNumber(min)}`);
    else if (max !== undefined) parts.push(`at most ${csNumber(max)}`);
    if (step !== undefined) parts.push(`step ${csNumber(step)}`);
    return parts.join(', ');
  };
  const groups = new Map<string, string[]>();
  for (const d of p.declarations) {
    const text = range(d.property) || 'any value';
    groups.set(text, [...(groups.get(text) ?? []), d.backend]);
  }
  if (groups.size === 1) {
    const [only] = groups.keys();
    return only === 'any value' ? '' : only;
  }
  return [...groups].map(([text, backends]) => `${backends.join(', ')}: ${text}`).join('; ');
}

function kinds(p: FluentProperty): string {
  const groups = new Map<string, string[]>();
  for (const d of p.declarations) groups.set(d.property.type, [...(groups.get(d.property.type) ?? []), d.backend]);
  return [...groups].map(([kind, backends]) => `${backends.join(', ')}: ${kind}`).join('; ');
}

function channelMethods(t: FluentTemplate, self: string): { code: string; api: string[] } {
  let code = '';
  const api: string[] = [];
  const apiSelf = `Graspea.Flint.${t.builder}<TRow>`;
  for (const c of t.channels) {
    const support = declaredBy(t.backends, c.backends);
    const tail = support.all ? `${support.text}.` : `${support.text}, where \`Validate(backend)\` reports \`unsupported_channel\`.`;
    const call = `Channel.${c.constant}`;
    code +=
      `\n${docComment(INDENT, `Binds the \`${c.channel}\` channel to a field. ${tail}`)}` +
      param('field', 'The field: a key of the rows, used verbatim.') +
      SEMANTIC_TYPE +
      RETURNS +
      `${INDENT}public ${self} ${c.method}(string field, SemanticType? semanticType = null) => Encode(${call}, field, semanticType);\n` +
      `\n${docComment(INDENT, `Binds the \`${c.channel}\` channel to the field a row member is written as. ${tail}`)}` +
      param('selector', 'A property or field of the row, such as `r => r.Month`.') +
      SEMANTIC_TYPE +
      RETURNS +
      `${INDENT}public ${self} ${c.method}(Expression<Func<TRow, object?>> selector, SemanticType? semanticType = null) => Encode(${call}, selector, semanticType);\n` +
      `\n${docComment(INDENT, `Binds the \`${c.channel}\` channel to an encoding with any of its members. ${tail}`)}` +
      param('encoding', 'The encoding; the builder keeps a copy.') +
      RETURNS +
      `${INDENT}public ${self} ${c.method}(ChartEncoding encoding) => Encode(${call}, encoding);\n`;
    api.push(
      `${apiSelf}.${c.method}(string! field, Graspea.Flint.SemanticType? semanticType = null) -> ${apiSelf}!`,
      `${apiSelf}.${c.method}(System.Linq.Expressions.Expression<System.Func<TRow, object?>!>! selector, Graspea.Flint.SemanticType? semanticType = null) -> ${apiSelf}!`,
      `${apiSelf}.${c.method}(Graspea.Flint.ChartEncoding! encoding) -> ${apiSelf}!`,
    );
  }
  return { code, api };
}

function propertyMethods(t: FluentTemplate, self: string, hostStateKeys: ReadonlySet<string>): { code: string; api: string[] } {
  let code = '';
  const api: string[] = [];
  const apiSelf = `Graspea.Flint.${t.builder}<TRow>`;
  for (const p of t.properties) {
    const support = declaredBy(t.backends, p.backends);
    // Validate(backend) reports an undeclared key as unknown_chart_property, except a host-state key (upstream reads it anyway).
    const tail = support.all
      ? `${support.text}.`
      : hostStateKeys.has(p.key)
        ? `${support.text}. It is a host-state key, read even where undeclared, so \`Validate(backend)\` does not report it.`
        : `${support.text}, where \`Validate(backend)\` reports \`unknown_chart_property\`.`;
    let summary = `Sets the \`${p.key}\` chart property ("${p.label}"). ${tail}`;
    const key = csString(p.key);
    let valueDoc = 'The value.';
    let signature: string;
    let body: string;
    let apiType: string;
    switch (p.kind) {
      case 'discrete':
        valueDoc = 'The value; `Default` omits the key.';
        signature = `${p.enum!.name} value`;
        body = `Property(${key}, ${p.enum!.name}Values.ToJson(value))`;
        apiType = `Graspea.Flint.${p.enum!.name} value`;
        break;
      case 'binary':
        signature = 'bool value';
        body = `Property(${key}, value)`;
        apiType = 'bool value';
        break;
      case 'continuous': {
        const range = ranges(p);
        if (range) valueDoc = `The value; ${range}.`;
        signature = 'double value';
        body = `Property(${key}, value)`;
        apiType = 'double value';
        break;
      }
      case 'json':
        summary += ` Its kind differs across backends (${kinds(p)}), so the value is raw JSON.`;
        valueDoc = 'The value; null removes the key.';
        signature = 'JsonNode? value';
        body = `Property(${key}, value)`;
        apiType = 'System.Text.Json.Nodes.JsonNode? value';
        break;
    }
    code += `\n${docComment(INDENT, summary)}${param('value', valueDoc)}${RETURNS}${INDENT}public ${self} ${p.method}(${signature}) => ${body};\n`;
    api.push(`${apiSelf}.${p.method}(${apiType}) -> ${apiSelf}!`);
  }
  return { code, api };
}

/** Facet channels split the chart into panels; the example binds the channels that draw it. */
const FACETS = new Set(['column', 'row']);

/**
 * The `<example>` of a builder (design 05 XML doc rules): anonymous rows with one numeric member per channel, bound to the
 * first two non-facet channels through the entry, so the snippet compiles for every template.
 */
function example(t: FluentTemplate): string {
  const methods = t.channels.filter((c) => !FACETS.has(c.channel)).slice(0, 2).map((c) => c.method);
  const row = (values: number[]) => `new { ${methods.map((m, i) => `${m} = ${values[i].toFixed(1)}`).join(', ')} }`;
  const chain = methods.map((m) => `.${m}(r =&gt; r.${m})`).join('');
  return (
    '/// <example>\n/// <code>\n' +
    `/// var rows = new[] { ${row([1, 3])}, ${row([2, 5])} };\n` +
    `/// string json = Chart.${t.entry}(rows)${chain}.ToJson();\n` +
    '/// </code>\n/// </example>\n'
  );
}

export function emitBuilders(ir: Ir, overrides: Overrides): EmitResult {
  const result = emptyResult();
  for (const t of fluentModel(ir, overrides)) {
    const self = `${t.builder}<TRow>`;
    const channels = channelMethods(t, self);
    const properties = propertyMethods(t, self, new Set(ir.catalog.hostStateKeys));
    const content =
      `${header(ir)}\nusing System.Linq.Expressions;\nusing System.Text.Json.Nodes;\n\nnamespace Graspea.Flint;\n\n` +
      docComment('', `Builds a \`${t.name}\` document; backends: ${t.backends.join(', ')}. Start with \`Chart.${t.entry}(rows)\`.`) +
      '/// <typeparam name="TRow">The row type.</typeparam>\n' +
      example(t) +
      `public sealed partial class ${self} : ChartBuilder<${self}, TRow>\n{\n` +
      `${INDENT}internal ${t.builder}(ChartRows<TRow> rows)\n${INDENT}    : base(ChartType.${t.constant}, rows.Data, rows.TypeInfo)\n${INDENT}{\n${INDENT}}\n` +
      channels.code +
      properties.code +
      '}\n';
    result.files.push({ path: `Builders/${t.builder}.g.cs`, content });
    result.apiLines.push(`Graspea.Flint.${self}`, ...channels.api, ...properties.api);
    result.ownedTypes.push(`Graspea.Flint.${self}`);
  }
  return result;
}
