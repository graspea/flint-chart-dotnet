// The change report of upstream-sync.yml, job `generate` (companion part 1, End-to-end flow step 2.4, "PR body
// template", "Suggested SDK tag", Labels). Run from the repository root after `npm run generate`:
//
//   node --import tsx generator/scripts/upstream-report.mjs --old-ir <file> --old-retired <file> --out <dir>
//
// Inputs from the environment: CONTEXT (the `context` JSON output of job detect), GENERATION (ok or failed),
// FORWARD, TESTS_DOTNET, TESTS_GENERATOR (step outcomes), FLINT_M2_DONE (repository variable, optional).
// Reads <out>/generate.log and <out>/forward/forward.trx when present. Writes <out>/pr-body.md and <out>/result.json.
// Upstream text (CHANGELOG, generation log) goes into the file quoted, never through a shell expression.
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { diffIr, flattenIr, retiredEntries } from '../src/diff.ts';
import { changelogSection } from './release-check.mjs';
import { fetchTextOrUndefined, run, runMain } from './gha.mjs';
import { bumpKind, isUpstreamVersion } from './versions.mjs';

const UPSTREAM = 'https://github.com/microsoft/flint-chart';
const RAW = 'https://raw.githubusercontent.com/microsoft/flint-chart';
const SKILL_PATH = 'agent-skills/flint-chart-author/SKILL.md';
const SEVERITIES = /** @type {const} */ (['BREAKING', 'behavioural', 'additive', 'cosmetic']);
const AREAS = ['Chart types', 'Channels', 'Properties', 'Semantic types', 'Theme presets', 'Input types', 'Other catalog'];

/**
 * @typedef {'BREAKING' | 'behavioural' | 'additive' | 'cosmetic'} Severity
 * @typedef {{ change: 'added' | 'removed' | 'changed', path: string, before?: string, after?: string }} Row
 * @typedef {Row & { severity: Severity }} ClassifiedRow
 */

/** @param {Row} row @param {Map<string, string>} after the flattened new IR @returns {Severity | null} */
function severityOf(row, after) {
  const { change, path } = row;
  if (path === 'upstream' || path.startsWith('upstream.')) return null;
  if (/\.(doc|label|description|category|t0|t1)$/.test(path)) return 'cosmetic';
  if (path.startsWith('types.')) {
    if (change === 'removed') return 'BREAKING';
    if (change === 'added') return /\.props\[[^\]]+\]$/.test(path) && after.get(`${path}.optional`) === 'false' ? 'BREAKING' : 'additive';
    if (path.endsWith('.optional')) return row.after === 'false' ? 'BREAKING' : 'additive';
    return 'BREAKING';
  }
  if (change === 'added') return 'additive';
  if (change === 'removed') {
    const retirable = retiredEntries([row], '', '').length > 0 || /^catalog\.backends\[[^\]]+\]$/.test(path);
    return retirable ? 'BREAKING' : 'behavioural';
  }
  return /\.properties\[[^\]]+\]\.type$/.test(path) ? 'BREAKING' : 'behavioural';
}

/** IR diff rows with their computed severity; the upstream version and integrity rows are left out. @param {unknown} before @param {unknown} after */
export function classifyIrDiff(before, after) {
  const flat = flattenIr(after);
  /** @type {ClassifiedRow[]} */
  const rows = [];
  for (const row of diffIr(before, after)) {
    const severity = severityOf(row, flat);
    if (severity) rows.push({ ...row, severity });
  }
  return rows;
}

/** @param {string} path */
function areaOf(path) {
  if (path.startsWith('types.')) return 'Input types';
  if (/^catalog\.chartTypes\[[^\]]+\](\.backends\.[a-z]+)?$/.test(path)) return 'Chart types';
  if (path.includes('.channels[') || path.startsWith('catalog.channels[')) return 'Channels';
  if (path.includes('.properties[')) return 'Properties';
  if (path.startsWith('catalog.semanticTypes')) return 'Semantic types';
  if (path.startsWith('catalog.themePresets')) return 'Theme presets';
  return 'Other catalog';
}

/**
 * The suggested tag under design part 3 §8.1 (suggestion only).
 * @param {{ prefix: string, version: string, stableExists: boolean, breaking: boolean, m2Done: boolean, cleanSyncStreak: number }} input
 */
export function suggestTag({ prefix, version, stableExists, breaking, m2Done, cleanSyncStreak }) {
  const stable = stableExists ? !breaking : m2Done && cleanSyncStreak >= 2;
  return `${prefix}${version}.1${stable ? '' : '-preview.1'}`;
}

/** @param {{ breaking: boolean, tests: string, generation: string, mcpFallback: boolean, bump: string }} input */
export function labelsFor({ breaking, tests, generation, mcpFallback, bump }) {
  const labels = ['upstream'];
  if (breaking) labels.push('breaking');
  if (tests !== 'success' || generation !== 'ok' || mcpFallback) labels.push('needs-attention');
  labels.push(`upstream-${bump}`);
  return labels;
}

/** @param {string} trx */
export function trxCounts(trx) {
  const m = /<Counters\b[^>]*>/.exec(trx);
  if (!m) return null;
  const attr = (/** @type {string} */ name) => Number(new RegExp(`\\b${name}="(\\d+)"`).exec(m[0])?.[1] ?? 0);
  return { total: attr('total'), passed: attr('passed'), failed: attr('failed') };
}

/** @param {string} content */
export function gitBlobSha(content) {
  const bytes = Buffer.from(content, 'utf8');
  return createHash('sha1').update(Buffer.concat([Buffer.from(`blob ${bytes.length}\0`), bytes])).digest('hex');
}

/** @param {string} text @param {string} info */
function fenced(text, info) {
  const longest = Math.max(0, ...(text.match(/`+/g) ?? []).map((run) => run.length));
  const fence = '`'.repeat(Math.max(3, longest + 1));
  return `${fence}${info}\n${text.replace(/\n+$/, '')}\n${fence}\n`;
}

const MAX_CELL = 100;

/** The pull request body keeps the tail of the generation log: create-pull-request cuts a body above 65536 characters. */
const MAX_LOG_LINES = 200;
const MAX_LOG_CHARS = 20_000;

/** @param {string} log @param {string} version */
function logTail(log, version) {
  const lines = log.replace(/\n+$/, '').split('\n');
  let tail = lines.slice(-MAX_LOG_LINES).join('\n');
  if (tail.length > MAX_LOG_CHARS) tail = tail.slice(-MAX_LOG_CHARS);
  return tail.length < lines.join('\n').length
    ? `[truncated; full log in artifact upstream-${version}/generate.log]\n${tail}`
    : tail;
}

/** A GFM table cell holding a code span: shortened, pipes escaped, a longer delimiter when the value has backticks. @param {string | undefined} v */
function cell(v) {
  if (v === undefined) return '';
  const text = (v.length > MAX_CELL ? `${v.slice(0, MAX_CELL)}…` : v).replace(/\|/g, '\\|');
  return text.includes('`') ? `\`\` ${text} \`\`` : `\`${text}\``;
}
const outcome = (/** @type {string} */ o) => (o === 'success' ? 'PASS' : o === 'failure' ? 'FAIL' : o || 'not run');
const short = (/** @type {string} */ sha) => sha.slice(0, 8);

/**
 * @typedef {{ pin: string, version: string, published: string | null, gitHeadOld: string | null, gitHeadNew: string | null,
 *   upstreamRef: string, mcp: { version: string }, releaseNotes: boolean, skill: { old: string, new: string } | null,
 *   generation: string, generateLog: string, forward: { outcome: string, counts: { total: number, passed: number, failed: number } | null },
 *   tests: { dotnet: string, generator: string }, queued: string[], suggestedTag: string, stableTagForPin: string | null,
 *   rows: ClassifiedRow[], retired: { path: string, lastSeenIn: string, retiredIn: string }[], stats: string, changelog: string | undefined }} ReportModel
 * @param {ReportModel} m
 */
export function renderReport(m) {
  const at = m.gitHeadNew ?? m.version;
  const published = m.published ? `${m.published.slice(0, 10)} ${m.published.slice(11, 16)} UTC` : 'unknown';
  const compare = m.gitHeadOld && m.gitHeadNew ? `${UPSTREAM}/compare/${m.gitHeadOld}...${m.gitHeadNew}` : 'n/a (gitHead missing)';
  const mcp = m.mcp.version === m.version ? `${m.version} published; \`mcp-commands\` pins ${m.version}` : `missing; \`mcp-commands\` pins ${m.mcp.version} (needs-attention)`;
  const skillLink = `${UPSTREAM}/blob/${m.upstreamRef}/${SKILL_PATH}`;
  const skill = !m.skill
    ? 'not found upstream'
    : m.skill.old === m.skill.new
      ? `unchanged (${short(m.skill.new)}), ${skillLink}`
      : `changed upstream (${short(m.skill.old)} -> ${short(m.skill.new)}), ${skillLink}`;
  const forward = m.forward.counts
    ? `${m.forward.counts.passed} / ${m.forward.counts.total}`
    : `${m.forward.outcome === 'success' ? 'passed' : 'failed'} (no counts)`;

  const counts = SEVERITIES.map((s) => `${s} ${m.rows.filter((r) => r.severity === s).length}`).join(' · ');
  const areas = AREAS.map((area) => {
    const rows = m.rows.filter((r) => areaOf(r.path) === area);
    if (rows.length === 0) return '';
    return (
      `### ${area}\n| Change | IR path | Before | After | Severity |\n|---|---|---|---|---|\n` +
      rows.map((r) => `| ${r.change} | ${cell(r.path)} | ${cell(r.before)} | ${cell(r.after)} | ${r.severity} |\n`).join('') +
      '\n'
    );
  }).join('');
  const retired =
    m.retired.length === 0
      ? ''
      : '### Retired (retired.json additions)\n| Entry | Vanished in | Last seen in |\n|---|---|---|\n' +
        m.retired.map((e) => `| ${cell(e.path)} | ${e.retiredIn} | ${e.lastSeenIn} |\n`).join('') +
        '\n';

  return (
    `## flint-chart ${m.pin} -> ${m.version}\n\n` +
    '| | |\n|---|---|\n' +
    `| Published | ${published} |\n` +
    `| Upstream compare | ${compare} |\n` +
    '| Provenance | verified by the gate: `npm audit signatures`, then repository microsoft/flint-chart, workflow `.github/workflows/npm-publish.yml`, gitCommit = gitHead |\n' +
    `| flint-chart-mcp | ${mcp} |\n` +
    `| Release notes | ${m.releaseNotes ? `${UPSTREAM}/blob/${at}/.github/release-notes/${m.version}.md` : 'none'} |\n` +
    `| Agent skill | ${skill} |\n` +
    `| Generation | ${m.generation === 'ok' ? 'OK' : 'failed, see the log below'} |\n` +
    `| Forward-compat round-trip (old model, all stored fixture sets) | ${forward} |\n` +
    `| Tests | dotnet test: ${outcome(m.tests.dotnet)}, generator node:test: ${outcome(m.tests.generator)} |\n` +
    `| Queued after this | ${m.queued.length ? m.queued.join(', ') : 'none'} |\n` +
    `| Suggested SDK tag | \`${m.suggestedTag}\` (suggestion only; the maintainer may force preview or stable at tag time; rule: design part 3 §8.1) |\n` +
    (m.stableTagForPin ? `| Stable tag for the current line | \`${m.stableTagForPin}\` (the restarted ${m.pin} line had no generator fix since its sync) |\n` : '') +
    '\n' +
    `### Severity summary\n${counts}\n\n` +
    (m.rows.length === 0 ? 'No IR changes besides the version.\n\n' : areas) +
    retired +
    `### Fixtures and generated files\n${m.stats || 'no changes'}\n\n` +
    `### Upstream CHANGELOG ${m.version}\n${m.changelog === undefined ? 'Not found at the new gitHead.\n' : fenced(m.changelog, 'markdown')}\n` +
    '### After merge\n' +
    `1. Open the release pull request \`release: ${m.suggestedTag}\` (design part 3 §9.6 step 2: Shipped moved, baseline set, CHANGELOG entry, NuGet badge kind, install line) and merge it.\n` +
    `2. \`git tag ${m.suggestedTag} <release PR merge sha> && git push origin ${m.suggestedTag}\`\n\n` +
    'Review checklist: design part 3 §9.5. Never edit this pull request by hand: generator fixes go to `main` in their own pull request, then dispatch the workflow again.\n' +
    (m.generation === 'ok' ? '' : `\n### Generation log\n${fenced(logTail(m.generateLog, m.version), 'text')}`)
  );
}

/** @param {string} path */
const readIfExists = (path) => (existsSync(path) ? readFileSync(path, 'utf8') : '');

async function main() {
  const args = process.argv.slice(2);
  const arg = (/** @type {string} */ name) => {
    const i = args.indexOf(name);
    if (i < 0 || !args[i + 1]) throw new Error(`missing ${name}`);
    return args[i + 1];
  };
  const out = arg('--out');
  const context = JSON.parse(process.env.CONTEXT ?? '{}');
  const { pin, version } = context;
  if (!isUpstreamVersion(pin) || !isUpstreamVersion(version)) throw new Error('CONTEXT must carry the validated pin and version.');

  const root = new URL('../../', import.meta.url);
  const read = (/** @type {string} */ path) => readFileSync(new URL(path, root), 'utf8');
  const oldIr = JSON.parse(readFileSync(arg('--old-ir'), 'utf8'));
  const newIr = JSON.parse(read('generator/ir/flint-ir.json'));
  const rows = classifyIrDiff(oldIr, newIr);
  const known = new Set(JSON.parse(readFileSync(arg('--old-retired'), 'utf8')).map((/** @type {{ path: string }} */ e) => e.path));
  const retired = JSON.parse(read('generator/retired.json')).filter((/** @type {{ path: string }} */ e) => !known.has(e.path));
  const mcpPin = JSON.parse(read('generator/mcp-pin.json'));

  const generation = process.env.GENERATION === 'ok' ? 'ok' : 'failed';
  const tests = { dotnet: process.env.TESTS_DOTNET ?? '', generator: process.env.TESTS_GENERATOR ?? '' };
  const testsOutcome = tests.dotnet === 'success' && tests.generator === 'success' ? 'success' : 'failure';
  const trxDir = join(out, 'forward');
  const trx = existsSync(trxDir) ? readdirSync(trxDir).filter((f) => f.endsWith('.trx')) : [];
  const forward = { outcome: process.env.FORWARD ?? '', counts: trx.length ? trxCounts(readFileSync(join(trxDir, trx[0]), 'utf8')) : null };

  const fetchAt = async (/** @type {string | null} */ sha, /** @type {string} */ path) => (sha ? fetchTextOrUndefined(`${RAW}/${sha}/${path}`).catch(() => undefined) : undefined);
  const [oldSkill, newSkill, changelogText, releaseNotes] = await Promise.all([
    fetchAt(context.gitHeadOld, SKILL_PATH),
    fetchAt(context.gitHeadNew, SKILL_PATH),
    fetchAt(context.gitHeadNew, 'CHANGELOG.md'),
    fetchAt(context.gitHeadNew, `.github/release-notes/${version}.md`),
  ]);
  let changelog;
  try {
    changelog = changelogText === undefined ? undefined : changelogSection(changelogText, version);
  } catch {
    changelog = undefined;
  }

  const breaking = rows.some((r) => r.severity === 'BREAKING');
  const m2Done = process.env.FLINT_M2_DONE === 'true';
  const suggestedTag = suggestTag({
    prefix: context.tagPrefix,
    version,
    stableExists: context.stableExists === true,
    breaking,
    m2Done,
    cleanSyncStreak: Number(context.cleanSyncStreak) || 0,
  });
  const stableTagForPin = context.restartedLineClean === true ? `${context.tagPrefix}${pin}.1` : null;
  const stats = ['tests/fixtures', 'src/Graspea.Flint', 'generator', 'README.md', 'docs/package/AGENTS.md']
    .map((path) => {
      const line = run('git', ['diff', '--shortstat', '--', path], { cwd: root }).trim();
      return line ? `${path}: ${line}` : '';
    })
    .filter(Boolean)
    .join(' · ');

  /** @type {ReportModel} */
  const model = {
    pin,
    version,
    published: context.published,
    gitHeadOld: context.gitHeadOld,
    gitHeadNew: context.gitHeadNew,
    upstreamRef: mcpPin.upstreamRef ?? version,
    mcp: { version: mcpPin['flint-chart-mcp'] },
    releaseNotes: releaseNotes !== undefined,
    skill: oldSkill !== undefined && newSkill !== undefined ? { old: gitBlobSha(oldSkill), new: gitBlobSha(newSkill) } : null,
    generation,
    generateLog: readIfExists(join(out, 'generate.log')),
    forward,
    tests,
    queued: context.queued ?? [],
    suggestedTag,
    stableTagForPin,
    rows,
    retired,
    stats,
    changelog,
  };
  const labels = labelsFor({ breaking, tests: testsOutcome, generation, mcpFallback: model.mcp.version !== version, bump: bumpKind(pin, version) });
  writeFileSync(join(out, 'pr-body.md'), renderReport(model));
  const result = {
    tests: testsOutcome,
    generation,
    forward,
    mcp: { version: model.mcp.version, equalsPin: model.mcp.version === version },
    labels,
    queued: model.queued,
    suggested_tag: suggestedTag,
    stable_tag_for_pin: stableTagForPin,
  };
  writeFileSync(join(out, 'result.json'), `${JSON.stringify(result, null, 2)}\n`);
  console.log(`report: ${rows.length} IR change(s), ${breaking ? 'BREAKING' : 'no breaking rows'}, suggested ${suggestedTag}, labels ${labels.join(',')}`);
}

if (import.meta.main) await runMain(main);
