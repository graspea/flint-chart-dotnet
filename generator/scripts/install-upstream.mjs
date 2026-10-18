// The upstream gate (design §7.2, §9.2; companion part 1, End-to-end flow step 2.1), shared by the bot and the manual
// path. Run from generator/:
//
//   npm run install-upstream -- <version>
//
// In this order, stopping at the first failure: install flint-chart@<version> exact with --ignore-scripts (no package
// code runs), `npm audit signatures` (registry signature and Sigstore provenance bundle), the provenance identity
// check (verify-provenance.mjs), then resolve the flint-chart-mcp version and the upstream git ref into
// generator/mcp-pin.json so that `npm run generate` stays offline.
import { writeFileSync } from 'node:fs';
import { fetchJson, run, runMain } from './gha.mjs';
import { verifyProvenance } from './verify-provenance.mjs';
import { compareVersions, isUpstreamVersion } from './versions.mjs';

const UPSTREAM_GIT = 'https://github.com/microsoft/flint-chart';

/** flint-chart-mcp@<pin> when published, else the newest stable version below the pin. @param {string} pin @param {string[]} versions */
export function resolveMcpVersion(pin, versions) {
  const eligible = versions.filter((v) => isUpstreamVersion(v) && compareVersions(v, pin) <= 0).sort(compareVersions);
  const resolved = eligible.at(-1);
  if (!resolved) throw new Error(`There is no flint-chart-mcp version at or below ${pin}.`);
  return resolved;
}

/** Tag names from `git ls-remote --tags` output, without peeled entries. @param {string} text */
export function parseLsRemoteTags(text) {
  return text
    .split('\n')
    .map((line) => /^[0-9a-f]{40}\trefs\/tags\/(.+)$/.exec(line)?.[1])
    .filter((/** @type {string | undefined} */ tag) => tag !== undefined && !tag.endsWith('^{}'))
    .map((tag) => /** @type {string} */ (tag))
    .sort();
}

/** The ref for upstream links: the tag equal to the version, else the gitHead commit. @param {string} version @param {string[]} tags @param {string} gitHead */
export function upstreamRef(version, tags, gitHead) {
  if (tags.includes(version)) return version;
  if (!/^[0-9a-f]{40}$/.test(gitHead)) throw new Error(`flint-chart@${version} has no upstream tag and no valid gitHead (${gitHead}).`);
  return gitHead;
}

/** @param {{ 'flint-chart': string, 'flint-chart-mcp': string, upstreamRef: string }} pin */
export function mcpPinText(pin) {
  const ordered = { 'flint-chart': pin['flint-chart'], 'flint-chart-mcp': pin['flint-chart-mcp'], upstreamRef: pin.upstreamRef };
  return `${JSON.stringify(ordered, null, 2)}\n`;
}

async function main() {
  const version = process.argv[2] ?? '';
  if (!isUpstreamVersion(version)) throw new Error('usage: npm run install-upstream -- <X.Y.Z>');
  const generatorDir = new URL('../', import.meta.url);
  const npm = (/** @type {string[]} */ args) => run('npm', args, { cwd: generatorDir });

  console.log(`install-upstream: npm install --save-exact --ignore-scripts flint-chart@${version}`);
  npm(['install', '--save-exact', '--ignore-scripts', `flint-chart@${version}`]);
  console.log('install-upstream: npm audit signatures');
  process.stdout.write(npm(['audit', 'signatures']));
  const provenance = await verifyProvenance(version);
  console.log(`install-upstream: provenance ${provenance.workflow} at ${provenance.gitCommit}`);

  const mcpVersions = JSON.parse(npm(['view', 'flint-chart-mcp', 'versions', '--json']));
  const mcp = resolveMcpVersion(version, Array.isArray(mcpVersions) ? mcpVersions : [mcpVersions]);
  const tags = parseLsRemoteTags(run('git', ['ls-remote', '--tags', UPSTREAM_GIT]));
  const manifest = await fetchJson(`https://registry.npmjs.org/flint-chart/${version}`);
  const ref = upstreamRef(version, tags, manifest.gitHead);
  writeFileSync(new URL('mcp-pin.json', generatorDir), mcpPinText({ 'flint-chart': version, 'flint-chart-mcp': mcp, upstreamRef: ref }));
  console.log(`install-upstream: flint-chart ${version}, flint-chart-mcp ${mcp}${mcp === version ? '' : ' (fallback)'}, upstream ref ${ref}`);
}

if (import.meta.main) await runMain(main);
