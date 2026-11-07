import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { syncPatch } from '../scripts/write-sync-patch.mjs';

const git = (cwd: string, ...args: string[]) =>
  execFileSync('git', ['-c', 'commit.gpgsign=false', '-c', 'user.name=t', '-c', 'user.email=t@example.com', ...args], { cwd, encoding: 'utf8' });

describe('write-sync-patch', () => {
  // source: git-add(1) `-N` records new files as intent-to-add and `git add .` stages removals, so `git diff` (working tree
  // against the index) loses the deleted files; the flint-chart 1.0.0 sync dropped two deleted generated files this way.
  // The patch is the working tree against HEAD: modified, new and deleted files.
  it('writes modified, new and deleted files into a patch that applies on a clean checkout', () => {
    const dir = mkdtempSync(join(tmpdir(), 'sync-patch-'));
    try {
      writeFileSync(join(dir, 'kept.g.cs'), 'old\n');
      writeFileSync(join(dir, 'stale.g.cs'), 'stale\n');
      git(dir, 'init', '-q');
      git(dir, 'add', '.');
      git(dir, 'commit', '-qm', 'base');
      writeFileSync(join(dir, 'kept.g.cs'), 'new\n');
      writeFileSync(join(dir, 'added.g.cs'), 'added\n');
      rmSync(join(dir, 'stale.g.cs'));

      const patch = syncPatch(dir);
      assert.match(patch, /^deleted file mode .*\n.*\n--- a\/stale\.g\.cs/m);
      assert.match(patch, /^new file mode .*\n.*\n--- \/dev\/null\n\+\+\+ b\/added\.g\.cs/m);
      assert.match(patch, /^\+\+\+ b\/kept\.g\.cs\n@@ .*\n-old\n\+new/m);

      git(dir, 'reset', '-q', '--hard');
      git(dir, 'clean', '-qfd');
      writeFileSync(join(dir, 'p.patch'), patch);
      git(dir, 'apply', 'p.patch');
      rmSync(join(dir, 'p.patch'));
      assert.equal(git(dir, 'status', '--short'), ' M kept.g.cs\n D stale.g.cs\n?? added.g.cs\n');
      assert.equal(readFileSync(join(dir, 'added.g.cs'), 'utf8'), 'added\n');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
