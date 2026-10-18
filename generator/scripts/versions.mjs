// Version and tag grammar shared by the release check and the upstream sync scripts (design part 3 §8.1).
// Upstream versions are plain X.Y.Z (the bot ignores upstream prereleases); release tags are
// <prefix>X.Y.Z.OURS[-preview.N] with OURS >= 1 and N >= 1, no leading zeros (NuGet would normalise them away).

const NUMBER = '(0|[1-9]\\d*)';
const POSITIVE = '([1-9]\\d*)';
const UPSTREAM = new RegExp(`^${NUMBER}\\.${NUMBER}\\.${NUMBER}$`);
const RELEASE = new RegExp(`^(${NUMBER}\\.${NUMBER}\\.${NUMBER})\\.${POSITIVE}(?:-preview\\.${POSITIVE})?$`);

/** @param {string} version */
export function isUpstreamVersion(version) {
  return UPSTREAM.test(version);
}

/** @param {string} version @returns {number[]} */
function parts(version) {
  if (!isUpstreamVersion(version)) throw new Error(`"${version}" is not an X.Y.Z version.`);
  return version.split('.').map(Number);
}

/** Semver precedence of two X.Y.Z versions: negative, zero or positive. @param {string} a @param {string} b */
export function compareVersions(a, b) {
  const x = parts(a);
  const y = parts(b);
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i];
  return 0;
}

/** @param {string} from @param {string} to @returns {'major' | 'minor' | 'patch'} */
export function bumpKind(from, to) {
  const [a, b] = [parts(from), parts(to)];
  if (a[0] !== b[0]) return 'major';
  return a[1] !== b[1] ? 'minor' : 'patch';
}

/**
 * @typedef {{ version: string, upstream: string, ours: number, preview: number | null, prerelease: boolean }} ReleaseTag
 * @param {string} tag
 * @param {string} prefix
 * @returns {ReleaseTag}
 */
export function parseReleaseTag(tag, prefix) {
  const version = tag.startsWith(prefix) ? tag.slice(prefix.length) : undefined;
  const m = version === undefined ? null : RELEASE.exec(version);
  if (!m || version === undefined) {
    throw new Error(`tag ${JSON.stringify(tag)} does not match ${prefix}FLINT.MAJOR.FLINT.MINOR.FLINT.PATCH.OURS[-preview.N] (OURS and N start at 1).`);
  }
  const preview = m[6] === undefined ? null : Number(m[6]);
  return { version, upstream: m[1], ours: Number(m[5]), preview, prerelease: preview !== null };
}
