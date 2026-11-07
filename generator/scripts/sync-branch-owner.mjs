// Job open-pr of upstream-sync.yml: may the bot push its regeneration to branch upstream/flint-chart-<version>?
//
//   BRANCH=<branch> BOT=<app-slug>[bot] VERSION=<version> GH_TOKEN=<App token> GH_REPO=<owner/repo> \
//     node generator/scripts/sync-branch-owner.mjs <report file>
//
// The branch is the bot's while every commit on it since main was authored by the App's bot user (and committed by it,
// or by GitHub for a commit made through the API). Once a maintainer has added a commit, the pull request is theirs:
// the bot does not push, leaves labels and draft state alone and posts the report of its newer regeneration as one
// comment on its open pull request, which later runs update. Writes the output push=true|false.
import { readFileSync } from 'node:fs';
import { run, runMain, setOutputs } from './gha.mjs';
import { isBotPr } from './pick-upstream-version.mjs';

export const COMMENT_MARKER = '<!-- upstream-sync:regeneration -->';
/** GitHub stores at most 65536 characters in a comment. */
const MAX_REPORT_CHARS = 60_000;

/**
 * @typedef {{ sha: string, author: string | null, committer: string | null }} Commit
 * @typedef {(command: string, args: string[]) => string} Exec
 */

/** @param {Commit[]} commits @param {string} bot @returns {Commit[]} */
export function foreignCommits(commits, bot) {
  return commits.filter((c) => c.author !== bot || (c.committer !== bot && c.committer !== 'web-flow'));
}

/** @param {{ branch: string, version: string, foreign: Commit[], report: string }} input */
export function regenerationComment({ branch, version, foreign, report }) {
  const who = foreign
    .map((c) => `\`${c.sha.slice(0, 8)}\` by ${c.author ? `\`${c.author}\`` : 'an unlinked author'}`)
    .join(', ');
  const body =
    report.length > MAX_REPORT_CHARS
      ? `${report.slice(0, MAX_REPORT_CHARS)}\n\n[truncated; full report in artifact upstream-${version}/pr-body.md]`
      : report;
  return (
    `${COMMENT_MARKER}\n` +
    `A newer regeneration of flint-chart ${version} is available. The bot did not push it: \`${branch}\` has commits that are not the bot's (${who}), ` +
    'so this pull request is the maintainer\'s, and its labels and draft state are left as they are. ' +
    'To take the regeneration, merge `main` into the branch and run `npm run generate` (`docs/maintainers.md`). Its report:\n\n' +
    `<details><summary>Sync report</summary>\n\n${body}\n</details>\n`
  );
}

/**
 * Decides whether the bot may push; when it may not, upserts the regeneration comment on the bot's open pull request.
 * @param {{ branch: string, bot: string, version: string, report: string }} input @param {Exec} exec
 */
export function guardBranch({ branch, bot, version, report }, exec) {
  const refs = JSON.parse(exec('gh', ['api', `repos/{owner}/{repo}/git/matching-refs/heads/${branch}`]));
  if (!refs.some((/** @type {{ ref: string }} */ r) => r.ref === `refs/heads/${branch}`)) return true;

  // ponytail: compare lists at most 250 commits; a sync branch has one or a few.
  /** @type {Commit[]} */
  const commits = JSON.parse(
    exec('gh', ['api', `repos/{owner}/{repo}/compare/main...${branch}`, '--jq', '[.commits[] | {sha, author: .author.login, committer: .committer.login}]']),
  );
  const foreign = foreignCommits(commits, bot);
  if (foreign.length === 0) return true;

  const pr = JSON.parse(exec('gh', ['pr', 'list', '--head', branch, '--state', 'open', '--json', 'number,author,isCrossRepository'])).find(isBotPr);
  if (!pr) return false;
  const body = `body=${regenerationComment({ branch, version, foreign, report })}`;
  const existing = exec('gh', ['api', '--paginate', `repos/{owner}/{repo}/issues/${pr.number}/comments`, '--jq', '.[] | {id, login: .user.login, body}'])
    .split('\n')
    .filter(Boolean)
    .map((line) => JSON.parse(line))
    .find((c) => c.login === bot && c.body.startsWith(COMMENT_MARKER));
  if (existing) exec('gh', ['api', '-X', 'PATCH', `repos/{owner}/{repo}/issues/comments/${existing.id}`, '-f', body]);
  else exec('gh', ['api', `repos/{owner}/{repo}/issues/${pr.number}/comments`, '-f', body]);
  return false;
}

async function main() {
  const { BRANCH: branch, BOT: bot, VERSION: version } = process.env;
  const reportFile = process.argv[2];
  if (!branch || !bot || !version || !reportFile) throw new Error('BRANCH, BOT, VERSION and the report file are required.');
  const push = guardBranch({ branch, bot, version, report: readFileSync(reportFile, 'utf8') }, run);
  console.log(push ? `push: ${branch} has only the bot's commits` : `no push: ${branch} has commits that are not the bot's`);
  setOutputs({ push: String(push) });
}

if (import.meta.main) await runMain(main);
