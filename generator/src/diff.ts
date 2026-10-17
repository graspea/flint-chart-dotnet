// ir-diff (design §7.2): two IR files -> added / removed / changed rows with IR paths; feeds the sync PR body and
// the retirement ledger (design §7.8).
import { ordinal } from './ir.ts';

export interface DiffRow {
  change: 'added' | 'removed' | 'changed';
  path: string;
  before?: string;
  after?: string;
}

export interface RetiredEntry {
  path: string;
  lastSeenIn: string;
  retiredIn: string;
}

interface Node {
  value: string;
  parent: string | undefined;
}

/** Array elements are identified by name, key or id (or option value), so reordering is not a change. */
function elementId(element: unknown, index: number): string {
  if (element !== null && typeof element === 'object' && !Array.isArray(element)) {
    const o = element as Record<string, unknown>;
    for (const k of ['name', 'key', 'id']) if (typeof o[k] === 'string') return o[k] as string;
    if ('value' in o) return `=${JSON.stringify(o.value)}`;
    if (o.omitted === true) return 'omitted';
    return String(index);
  }
  return typeof element === 'string' ? element : String(index);
}

function display(value: unknown): string {
  if (Array.isArray(value)) return '[…]';
  if (value !== null && typeof value === 'object') return '{…}';
  return JSON.stringify(value);
}

function flatten(value: unknown, path: string, parent: string | undefined, out: Map<string, Node>): void {
  out.set(path, { value: display(value), parent });
  if (Array.isArray(value)) {
    const seen = new Map<string, number>();
    value.forEach((el, i) => {
      let id = elementId(el, i);
      const n = seen.get(id) ?? 0;
      seen.set(id, n + 1);
      if (n > 0) id = `${id}#${n}`;
      flatten(el, `${path}[${id}]`, path, out);
    });
  } else if (value !== null && typeof value === 'object') {
    for (const key of Object.keys(value).sort(ordinal)) {
      flatten((value as Record<string, unknown>)[key], path === '' ? key : `${path}.${key}`, path, out);
    }
  }
}

/** Every node of an IR document by IR path, with its display value (the form the diff rows use). */
export function flattenIr(value: unknown): Map<string, string> {
  const nodes = new Map<string, Node>();
  flatten(value, '', undefined, nodes);
  return new Map([...nodes].map(([path, node]) => [path, node.value]));
}

export function diffIr(before: unknown, after: unknown): DiffRow[] {
  const a = new Map<string, Node>();
  const b = new Map<string, Node>();
  flatten(before, '', undefined, a);
  flatten(after, '', undefined, b);
  const rows: DiffRow[] = [];
  for (const [path, node] of a) {
    const other = b.get(path);
    if (!other) {
      if (node.parent === undefined || b.has(node.parent)) rows.push({ change: 'removed', path, before: node.value });
    } else if (other.value !== node.value && !(node.value === '{…}' && other.value === '{…}') && !(node.value === '[…]' && other.value === '[…]')) {
      rows.push({ change: 'changed', path, before: node.value, after: other.value });
    }
  }
  for (const [path, node] of b) {
    if (!a.has(path) && (node.parent === undefined || a.has(node.parent))) rows.push({ change: 'added', path, after: node.value });
  }
  return rows.sort((x, y) => ordinal(x.path, y.path));
}

export function diffMarkdown(rows: DiffRow[]): string {
  if (rows.length === 0) return 'No IR changes.\n';
  const cell = (v?: string) => (v === undefined ? '' : `\`${v.replace(/\|/g, '\\|')}\``);
  return (
    '| Change | IR path | Before | After |\n|---|---|---|---|\n' +
    rows.map((r) => `| ${r.change} | ${cell(r.path)} | ${cell(r.before)} | ${cell(r.after)} |\n`).join('')
  );
}

const RETIRABLE = [
  /^catalog\.chartTypes\[[^\]]+\](\.backends\.[a-z]+(\.channels\[[^\]]+\]|\.properties\[[^\]]+\](\.options\[[^\]]+\])?)?)?$/,
  /^catalog\.(channels|semanticTypes|themePresets)\[[^\]]+\]$/,
];

/** Removed catalog members, keyed by their IR path (which carries template and backend). */
export function retiredEntries(rows: DiffRow[], lastSeenIn: string, retiredIn: string): RetiredEntry[] {
  return rows
    .filter((r) => r.change === 'removed' && RETIRABLE.some((re) => re.test(r.path)))
    .map((r) => ({ path: r.path, lastSeenIn, retiredIn }));
}
