// docs emitter (design §7.4, companion part 2 §5): Generated/catalog.json (the projection of IR `catalog`, packed as
// catalog/catalog.json), the marked sections of README.md, and docs/package/AGENTS.md, written whole from the README
// "For AI agents" section and the catalog tables. Text outside the markers is hand-written and never touched.
import { canonicalJson, ordinal, type Ir } from '../ir.ts';
import type { Overrides } from '../overrides.ts';
import { backendMember, emptyResult, type EmitResult } from './common.ts';
import { catalogConstantNames } from './constants.ts';
import { fluentModel } from './fluent.ts';

/** generator/mcp-pin.json: the flint-chart-mcp version the gate script resolved for the installed flint-chart. */
export interface McpPin {
  'flint-chart': string;
  'flint-chart-mcp': string;
  /** The upstream git ref of the pin for links: the tag when upstream tagged the version, else its gitHead commit. */
  upstreamRef?: string;
}

const REPOSITORY = 'https://github.com/graspea/flint-chart-dotnet';
const UPSTREAM = 'https://github.com/microsoft/flint-chart';
const HOSTED_MCP = 'https://flint.data-formulator.ai/mcp';
const AGENTS_HEADING = '## For AI agents';

export function emitDocs(ir: Ir): EmitResult {
  const result = emptyResult();
  result.files.push({ path: 'catalog.json', content: canonicalJson(ir.catalog) });
  return result;
}

const count = (n: number, noun: string) => `${n} ${noun}${n === 1 ? '' : 's'}`;
const code = (s: string) => `\`${s}\``;
/** A shields.io static badge path segment: `-` and `_` are doubled. */
const badgeText = (s: string) => s.replace(/-/g, '--').replace(/_/g, '__');

/** Section id -> body (ending with a newline) of every generated README section. */
export function readmeSections(ir: Ir, overrides: Overrides, mcpPin: McpPin): Record<string, string> {
  const version = ir.upstream.version;
  if (mcpPin['flint-chart'] !== version) {
    throw new Error(`generator/mcp-pin.json was resolved for flint-chart ${mcpPin['flint-chart']}, the IR is ${version}. Run npm run install-upstream -- ${version}.`);
  }
  const ref = mcpPin.upstreamRef ?? version;
  const upstreamLink = ref === version ? `${UPSTREAM}/releases/tag/${version}` : `${UPSTREAM}/tree/${ref}`;
  const catalog = ir.catalog;
  const names = catalogConstantNames(ir, overrides);
  const templates = fluentModel(ir, overrides);
  const constant = (struct: string, value: string) => `${struct}.${names.get(struct)!.get(value)!}`;
  const mcp = mcpPin['flint-chart-mcp'];

  const chartCatalog =
    `flint-chart ${version} declares ${count(templates.length, 'chart type')}. Backend ids map to ${code('FlintBackend')}: ` +
    `${catalog.backends.map((b) => `${code(b)} = ${code(backendMember(b))}`).join(', ')}. ` +
    'Channels and properties are wire names, the union over the backends of the template; ' +
    `${code('Validate(backend)')} reports one that the target backend does not declare (${code('unsupported_channel')}, ${code('unknown_chart_property')}).\n` +
    '\n' +
    '| Chart type | Constant and entry | Backends | Channels | Properties |\n' +
    '|---|---|---|---|---|\n' +
    templates
      .map(
        (t) =>
          `| ${t.name} | ${code(`ChartType.${t.constant}`)}, ${code(`Chart.${t.entry}`)} | ${t.backends.join(', ')} | ` +
          `${t.channels.map((c) => c.channel).join(', ') || '(none)'} | ${t.properties.map((p) => p.key).join(', ') || '(none)'} |\n`,
      )
      .join('');

  const groups = new Map<string, string[]>();
  for (const s of catalog.semanticTypes) {
    const group = `${s.t0} / ${s.t1}`;
    const member = names.get('SemanticType')!.get(s.name)!;
    groups.set(group, [...(groups.get(group) ?? []), member === s.name ? s.name : `${s.name} (${code(member)})`]);
  }
  const firstSemantic = catalog.semanticTypes.find((s) => s.name === 'YearMonth') ?? catalog.semanticTypes[0];
  const semanticTypes =
    `flint-chart ${version} registers ${count(catalog.semanticTypes.length, 'semantic type')}. Annotate a field with the ` +
    `${code('SemanticType')} constant of the same name (${code(constant('SemanticType', firstSemantic.name))}); a different constant name is given in brackets.\n` +
    '\n' +
    '| Group | Semantic types |\n' +
    '|---|---|\n' +
    [...groups]
      .sort(([a], [b]) => ordinal(a, b))
      .map(([group, members]) => `| ${group} | ${members.join(', ')} |\n`)
      .join('');

  const upstreamBadge =
    `[![flint-chart](https://img.shields.io/badge/flint--chart-${badgeText(version)}-blue)](${upstreamLink})\n` +
    `[![CI](${REPOSITORY}/actions/workflows/ci.yml/badge.svg)](${REPOSITORY}/actions/workflows/ci.yml)\n`;

  const upstreamVersion = `Flint.NET ${version}.x tracks flint-chart ${version}; the first three version parts always equal the pinned upstream release.\n`;

  const fallback =
    mcp === version ? '' : `flint-chart-mcp ${version} is not published; the snippet uses ${mcp}, the newest version below flint-chart ${version}.\n\n`;
  const mcpCommands =
    '```json\n{\n  "mcpServers": {\n    "flint-chart": {\n      "command": "npx",\n' +
    `      "args": ["-y", "flint-chart-mcp@${mcp}"]\n    }\n  }\n}\n` +
    '```\n' +
    '\n' +
    fallback +
    `The hosted endpoint is ${code(HOSTED_MCP)}. It runs the version upstream has deployed, which can differ from flint-chart ${version}.\n`;

  const first = templates.find((t) => t.name === 'Line Chart') ?? templates[0];
  const agentIdentifiers =
    `### Identifiers (flint-chart ${version})\n` +
    '\n' +
    `- Backends: ${catalog.backends.map((b) => code(backendMember(b))).join(', ')}.\n` +
    `- Chart types: ${count(templates.length, code('ChartType') + ' constant')}, each with a fluent entry ` +
    `(${code(`ChartType.${first.constant}`)} and ${code(`Chart.${first.entry}(rows)`)}); see the chart catalog.\n` +
    `- Channels: ${catalog.channels.map((c) => code(constant('Channel', c))).join(', ')}.\n` +
    `- Semantic types: ${count(catalog.semanticTypes.length, code('SemanticType') + ' constant')} (${code(constant('SemanticType', firstSemantic.name))}); see the semantic types.\n` +
    `- Theme presets: ${catalog.themePresets.map((p) => code(constant('ThemePreset', p.id))).join(', ')}.\n` +
    `- Upstream MCP server: ${code(`npx -y flint-chart-mcp@${mcp}`)}, tool ${code('validate_chart')} (vegalite, echarts and chartjs).\n` +
    `- Upstream skill for the JSON format: ${UPSTREAM}/blob/${ref}/agent-skills/flint-chart-author/SKILL.md\n` +
    `- Machine-readable catalog: ${code('catalog/catalog.json')} in the package, generated from the same data.\n`;

  return {
    'agent-identifiers': agentIdentifiers,
    'chart-catalog': chartCatalog,
    'mcp-commands': mcpCommands,
    'semantic-types': semanticTypes,
    'upstream-badge': upstreamBadge,
    'upstream-version': upstreamVersion,
  };
}

const MARKER = /^<!-- flint:generated:([a-z0-9-]+) (start|end) -->$/;

/**
 * Replaces the text between every `<!-- flint:generated:<id> start -->` and `<!-- flint:generated:<id> end -->` line pair
 * with `sections[id]`. Every id must have exactly one start and one end marker, in that order, and every marker must name a
 * known id.
 */
export function spliceSections(text: string, sections: Readonly<Record<string, string>>, file: string): string {
  const lines = text.split('\n');
  const found = new Map<string, { start: number[]; end: number[] }>(Object.keys(sections).map((id) => [id, { start: [], end: [] }]));
  lines.forEach((line, index) => {
    const match = MARKER.exec(line);
    if (!match) return;
    const [, id, kind] = match;
    const entry = found.get(id);
    if (!entry) throw new Error(`${file}: unknown generated section "${id}" (line ${index + 1}).`);
    entry[kind as 'start' | 'end'].push(index);
  });
  for (const [id, { start, end }] of found) {
    for (const [kind, at] of [['start', start], ['end', end]] as const) {
      if (at.length === 0) throw new Error(`${file}: generated section "${id}" has no ${kind} marker.`);
      if (at.length > 1) throw new Error(`${file}: generated section "${id}" has ${at.length} ${kind} markers.`);
    }
    if (end[0] < start[0]) throw new Error(`${file}: generated section "${id}" ends before it starts.`);
  }
  const ordered = [...found].sort(([, a], [, b]) => a.start[0] - b.start[0]);
  const out: string[] = [];
  let next = 0;
  for (const [id, { start, end }] of ordered) {
    if (start[0] < next) throw new Error(`${file}: generated section "${id}" overlaps another section.`);
    out.push(...lines.slice(next, start[0] + 1));
    const body = sections[id];
    if (body !== '') out.push(...body.replace(/\n$/, '').split('\n'));
    next = end[0];
  }
  out.push(...lines.slice(next));
  return out.join('\n');
}

/** The README "## For AI agents" section up to the next `## ` heading outside a code fence, without marker lines. */
function agentsSection(readme: string): string {
  const lines = readme.split('\n');
  const start = lines.indexOf(AGENTS_HEADING);
  if (start < 0) throw new Error(`README.md has no "${AGENTS_HEADING}" section.`);
  let fenced = false;
  let end = lines.length;
  for (let i = start + 1; i < lines.length; i++) {
    if (lines[i].startsWith('```')) fenced = !fenced;
    if (!fenced && lines[i].startsWith('## ')) {
      end = i;
      break;
    }
  }
  const body = lines.slice(start, end).filter((line) => !MARKER.test(line));
  while (body.length > 0 && body[body.length - 1] === '') body.pop();
  return body.join('\n') + '\n';
}

/** docs/package/AGENTS.md: a header, the README "For AI agents" section verbatim, then the catalog tables. */
export function packageAgents(readme: string, ir: Ir, overrides: Overrides, mcpPin: McpPin): string {
  const version = ir.upstream.version;
  const sections = readmeSections(ir, overrides, mcpPin);
  return (
    '# Graspea.Flint for AI agents\n' +
    '\n' +
    `> Generated from the "For AI agents" section of README.md and the flint-chart ${version} catalog; do not edit this file. ` +
    `Source: ${REPOSITORY}\n` +
    '\n' +
    agentsSection(readme) +
    '\n' +
    `## Chart catalog (flint-chart ${version})\n\n` +
    sections['chart-catalog'] +
    '\n' +
    `## Semantic types (flint-chart ${version})\n\n` +
    sections['semantic-types']
  );
}
