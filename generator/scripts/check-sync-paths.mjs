// Job `open-pr` of upstream-sync.yml (review D5-1). The sync patch was written by a job that ran upstream package code.
// create-pull-request commits the whole index after `git add <add-paths>`, so the allowlist is enforced here: after
// `git apply` (working tree only), every changed or new path must match an entry of SYNC_PATHS, the same list the
// workflow passes as add-paths.
//
//   SYNC_PATHS="<one entry per line>" node generator/scripts/check-sync-paths.mjs
import { run, runMain } from './gha.mjs';

/** One entry per line: an exact path, or a folder followed by `/**`. @param {string} text */
export function parseAllowlist(text) {
  const entries = text.split('\n').map((l) => l.trim()).filter(Boolean);
  for (const entry of entries) {
    const folder = entry.endsWith('/**') ? entry.slice(0, -3) : entry;
    if (folder.includes('*')) throw new Error(`SYNC_PATHS entry ${JSON.stringify(entry)} is neither an exact path nor <folder>/**.`);
  }
  return entries;
}

/** @param {string[]} paths @param {string[]} allowlist */
export function outsideAllowlist(paths, allowlist) {
  return paths.filter((path) => !allowlist.some((entry) => (entry.endsWith('/**') ? path.startsWith(entry.slice(0, -2)) : path === entry)));
}

function main() {
  const allowlist = parseAllowlist(process.env.SYNC_PATHS ?? '');
  if (allowlist.length === 0) throw new Error('SYNC_PATHS is empty.');
  const lines = (/** @type {string[]} */ args) => run('git', args).split('\n').filter(Boolean);
  const changed = [...lines(['diff', '--name-only', '--no-renames']), ...lines(['ls-files', '--others', '--exclude-standard'])];
  const outside = outsideAllowlist(changed, allowlist);
  if (outside.length > 0) throw new Error(`The sync patch changes paths outside SYNC_PATHS:\n${outside.join('\n')}`);
  console.log(`sync patch: ${changed.length} path(s), all inside SYNC_PATHS`);
}

if (import.meta.main) await runMain(main);
