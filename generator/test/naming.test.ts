import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { builderName, catalogNames, entryName, entryNames, enumMemberName, enumMembers, enumName, pascal, templateMemberNames } from '../src/naming.ts';
import type { IrOption } from '../src/ir.ts';
import { syntheticIr } from './support/synthetic-ir.ts';

describe('naming', () => {
  // source: design §7.6 rule 1 and §10.4 test 1
  for (const [input, expected] of [
    ['KPI Card', 'KpiCard'],
    ['ECDF', 'Ecdf'],
    ['step-after', 'StepAfter'],
    ['catmull-rom', 'CatmullRom'],
    ['LR', 'Lr'],
    ['logScale_x', 'LogScaleX'],
    ['3d', '_3d'],
  ] as const) {
    it(`pascal turns "${input}" into ${expected}`, () => {
      assert.equal(pascal(input), expected);
    });
  }

  // source: design §7.6 rule 2 and §10.4 test 1
  for (const [input, expected] of [
    ['Line Chart', 'Line'],
    ['Boxplot', 'Boxplot'],
    ['Sankey Diagram', 'SankeyDiagram'],
    ['Ranged Dot Plot', 'RangedDot'],
  ] as const) {
    it(`entryName strips one trailing Chart/Plot from "${input}"`, () => {
      assert.equal(entryName(input), expected);
    });
  }

  // source: design §7.6 rule 6 and §10.4 test 1
  it('gives the property the Value suffix when a channel and a property share a key', () => {
    const names = templateMemberNames('Scatter Plot', ['x', 'opacity'], ['opacity']);
    assert.equal(names.channels.get('opacity'), 'Opacity');
    assert.equal(names.properties.get('opacity'), 'OpacityValue');
  });

  // source: design §7.6 rule 6 and §10.4 test 1 (the core uses BaseSize, not Size)
  it('leaves the property size alone because Size is not a reserved core name', () => {
    const names = templateMemberNames('Bubble Chart', ['x'], ['size']);
    assert.equal(names.properties.get('size'), 'Size');
  });

  // source: design §7.6 rule 6 (reserved core names get a Channel/Value suffix)
  it('suffixes a channel or property that maps to a reserved core name', () => {
    const names = templateMemberNames('Some Chart', ['title'], ['theme']);
    assert.equal(names.channels.get('title'), 'TitleChannel');
    assert.equal(names.properties.get('theme'), 'ThemeValue');
  });

  // source: design §7.6 rule 6 and §10.4 test 1
  it('throws with both IR paths when two entries are equal after stripping', () => {
    assert.throws(
      () => entryNames(['Line Chart', 'Line Plot']),
      (e: Error) =>
        e.message.includes('catalog.chartTypes[Line Chart]') && e.message.includes('catalog.chartTypes[Line Plot]'),
    );
  });

  // source: design §7.6 rule 6 (any remaining collision fails generation)
  it('throws with both IR paths when two properties collide after Pascal', () => {
    assert.throws(
      () => templateMemberNames('Some Chart', [], ['log_scale', 'logScale']),
      (e: Error) =>
        e.message.includes('catalog.chartTypes[Some Chart].properties[log_scale]') &&
        e.message.includes('catalog.chartTypes[Some Chart].properties[logScale]'),
    );
  });

  // source: design §7.6 rules 3 and 6 (a template's members are the union over its backends; collisions fail)
  it('catalogNames takes the channel and property union across backends and fails on a collision', () => {
    const ir = syntheticIr();
    const names = catalogNames(ir.catalog, {});
    assert.deepEqual([...names.entries], [['Line Chart', 'Line']]);
    assert.deepEqual([...names.members.get('Line Chart')!.channels.keys()], ['color', 'x', 'y']);
    ir.catalog.chartTypes[0].backends.excel.properties = [{ key: 'log_scale', label: 'Log', type: 'binary' }];
    ir.catalog.chartTypes[0].backends.vegalite.properties.push({ key: 'logScale', label: 'Log', type: 'binary' });
    assert.throws(() => catalogNames(ir.catalog, {}), /properties\[log_scale\].*properties\[logScale\]|properties\[logScale\].*properties\[log_scale\]/);
  });

  // source: design §7.6 rule 6 (the fix for a residual collision is renames["<Template>/property/<key>"]) and §7.4 (an
  // overrides.json entry that matches nothing fails generation)
  it('catalogNames applies template renames and fails on one that matches nothing', () => {
    const ir = syntheticIr();
    const names = catalogNames(ir.catalog, { 'Line Chart/property/showPoints': 'Dots', 'Line Chart/channel/color': 'Hue', 'catalog.semanticTypes[Name]': 'NameType' });
    assert.equal(names.members.get('Line Chart')!.properties.get('showPoints'), 'Dots');
    assert.equal(names.members.get('Line Chart')!.channels.get('color'), 'Hue');
    assert.throws(
      () => catalogNames(ir.catalog, { 'Line Chart/property/gone': 'Gone' }),
      /renames\["Line Chart\/property\/gone"\].*matches nothing/,
    );
  });

  // source: design §7.6 rules 3 and 6 (an enum name is renamed by renames["<Template>/enum/<key>"]; an overrides.json
  // entry that matches nothing fails generation, and an enum rename matches only a discrete property)
  it('catalogNames applies enum renames and fails on one that matches no discrete property', () => {
    const ir = syntheticIr();
    const names = catalogNames(ir.catalog, { 'Line Chart/enum/interpolate': 'Curve' });
    assert.equal(names.members.get('Line Chart')!.enums.get('interpolate'), 'Curve');
    assert.equal(catalogNames(ir.catalog, {}).members.get('Line Chart')!.enums.get('interpolate'), 'LineChartInterpolate');
    assert.throws(() => catalogNames(ir.catalog, { 'Line Chart/enum/showPoints': 'Dots' }), /renames\["Line Chart\/enum\/showPoints"\].*matches nothing/);
  });

  // source: design §7.6 rule 3 (an enum whose key repeats the template name, such as Regression regressionMethod ->
  // RegressionRegressionMethod, fails generation until renames["<Template>/enum/<key>"] names it)
  it('enumName fails on a doubled template token unless renamed', () => {
    assert.throws(() => enumName('Regression', 'regressionMethod'), /RegressionRegressionMethod.*renames\["Regression\/enum\/regressionMethod"\]/);
    assert.equal(enumName('Regression', 'regressionMethod', { 'Regression/enum/regressionMethod': 'RegressionMethod' }), 'RegressionMethod');
    assert.equal(enumName('Line Chart', 'lineStyle'), 'LineChartLineStyle');
  });

  // source: design §7.6 rule 2 (builder {Pascal(name)}Builder) and rule 3 (enum {Pascal(chartType)}{Pascal(key)})
  it('names builders and per-template enums', () => {
    assert.equal(builderName('KPI Card'), 'KpiCardBuilder');
    assert.equal(builderName('Line Chart'), 'LineChartBuilder');
    assert.equal(enumName('Line Chart', 'interpolate'), 'LineChartInterpolate');
    assert.equal(enumName('Map', 'projectionCenter'), 'MapProjectionCenter');
  });

  // source: design §7.6 rule 4 (a string value names the member; a non-string value takes its label, "[10,50] Europe" ->
  // Europe; omitted -> Default; the literal "default" -> DefaultLiteral). The labels of the 0.5.1 IR repeat the value in
  // brackets ("Europe [10, 50]") and mark the default ("Show (default)"); both are dropped so the member is the label's name
  for (const [option, expected] of [
    [{ label: 'Monotone', value: 'step-after' }, 'StepAfter'],
    [{ label: 'Catmull-Rom', value: 'catmull-rom' }, 'CatmullRom'],
    [{ label: 'Left to right', value: 'LR' }, 'Lr'],
    [{ label: 'Default (linear)', omitted: true }, 'Default'],
    [{ label: 'Default', value: 'default' }, 'DefaultLiteral'],
    [{ label: 'Europe [10, 50]', value: [10, 50] }, 'Europe'],
    [{ label: 'World (Atlantic) [0, 0]', value: [0, 0] }, 'WorldAtlantic'],
    [{ label: 'Show (default)', value: true }, 'Show'],
    [{ label: 'Horizontal', value: 0 }, 'Horizontal'],
    [{ label: '', value: 0 }, 'N0'],
  ] as const) {
    it(`enumMemberName names ${JSON.stringify(option)} ${expected}`, () => {
      assert.equal(enumMemberName(option as IrOption), expected);
    });
  }

  // source: design §7.6 rule 6 (two enum members equal fail generation with the IR paths)
  it('enumMembers fails with both IR paths when two options give the same member', () => {
    assert.throws(
      () => enumMembers('catalog.chartTypes[Map].properties[projection]', [{ label: 'A', value: 'a-b' }, { label: 'B', value: 'aB' }]),
      (e: Error) => e.message.includes('properties[projection].options[a-b]') && e.message.includes('properties[projection].options[aB]'),
    );
  });
});
