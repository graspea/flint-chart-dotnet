import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkReleaseTag, keyFingerprint, parseAllowedSigners, parseTagName, parseVerifyOutput, verifyReleaseTag } from '../scripts/verify-release-tag.mjs';

const KEY = 'AAAAC3NzaC1lZDI1NTE5AAAAIPl5s9IqvPpg9TKXSCQFOdjkY33BUwnxzxqd1WHpQRMu';

describe('verify-release-tag: allowed signers', () => {
  // source: ssh-keygen(1), ALLOWED SIGNERS (principals, optional options, key type, base64 key, optional comment;
  // "#" comments and empty lines are ignored)
  it('reads the key lines and skips comments and blank lines', () => {
    const text = `# header\n\n  # indented comment\nalice@example.com namespaces="git" ssh-ed25519 ${KEY} alice laptop\n`;
    assert.deepEqual(parseAllowedSigners(text), [
      { line: 4, principals: ['alice@example.com'], options: { namespaces: 'git' }, keyType: 'ssh-ed25519', key: KEY },
    ]);
  });

  // source: ssh-keygen(1), ALLOWED SIGNERS (principals are comma-separated; options are comma-separated, values may be
  // quoted and contain commas)
  it('reads several principals and quoted option values with commas', () => {
    const [entry] = parseAllowedSigners(`a@example.com,b@example.com namespaces="git,file",valid-after="20260101" ssh-ed25519 ${KEY}\n`);
    assert.deepEqual(entry.principals, ['a@example.com', 'b@example.com']);
    assert.deepEqual(entry.options, { namespaces: 'git,file', 'valid-after': '20260101' });
  });

  // source: .github/allowed_signers header (a release cannot start before a maintainer key is added)
  it('returns no entries for a file with comments only', () => {
    assert.deepEqual(parseAllowedSigners('# only comments\n\n'), []);
  });

  // source: design §9.6 step 4 (every key is limited to the git namespace; one maintainer email per principal, no
  // wildcard patterns, no certificate authorities)
  for (const [line, message] of [
    [`alice@example.com ssh-ed25519 ${KEY}`, /line 1: .*namespaces="git"/],
    [`alice@example.com namespaces="file" ssh-ed25519 ${KEY}`, /line 1: .*namespaces="git"/],
    [`*@example.com namespaces="git" ssh-ed25519 ${KEY}`, /line 1: principal "\*@example\.com"/],
    [`alice namespaces="git" ssh-ed25519 ${KEY}`, /line 1: principal "alice"/],
    [`alice@example.com cert-authority,namespaces="git" ssh-ed25519 ${KEY}`, /line 1: .*cert-authority/],
    ['alice@example.com namespaces="git" ssh-ed25519', /line 1: .*key/],
    [`alice@example.com namespaces="git" not-a-key-type ${KEY}`, /line 1: .*key type/],
  ] as const) {
    it(`rejects ${JSON.stringify(line.replace(KEY, '<key>'))}`, () => {
      assert.throws(() => parseAllowedSigners(line), message);
    });
  }
});

describe('verify-release-tag: verify-tag output', () => {
  // source: git verify-tag --raw with gpg.format=ssh (git 2.54, OpenSSH 10.3), stderr of a trusted signature
  it('reads the principal and the key of a good signature', () => {
    assert.deepEqual(parseVerifyOutput('Good "git" signature for alice@example.com with ED25519 key SHA256:ze2CxyZ5Dmu4qHt7CTAdFjjpRQCSD2K6WpoU/gFjaoc\n'), {
      principal: 'alice@example.com',
      keyType: 'ED25519',
      fingerprint: 'SHA256:ze2CxyZ5Dmu4qHt7CTAdFjjpRQCSD2K6WpoU/gFjaoc',
    });
  });

  // source: the same probe: a valid signature by a key that is not in the file has no "for <principal>"
  it('reads no principal from an untrusted signature', () => {
    assert.equal(parseVerifyOutput('Good "git" signature with ED25519 key SHA256:FjPX\nNo principal matched.\n'), undefined);
    assert.equal(parseVerifyOutput('error: no signature found\n'), undefined);
  });
});

describe('verify-release-tag: key fingerprint and tag object', () => {
  // source: ssh-keygen -lf on a file holding "ssh-ed25519 <KEY>" (OpenSSH 10.3)
  it('computes the SHA256 fingerprint that ssh-keygen prints', () => {
    assert.equal(keyFingerprint(KEY), 'SHA256:ze2CxyZ5Dmu4qHt7CTAdFjjpRQCSD2K6WpoU/gFjaoc');
  });

  // source: git cat-file tag (headers object, type, tag, tagger, then a blank line and the message)
  it('reads the tag name from the header of the tag object, not from the message', () => {
    const object = 'object 1111111111111111111111111111111111111111\ntype commit\ntag v0.5.1.1\ntagger Alice <alice@example.com> 1760000000 +0000\n\nrelease\ntag v9.9.9.9\n';
    assert.equal(parseTagName(object), 'v0.5.1.1');
    assert.equal(parseTagName('object 1111111111111111111111111111111111111111\ntype commit\n\ntag v1\n'), undefined);
  });
});

describe('verify-release-tag: decision', () => {
  const signers = parseAllowedSigners(`alice@example.com namespaces="git" ssh-ed25519 ${KEY}\n`);
  const fingerprint = keyFingerprint(KEY);
  const good = {
    tag: 'v0.5.1.1',
    objectType: 'tag',
    signers,
    verify: { status: 0, output: `Good "git" signature for alice@example.com with ED25519 key ${fingerprint}\n` },
    taggerEmail: '<alice@example.com>',
    onMain: true,
    tagName: 'v0.5.1.1',
    mainSigners: signers,
    mainRef: 'origin/main',
  };

  // source: design §9.6 step 4 (annotated, verified against .github/allowed_signers of the tagged commit, tagger is the
  // signer, the tagged commit is on main)
  it('accepts a signed annotated tag by a listed maintainer on main', () => {
    assert.deepEqual(checkReleaseTag(good), { principal: 'alice@example.com', fingerprint });
  });

  // source: git cat-file -t (a lightweight tag names the commit itself and carries no tagger and no signature)
  it('rejects a lightweight tag before anything else', () => {
    assert.throws(() => checkReleaseTag({ ...good, objectType: 'commit', verify: { status: 1, output: '' } }), /v0\.5\.1\.1 is a lightweight tag/);
  });

  // source: .github/allowed_signers header (the file holds comments only until a maintainer key is added)
  it('fails with a clear message when no maintainer key is listed', () => {
    assert.throws(() => checkReleaseTag({ ...good, signers: [] }), /\.github\/allowed_signers lists no maintainer key/);
  });

  it('rejects a signature that git does not verify, quoting the git output', () => {
    assert.throws(
      () => checkReleaseTag({ ...good, verify: { status: 1, output: 'Good "git" signature with ED25519 key SHA256:x\nNo principal matched.\n' } }),
      /signature of v0\.5\.1\.1 does not verify against \.github\/allowed_signers[\s\S]*No principal matched\./,
    );
    assert.throws(() => checkReleaseTag({ ...good, verify: { status: 1, output: 'error: no signature found\n' } }), /no signature found/);
  });

  // source: defence in depth: exit status 0 alone is not enough, the principal must be printed and listed
  it('rejects exit status 0 without a listed principal', () => {
    assert.throws(() => checkReleaseTag({ ...good, verify: { status: 0, output: '' } }), /does not verify/);
    assert.throws(
      () => checkReleaseTag({ ...good, verify: { status: 0, output: 'Good "git" signature for eve@example.com with ED25519 key SHA256:e\n' } }),
      /eve@example\.com is not listed/,
    );
  });

  // source: design §9.6 step 4 (the tagger is the maintainer whose key signed; emails compare case-insensitively)
  it('rejects a tagger who is not the signer', () => {
    assert.throws(() => checkReleaseTag({ ...good, taggerEmail: '<bob@example.com>' }), /tagger bob@example\.com is not the signer alice@example\.com/);
    assert.deepEqual(checkReleaseTag({ ...good, taggerEmail: '<Alice@Example.com>' }).principal, 'alice@example.com');
  });

  // source: design §9.6 step 3 (the tag goes on the release pull request's merge commit on main)
  it('rejects a commit that is not on main', () => {
    assert.throws(() => checkReleaseTag({ ...good, onMain: false }), /not reachable from origin\/main/);
  });

  // source: review 2026-10-17 (a signed tag object pushed again under another v* name must not pass as that version)
  it('rejects a tag object whose own tag name is not the ref name', () => {
    assert.throws(() => checkReleaseTag({ ...good, tagName: 'v0.5.1.0' }), /tag object of v0\.5\.1\.1 names v0\.5\.1\.0/);
    assert.throws(() => checkReleaseTag({ ...good, tagName: undefined }), /tag object of v0\.5\.1\.1 names no tag/);
  });

  // source: review 2026-10-17 (a key removed from main's .github/allowed_signers is revoked for every tag; the signer and
  // the key must be in both files)
  it('rejects a signer whose key is no longer listed on main', () => {
    const otherKey = 'AAAAC3NzaC1lZDI1NTE5AAAAIHk1h0xYH2gD3zWbH2b6vYc3oXo0t0dJ5F9n8m4mK1qZ';
    assert.throws(() => checkReleaseTag({ ...good, mainSigners: [] }), /alice@example\.com .* not listed in \.github\/allowed_signers on origin\/main/);
    assert.throws(
      () => checkReleaseTag({ ...good, mainSigners: parseAllowedSigners(`alice@example.com namespaces="git" ssh-ed25519 ${otherKey}\n`) }),
      /not listed in \.github\/allowed_signers on origin\/main/,
    );
    assert.throws(
      () => checkReleaseTag({ ...good, mainSigners: parseAllowedSigners(`bob@example.com namespaces="git" ssh-ed25519 ${KEY}\n`) }),
      /not listed in \.github\/allowed_signers on origin\/main/,
    );
  });

  it('lists every problem at once', () => {
    assert.throws(
      () => checkReleaseTag({ ...good, taggerEmail: '<bob@example.com>', onMain: false }),
      (error: Error) => /tagger bob@example\.com/.test(error.message) && /origin\/main/.test(error.message),
    );
  });
});

const hasSshKeygen = spawnSync('ssh-keygen', ['-?'], { stdio: 'ignore' }).error === undefined;

describe('verify-release-tag: against git', { skip: !hasSshKeygen && 'ssh-keygen is not on the PATH' }, () => {
  // source: git verify-tag and ssh-keygen -Y verify, run for real on a throw-away repository
  it('accepts a maintainer-signed tag on main and rejects the others', () => {
    const dir = mkdtempSync(join(tmpdir(), 'verify-release-tag-'));
    try {
      const git = (...args: string[]) => execFileSync('git', args, { cwd: join(dir, 'repo'), encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
      for (const name of ['alice', 'mallory']) execFileSync('ssh-keygen', ['-q', '-t', 'ed25519', '-N', '', '-C', name, '-f', join(dir, name)]);
      mkdirSync(join(dir, 'repo', '.github'), { recursive: true });
      git('init', '-q', '-b', 'main');
      git('config', 'user.name', 'Alice');
      git('config', 'user.email', 'alice@example.com');
      git('config', 'gpg.format', 'ssh');
      git('config', 'commit.gpgsign', 'false');
      git('config', 'tag.gpgsign', 'false');
      const pub = readFileSync(join(dir, 'alice.pub'), 'utf8').trim();
      writeFileSync(join(dir, 'repo', '.github', 'allowed_signers'), `# maintainers\nalice@example.com namespaces="git" ${pub}\n`);
      git('add', '.');
      git('commit', '-qm', 'init');
      git('update-ref', 'refs/remotes/origin/main', 'HEAD');
      git('-c', `user.signingkey=${join(dir, 'alice')}`, 'tag', '-s', 'v0.5.1.1', '-m', 'release');
      git('-c', `user.signingkey=${join(dir, 'mallory')}`, 'tag', '-s', 'v0.5.1.2', '-m', 'release');
      git('tag', 'v0.5.1.3');
      git('checkout', '-q', '-b', 'side');
      git('commit', '-q', '--allow-empty', '-m', 'side');
      git('-c', `user.signingkey=${join(dir, 'alice')}`, 'tag', '-s', 'v0.5.1.4', '-m', 'release');

      const cwd = join(dir, 'repo');
      assert.equal(verifyReleaseTag({ tag: 'v0.5.1.1', cwd }).principal, 'alice@example.com');
      assert.throws(() => verifyReleaseTag({ tag: 'v0.5.1.2', cwd }), /does not verify[\s\S]*No principal matched/);
      assert.throws(() => verifyReleaseTag({ tag: 'v0.5.1.3', cwd }), /lightweight/);
      assert.throws(() => verifyReleaseTag({ tag: 'v0.5.1.4', cwd }), /not reachable from origin\/main/);
      assert.throws(() => verifyReleaseTag({ tag: 'v9.9.9.9', cwd }), /no tag v9\.9\.9\.9/);

      // The same signed tag object under another name.
      git('update-ref', 'refs/tags/v0.5.1.9', git('rev-parse', 'refs/tags/v0.5.1.1').trim());
      assert.throws(() => verifyReleaseTag({ tag: 'v0.5.1.9', cwd }), /tag object of v0\.5\.1\.9 names v0\.5\.1\.1/);

      // Alice's key is retired on main: her earlier tag no longer verifies, although its own commit still lists her.
      git('checkout', '-q', 'main');
      execFileSync('ssh-keygen', ['-q', '-t', 'ed25519', '-N', '', '-C', 'bob', '-f', join(dir, 'bob')]);
      const bob = readFileSync(join(dir, 'bob.pub'), 'utf8').trim();
      writeFileSync(join(dir, 'repo', '.github', 'allowed_signers'), `# maintainers\nbob@example.com namespaces="git" ${bob}\n`);
      git('commit', '-qam', 'retire alice');
      git('update-ref', 'refs/remotes/origin/main', 'HEAD');
      assert.throws(() => verifyReleaseTag({ tag: 'v0.5.1.1', cwd }), /alice@example\.com .* not listed in \.github\/allowed_signers on origin\/main/);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});

describe('verify-release-tag: release.yml', () => {
  // source: design §9.6 step 4 (the tag is verified in job check, before the test suite and the publish job run)
  it('verifies the tag in job check, before any job that builds', () => {
    const yml = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
    const check = yml.slice(yml.indexOf('\n  check:\n'), yml.indexOf('\n  ci:\n'));
    assert.match(check, /node generator\/scripts\/verify-release-tag\.mjs --tag "\$TAG"/);
    assert.ok(check.indexOf('verify-release-tag.mjs') < check.indexOf('release-check.mjs'));
    assert.match(yml, /\n  ci:\n    needs: check\n/);
    assert.match(yml, /\n  publish:\n    needs: \[check, ci\]\n/);
  });

  // source: review 2026-10-17 (every job of a run can replace that run's artifacts, including job ci, which runs npm
  // and NuGet dependencies; the release text must not pass through an artifact)
  it('writes the release notes in job github-release from the tagged commit, not from an artifact', () => {
    const yml = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
    const release = yml.slice(yml.indexOf('\n  github-release:\n'));
    assert.doesNotMatch(yml, /release-notes/);
    assert.match(release, /node generator\/scripts\/release-check\.mjs --tag "\$TAG" --notes-dir notes/);
    assert.ok(release.indexOf('release-check.mjs') < release.indexOf('gh release create'));
  });
});
