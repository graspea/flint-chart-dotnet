import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { COMMENT_MARKER, foreignCommits, guardBranch, regenerationComment } from '../scripts/sync-branch-owner.mjs';

const bot = 'flint-sync[bot]';
const branch = 'upstream/flint-chart-0.6.0';
const commit = (sha: string, author: string | null, committer: string | null = author) => ({ sha: sha.repeat(40), author, committer });

/** A fake `gh` that answers from the given state and records every call. */
function fakeGh(state: { refs: string[]; commits?: ReturnType<typeof commit>[]; prs?: unknown[]; comments?: { id: number; login: string; body: string }[] }) {
  const calls: string[][] = [];
  const exec = (command: string, args: string[]) => {
    calls.push([command, ...args]);
    const path = args.find((a) => a.startsWith('repos/')) ?? '';
    if (args[0] === 'api' && path.includes('/git/matching-refs/')) return JSON.stringify(state.refs.map((ref) => ({ ref })));
    if (args[0] === 'api' && path.includes('/compare/')) return JSON.stringify(state.commits ?? []);
    if (args[0] === 'pr' && args[1] === 'list') return JSON.stringify(state.prs ?? []);
    if (args[0] === 'api' && path.endsWith('/comments') && !args.includes('-f')) return (state.comments ?? []).map((c) => JSON.stringify(c)).join('\n');
    return '{}';
  };
  return { calls, exec };
}

const botPr = { number: 7, author: { login: 'app/flint-sync', is_bot: true }, isCrossRepository: false };
const input = { branch, bot, version: '0.6.0', report: '## flint-chart 0.5.1 -> 0.6.0\n' };

describe('sync-branch-owner', () => {
  // source: maintainer decision on maintainer commits (the branch is the bot's while every commit since main is the
  // App's; GitHub commits API-made commits as web-flow); a commit with no linked user is not the bot's
  it('finds the commits that are not the bot\'s', () => {
    assert.deepEqual(foreignCommits([commit('a', bot, 'web-flow'), commit('b', bot)], bot), []);
    assert.deepEqual(
      foreignCommits([commit('a', bot), commit('b', 'maintainer', 'web-flow'), commit('c', bot, 'maintainer'), commit('d', null)], bot).map((c) => c.sha[0]),
      ['b', 'c', 'd'],
    );
  });

  // source: create-pull-request (creates the branch on the first run)
  it('pushes when the branch does not exist yet', () => {
    const gh = fakeGh({ refs: [`refs/heads/${branch}-rc`] });
    assert.equal(guardBranch(input, gh.exec), true);
    assert.equal(gh.calls.length, 1);
  });

  // source: maintainer decision on maintainer commits (a bot-only branch is regenerated as before)
  it('pushes when every commit on the branch is the bot\'s', () => {
    const gh = fakeGh({ refs: [`refs/heads/${branch}`], commits: [commit('a', bot, 'web-flow')] });
    assert.equal(guardBranch(input, gh.exec), true);
    assert.deepEqual(gh.calls[1].slice(0, 3), ['gh', 'api', `repos/{owner}/{repo}/compare/main...${branch}`]);
    assert.equal(gh.calls.length, 2);
  });

  // source: maintainer decision on maintainer commits (no push; one comment with the newer regeneration, updated on
  // later runs, on the bot's open pull request)
  it('does not push and posts the regeneration once when a maintainer has committed', () => {
    const commits = [commit('a', bot), commit('b', 'maintainer')];
    const first = fakeGh({ refs: [`refs/heads/${branch}`], commits, prs: [botPr], comments: [{ id: 3, login: 'maintainer', body: COMMENT_MARKER }] });
    assert.equal(guardBranch(input, first.exec), false);
    const post = first.calls.at(-1)!;
    assert.deepEqual(post.slice(0, 3), ['gh', 'api', 'repos/{owner}/{repo}/issues/7/comments']);
    assert.ok(post.at(-1)!.startsWith(`body=${COMMENT_MARKER}`));

    const later = fakeGh({ refs: [`refs/heads/${branch}`], commits, prs: [botPr], comments: [{ id: 9, login: bot, body: `${COMMENT_MARKER}\nold` }] });
    assert.equal(guardBranch(input, later.exec), false);
    assert.deepEqual(later.calls.at(-1)!.slice(0, 5), ['gh', 'api', '-X', 'PATCH', 'repos/{owner}/{repo}/issues/comments/9']);
  });

  // source: pick-upstream-version isBotPr (a pull request on the bot's branch name from a human or a fork is not the bot's)
  it('comments only on the bot\'s own open pull request', () => {
    const human = { number: 8, author: { login: 'maintainer', is_bot: false }, isCrossRepository: false };
    const gh = fakeGh({ refs: [`refs/heads/${branch}`], commits: [commit('b', 'maintainer')], prs: [human] });
    assert.equal(guardBranch(input, gh.exec), false);
    assert.ok(!gh.calls.some((c) => c.join(' ').includes('/comments')));
  });

  // source: GitHub comment limit (65536 characters); mentions in backticks notify nobody
  it('names the foreign commits without mentioning anyone and caps the report', () => {
    const body = regenerationComment({ ...input, foreign: [commit('b', 'maintainer'), commit('d', null)], report: 'x'.repeat(70_000) });
    assert.ok(body.startsWith(COMMENT_MARKER));
    assert.ok(body.includes('`bbbbbbbb` by `maintainer`'), body.slice(0, 600));
    assert.ok(body.includes('`dddddddd` by an unlinked author'), body.slice(0, 600));
    assert.ok(!body.includes('@maintainer'));
    assert.ok(body.length < 65_536, `${body.length}`);
    assert.ok(body.includes('[truncated; full report in artifact upstream-0.6.0/pr-body.md]'));
  });
});
