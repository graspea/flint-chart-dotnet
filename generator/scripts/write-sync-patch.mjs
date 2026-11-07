// Job `generate` of upstream-sync.yml: writes the sync patch, the working tree against HEAD, so modified, new and
// deleted files all reach job `open-pr`. `git add -N .` makes new files visible to git diff; it also stages removals,
// so a diff against the index would drop deleted files.
//
//   node generator/scripts/write-sync-patch.mjs <patch file>
import { writeFileSync } from 'node:fs';
import { run, runMain } from './gha.mjs';

/** The binary patch of the working tree of `root` against HEAD. @param {string} root */
export function syncPatch(root) {
  run('git', ['add', '-N', '.'], { cwd: root });
  return run('git', ['diff', '--binary', 'HEAD'], { cwd: root });
}

function main() {
  const out = process.argv[2];
  if (!out) throw new Error('usage: write-sync-patch.mjs <patch file>');
  writeFileSync(out, syncPatch('.'));
}

if (import.meta.main) await runMain(main);
