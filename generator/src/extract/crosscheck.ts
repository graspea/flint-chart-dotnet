// One-off, not in CI (design §7.9): compares the mapped fixtures with the shared/test-data/*/input.json files of an
// upstream checkout at the pinned tag, matched by the generator group and the title in each meta.json.
//
//   curl -sL https://codeload.github.com/microsoft/flint-chart/tar.gz/refs/tags/0.5.1 | tar xz -C <dir>
//   npm run crosscheck -- <dir>/flint-chart-0.5.1
//
// Prints a markdown summary for docs/fixtures.md.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';

type Json = Record<string, any>;

const annotationType = (v: unknown) => (typeof v === 'string' ? { semanticType: v } : v);
const normaliseSemanticTypes = (s: Json | undefined) =>
  Object.fromEntries(Object.entries(s ?? {}).map(([k, v]) => [k, annotationType(v)]));
const project = (encodings: Json, pick: (e: Json) => unknown) =>
  Object.fromEntries(
    Object.entries(encodings ?? {}).map(([ch, e]) => [ch, Array.isArray(e) ? e.map((x) => pick(typeof x === 'string' ? { field: x } : x)) : pick(typeof e === 'string' ? { field: e } : e)]),
  );
const shape = (values: Json[] | undefined) => ({ rows: values?.length ?? 0, keys: Object.keys(values?.[0] ?? {}) });

/** The differences between a mapped fixture and upstream's input, as stable labels (empty when equivalent). */
export function compareInputs(ours: Json, theirs: Json): string[] {
  const diffs: string[] = [];
  const same = (a: unknown, b: unknown) => isDeepStrictEqual(a, b);
  if (ours.chart_spec?.chartType !== theirs.chart_spec?.chartType) diffs.push('chartType');
  if (!same(shape(ours.data?.values), shape(theirs.data?.values))) diffs.push('data shape');
  if (!same(ours.data?.values, theirs.data?.values)) diffs.push('data values');
  if (!same(project(ours.chart_spec?.encodings, (e) => e.field), project(theirs.chart_spec?.encodings, (e) => e.field))) diffs.push('encoding fields');
  const rest = (e: Json) => {
    const { field: _field, ...others } = e;
    return others;
  };
  if (!same(project(ours.chart_spec?.encodings, rest), project(theirs.chart_spec?.encodings, rest))) diffs.push('encoding types');
  if (!same(normaliseSemanticTypes(ours.semantic_types), normaliseSemanticTypes(theirs.semantic_types))) diffs.push('semantic_types');
  for (const [label, get] of [
    ['chart_spec.chartProperties', (i: Json) => i.chart_spec?.chartProperties],
    ['chart_spec.baseSize', (i: Json) => i.chart_spec?.baseSize],
    ['chart_spec.canvasSize', (i: Json) => i.chart_spec?.canvasSize],
    ['options', (i: Json) => i.options],
  ] as const) {
    const a = get(ours);
    const b = get(theirs);
    if (a === undefined && b !== undefined) diffs.push(`${label} only upstream`);
    else if (a !== undefined && b === undefined) diffs.push(`${label} only here`);
    else if (!same(a, b)) diffs.push(label);
  }
  return diffs;
}

/** Counts per difference label; `identical` counts the inputs with none. */
export function summarise(results: string[][]): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const diffs of results) for (const d of diffs.length === 0 ? ['identical'] : diffs) counts[d] = (counts[d] ?? 0) + 1;
  return Object.fromEntries(Object.entries(counts).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
}

/** Upstream inputs keyed by directory, with the generator group and title from meta.json. */
function upstreamInputs(checkout: string): Map<string, { generator?: string; title: string; input: Json }> {
  const root = `${checkout}/shared/test-data`;
  const inputs = new Map<string, { generator?: string; title: string; input: Json }>();
  for (const dir of readdirSync(root).sort()) {
    const inputFile = `${root}/${dir}/input.json`;
    if (!existsSync(inputFile)) continue;
    const upstream = JSON.parse(readFileSync(inputFile, 'utf8'));
    const metaFile = `${root}/${dir}/meta.json`;
    const meta = existsSync(metaFile) ? JSON.parse(readFileSync(metaFile, 'utf8')) : {};
    inputs.set(dir, { generator: meta.generator, title: upstream.title, input: upstream.input });
  }
  return inputs;
}

function main(checkout: string, casesFile: string): void {
  const cases = new Map<string, Json>();
  for (const line of readFileSync(casesFile, 'utf8').trimEnd().split('\n')) {
    const c = JSON.parse(line);
    cases.set(`${c.id.slice(0, c.id.lastIndexOf('|#'))}|${c.title}`, c);
  }
  const inputs = upstreamInputs(checkout);
  const results: string[][] = [];
  const unmatched: string[] = [];
  for (const [dir, u] of inputs) {
    const ours = u.generator ? cases.get(`${u.generator}|${u.title}`) : undefined;
    if (ours) results.push(compareInputs(ours.input, u.input));
    else unmatched.push(dir);
  }
  const staticSeries = unmatched.filter((d) => Object.values(inputs.get(d)!.input.chart_spec.encodings ?? {}).some(Array.isArray));

  const out = [
    `Upstream input.json files: ${inputs.size}; matched to a mapped fixture by generator group and title: ${results.length}; unmatched: ${unmatched.length} (${staticSeries.length} of them use a static series).`,
    `Mapped fixtures: ${cases.size}.`,
    '',
    '| Difference | Matched inputs |',
    '|---|---|',
    ...Object.entries(summarise(results)).map(([label, count]) => `| ${label} | ${count} |`),
  ];
  console.log(out.join('\n'));
}

if (process.argv[1]?.endsWith('crosscheck.ts')) {
  const [checkout, casesFile = new URL('../../../tests/fixtures/flint-chart@0.5.1/cases.jsonl', import.meta.url).pathname] = process.argv.slice(2);
  if (!checkout) throw new Error('usage: npm run crosscheck -- <flint-chart checkout at the pinned tag> [cases.jsonl]');
  main(checkout, casesFile);
}
