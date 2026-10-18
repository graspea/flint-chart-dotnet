import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { emitDocs, packageAgents, readmeSections, spliceSections, type McpPin } from '../src/emit/docs.ts';
import { pascal } from '../src/naming.ts';
import { emptyOverrides, loadOverrides } from '../src/overrides.ts';
import type { Ir } from '../src/ir.ts';
import { syntheticIr } from './support/synthetic-ir.ts';

const pin: McpPin = { 'flint-chart': '0.5.1', 'flint-chart-mcp': '0.5.1' };
const fence = '```';

describe('emit/docs', () => {
  // source: design §7.4 docs row (Generated/catalog.json is the projection of IR `catalog`) and companion part 2 §4
  // generator tests ("catalog.json equals the IR catalog projection")
  it('writes catalog.json as the IR catalog projection, canonically', () => {
    const ir = syntheticIr();
    const result = emitDocs(ir);
    const file = result.files.find((f) => f.path === 'catalog.json');
    assert.ok(file);
    assert.deepEqual(JSON.parse(file.content), ir.catalog);
    assert.ok(file.content.startsWith('{\n  "backends": [\n    "vegalite",\n    "excel"\n  ],\n'));
    assert.ok(file.content.endsWith('}\n'));
    assert.deepEqual(result.apiLines, []);
  });

  // source: companion part 2 §5 (section ids chart-catalog, semantic-types, upstream-version, mcp-commands,
  // agent-identifiers) and part 3 (the flint-chart badge version is generator-owned, the NuGet badge is not)
  it('renders one body per generated README section', () => {
    assert.deepEqual(Object.keys(readmeSections(syntheticIr(), emptyOverrides(), pin)).sort(), [
      'agent-identifiers',
      'chart-catalog',
      'mcp-commands',
      'semantic-types',
      'upstream-badge',
      'upstream-version',
    ]);
  });

  // source: companion part 2 §1 (table: chartType | backends | channels | notable chartProperties), design §7.6 (constant
  // and entry names) and the validation codes unsupported_channel / unknown_chart_property (docs/validation-codes.md)
  it('renders the chart catalog table from the IR', () => {
    assert.equal(
      readmeSections(syntheticIr(), emptyOverrides(), pin)['chart-catalog'],
      'flint-chart 0.5.1 declares 1 chart type. Backend ids map to `FlintBackend`: `vegalite` = `FlintBackend.VegaLite`, ' +
        '`excel` = `FlintBackend.Excel`. Channels and properties are wire names, the union over the backends of the template; ' +
        '`Validate(backend)` reports one that the target backend does not declare (`unsupported_channel`, `unknown_chart_property`).\n' +
        '\n' +
        '| Chart type | Constant and entry | Backends | Channels | Properties |\n' +
        '|---|---|---|---|---|\n' +
        '| Line Chart | `ChartType.LineChart`, `Chart.Line` | vegalite, excel | x, y, color | interpolate, showPoints, strokeWidth |\n',
    );
  });

  // source: design §7.6 rule 6 (renames["catalog.semanticTypes[Name]"] = NameType: the constant differs from the wire name)
  // and the IR semantic type groups (t0 / t1)
  it('renders the semantic types grouped by t0 and t1 and notes a renamed constant', () => {
    const ir = syntheticIr();
    ir.catalog.semanticTypes.push({ name: 'Name', t0: 'Categorical', t1: 'Entity', visEncodings: ['nominal'] });
    const overrides = { ...emptyOverrides(), renames: { 'catalog.semanticTypes[Name]': 'NameType' } };
    assert.equal(
      readmeSections(ir, overrides, pin)['semantic-types'],
      'flint-chart 0.5.1 registers 2 semantic types. Annotate a field with the `SemanticType` constant of the same name ' +
        '(`SemanticType.YearMonth`); a different constant name is given in brackets.\n' +
        '\n' +
        '| Group | Semantic types |\n' +
        '|---|---|\n' +
        '| Categorical / Entity | Name (`NameType`) |\n' +
        '| Temporal / DateGranule | YearMonth |\n',
    );
  });

  // source: companion part 3 (README title block: the flint-chart badge links the upstream release; the "tracks" sentence)
  it('renders the flint-chart badge and the tracks sentence from the IR version', () => {
    const sections = readmeSections(syntheticIr(), emptyOverrides(), pin);
    assert.equal(
      sections['upstream-badge'],
      '[![flint-chart](https://img.shields.io/badge/flint--chart-0.5.1-blue)](https://github.com/microsoft/flint-chart/releases/tag/0.5.1)\n' +
        '[![CI](https://github.com/graspea/flint-chart-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/graspea/flint-chart-dotnet/actions/workflows/ci.yml)\n',
    );
    assert.equal(
      sections['upstream-version'],
      'Flint.NET 0.5.1.x tracks flint-chart 0.5.1; the first three version parts always equal the pinned upstream release.\n',
    );
  });

  // source: companion part 2 §1 and §4 (mcp.json for npx -y flint-chart-mcp@<pin> and the hosted endpoint, which runs
  // upstream's deployed version) and §5 (<pin> from generator/mcp-pin.json)
  it('renders the MCP commands with the resolved flint-chart-mcp version', () => {
    assert.equal(
      readmeSections(syntheticIr(), emptyOverrides(), pin)['mcp-commands'],
      `${fence}json\n{\n  "mcpServers": {\n    "flint-chart": {\n      "command": "npx",\n      "args": ["-y", "flint-chart-mcp@0.5.1"]\n    }\n  }\n}\n${fence}\n` +
        '\n' +
        'The hosted endpoint is `https://flint.data-formulator.ai/mcp`. It runs the version upstream has deployed, which can differ from flint-chart 0.5.1.\n',
    );
  });

  // source: companion part 2 §5 (fallback: the newest flint-chart-mcp version not above the pin, reported as such)
  it('says so when flint-chart-mcp has no release for the pinned version', () => {
    const ir = syntheticIr();
    ir.upstream.version = '0.6.0';
    const text = readmeSections(ir, emptyOverrides(), { 'flint-chart': '0.6.0', 'flint-chart-mcp': '0.5.1' })['mcp-commands'];
    assert.match(text, /"flint-chart-mcp@0\.5\.1"/);
    assert.match(text, /\nflint-chart-mcp 0\.6\.0 is not published; the snippet uses 0\.5\.1, the newest version below flint-chart 0\.6\.0\.\n/);
  });

  // source: companion part 1, "What it watches" (upstream tags cover 6 of 12 versions; a link at tag X.Y.Z falls back to
  // the gitHead commit when no tag exists); the gate script records the ref in generator/mcp-pin.json
  it('links the badge and the skill at the gitHead commit when the version has no upstream tag', () => {
    const sha = '34ef4516554b323a740a426bd1a1e6ba31ee8245';
    const sections = readmeSections(syntheticIr(), emptyOverrides(), { ...pin, upstreamRef: sha });
    assert.match(sections['upstream-badge'], new RegExp(`\\]\\(https://github\\.com/microsoft/flint-chart/tree/${sha}\\)\n`));
    assert.match(sections['agent-identifiers'], new RegExp(`https://github\\.com/microsoft/flint-chart/blob/${sha}/agent-skills/flint-chart-author/SKILL\\.md\n`));
    const tagged = readmeSections(syntheticIr(), emptyOverrides(), { ...pin, upstreamRef: '0.5.1' });
    assert.match(tagged['upstream-badge'], /\]\(https:\/\/github\.com\/microsoft\/flint-chart\/releases\/tag\/0\.5\.1\)\n/);
  });

  // source: companion part 2 §5 (generator/mcp-pin.json is written by the gate script for the installed pin)
  it('fails when generator/mcp-pin.json was resolved for another flint-chart version', () => {
    assert.throws(
      () => readmeSections(syntheticIr(), emptyOverrides(), { 'flint-chart': '0.5.0', 'flint-chart-mcp': '0.5.0' }),
      /generator\/mcp-pin\.json was resolved for flint-chart 0\.5\.0, the IR is 0\.5\.1/,
    );
  });

  // source: companion part 2 §1a (identifiers are the real ones from the IR) and §3 (the upstream skill is linked at the
  // pinned tag, never vendored)
  it('renders the agent identifiers with the real names and the skill link at the pinned tag', () => {
    assert.equal(
      readmeSections(syntheticIr(), emptyOverrides(), pin)['agent-identifiers'],
      '### Identifiers (flint-chart 0.5.1)\n' +
        '\n' +
        '- Backends: `FlintBackend.VegaLite`, `FlintBackend.Excel`.\n' +
        '- Chart types: 1 `ChartType` constant, each with a fluent entry (`ChartType.LineChart` and `Chart.Line(rows)`); see the chart catalog.\n' +
        '- Channels: `Channel.X`, `Channel.Y`, `Channel.Color`.\n' +
        '- Semantic types: 1 `SemanticType` constant (`SemanticType.YearMonth`); see the semantic types.\n' +
        '- Theme presets: `ThemePreset.PowerbiLight`.\n' +
        '- Upstream MCP server: `npx -y flint-chart-mcp@0.5.1`, tool `validate_chart` (vegalite, echarts and chartjs).\n' +
        '- Upstream skill for the JSON format: https://github.com/microsoft/flint-chart/blob/0.5.1/agent-skills/flint-chart-author/SKILL.md\n' +
        '- Machine-readable catalog: `catalog/catalog.json` in the package, generated from the same data.\n',
    );
  });

  // source: companion part 2 §2 and §5 (the generator splices between <!-- flint:generated:<id> start/end --> and owns
  // nothing outside; a missing or duplicated marker is an error, not a silent skip)
  describe('spliceSections', () => {
    const start = (id: string) => `<!-- flint:generated:${id} start -->`;
    const end = (id: string) => `<!-- flint:generated:${id} end -->`;

    it('replaces the text between the markers and keeps everything else', () => {
      const text = `# T\n\n${start('a')}\nold\nlines\n${end('a')}\n\nhand ${fence}x${fence}\n${start('b')}\n${end('b')}\n`;
      const spliced = spliceSections(text, { a: 'new\n', b: 'B\n' }, 'README.md');
      assert.equal(spliced, `# T\n\n${start('a')}\nnew\n${end('a')}\n\nhand ${fence}x${fence}\n${start('b')}\nB\n${end('b')}\n`);
      assert.equal(spliceSections(spliced, { a: 'new\n', b: 'B\n' }, 'README.md'), spliced);
    });

    it('fails on a missing, duplicated, unknown or reversed marker', () => {
      assert.throws(() => spliceSections(`${start('a')}\n${end('a')}\n`, { a: '', b: '' }, 'README.md'), /README\.md: generated section "b" has no start marker/);
      assert.throws(() => spliceSections(`${start('a')}\n`, { a: '' }, 'README.md'), /README\.md: generated section "a" has no end marker/);
      assert.throws(
        () => spliceSections(`${start('a')}\n${end('a')}\n${start('a')}\n${end('a')}\n`, { a: '' }, 'README.md'),
        /README\.md: generated section "a" has 2 start markers/,
      );
      assert.throws(() => spliceSections(`${start('c')}\n${end('c')}\n`, {}, 'README.md'), /README\.md: unknown generated section "c"/);
      assert.throws(() => spliceSections(`${end('a')}\n${start('a')}\n`, { a: '' }, 'README.md'), /README\.md: generated section "a" ends before it starts/);
    });
  });

  // source: companion part 2 §3 (package AGENTS.md: a "generated; edit README.md" header, the README "For AI agents"
  // section copied verbatim, then the generated catalog table; no markers)
  it('writes the package AGENTS.md from the README "For AI agents" section and the catalog', () => {
    const readme =
      '# Title\n\nIntro.\n\n## For AI agents\n\n### Rules\n\n1. Rule.\n\n' +
      `${fence}csharp\n## not a heading\n${fence}\n\n` +
      '<!-- flint:generated:agent-identifiers start -->\nids\n<!-- flint:generated:agent-identifiers end -->\n\n## Next\n\nTail.\n';
    const sections = readmeSections(syntheticIr(), emptyOverrides(), pin);
    assert.equal(
      packageAgents(readme, syntheticIr(), emptyOverrides(), pin),
      '# Graspea.Flint for AI agents\n' +
        '\n' +
        '> Generated from the "For AI agents" section of README.md and the flint-chart 0.5.1 catalog; do not edit this file. ' +
        'Source: https://github.com/graspea/flint-chart-dotnet\n' +
        '\n' +
        '## For AI agents\n\n### Rules\n\n1. Rule.\n\n' +
        `${fence}csharp\n## not a heading\n${fence}\n\n` +
        'ids\n' +
        '\n' +
        '## Chart catalog (flint-chart 0.5.1)\n\n' +
        sections['chart-catalog'] +
        '\n' +
        '## Semantic types (flint-chart 0.5.1)\n\n' +
        sections['semantic-types'],
    );
    assert.throws(() => packageAgents('# Title\n', syntheticIr(), emptyOverrides(), pin), /README\.md has no "## For AI agents" section/);
  });

  // source: companion part 2 §5 generator tests ("every chart type constant appears in the README table and vice versa"),
  // against the committed README and IR
  it('lists every chart type constant of the committed IR in the committed README table, and nothing else', () => {
    const ir = JSON.parse(readFileSync(new URL('../ir/flint-ir.json', import.meta.url), 'utf8')) as Ir;
    const renames = loadOverrides(new URL('../overrides.json', import.meta.url).pathname).renames;
    const readme = readFileSync(new URL('../../README.md', import.meta.url), 'utf8');
    const table = readme.slice(readme.indexOf('<!-- flint:generated:chart-catalog start -->'), readme.indexOf('<!-- flint:generated:chart-catalog end -->'));
    const inReadme = [...table.matchAll(/`ChartType\.(\w+)`/g)].map((m) => m[1]).sort();
    const inIr = ir.catalog.chartTypes.map((t) => renames[`catalog.chartTypes[${t.name}]`] ?? pascal(t.name)).sort();
    assert.equal(inIr.length, 48);
    assert.deepEqual(inReadme, inIr);
  });
});
