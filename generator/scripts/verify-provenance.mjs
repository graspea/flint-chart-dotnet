// Provenance identity check of the upstream gate (companion part 1, End-to-end flow step 2.1; design §7.2).
//
//   node scripts/verify-provenance.mjs <version>
//
// Runs after `npm audit signatures` has verified the registry signature and the Sigstore bundle, so the claims read
// here are verified text. Requires the SLSA provenance of flint-chart@<version> to name the repository
// microsoft/flint-chart, the workflow .github/workflows/npm-publish.yml, the packument's gitHead as gitCommit, and the
// package and tarball digest of the packument. The ref is refs/heads/main for every release, so no tag ref is checked.
import { fetchJson, runMain } from './gha.mjs';
import { isUpstreamVersion } from './versions.mjs';

const PACKAGE = 'flint-chart';
const REPOSITORY = 'https://github.com/microsoft/flint-chart';
const WORKFLOW = '.github/workflows/npm-publish.yml';
const SLSA_V1 = 'https://slsa.dev/provenance/v1';

/**
 * @param {{ attestations: { attestations: { predicateType: string, bundle: { dsseEnvelope: { payload: string } } }[] },
 *   version: string, gitHead: string, integrity: string }} input
 */
export function checkProvenance({ attestations, version, gitHead, integrity }) {
  const slsa = attestations.attestations.find((a) => a.predicateType === SLSA_V1);
  if (!slsa) throw new Error(`${PACKAGE}@${version} has no SLSA provenance attestation.`);
  const statement = JSON.parse(Buffer.from(slsa.bundle.dsseEnvelope.payload, 'base64').toString('utf8'));

  const problems = [];
  const subject = statement.subject?.[0];
  if (subject?.name !== `pkg:npm/${PACKAGE}@${version}`) problems.push(`subject is ${subject?.name}, expected pkg:npm/${PACKAGE}@${version}`);
  const expectedDigest = integrity.startsWith('sha512-') ? Buffer.from(integrity.slice(7), 'base64').toString('hex') : undefined;
  if (!expectedDigest || subject?.digest?.sha512 !== expectedDigest) problems.push('subject digest differs from the packument integrity');
  const workflow = statement.predicate?.buildDefinition?.externalParameters?.workflow ?? {};
  if (workflow.repository !== REPOSITORY) problems.push(`repository is ${workflow.repository}, expected ${REPOSITORY}`);
  if (workflow.path !== WORKFLOW) problems.push(`workflow is ${workflow.path}, expected ${WORKFLOW}`);
  const gitCommit = statement.predicate?.buildDefinition?.resolvedDependencies?.[0]?.digest?.gitCommit;
  if (gitCommit !== gitHead) problems.push(`gitCommit is ${gitCommit}, the packument gitHead is ${gitHead}`);
  if (problems.length > 0) throw new Error(`Provenance of ${PACKAGE}@${version} rejected: ${problems.join('; ')}.`);

  return { repository: workflow.repository, workflow: workflow.path, gitCommit, run: statement.predicate?.runDetails?.metadata?.invocationId };
}

/** Fetches the packument entry and the attestations of a version and checks them. @param {string} version */
export async function verifyProvenance(version) {
  if (!isUpstreamVersion(version)) throw new Error(`"${version}" is not an X.Y.Z version.`);
  const manifest = await fetchJson(`https://registry.npmjs.org/${PACKAGE}/${version}`);
  const attestations = await fetchJson(`https://registry.npmjs.org/-/npm/v1/attestations/${PACKAGE}@${version}`);
  return checkProvenance({ attestations, version, gitHead: manifest.gitHead, integrity: manifest.dist.integrity });
}

if (import.meta.main) {
  await runMain(async () => {
    const version = process.argv[2] ?? '';
    const result = await verifyProvenance(version);
    console.log(`provenance: ${PACKAGE}@${version} built by ${result.repository} ${result.workflow} at ${result.gitCommit} (${result.run})`);
  });
}
