import { before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { extractTypes } from '../src/extract/dts.ts';
import { flintPackageDir } from '../src/extract/package.ts';
import type { IrNamedType, IrProp, IrType } from '../src/ir.ts';

// Runs against the d.ts of the installed flint-chart@0.5.1. T: = dist/types-BWc1W3v4.d.ts.
describe('extract/dts', () => {
  let types: Record<string, IrNamedType>;
  const props = (name: string): IrProp[] => {
    const t = types[name];
    assert.equal(t.kind, 'object', name);
    return (t as { props: IrProp[] }).props;
  };
  const prop = (name: string, member: string): IrProp => props(name).find((p) => p.name === member)!;

  before(() => {
    types = extractTypes(flintPackageDir());
  });

  // source: T:3032-3300, design §2 (AssembleOptions has 27 optional members)
  it('extracts AssembleOptions with 27 optional members sorted by wire name', () => {
    const members = props('AssembleOptions');
    assert.equal(members.length, 27);
    assert.ok(members.every((p) => p.optional));
    const names = members.map((p) => p.name);
    assert.deepEqual(names, [...names].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)));
  });

  // source: T:3233-3254 (continuousMarkCrossSection is number | {x, y, elasticity?, maxStretch?, seriesCountAxis?})
  it('records continuousMarkCrossSection as a union of number and an object', () => {
    const t = prop('AssembleOptions', 'continuousMarkCrossSection').type;
    assert.equal(t.kind, 'union');
    const members = (t as { members: IrType[] }).members;
    assert.deepEqual(members[0], { kind: 'number' });
    const obj = members[1] as { kind: string; props: IrProp[] };
    assert.equal(obj.kind, 'object');
    assert.deepEqual(obj.props.map((p) => `${p.name}${p.optional ? '?' : ''}`), ['elasticity?', 'maxStretch?', 'seriesCountAxis?', 'x', 'y']);
    assert.deepEqual(obj.props.find((p) => p.name === 'seriesCountAxis')!.type, { kind: 'literalUnion', values: ['x', 'y', 'auto'] });
  });

  // source: T:3089-3092 (facetFixedPadding: {width, height})
  it('records facetFixedPadding as an inline object with required width and height', () => {
    assert.deepEqual(prop('AssembleOptions', 'facetFixedPadding').type, {
      kind: 'object',
      props: [
        { name: 'height', optional: false, type: { kind: 'number' } },
        { name: 'width', optional: false, type: { kind: 'number' } },
      ],
    });
  });

  // source: T:1994-2001 (ChartEncoding literal unions)
  it('records the ChartEncoding literal unions', () => {
    assert.deepEqual(prop('ChartEncoding', 'aggregate').type, { kind: 'literalUnion', values: ['count', 'sum', 'average', 'mean'] });
    assert.deepEqual(prop('ChartEncoding', 'type').type, { kind: 'literalUnion', values: ['quantitative', 'nominal', 'ordinal', 'temporal'] });
    assert.deepEqual(prop('ChartEncoding', 'sortOrder').type, { kind: 'literalUnion', values: ['ascending', 'descending'] });
  });

  // source: T:2916-3027 (six top-level keys; data and chart_spec required)
  it('extracts the six ChartAssemblyInput keys', () => {
    assert.deepEqual(
      props('ChartAssemblyInput').map((p) => `${p.name}${p.optional ? '?' : ''}`),
      ['chart_spec', 'data', 'field_display_names?', 'options?', 'semantic_types?', 'theme_spec?'],
    );
    assert.deepEqual(prop('ChartAssemblyInput', 'options').type, { kind: 'ref', name: 'AssembleOptions' });
  });

  // source: T:612-644 (SemanticAnnotation)
  it('extracts SemanticAnnotation with a required semanticType and a number tuple domain', () => {
    assert.deepEqual(prop('SemanticAnnotation', 'semanticType').optional, false);
    assert.deepEqual(prop('SemanticAnnotation', 'intrinsicDomain').type, {
      kind: 'tuple',
      elements: [{ kind: 'number' }, { kind: 'number' }],
    });
  });

  // source: design §7.1 (core types without an upstream name are extracted for the parity test)
  it('adds the anonymous core types under their SDK names', () => {
    assert.deepEqual(
      props('ChartSpec').map((p) => p.name),
      ['baseSize', 'canvasSize', 'chartProperties', 'chartType', 'encodings', 'subtitle', 'title'],
    );
    assert.deepEqual(props('ChartSize').map((p) => p.name), ['height', 'width']);
    assert.equal(types.ContinuousMarkCrossSection.kind, 'union');
    assert.equal(types.ChartData.kind, 'union');
  });

  // source: design §7.4 (compileDefaults is accepted as a flattened 29-property class; upstream names it ThemeCompileDefaults)
  it('flattens ThemeCompileDefaults extends Partial<AssembleOptions> into 29 members', () => {
    assert.deepEqual(prop('ThemeSpec', 'compileDefaults').type, { kind: 'ref', name: 'ThemeCompileDefaults' });
    assert.equal(props('ThemeCompileDefaults').length, 29);
  });

  // source: T:1425-1431 (ThemeVariant.then: Partial<Omit<ThemeSpec, ...>> is a mapped type)
  it('records ThemeVariant.then as a mapped type with its resolved members', () => {
    const then = prop('ThemeVariant', 'then').type as { kind: string; props: IrProp[] };
    assert.equal(then.kind, 'mapped');
    assert.ok(!then.props.some((p) => ['extends', 'id', 'label', 'ink', 'type', 'variants'].includes(p.name)));
    assert.ok(then.props.some((p) => p.name === 'marks'));
    assert.deepEqual(prop('ThemeVariant', 'when').type, { kind: 'ref', name: 'ThemeGuard' });
  });

  // source: T:1534-1536 (ThemeChartDefaults { [chartType: string]: Record<string, unknown> })
  it('records ThemeChartDefaults as a record of records of unknown', () => {
    const t = types.ThemeChartDefaults;
    assert.equal(t.kind, 'record');
    assert.deepEqual((t as { value: IrType }).value, { kind: 'record', value: { kind: 'unknown' } });
  });

  // source: T:963-971 (SizeToken = string | number, used by TypeRole.size)
  it('inlines type aliases such as SizeToken', () => {
    assert.deepEqual(prop('TypeRole', 'size').type, { kind: 'union', members: [{ kind: 'string' }, { kind: 'number' }] });
    assert.deepEqual(prop('AxisRole', 'line').type, {
      kind: 'literalUnion',
      values: ['omit', 'hairline', 'quiet', 'full', 'emphasised'],
    });
  });

  // source: the interface declarations reachable from ThemeSpec in the d.ts: ArcGeometry T:1480, AreaGeometry T:1464,
  // AxisRole T:973, BandGeometry T:1471, CellGeometry T:1487, LineGeometry T:1442, NumericGuard T:1418,
  // PointGeometry T:1449, Ramp T:1005, ThemeAnnotation T:1326, ThemeCompileDefaults T:1546, ThemeDataLabels T:1321,
  // ThemeFacets T:1366, ThemeFurniture T:1359, ThemeGeometry T:1511, ThemeGuard T:1406, ThemeInk T:1015,
  // ThemeLabels T:1293, ThemeLayout T:1378, ThemeLegend T:1299, ThemeMarks T:1136, ThemeSpec T:1566,
  // ThemeStructure T:1100, ThemeType T:1087, ThemeVariant T:1425, TypeRole T:964; with the index-signature-only
  // ThemeChartDefaults T:1534 (a record, not listed) these are the 27 named types of design §7.4
  it('collects the named object types of the ThemeSpec tree', () => {
    const reachable = new Set<string>();
    const visit = (t: IrType) => {
      switch (t.kind) {
        case 'ref':
          if (!reachable.has(t.name)) {
            reachable.add(t.name);
            visit(types[t.name]);
          }
          break;
        case 'object':
        case 'mapped':
          t.props.forEach((p) => visit(p.type));
          break;
        case 'array':
          visit(t.element);
          break;
        case 'record':
          visit(t.value);
          break;
        case 'union':
        case 'intersection':
          t.members.forEach(visit);
          break;
        case 'partial':
          visit(t.of);
          break;
        default:
          break;
      }
    };
    visit({ kind: 'ref', name: 'ThemeSpec' });
    const objects = [...reachable].filter((n) => types[n].kind === 'object').sort();
    assert.deepEqual(objects, [
      'ArcGeometry', 'AreaGeometry', 'AxisRole', 'BandGeometry', 'CellGeometry', 'LineGeometry', 'NumericGuard',
      'PointGeometry', 'Ramp', 'ThemeAnnotation', 'ThemeCompileDefaults', 'ThemeDataLabels', 'ThemeFacets',
      'ThemeFurniture', 'ThemeGeometry', 'ThemeGuard', 'ThemeInk', 'ThemeLabels', 'ThemeLayout', 'ThemeLegend',
      'ThemeMarks', 'ThemeSpec', 'ThemeStructure', 'ThemeType', 'ThemeVariant', 'TypeRole',
    ]);
  });

  // source: JSDoc on AssembleOptions.addTooltips (T:3034)
  it('keeps JSDoc text', () => {
    assert.equal(prop('AssembleOptions', 'addTooltips').doc, 'Whether to add tooltips to the chart (default: false)');
  });

  // source: review D2-7 (the IR keys named types by name, so two interfaces with one name from different declaration
  // files would collapse into the first one's shape)
  it('fails when two different interfaces share a name', () => {
    const dir = mkdtempSync(join(tmpdir(), 'flint-dts-'));
    try {
      writeFileSync(join(dir, 'package.json'), JSON.stringify({ name: 'fake', version: '0.0.0', types: 'index.d.ts' }));
      writeFileSync(join(dir, 'a.d.ts'), 'export interface Options { a?: number; }\n');
      writeFileSync(join(dir, 'b.d.ts'), 'export interface Options { b?: string; }\n');
      writeFileSync(
        join(dir, 'index.d.ts'),
        [
          "import type { Options as A } from './a';",
          "import type { Options as B } from './b';",
          'export interface AssembleOptions { a?: A; }',
          'export interface ThemeSpec { b?: B; }',
          'export interface ChartAssemblyInput { x?: number; }',
          'export interface ChartEncoding { x?: number; }',
          'export interface SemanticAnnotation { x?: number; }',
        ].join('\n'),
      );
      assert.throws(() => extractTypes(dir), /Options is declared twice: a\.d\.ts and b\.d\.ts/);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
