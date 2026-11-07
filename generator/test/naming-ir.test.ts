import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { catalogNames, pascal } from '../src/naming.ts';
import { catalogConstantNames } from '../src/emit/constants.ts';
import { fluentModel } from '../src/emit/fluent.ts';
import { loadOverrides } from '../src/overrides.ts';
import type { Ir } from '../src/ir.ts';

// Runs the naming rules against the committed IR (design §10.1: the committed IR is an admissible source).
describe('naming on the committed IR', () => {
  const ir = JSON.parse(readFileSync(new URL('../ir/flint-ir.json', import.meta.url), 'utf8')) as Ir;
  const names = catalogNames(ir.catalog, loadOverrides(new URL('../overrides.json', import.meta.url).pathname).renames);

  // source: ir/flint-ir.json catalog.chartTypes (one entry per chart type; 48 on 0.5.1, design §2) and §7.6 rule 6 (two
  // entries equal after stripping fail)
  it('derives one distinct entry name per chart type', () => {
    assert.equal(names.entries.size, ir.catalog.chartTypes.length);
    assert.equal(new Set(names.entries.values()).size, ir.catalog.chartTypes.length);
  });

  // source: design §4.2 collision rule (a property named like a channel of its template gets Value; on 0.5.1 OpacityValue
  // on Scatter Plot, Area Chart and Bubble Chart only, and no reserved core name is hit) and ir/flint-ir.json (the
  // channels and properties of each template)
  it('renames exactly the properties that collide with a channel of their template', () => {
    const renamed: string[] = [];
    for (const [chartType, members] of names.members) {
      for (const [raw, name] of [...members.channels, ...members.properties]) {
        if (name !== pascal(raw)) renamed.push(`${chartType}/${raw} -> ${name}`);
      }
    }
    const colliding = ir.catalog.chartTypes.flatMap((t) => {
      const backends = Object.values(t.backends);
      const channels = new Set(backends.flatMap((b) => b.channels.map(pascal)));
      const keys = [...new Set(backends.flatMap((b) => b.properties.map((p) => p.key)))];
      return keys.filter((k) => channels.has(pascal(k))).map((k) => `${t.name}/${k} -> ${pascal(k)}Value`);
    });
    assert.deepEqual(renamed.sort(), colliding.sort());
    assert.ok(renamed.includes('Scatter Plot/opacity -> OpacityValue'));
  });

  // source: design §3 and §7.4 (one enum per distinct template-and-key discrete pair; 46 pairs on 0.5.1), ir/flint-ir.json
  // (the discrete pairs of the pinned version) and overrides.json (Radar Chart filled, binary on vegalite and discrete
  // elsewhere, maps to JsonNode, so it has no enum)
  it('derives one enum per discrete pair except the JsonNode property', () => {
    const model = fluentModel(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname));
    const properties = model.flatMap((t) => t.properties.map((p) => ({ t: t.name, p })));
    const json = properties.filter(({ p }) => p.kind === 'json').map(({ t, p }) => `${t}/${p.key}`);
    assert.deepEqual(json, ['Radar Chart/filled']);
    const discrete = new Set(
      ir.catalog.chartTypes.flatMap((t) => Object.values(t.backends).flatMap((b) => b.properties.filter((p) => p.type === 'discrete').map((p) => `${t.name}/${p.key}`))),
    );
    assert.deepEqual(properties.filter(({ p }) => p.enum).map(({ t, p }) => `${t}/${p.key}`).sort(), [...discrete].filter((k) => !json.includes(k)).sort());
  });

  // source: design §7.6 rule 3 and overrides.json (renames["Regression/enum/regressionMethod"] = RegressionMethod, so no
  // enum name repeats its template's token)
  it('names the Regression method enum RegressionMethod', () => {
    const model = fluentModel(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname));
    const enums = model.flatMap((t) => t.properties.flatMap((p) => (p.enum ? [p.enum.name] : [])));
    assert.ok(enums.includes('RegressionMethod'));
    assert.ok(!enums.includes('RegressionRegressionMethod'));
  });

  // source: design §7.6 rule 4 (non-string values are named from their labels: "[10,50] Europe" -> Europe; the literal
  // "default" -> DefaultLiteral) and ir/flint-ir.json (the Map, Gauge Chart and Sunburst Chart option labels)
  it('names the members of non-string option values from their labels', () => {
    const model = fluentModel(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname));
    const members = (t: string, key: string) => model.find((m) => m.name === t)!.properties.find((p) => p.key === key)!.enum!.members.map((m) => m.name);
    assert.deepEqual(members('Map', 'projectionCenter').slice(0, 6), ['Default', 'WorldAtlantic', 'WorldPacific', 'China', 'Usa', 'Europe']);
    assert.deepEqual(members('Map', 'projection').slice(0, 3), ['Default', 'DefaultLiteral', 'Mercator']);
    assert.deepEqual(members('Gauge Chart', 'showProgress'), ['Default', 'Show', 'Hide']);
    assert.deepEqual(members('Sunburst Chart', 'labelRotate'), ['Default', 'Radial', 'Tangential', 'Horizontal']);
  });

  // source: owner decision (TreeOrient members get readable names through overrides.json renames; the wire values LR and
  // TB stay) and ir/flint-ir.json (Tree orient on echarts offers LR and TB only)
  it('names the TreeOrient members LeftToRight and TopToBottom and keeps the wire values', () => {
    const model = fluentModel(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname));
    const members = model.find((m) => m.name === 'Tree')!.properties.find((p) => p.key === 'orient')!.enum!.members;
    assert.deepEqual(members.map((m) => [m.name, m.value]), [['Default', undefined], ['LeftToRight', 'LR'], ['TopToBottom', 'TB']]);
  });

  // source: owner decision 2026-10-17 (the minmax whisker method reads MinMax; the wire value stays "minmax") and
  // ir/flint-ir.json (Boxplot whiskerMethod offers iqr and minmax)
  it('names the BoxplotWhiskerMethod members Iqr and MinMax and keeps the wire values', () => {
    const model = fluentModel(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname));
    const members = model.find((m) => m.name === 'Boxplot')!.properties.find((p) => p.key === 'whiskerMethod')!.enum!.members;
    assert.deepEqual(members.map((m) => [m.name, m.value]), [['Default', undefined], ['Iqr', 'iqr'], ['MinMax', 'minmax']]);
  });

  // source: owner decision 2026-10-17 (the Power BI presets read PowerBi and PowerBiLight; the wire ids stay) and
  // ir/flint-ir.json catalog.themePresets
  it('names the Power BI theme presets PowerBi and PowerBiLight and keeps the wire ids', () => {
    const presets = catalogConstantNames(ir, loadOverrides(new URL('../overrides.json', import.meta.url).pathname)).get('ThemePreset')!;
    assert.equal(presets.get('powerbi'), 'PowerBi');
    assert.equal(presets.get('powerbi-light'), 'PowerBiLight');
    assert.equal(presets.get('economist'), 'Economist');
  });
});
