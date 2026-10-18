// enums emitter (design §4.2, §7.4): one public enum per discrete template property, over the union of its option values
// across backends, with `Default` (= omit the key) first, and an internal value table that turns a member into the JSON
// value. Values a backend does not offer are documented; Validate(backend) reports them as invalid_option_value.
// Member numbers are explicit and stable (design §8.2): a member shipped in PublicAPI.Shipped.txt keeps its number, and a
// new member takes the next number above every number the enum ever shipped, wherever upstream inserts its value.
import type { Ir } from '../ir.ts';
import type { Overrides } from '../overrides.ts';
import { csNumber, csString, docComment, emptyResult, header, type EmitResult } from './common.ts';
import { fluentModel, type FluentEnum } from './fluent.ts';

type WireValue = string | number | boolean;

function jsonExpression(value: WireValue): string {
  if (typeof value === 'string') return `JsonValue.Create(${csString(value)})`;
  if (typeof value === 'number') return `JsonValue.Create(${csNumber(value)})`;
  return `JsonValue.Create(${value})`;
}

/**
 * The JSON value a member writes; undefined for `Default` (omit the key). flint-chart accepts an array option only by
 * reference (`o.value === value`), so a parsed array never matches; the label is written instead, which upstream
 * coerces to the option (normalize-properties.ts, `coerced-option-label`).
 */
export function wireValue(m: FluentEnum['members'][number]): WireValue | undefined {
  return Array.isArray(m.value) ? m.options[0].label : m.value;
}

function wireExpression(m: FluentEnum['members'][number]): string {
  const value = wireValue(m);
  return value === undefined ? 'null' : jsonExpression(value);
}

function memberDoc(e: FluentEnum, m: FluentEnum['members'][number]): string {
  if (m.value === undefined) {
    const labels = [...new Set(m.options.map((o) => `"${o.label}"`))];
    return `Omits the key, so the backend applies its default${labels.length > 0 ? ` (${labels.join(', ')})` : ''}.`;
  }
  const missing = e.backends.filter((b) => !m.backends.includes(b));
  const note = missing.length > 0 ? ` Not offered by ${missing.join(', ')} (\`invalid_option_value\`).` : '';
  const label = m.options[0].label;
  const written = Array.isArray(m.value)
    ? `Writes the label \`${JSON.stringify(label)}\`, which flint-chart maps to \`${JSON.stringify(m.value)}\`.`
    : `\`${JSON.stringify(m.value)}\` ("${label}").`;
  return `${written}${note}`;
}

const SHIPPED_MEMBER = /^Graspea\.Flint\.(\w+)\.(\w+) = (-?\d+) -> Graspea\.Flint\.\1$/;

/** Enum name -> member name -> number, from the `Graspea.Flint.E.M = N -> Graspea.Flint.E` lines of PublicAPI.Shipped.txt. */
export function shippedEnumNumbers(shipped: string): Map<string, Map<string, number>> {
  const result = new Map<string, Map<string, number>>();
  for (const line of shipped.split('\n')) {
    const match = SHIPPED_MEMBER.exec(line.trim());
    if (!match) continue;
    const [, name, member, value] = match;
    const members = result.get(name) ?? new Map<string, number>();
    const number = Number(value);
    const other = [...members].find(([, n]) => n === number);
    if (other) throw new Error(`PublicAPI.Shipped.txt: ${name}.${other[0]} and ${name}.${member} are both shipped as ${number}.`);
    members.set(member, number);
    result.set(name, members);
  }
  return result;
}

/** Member name -> number: shipped numbers kept, Default = 0, every other member max + 1 in union order. */
function memberNumbers(e: FluentEnum, shipped: ReadonlyMap<string, number>): Map<string, number> {
  const shippedDefault = shipped.get('Default');
  if (shippedDefault !== undefined && shippedDefault !== 0) {
    throw new Error(`PublicAPI.Shipped.txt: ${e.name}.Default is shipped as ${shippedDefault}; Default (omit the key) must stay 0.`);
  }
  const numbers = new Map<string, number>([['Default', 0]]);
  let next = Math.max(0, ...shipped.values()) + 1;
  for (const m of e.members) {
    if (numbers.has(m.name)) continue;
    numbers.set(m.name, shipped.get(m.name) ?? next++);
  }
  return numbers;
}

export function emitEnums(ir: Ir, overrides: Overrides, shippedApi = ''): EmitResult {
  const result = emptyResult();
  const shipped = shippedEnumNumbers(shippedApi);
  const enums = fluentModel(ir, overrides).flatMap((t) => t.properties.flatMap((p) => (p.enum ? [p.enum] : [])));
  for (const e of enums) {
    const numbers = memberNumbers(e, shipped.get(e.name) ?? new Map());
    const members = e.members.map((m) => `${docComment('    ', memberDoc(e, m))}    ${m.name} = ${numbers.get(m.name)},\n`).join('\n');
    const arms = e.members.map((m) => `        ${e.name}.${m.name} => ${wireExpression(m)},\n`).join('');
    const content =
      `${header(ir)}\nusing System.Text.Json.Nodes;\n\nnamespace Graspea.Flint;\n\n` +
      docComment('', `The values of the \`${e.key}\` property ("${e.label}") of the \`${e.chartType}\` template.`) +
      `public enum ${e.name}\n{\n${members}}\n\n` +
      `internal static class ${e.name}Values\n{\n` +
      `    internal static JsonNode? ToJson(${e.name} value) => value switch\n    {\n${arms}` +
      '        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),\n    };\n}\n';
    result.files.push({ path: `Enums/${e.name}.g.cs`, content });
    result.apiLines.push(`Graspea.Flint.${e.name}`, ...e.members.map((m) => `Graspea.Flint.${e.name}.${m.name} = ${numbers.get(m.name)} -> Graspea.Flint.${e.name}`));
    result.ownedTypes.push(`Graspea.Flint.${e.name}`);
  }
  return result;
}
