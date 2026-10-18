import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { cleanSyncStreak, ensureIssue, isBotPr, pickUpstreamVersion, restartedLineClean, stableTagExists } from '../scripts/pick-upstream-version.mjs';

type Pr = {
  number: number;
  headRefName: string;
  state: 'OPEN' | 'CLOSED' | 'MERGED';
  labels: { name: string }[];
  author: { login: string; is_bot: boolean };
  isCrossRepository: boolean;
};

/** A packument with the given versions, published one day apart in the order given. */
function packument(versions: string[], deprecated: string[] = []) {
  const doc = { versions: {} as Record<string, { gitHead: string; deprecated?: string }>, time: {} as Record<string, string> };
  versions.forEach((v, i) => {
    doc.versions[v] = { gitHead: `${i}`.repeat(40).slice(0, 40), ...(deprecated.includes(v) ? { deprecated: 'broken' } : {}) };
    doc.time[v] = new Date(Date.UTC(2026, 7, 1 + i)).toISOString();
  });
  return doc;
}

let nextNumber = 1;
const pr = (version: string, state: Pr['state'], ...labels: string[]): Pr => ({
  number: nextNumber++,
  headRefName: `upstream/flint-chart-${version}`,
  state,
  labels: ['upstream', ...labels].map((name) => ({ name })),
  author: { login: 'app/flint-sync', is_bot: true },
  isCrossRepository: false,
});

describe('pick-upstream-version', () => {
  // source: companion part 1, End-to-end flow step 1 (S = stable, not deprecated, greater than the pin;
  // version = min(S \ skipped); queued = the rest)
  it('picks the lowest stable version above the pin and queues the rest', () => {
    const result = pickUpstreamVersion({ packument: packument(['0.5.0', '0.5.1', '0.6.0', '0.5.2', '0.7.0']), pin: '0.5.1', prs: [] });
    assert.equal(result.version, '0.5.2');
    assert.deepEqual(result.queued, ['0.6.0', '0.7.0']);
    assert.deepEqual(result.skipped, []);
    assert.deepEqual(result.stale, []);
  });

  it('has nothing to do when the pin is the newest stable version', () => {
    const result = pickUpstreamVersion({ packument: packument(['0.5.0', '0.5.1']), pin: '0.5.1', prs: [] });
    assert.equal(result.version, '');
    assert.deepEqual(result.queued, []);
  });

  // source: companion part 1, "Prerelease and backport handling" (prereleases ignored, listed) and "Deprecated
  // versions are treated as absent"
  it('ignores prereleases and deprecated versions', () => {
    const result = pickUpstreamVersion({ packument: packument(['0.5.1', '0.6.0-beta.1', '0.6.0', '0.6.1'], ['0.6.0']), pin: '0.5.1', prs: [] });
    assert.equal(result.version, '0.6.1');
    assert.deepEqual(result.prereleases, ['0.6.0-beta.1']);
  });

  // source: companion part 1, End-to-end flow step 1 (skipped = closed unmerged PR or open PR labelled upstream-skip)
  // and Idempotency (a skipped version never blocks the versions after it)
  it('skips versions whose bot PR was closed unmerged or carries upstream-skip', () => {
    const prs = [pr('0.5.2', 'CLOSED'), pr('0.6.0', 'OPEN', 'upstream-skip'), pr('0.6.1', 'OPEN')];
    const result = pickUpstreamVersion({ packument: packument(['0.5.1', '0.5.2', '0.6.0', '0.6.1', '0.7.0']), pin: '0.5.1', prs });
    assert.equal(result.version, '0.6.1');
    assert.deepEqual(result.skipped, ['0.5.2', '0.6.0']);
    assert.deepEqual(result.queued, ['0.7.0']);
  });

  // source: companion part 1, "The upstream-skip label" (removing the label and dispatching re-opens the path; a new
  // open PR on the branch supersedes the closed one)
  it('does not skip a version that has an open PR without upstream-skip next to a closed one', () => {
    const prs = [pr('0.5.2', 'CLOSED'), pr('0.5.2', 'OPEN')];
    assert.equal(pickUpstreamVersion({ packument: packument(['0.5.1', '0.5.2']), pin: '0.5.1', prs }).version, '0.5.2');
  });

  // source: companion part 1, End-to-end flow step 1 (the workflow_dispatch input must be in S and overrides skipped)
  it('takes a dispatched version even when it is skipped', () => {
    const prs = [pr('0.5.2', 'CLOSED')];
    const result = pickUpstreamVersion({ packument: packument(['0.5.1', '0.5.2', '0.6.0']), pin: '0.5.1', prs, requested: '0.5.2' });
    assert.equal(result.version, '0.5.2');
    assert.deepEqual(result.queued, ['0.6.0']);
  });

  for (const requested of ['0.5.1', '0.4.0', '0.9.0', '0.6.0-beta.1', '0.6', 'latest', '0.5.2; rm -rf /']) {
    it(`rejects the dispatched version ${JSON.stringify(requested)}`, () => {
      assert.throws(
        () => pickUpstreamVersion({ packument: packument(['0.4.0', '0.5.1', '0.5.2', '0.6.0-beta.1']), pin: '0.5.1', prs: [], requested }),
        /not a stable flint-chart version above the pin 0\.5\.1/,
      );
    });
  }

  // source: companion part 1, End-to-end flow step 1 (stale = open bot PRs whose version is no longer in S) and
  // Idempotency (closed with a comment by the workflow)
  it('lists open bot PRs whose version left the candidate set as stale', () => {
    const prs = [pr('0.6.0', 'OPEN'), pr('0.5.1', 'OPEN'), pr('0.6.1', 'OPEN'), pr('0.5.0', 'MERGED')];
    const result = pickUpstreamVersion({ packument: packument(['0.5.0', '0.5.1', '0.6.0', '0.6.1'], ['0.6.0']), pin: '0.5.1', prs });
    assert.deepEqual(result.stale, ['0.5.1', '0.6.0']);
    assert.deepEqual(result.staleNumbers, [prs[1].number, prs[0].number]);
    assert.equal(result.version, '0.6.1');
  });

  // source: review D5-11 (only pull requests the App opened in this repository count: a labelled human or fork pull
  // request on a bot-like branch neither skips a version nor is closed as stale)
  it('ignores pull requests that the bot did not open in this repository', () => {
    const fork = { ...pr('0.5.2', 'CLOSED'), isCrossRepository: true };
    const human = { ...pr('0.6.0', 'OPEN', 'upstream-skip'), author: { login: 'someone', is_bot: false } };
    const staleFork = { ...pr('0.4.0', 'OPEN'), isCrossRepository: true };
    assert.equal(isBotPr(pr('0.5.2', 'OPEN')), true);
    assert.equal(isBotPr(fork), false);
    assert.equal(isBotPr(human), false);
    const result = pickUpstreamVersion({ packument: packument(['0.4.0', '0.5.1', '0.5.2', '0.6.0']), pin: '0.5.1', prs: [fork, human, staleFork] });
    assert.equal(result.version, '0.5.2');
    assert.deepEqual(result.skipped, []);
    assert.deepEqual(result.stale, []);
  });

  // source: companion part 1, End-to-end flow step 1 (below_pin = stable versions newer by publish time but lower by
  // semver: the backport case)
  it('reports stable versions published after the pin but below it', () => {
    const result = pickUpstreamVersion({ packument: packument(['0.4.1', '0.5.0', '0.5.1', '0.4.2', '0.5.2']), pin: '0.5.1', prs: [] });
    assert.deepEqual(result.belowPin, ['0.4.2']);
    assert.equal(result.version, '0.5.2');
  });

  // source: companion part 1, Security (every version string is validated before it is exported)
  it('never exports a version string outside X.Y.Z', () => {
    const doc = packument(['0.5.1', '0.5.2']);
    doc.versions['0.6.0\nx=1'] = { gitHead: 'f'.repeat(40) };
    doc.time['0.6.0\nx=1'] = '2026-09-01T00:00:00.000Z';
    const result = pickUpstreamVersion({ packument: doc, pin: '0.5.1', prs: [] });
    assert.equal(result.version, '0.5.2');
    assert.deepEqual(result.queued, []);
  });

  // source: companion part 1, "Suggested SDK tag" (stable_exists: a tag without a label exists)
  it('tells whether a stable release tag exists', () => {
    assert.equal(stableTagExists(['v0.5.1.1-preview.1', 'v0.5.1.1-preview.2', 'other'], 'v'), false);
    assert.equal(stableTagExists(['v0.5.1.1-preview.2', 'v0.5.1.1'], 'v'), true);
  });

  // source: design §8.1 (a sync is clean when no change to generator/src/** or generator/overrides.json was needed) and
  // companion part 1, "Suggested SDK tag" (clean_sync_streak: consecutive merged sync PRs with no such change between
  // them)
  it('counts the clean merged syncs from the newest back to the first one that needed a generator change', () => {
    const syncs = [
      { mergeCommit: 'c', createdAt: '2026-11-05T00:00:00Z' },
      { mergeCommit: 'b', createdAt: '2026-11-03T00:00:00Z' },
      { mergeCommit: 'a', createdAt: '2026-11-01T00:00:00Z' },
    ];
    const ranges: string[] = [];
    const changedIn = (dirty: string[]) => (range: { from?: string; since?: string; to: string }) => {
      const key = `${range.from ?? range.since}..${range.to}`;
      ranges.push(key);
      return dirty.includes(key);
    };
    assert.equal(cleanSyncStreak(syncs, changedIn([])), 3);
    assert.deepEqual(ranges, ['b..c', 'a..b', '2026-11-01T00:00:00Z..a']);
    assert.equal(cleanSyncStreak(syncs, changedIn(['a..b'])), 1);
    assert.equal(cleanSyncStreak(syncs, changedIn(['b..c'])), 0);
    assert.equal(cleanSyncStreak([], changedIn([])), 0);
  });

  // source: design §8.1 and companion part 1 (restarted_line_clean: the pin's sync PR had BREAKING rows and no generator
  // change has landed since its merge; a line restarts at -preview.1 only after the stable exit, and a tagged line is done)
  it('tells whether the restarted line of the pin is clean', () => {
    const merged = { mergeCommit: 'm', labels: [{ name: 'upstream' }, { name: 'breaking' }] };
    const stable = { stableExists: true, lineTagged: false };
    assert.equal(restartedLineClean(merged, () => false, stable), true);
    assert.equal(restartedLineClean(merged, () => true, stable), false);
    assert.equal(restartedLineClean({ ...merged, labels: [{ name: 'upstream' }] }, () => false, stable), false);
    assert.equal(restartedLineClean(undefined, () => false, stable), false);
  });

  // source: review D5-3 (no stable release yet: the preview era has no restarted line; <prefix><pin>.1 already tagged)
  it('finds no restarted line in the preview era or once its stable tag exists', () => {
    const merged = { mergeCommit: 'm', labels: [{ name: 'upstream' }, { name: 'breaking' }] };
    assert.equal(restartedLineClean(merged, () => false, { stableExists: false, lineTagged: false }), false);
    assert.equal(restartedLineClean(merged, () => false, { stableExists: true, lineTagged: true }), false);
  });

  // source: review D5-4 (a backport issue the maintainer closed is a decision: it is found in any state and never filed again)
  it('files a backport issue only when none with that title exists in any state', () => {
    const calls: string[][] = [];
    const exec = (issues: { title: string; state: string }[]) => (command: string, args: string[]) => {
      calls.push([command, ...args]);
      return args[1] === 'list' ? JSON.stringify(issues.map((i, n) => ({ number: n + 1, ...i }))) : '';
    };
    ensureIssue('upstream backport flint-chart 0.4.2', 'body', exec([{ title: 'upstream backport flint-chart 0.4.2', state: 'CLOSED' }]));
    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0].slice(0, 7), ['gh', 'issue', 'list', '--label', 'upstream', '--state', 'all']);
    calls.length = 0;
    ensureIssue('upstream backport flint-chart 0.4.2', 'body', exec([{ title: 'upstream backport flint-chart 0.4.21', state: 'OPEN' }]));
    assert.deepEqual(calls[1].slice(0, 4), ['gh', 'issue', 'create', '--title']);
  });
});
