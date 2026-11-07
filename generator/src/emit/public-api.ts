// public-api emitter (design §7.4): PublicAPI.Unshipped.txt = hand-written lines + generated lines not yet shipped +
// a *REMOVED* line for each shipped generated line a fresh run no longer emits.
import { ordinal } from '../ir.ts';
import type { EmitResult } from './common.ts';

const NULLABLE = '#nullable enable';
const REMOVED = '*REMOVED*';
const MODIFIERS = new Set(['static', 'readonly', 'const', 'override', 'abstract', 'virtual', 'sealed']);

const lines = (text: string) => text.split('\n').map((l) => l.trim()).filter((l) => l.length > 0 && l !== NULLABLE);

/** True when the line belongs to a generated type or is a generated constant of a hand-written struct. */
function isOwned(line: string, result: EmitResult): boolean {
  if (line.startsWith(REMOVED)) line = line.slice(REMOVED.length);
  if (result.ownedConstantStructs.some((s) => line.startsWith(`static readonly ${s}.`))) return true;
  const words = line.replace(/^~/, '').split(' ');
  while (words.length > 1 && MODIFIERS.has(words[0])) words.shift();
  const symbol = words.join(' ');
  if (result.ownedMembers.some((m) => symbol.startsWith(`${m}(`) || symbol.startsWith(`${m}<`))) return true;
  return result.ownedTypes.some((t) => symbol === t || symbol.startsWith(`${t}.`));
}

/**
 * The public types a generated file owns outright: its `public enum`, `public sealed [partial] class` and
 * `public readonly partial record struct` declarations, with
 * their namespace. Partial members of hand-written types (`static partial class Chart`, the constant structs) are not
 * included. The CLI adds the types of the generated files it removes to `ownedTypes`, so their lines leave
 * PublicAPI.Unshipped.txt.
 */
export function declaredPublicTypes(content: string): string[] {
  const namespace = /^namespace ([\w.]+);/m.exec(content)?.[1];
  if (namespace === undefined) return [];
  return [...content.matchAll(/^public (?:enum|sealed (?:partial )?class|readonly partial record struct) (\w+(?:<[^>]+>)?)/gm)].map((m) => `${namespace}.${m[1]}`);
}

export function unshippedText(shipped: string, unshipped: string, result: EmitResult): string {
  const shippedSet = new Set(lines(shipped));
  const generatedSet = new Set(result.apiLines);
  const kept = lines(unshipped).filter((l) => !isOwned(l, result));
  const added = result.apiLines.filter((l) => !shippedSet.has(l));
  const removed = [...shippedSet].filter((l) => isOwned(l, result) && !generatedSet.has(l)).map((l) => `${REMOVED}${l}`);
  const all = [...new Set([...kept, ...added, ...removed])].sort(ordinal);
  return `${NULLABLE}\n${all.map((l) => `${l}\n`).join('')}`;
}
