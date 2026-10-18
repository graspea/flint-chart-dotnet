import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { mcpPinText, parseLsRemoteTags, resolveMcpVersion, upstreamRef } from '../scripts/install-upstream.mjs';
import { checkProvenance } from '../scripts/verify-provenance.mjs';

const GIT_HEAD = '34ef4516554b323a740a426bd1a1e6ba31ee8245';
const DIGEST = 'd895adc0cfeb8fce925c665880db05cf26bf554cb94ac1b85fe9c478656776bf3c32663ec8dd330028b5328b47392a59f4c658f51d03ff14221924ee8509e0bd';
const INTEGRITY = `sha512-${Buffer.from(DIGEST, 'hex').toString('base64')}`;

/** The SLSA v1 provenance statement npm recorded for flint-chart@0.5.1 (registry attestations endpoint, 2026-10-17). */
function statement() {
  return {
    _type: 'https://in-toto.io/Statement/v1',
    subject: [{ name: 'pkg:npm/flint-chart@0.5.1', digest: { sha512: DIGEST } }],
    predicateType: 'https://slsa.dev/provenance/v1',
    predicate: {
      buildDefinition: {
        externalParameters: {
          workflow: { ref: 'refs/heads/main', repository: 'https://github.com/microsoft/flint-chart', path: '.github/workflows/npm-publish.yml' },
        },
        resolvedDependencies: [{ uri: 'git+https://github.com/microsoft/flint-chart@refs/heads/main', digest: { gitCommit: GIT_HEAD } }],
      },
      runDetails: { metadata: { invocationId: 'https://github.com/microsoft/flint-chart/actions/runs/31762121781/attempts/1' } },
    },
  };
}

function attestations(s: unknown = statement()) {
  const payload = (x: unknown) => Buffer.from(JSON.stringify(x)).toString('base64');
  return {
    attestations: [
      { predicateType: 'https://github.com/npm/attestation/tree/main/specs/publish/v0.1', bundle: { dsseEnvelope: { payload: payload({ predicate: {} }) } } },
      { predicateType: 'https://slsa.dev/provenance/v1', bundle: { dsseEnvelope: { payload: payload(s) } } },
    ],
  };
}

const input = { version: '0.5.1', gitHead: GIT_HEAD, integrity: INTEGRITY };

describe('verify-provenance', () => {
  // source: companion part 1, End-to-end flow step 2.1 (repository microsoft/flint-chart, workflow
  // .github/workflows/npm-publish.yml, gitCommit equal to the packument gitHead; the ref is refs/heads/main, not a tag)
  it('accepts the recorded 0.5.1 provenance', () => {
    assert.deepEqual(checkProvenance({ ...input, attestations: attestations() }), {
      repository: 'https://github.com/microsoft/flint-chart',
      workflow: '.github/workflows/npm-publish.yml',
      gitCommit: GIT_HEAD,
      run: 'https://github.com/microsoft/flint-chart/actions/runs/31762121781/attempts/1',
    });
  });

  const tamper = (edit: (s: ReturnType<typeof statement>) => void) => {
    const s = statement();
    edit(s);
    return attestations(s);
  };

  const cases: [string, ReturnType<typeof attestations>, RegExp][] = [
    ['another repository', tamper((s) => (s.predicate.buildDefinition.externalParameters.workflow.repository = 'https://github.com/someone/flint-chart')), /repository/],
    ['another workflow', tamper((s) => (s.predicate.buildDefinition.externalParameters.workflow.path = '.github/workflows/other.yml')), /workflow/],
    ['another commit', tamper((s) => (s.predicate.buildDefinition.resolvedDependencies[0].digest.gitCommit = 'f'.repeat(40))), /gitCommit/],
    ['another package', tamper((s) => (s.subject[0].name = 'pkg:npm/flint-chart@0.5.0')), /subject/],
    ['another tarball', tamper((s) => (s.subject[0].digest.sha512 = '0'.repeat(128))), /digest/],
    ['no SLSA provenance', { attestations: [attestations().attestations[0]] }, /no SLSA provenance/],
  ];
  for (const [name, att, message] of cases) {
    it(`rejects ${name}`, () => {
      assert.throws(() => checkProvenance({ ...input, attestations: att }), message);
    });
  }
});

describe('install-upstream', () => {
  // source: companion part 2 §5 (flint-chart-mcp@<ver> when it exists, else the newest version not above the pin)
  it('resolves the flint-chart-mcp version for the pin', () => {
    const versions = ['0.4.0', '0.5.0', '0.5.1', '0.6.0-beta.1', '0.7.0'];
    assert.equal(resolveMcpVersion('0.5.1', versions), '0.5.1');
    assert.equal(resolveMcpVersion('0.6.0', versions), '0.5.1');
    assert.equal(resolveMcpVersion('0.4.5', versions), '0.4.0');
    assert.throws(() => resolveMcpVersion('0.3.0', versions), /no flint-chart-mcp version at or below 0\.3\.0/);
  });

  // source: verify-external.md (upstream tags 0.5.1, 0.5, 0.4.0, 0.3.0, 0.2.1, 0.1.1; fall back to the gitHead commit)
  it('uses the upstream tag equal to the version, else the gitHead commit', () => {
    const tags = parseLsRemoteTags(
      `${'a'.repeat(40)}\trefs/tags/0.5\n${'b'.repeat(40)}\trefs/tags/0.5.1\n${'c'.repeat(40)}\trefs/tags/0.5.1^{}\n${'d'.repeat(40)}\trefs/tags/0.4.0\n`,
    );
    assert.deepEqual(tags, ['0.4.0', '0.5', '0.5.1']);
    assert.equal(upstreamRef('0.5.1', tags, GIT_HEAD), '0.5.1');
    assert.equal(upstreamRef('0.5.0', tags, '84a8e8d044f47440e08da2059fc5d48935600da0'), '84a8e8d044f47440e08da2059fc5d48935600da0');
    assert.throws(() => upstreamRef('0.5.0', tags, 'main'), /gitHead/);
  });

  // source: docs/generator.md, Documentation (generator/mcp-pin.json shape)
  it('writes generator/mcp-pin.json canonically', () => {
    assert.equal(
      mcpPinText({ 'flint-chart': '0.5.1', 'flint-chart-mcp': '0.5.1', upstreamRef: '0.5.1' }),
      '{\n  "flint-chart": "0.5.1",\n  "flint-chart-mcp": "0.5.1",\n  "upstreamRef": "0.5.1"\n}\n',
    );
  });
});
