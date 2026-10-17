// Release guard and notes for release.yml (design part 3 §9.6 step 4; companion part 3 for the title and body).
//
//   node generator/scripts/release-check.mjs --tag <tag> [--notes-dir <dir>]
//
// Fails unless the tag matches the version grammar, its first three parts equal FlintUpstream.Version and the
// generator pin, and CHANGELOG.md has a non-empty entry for the version. Writes version, upstream, prerelease and latest
// (the tag is the highest stable release among the repository's tags) to $GITHUB_OUTPUT when set, and title.txt and
// notes.md to --notes-dir.
import { execFileSync } from 'node:child_process';
import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { compareVersions, parseReleaseTag } from './versions.mjs';

const UPSTREAM_REPO = 'https://github.com/microsoft/flint-chart';

/** The value of the single `TAG_PREFIX:` env line in release.yml. @param {string} releaseYml */
export function readTagPrefix(releaseYml) {
  const m = /^\s+TAG_PREFIX:\s*(['"]?)([^'"\s#]*)\1\s*(#.*)?$/m.exec(releaseYml);
  if (!m) throw new Error('release.yml has no TAG_PREFIX env line.');
  return m[2];
}

/** @param {string} csharp the text of Generated/Constants/FlintUpstream.g.cs */
export function readFlintUpstreamVersion(csharp) {
  const m = /public const string Version = "([^"]+)";/.exec(csharp);
  if (!m) throw new Error('FlintUpstream.Version not found in the generated C#.');
  return m[1];
}

/**
 * @param {{ tag: string, prefix: string, flintUpstream: string, generatorPin: string }} input
 * @returns {{ version: string, upstream: string, prerelease: boolean }}
 */
export function checkRelease({ tag, prefix, flintUpstream, generatorPin }) {
  const parsed = parseReleaseTag(tag, prefix);
  const problems = [];
  if (parsed.upstream !== flintUpstream) {
    problems.push(`tag ${tag} is for flint-chart ${parsed.upstream}, but FlintUpstream.Version is ${flintUpstream}.`);
  }
  if (generatorPin !== flintUpstream) {
    problems.push(`generator/package.json pins flint-chart ${generatorPin}, but FlintUpstream.Version is ${flintUpstream}.`);
  }
  if (problems.length > 0) throw new Error(problems.join('\n'));
  return { version: parsed.version, upstream: parsed.upstream, prerelease: parsed.prerelease };
}

/**
 * Whether a stable tag is at least as high as every stable release tag, so its GitHub release may become Latest. A fix
 * released on an older line is not. A prerelease is never Latest.
 * @param {string} tag @param {string[]} tags @param {string} prefix
 */
export function isLatestStable(tag, tags, prefix) {
  const own = parseReleaseTag(tag, prefix);
  if (own.prerelease) return false;
  return tags.every((other) => {
    let parsed;
    try {
      parsed = parseReleaseTag(other, prefix);
    } catch {
      return true;
    }
    if (parsed.prerelease) return true;
    const byUpstream = compareVersions(parsed.upstream, own.upstream);
    return byUpstream < 0 || (byUpstream === 0 && parsed.ours <= own.ours);
  });
}

/** The body of the `## [<version>]` entry of a Keep a Changelog file, without its heading. @param {string} changelog @param {string} version */
export function changelogSection(changelog, version) {
  const lines = changelog.split(/\r?\n/);
  const start = lines.findIndex((l) => l.startsWith(`## [${version}]`));
  if (start < 0) throw new Error(`CHANGELOG.md has no "## [${version}]" entry; the release pull request adds it.`);
  const rest = lines.slice(start + 1);
  const end = rest.findIndex((l) => l.startsWith('## ') || /^\[[^\]]+\]:\s/.test(l));
  const body = (end < 0 ? rest : rest.slice(0, end)).join('\n').trim();
  if (body === '') throw new Error(`The CHANGELOG.md entry for ${version} is empty.`);
  return body;
}

/** The upstream page for a pinned version: its release when tagged, else the commit tree. @param {string} version @param {string} ref */
export function upstreamUrl(version, ref) {
  return ref === version ? `${UPSTREAM_REPO}/releases/tag/${version}` : `${UPSTREAM_REPO}/tree/${ref}`;
}

/** @param {{ version: string, upstream: string, upstreamRef: string, entry: string }} input */
export function releaseNotes({ version, upstream, upstreamRef, entry }) {
  return {
    title: `Flint.NET ${version} (flint-chart ${upstream})`,
    body: `Tracks [flint-chart ${upstream}](${upstreamUrl(upstream, upstreamRef)}) (npm).\n\n${entry}\n`,
  };
}

function main() {
  const args = process.argv.slice(2);
  const arg = (/** @type {string} */ name) => {
    const i = args.indexOf(name);
    return i >= 0 ? args[i + 1] : undefined;
  };
  const tag = arg('--tag');
  if (!tag) throw new Error('usage: release-check.mjs --tag <tag> [--notes-dir <dir>]');
  const root = new URL('../../', import.meta.url);
  const read = (/** @type {string} */ path) => readFileSync(new URL(path, root), 'utf8');

  const prefix = readTagPrefix(read('.github/workflows/release.yml'));
  const flintUpstream = readFlintUpstreamVersion(read('src/Graspea.Flint/Generated/Constants/FlintUpstream.g.cs'));
  const generatorPin = JSON.parse(read('generator/package.json')).dependencies['flint-chart'];
  const release = checkRelease({ tag, prefix, flintUpstream, generatorPin });
  const entry = changelogSection(read('CHANGELOG.md'), release.version);
  const upstreamRef = JSON.parse(read('generator/mcp-pin.json')).upstreamRef ?? release.upstream;

  const notesDir = arg('--notes-dir');
  if (notesDir) {
    const notes = releaseNotes({ version: release.version, upstream: release.upstream, upstreamRef, entry });
    mkdirSync(notesDir, { recursive: true });
    writeFileSync(join(notesDir, 'title.txt'), `${notes.title}\n`);
    writeFileSync(join(notesDir, 'notes.md'), notes.body);
  }
  const tags = execFileSync('git', ['tag', '--list'], { cwd: root, encoding: 'utf8' }).split('\n').filter(Boolean);
  const latest = isLatestStable(tag, tags, prefix);
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(
      process.env.GITHUB_OUTPUT,
      `version=${release.version}\nupstream=${release.upstream}\nprerelease=${release.prerelease}\nlatest=${latest}\n`,
    );
  }
  console.log(`release ${release.version}: flint-chart ${release.upstream}, ${release.prerelease ? 'prerelease' : 'stable'}`);
}

if (import.meta.main) {
  try {
    main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  }
}
