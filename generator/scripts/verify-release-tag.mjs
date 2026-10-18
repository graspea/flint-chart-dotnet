// Release tag guard for release.yml (design part 3 §9.6 step 4), run before anything is built.
//
//   node generator/scripts/verify-release-tag.mjs --tag <tag> [--main-ref origin/main]
//
// Fails unless the tag is annotated and its tag object names this tag, its SSH signature verifies (`git verify-tag` with
// gpg.format=ssh) against .github/allowed_signers as it is in the tagged commit, the signer and the key are also listed
// in .github/allowed_signers on origin/main (a key removed there is revoked), the signer is also the tagger, and the
// tagged commit is reachable from origin/main. Needs the full history and the tag objects (checkout with fetch-depth 0).
//
// It runs from the tagged commit, so a tag on a commit with another release.yml can skip it: the check is advisory. The
// security boundary is the tag ruleset and the nuget environment's reviewers, who run the same checks locally before
// they approve (docs/maintainers.md, release checklist step 4).
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runMain } from './gha.mjs';

const SIGNERS_PATH = '.github/allowed_signers';

// ssh-keygen(1) public key types; sk-* are hardware-backed keys.
const KEY_TYPES = new Set([
  'ssh-ed25519',
  'sk-ssh-ed25519@openssh.com',
  'ecdsa-sha2-nistp256',
  'ecdsa-sha2-nistp384',
  'ecdsa-sha2-nistp521',
  'sk-ecdsa-sha2-nistp256@openssh.com',
  'ssh-rsa',
]);

/**
 * @typedef {{ line: number, principals: string[], options: Record<string, string | true>, keyType: string, key: string }} SignerEntry
 */

/** Splits on whitespace outside double quotes. @param {string} text */
function fields(text) {
  return text.match(/(?:[^\s"]+|"[^"]*")+/g) ?? [];
}

/** `a,b="x,y",c` -> { a: true, b: 'x,y', c: true }. @param {string} text */
function parseOptions(text) {
  /** @type {Record<string, string | true>} */
  const options = {};
  for (const part of text.match(/(?:[^,"]+|"[^"]*")+/g) ?? []) {
    const eq = part.indexOf('=');
    if (eq < 0) options[part.toLowerCase()] = true;
    else options[part.slice(0, eq).toLowerCase()] = part.slice(eq + 1).replace(/^"(.*)"$/, '$1');
  }
  return options;
}

/**
 * The key lines of an allowed signers file (ssh-keygen(1), ALLOWED SIGNERS), checked against the release rules: every
 * principal is one email address (no wildcard), every key is limited to the git namespace, no certificate authority.
 * @param {string} text
 * @returns {SignerEntry[]}
 */
export function parseAllowedSigners(text) {
  /** @type {SignerEntry[]} */
  const entries = [];
  text.split(/\r?\n/).forEach((raw, index) => {
    const line = index + 1;
    const trimmed = raw.trim();
    if (trimmed === '' || trimmed.startsWith('#')) return;
    const fail = (/** @type {string} */ message) => {
      throw new Error(`${SIGNERS_PATH} line ${line}: ${message}`);
    };
    const [principalField = '', ...rest] = fields(trimmed);
    const hasOptions = rest.length > 0 && !KEY_TYPES.has(rest[0]) && rest[0].includes('=');
    const options = hasOptions ? parseOptions(rest[0]) : {};
    const [keyType, key] = hasOptions ? rest.slice(1) : rest;
    if (!keyType || !key) fail('expected "<email> namespaces=\\"git\\" <key type> <public key>"; the key is missing.');
    if (!KEY_TYPES.has(keyType)) fail(`unknown key type ${JSON.stringify(keyType)}.`);
    const principals = principalField.replace(/^"(.*)"$/, '$1').split(',');
    for (const principal of principals) {
      if (!/^[^\s@*?!,]+@[^\s@*?!,]+$/.test(principal)) fail(`principal ${JSON.stringify(principal)} is not one email address (no wildcards).`);
    }
    if ('cert-authority' in options) fail('cert-authority lines are not allowed; list each maintainer key.');
    const namespaces = typeof options.namespaces === 'string' ? options.namespaces.split(',') : [];
    if (!namespaces.includes('git')) fail('the key must be limited to the git namespace with namespaces="git".');
    entries.push({ line, principals, options, keyType, key });
  });
  return entries;
}

/**
 * The signer of a good SSH signature, from the output of `git verify-tag --raw` (ssh-keygen -Y verify). A signature by a
 * key that is not in the allowed signers file prints no "for <principal>" and gives undefined.
 * @param {string} output
 */
export function parseVerifyOutput(output) {
  const m = /^Good "git" signature for (\S+) with (\S+) key (\S+)$/m.exec(output);
  return m ? { principal: m[1], keyType: m[2], fingerprint: m[3] } : undefined;
}

/** The fingerprint ssh-keygen -l prints for a base64 public key: SHA256 of the key blob, base64 without padding. @param {string} key */
export function keyFingerprint(key) {
  return `SHA256:${createHash('sha256').update(Buffer.from(key, 'base64')).digest('base64').replace(/=+$/, '')}`;
}

/** The `tag <name>` header of a tag object (`git cat-file tag`); headers end at the first blank line. @param {string} object */
export function parseTagName(object) {
  const headers = object.split(/\r?\n\r?\n/, 1)[0];
  return /^tag (\S+)$/m.exec(headers)?.[1];
}

/**
 * Decides from the collected facts; throws with every problem found.
 * @param {{ tag: string, objectType: string, signers: SignerEntry[], verify: { status: number | null, output: string },
 *   taggerEmail: string, onMain: boolean, tagName?: string, mainSigners: SignerEntry[], mainRef?: string }} facts
 */
export function checkReleaseTag({ tag, objectType, signers, verify, taggerEmail, onMain, tagName, mainSigners, mainRef = 'origin/main' }) {
  if (objectType !== 'tag') {
    throw new Error(`${tag} is a lightweight tag (it names a ${objectType}); release tags are annotated and signed: git tag -s ${tag} -m "..."`);
  }
  if (signers.length === 0) {
    throw new Error(`${SIGNERS_PATH} lists no maintainer key; add one through a reviewed pull request before tagging (docs/maintainers.md).`);
  }
  const problems = [];
  if (tagName !== tag) {
    problems.push(`the tag object of ${tag} names ${tagName ? tagName : 'no tag'}; a signed tag is valid only under the name it was signed with.`);
  }
  const signed = parseVerifyOutput(verify.output);
  const listed = new Set(signers.flatMap((s) => s.principals.map((p) => p.toLowerCase())));
  if (verify.status !== 0 || !signed) {
    problems.push(`the signature of ${tag} does not verify against ${SIGNERS_PATH} of the tagged commit:\n${verify.output.trim() || '(no output)'}`);
  } else if (!listed.has(signed.principal.toLowerCase())) {
    problems.push(`signer ${signed.principal} is not listed in ${SIGNERS_PATH}.`);
  }
  if (signed) {
    const principal = signed.principal.toLowerCase();
    const current = mainSigners.some(
      (s) => s.principals.some((p) => p.toLowerCase() === principal) && keyFingerprint(s.key) === signed.fingerprint,
    );
    if (!current) {
      problems.push(`signer ${signed.principal} with key ${signed.fingerprint} is not listed in ${SIGNERS_PATH} on ${mainRef}; the key was removed there or never added.`);
    }
  }
  const tagger = taggerEmail.replace(/^<(.*)>$/, '$1');
  if (signed && tagger.toLowerCase() !== signed.principal.toLowerCase()) {
    problems.push(`tagger ${tagger} is not the signer ${signed.principal}; tag with the git user.email listed for your key.`);
  }
  if (!onMain) problems.push(`the commit of ${tag} is not reachable from ${mainRef}; tag the release pull request's merge commit.`);
  if (problems.length > 0 || !signed) throw new Error(problems.join('\n'));
  return { principal: signed.principal, fingerprint: signed.fingerprint };
}

/**
 * Collects the facts with git in `cwd` and decides.
 * @param {{ tag: string, cwd?: string, mainRef?: string }} input
 */
export function verifyReleaseTag({ tag, cwd = process.cwd(), mainRef = 'origin/main' }) {
  const git = (/** @type {string[]} */ args) => spawnSync('git', args, { cwd, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  const ok = (/** @type {string[]} */ args) => {
    const r = git(args);
    if (r.status !== 0) throw new Error(`git ${args.join(' ')} failed:\n${r.stderr.trim()}`);
    return r.stdout.trim();
  };
  const ref = `refs/tags/${tag}`;
  if (git(['rev-parse', '--verify', '--quiet', ref]).status !== 0) throw new Error(`There is no tag ${tag} in the checkout.`);
  const objectType = ok(['cat-file', '-t', ref]);
  if (objectType !== 'tag') {
    return checkReleaseTag({ tag, objectType, signers: [], verify: { status: null, output: '' }, taggerEmail: '', onMain: false, mainSigners: [], mainRef });
  }
  const tagName = parseTagName(ok(['cat-file', 'tag', ref]));

  const signersText = ok(['show', `${ref}^{commit}:${SIGNERS_PATH}`]);
  const signers = parseAllowedSigners(signersText);
  ok(['rev-parse', '--verify', `${mainRef}^{commit}`]);
  let mainSigners;
  try {
    mainSigners = parseAllowedSigners(ok(['show', `${mainRef}:${SIGNERS_PATH}`]));
  } catch (error) {
    throw new Error(`${mainRef}: ${/** @type {Error} */ (error).message}`);
  }
  const dir = mkdtempSync(join(process.env.RUNNER_TEMP ?? tmpdir(), 'allowed-signers-'));
  try {
    const file = join(dir, 'allowed_signers');
    writeFileSync(file, `${signersText}\n`);
    const v = git(['-c', 'gpg.format=ssh', '-c', 'gpg.ssh.program=ssh-keygen', '-c', `gpg.ssh.allowedSignersFile=${file}`, 'verify-tag', '--raw', ref]);
    const verify = { status: v.status, output: `${v.stdout}${v.stderr}` };
    const taggerEmail = ok(['for-each-ref', '--format=%(taggeremail)', ref]);
    const ancestor = git(['merge-base', '--is-ancestor', `${ref}^{commit}`, mainRef]);
    if (ancestor.status !== 0 && ancestor.status !== 1) throw new Error(`git merge-base failed:\n${ancestor.stderr.trim()}`);
    return checkReleaseTag({ tag, objectType, signers, verify, taggerEmail, onMain: ancestor.status === 0, tagName, mainSigners, mainRef });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function main() {
  const args = process.argv.slice(2);
  const arg = (/** @type {string} */ name) => {
    const i = args.indexOf(name);
    return i >= 0 ? args[i + 1] : undefined;
  };
  const tag = arg('--tag');
  if (!tag) throw new Error('usage: verify-release-tag.mjs --tag <tag> [--main-ref origin/main]');
  const mainRef = arg('--main-ref') ?? 'origin/main';
  const { principal, fingerprint } = verifyReleaseTag({ tag, mainRef });
  console.log(`${tag}: annotated, signed by ${principal} (${fingerprint}) as listed in ${SIGNERS_PATH} of the tag and of ${mainRef}, on ${mainRef}`);
}

if (import.meta.main) await runMain(main);
