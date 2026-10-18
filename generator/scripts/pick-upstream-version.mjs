// Job `detect` of upstream-sync.yml (companion part 1, End-to-end flow step 1).
//
//   REQUESTED=<version or empty> GH_TOKEN=... node generator/scripts/pick-upstream-version.mjs
//
// Reads the flint-chart packument, the pin in generator/package.json and the bot's pull requests (one `gh pr list`),
// picks the next upstream version, files the deduplicated backport issues and writes the job outputs `version`,
// `queued`, `stale`, `stale_numbers` and `context` (JSON for the report) and a step summary on every run.
import { readFileSync } from 'node:fs';
import { readTagPrefix } from './release-check.mjs';
import { appendSummary, fetchJson, run, runMain, setOutputs } from './gha.mjs';
import { compareVersions, isUpstreamVersion, parseReleaseTag } from './versions.mjs';

export const BRANCH_PREFIX = 'upstream/flint-chart-';
const GENERATOR_PATHS = ['generator/src', 'generator/overrides.json'];
const SHA = /^[0-9a-f]{40}$/;

/** @param {string} version */
export const botBranch = (version) => `${BRANCH_PREFIX}${version}`;

/**
 * @typedef {{ name: string }} Label
 * @typedef {{ number: number, headRefName: string, state: string, labels: Label[], author?: { login: string, is_bot?: boolean },
 *   isCrossRepository?: boolean }} BotPr
 * @typedef {{ versions: Record<string, { deprecated?: unknown, gitHead?: string }>, time: Record<string, string> }} Packument
 * @param {{ packument: Packument, pin: string, prs: BotPr[], requested?: string }} input
 */
export function pickUpstreamVersion({ packument, pin, prs: listed, requested }) {
  const prs = listed.filter(isBotPr);
  const all = Object.keys(packument.versions);
  const stable = all.filter((v) => isUpstreamVersion(v) && !packument.versions[v].deprecated);
  const candidates = stable.filter((v) => compareVersions(v, pin) > 0).sort(compareVersions);
  const prereleases = all
    .filter((v) => /^\d+\.\d+\.\d+-[0-9A-Za-z.-]+$/.test(v) && compareVersions(v.split('-')[0], pin) >= 0)
    .sort();

  /** @param {string} version */
  const prsOf = (version) => prs.filter((p) => p.headRefName === botBranch(version));
  /** @param {BotPr} p */
  const hasSkip = (p) => p.labels.some((l) => l.name === 'upstream-skip');
  const skipped = candidates.filter((v) => {
    const open = prsOf(v).filter((p) => p.state === 'OPEN');
    if (open.some(hasSkip)) return true;
    return open.length === 0 && prsOf(v).some((p) => p.state === 'CLOSED');
  });

  let version;
  if (requested) {
    if (!candidates.includes(requested)) {
      throw new Error(`Requested version ${JSON.stringify(requested)} is not a stable flint-chart version above the pin ${pin}.`);
    }
    version = requested;
  } else {
    version = candidates.find((v) => !skipped.includes(v)) ?? '';
  }
  const queued = candidates.filter((v) => v !== version && !skipped.includes(v));

  const stalePrs = prs
    .filter((p) => p.state === 'OPEN' && p.headRefName.startsWith(BRANCH_PREFIX))
    .map((p) => ({ version: p.headRefName.slice(BRANCH_PREFIX.length), number: p.number }))
    .filter((p) => isUpstreamVersion(p.version) && Number.isInteger(p.number) && !candidates.includes(p.version))
    .sort((a, b) => compareVersions(a.version, b.version));
  const stale = stalePrs.map((p) => p.version);
  const staleNumbers = stalePrs.map((p) => p.number);

  const pinTime = packument.time[pin];
  const belowPin = stable
    .filter((v) => compareVersions(v, pin) < 0 && pinTime !== undefined && packument.time[v] > pinTime)
    .sort(compareVersions);

  return { version, queued, skipped, stale, staleNumbers, belowPin, prereleases };
}

/**
 * A pull request the sync App opened in this repository. The `upstream` label and the branch name alone are not enough:
 * a maintainer could label a human or fork pull request on a branch with the bot's name.
 * @param {BotPr} p
 */
export function isBotPr(p) {
  return p.author?.is_bot === true && p.isCrossRepository === false;
}

/** @param {string[]} tags @param {string} prefix */
export function stableTagExists(tags, prefix) {
  return tags.some((tag) => {
    try {
      return !parseReleaseTag(tag, prefix).prerelease;
    } catch {
      return false;
    }
  });
}

/**
 * Consecutive clean merged syncs, newest first. A sync is clean when no generator change landed between the previous
 * sync's merge and its own merge (for the oldest one: between its creation and its merge).
 * @typedef {{ from?: string, since?: string, to: string }} Range
 * @param {{ mergeCommit: string, createdAt: string }[]} syncs merged sync PRs, newest first
 * @param {(range: Range) => boolean} changed whether generator/src or generator/overrides.json changed in the range
 */
export function cleanSyncStreak(syncs, changed) {
  let streak = 0;
  for (let i = 0; i < syncs.length; i++) {
    const previous = syncs[i + 1];
    const range = previous ? { from: previous.mergeCommit, to: syncs[i].mergeCommit } : { since: syncs[i].createdAt, to: syncs[i].mergeCommit };
    if (changed(range)) break;
    streak++;
  }
  return streak;
}

/**
 * True when the pin's sync PR was labelled breaking (its line restarted at -preview.1) and no generator change has
 * landed since its merge, so the report can suggest the stable tag of the pin's line. A line restarts only after the
 * stable exit (design part 3 §8.1), and a line whose stable tag `<prefix><pin>.1` exists needs no suggestion.
 * @param {{ mergeCommit: string, labels: Label[] } | undefined} pinSync
 * @param {(mergeCommit: string) => boolean} changedSince
 * @param {{ stableExists: boolean, lineTagged: boolean }} tags
 */
export function restartedLineClean(pinSync, changedSince, { stableExists, lineTagged }) {
  if (!stableExists || lineTagged) return false;
  if (!pinSync || !pinSync.labels.some((l) => l.name === 'breaking')) return false;
  return !changedSince(pinSync.mergeCommit);
}

/** @param {Range} range */
function generatorChanged(range) {
  const args = ['log', '--format=%H'];
  if (range.since) args.push(`--since=${range.since}`);
  args.push(range.from ? `${range.from}..${range.to}` : range.to, '--', ...GENERATOR_PATHS);
  return run('git', args).trim() !== '';
}

/**
 * Files an issue unless one with exactly this title exists in any state: a closed one records the maintainer's decision.
 * @param {string} title @param {string} body @param {(command: string, args: string[]) => string} exec
 */
export function ensureIssue(title, body, exec = run) {
  const found = JSON.parse(exec('gh', ['issue', 'list', '--label', 'upstream', '--state', 'all', '--search', `"${title}" in:title`, '--json', 'number,title']));
  if (found.some((/** @type {{ title: string }} */ i) => i.title === title)) return;
  exec('gh', ['issue', 'create', '--title', title, '--label', 'upstream,needs-attention', '--body', body]);
}

async function main() {
  const root = new URL('../../', import.meta.url);
  const read = (/** @type {string} */ path) => readFileSync(new URL(path, root), 'utf8');
  const pin = JSON.parse(read('generator/package.json')).dependencies['flint-chart'];
  const requested = (process.env.REQUESTED ?? '').trim();

  /** @type {Packument} */
  const packument = await fetchJson('https://registry.npmjs.org/flint-chart');
  const prs = JSON.parse(
    run('gh', [
      'pr', 'list', '--label', 'upstream', '--state', 'all', '--limit', '1000',
      '--json', 'number,headRefName,state,createdAt,mergedAt,labels,mergeCommit,author,isCrossRepository',
    ]),
  ).filter(isBotPr);
  const result = pickUpstreamVersion({ packument, pin, prs, requested: requested || undefined });

  const prefix = readTagPrefix(read('.github/workflows/release.yml'));
  const tags = run('git', ['tag', '--list']).split('\n').filter(Boolean);
  const merged = prs
    .filter((/** @type {any} */ p) => p.state === 'MERGED' && p.mergeCommit && p.headRefName.startsWith(BRANCH_PREFIX))
    .sort((/** @type {any} */ a, /** @type {any} */ b) => (a.mergedAt < b.mergedAt ? 1 : -1))
    .map((/** @type {any} */ p) => ({ headRefName: p.headRefName, labels: p.labels, mergeCommit: p.mergeCommit.oid, createdAt: p.createdAt }));
  const streak = cleanSyncStreak(merged, generatorChanged);
  const stableExists = stableTagExists(tags, prefix);
  const restarted = restartedLineClean(
    merged.find((/** @type {{ headRefName: string }} */ p) => p.headRefName === botBranch(pin)),
    (from) => generatorChanged({ from, to: 'HEAD' }),
    { stableExists, lineTagged: tags.includes(`${prefix}${pin}.1`) },
  );

  /** @param {string} v */
  const gitHead = (v) => {
    const head = packument.versions[v]?.gitHead;
    return head && SHA.test(head) ? head : null;
  };
  const context = {
    pin,
    version: result.version,
    queued: result.queued,
    skipped: result.skipped,
    belowPin: result.belowPin,
    prereleases: result.prereleases.filter((v) => /^[0-9A-Za-z.-]+$/.test(v)),
    published: result.version ? packument.time[result.version] : null,
    gitHeadOld: gitHead(pin),
    gitHeadNew: result.version ? gitHead(result.version) : null,
    tagPrefix: prefix,
    stableExists,
    cleanSyncStreak: streak,
    restartedLineClean: restarted,
  };

  for (const v of result.belowPin) {
    ensureIssue(
      `upstream backport flint-chart ${v}`,
      `flint-chart ${v} was published after the pin ${pin} but is lower by semver. No pull request is opened for it. ` +
        'Decide whether a release/X.Y branch should track it (companion part 1, "Prerelease and backport handling").',
    );
  }

  setOutputs({
    version: result.version,
    queued: result.queued.join(' '),
    stale: result.stale.join(' '),
    stale_numbers: result.staleNumbers.join(' '),
    context: JSON.stringify(context),
  });
  const list = (/** @type {string[]} */ items) => (items.length ? items.join(', ') : 'none');
  appendSummary(
    '## Upstream sync: detect\n\n' +
      `- Pin: flint-chart ${pin}\n` +
      `- Decision: ${result.version ? `sync flint-chart ${result.version}${requested ? ' (dispatched)' : ''}` : 'nothing to do'}\n` +
      `- Queued: ${list(result.queued)}\n` +
      `- Skipped (upstream-skip or closed unmerged): ${list(result.skipped)}\n` +
      `- Stale bot PRs to close: ${list(result.stale)}\n` +
      `- Below the pin (backport issues): ${list(result.belowPin)}\n` +
      `- Upstream prereleases (ignored): ${list(context.prereleases)}\n` +
      `- Stable tag exists: ${context.stableExists}; clean sync streak: ${streak}; restarted line clean: ${restarted}\n`,
  );
}

if (import.meta.main) await runMain(main);
