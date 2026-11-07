import { before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { extractTypes } from '../src/extract/dts.ts';
import { flintPackageDir } from '../src/extract/package.ts';
import type { Ir, IrNamedType, IrProp, IrType } from '../src/ir.ts';

// Runs against the d.ts of the installed flint-chart. T: = dist/types-BWc1W3v4.d.ts of 0.5.1, which the cited facts hold
// for; a value that changes between upstream versions (a member count, a member list) is read from the committed IR of
// the pinned version, ir/flint-ir.json, and pinned on a synthetic d.ts below where the rule behind it matters.
describe('extract/dts', () => {
  let types: Record<string, IrNamedType>;
  const committed = (JSON.parse(readFileSync(new URL('../ir/flint-ir.json', import.meta.url), 'utf8')) as Ir).types;
  const propsOf = (all: Record<string, IrNamedType>, name: string): IrProp[] => {
    const t = all[name];
    assert.equal(t?.kind, 'object', name);
    return (t as { props: IrProp[] }).props;
  };
  const props = (name: string): IrProp[] => propsOf(types, name);
  const committedNames = (name: string) => propsOf(committed, name).map((p) => `${p.name}${p.optional ? '?' : ''}`);
  const prop = (name: string, member: string): IrProp => props(name).find((p) => p.name === member)!;

  before(() => {
    types = extractTypes(flintPackageDir());
  });

  // source: T:3032-3300 and design §2 (every AssembleOptions member is optional; 27 on 0.5.1) and ir/flint-ir.json
  // types.AssembleOptions (the members of the pinned version)
  it('extracts the AssembleOptions members of the committed IR, all optional, sorted by wire name', () => {
    const members = props('AssembleOptions');
    assert.deepEqual(members.map((p) => `${p.name}${p.optional ? '?' : ''}`), committedNames('AssembleOptions'));
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

  // source: T:2916-3027 (data and chart_spec required, options a reference to AssembleOptions; six keys on 0.5.1) and
  // ir/flint-ir.json types.ChartAssemblyInput (the keys of the pinned version)
  it('extracts the ChartAssemblyInput keys of the committed IR, with data and chart_spec required', () => {
    const keys = props('ChartAssemblyInput').map((p) => `${p.name}${p.optional ? '?' : ''}`);
    assert.deepEqual(keys, committedNames('ChartAssemblyInput'));
    assert.ok(keys.includes('chart_spec') && keys.includes('data'));
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

  // source: design §7.1 (core types without an upstream name are extracted for the parity test) and ir/flint-ir.json
  // types.ChartSpec (the chart_spec members of the pinned version; seven on 0.5.1)
  it('adds the anonymous core types under their SDK names', () => {
    assert.deepEqual(props('ChartSpec').map((p) => `${p.name}${p.optional ? '?' : ''}`), committedNames('ChartSpec'));
    assert.ok(props('ChartSpec').some((p) => p.name === 'chartType'));
    assert.deepEqual(props('ChartSize').map((p) => p.name), ['height', 'width']);
    assert.equal(types.ContinuousMarkCrossSection.kind, 'union');
    assert.equal(types.ChartData.kind, 'union');
  });

  // source: design §7.4 (compileDefaults is accepted as a flattened class, 29 members on 0.5.1; upstream names it
  // ThemeCompileDefaults) and ir/flint-ir.json types.ThemeCompileDefaults; the rule is pinned on a synthetic d.ts below
  it('flattens ThemeCompileDefaults extends Partial<AssembleOptions> into every AssembleOptions member and its own', () => {
    assert.deepEqual(prop('ThemeSpec', 'compileDefaults').type, { kind: 'ref', name: 'ThemeCompileDefaults' });
    const names = props('ThemeCompileDefaults').map((p) => p.name);
    assert.deepEqual(names, propsOf(committed, 'ThemeCompileDefaults').map((p) => p.name));
    for (const p of props('AssembleOptions')) assert.ok(names.includes(p.name), p.name);
    assert.ok(names.length > props('AssembleOptions').length);
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

  // source: the interface declarations reachable from ThemeSpec in the d.ts (on 0.5.1: ArcGeometry T:1480, AreaGeometry
  // T:1464, AxisRole T:973, BandGeometry T:1471, CellGeometry T:1487, LineGeometry T:1442, NumericGuard T:1418,
  // PointGeometry T:1449, Ramp T:1005, ThemeAnnotation T:1326, ThemeCompileDefaults T:1546, ThemeDataLabels T:1321,
  // ThemeFacets T:1366, ThemeFurniture T:1359, ThemeGeometry T:1511, ThemeGuard T:1406, ThemeInk T:1015,
  // ThemeLabels T:1293, ThemeLayout T:1378, ThemeLegend T:1299, ThemeMarks T:1136, ThemeSpec T:1566,
  // ThemeStructure T:1100, ThemeType T:1087, ThemeVariant T:1425, TypeRole T:964; with the index-signature-only
  // ThemeChartDefaults T:1534, a record, the 27 named types of design §7.4), as ir/flint-ir.json records them for the
  // pinned version
  it('collects the named object types of the ThemeSpec tree', () => {
    const treeOf = (all: Record<string, IrNamedType>): string[] => {
      const reachable = new Set<string>();
      const visit = (t: IrType) => {
        switch (t.kind) {
          case 'ref':
            if (!reachable.has(t.name)) {
              reachable.add(t.name);
              visit(all[t.name]);
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
      return [...reachable].filter((n) => all[n].kind === 'object').sort();
    };
    const objects = treeOf(types);
    assert.deepEqual(objects, treeOf(committed));
    for (const name of ['ThemeCompileDefaults', 'ThemeInk', 'ThemeSpec', 'ThemeVariant']) assert.ok(objects.includes(name), name);
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

describe('extract/dts on a synthetic d.ts', () => {
  /** Extracts a package whose index.d.ts declares the roots and core aliases plus `ThemeSpec { <members> }`. */
  const extractTheme = (members: string, ...declarations: string[]): Record<string, IrNamedType> => {
    const dir = mkdtempSync(join(tmpdir(), 'flint-dts-'));
    try {
      writeFileSync(join(dir, 'package.json'), JSON.stringify({ name: 'fake', version: '0.0.0', types: 'index.d.ts' }));
      writeFileSync(
        join(dir, 'index.d.ts'),
        [
          'export interface AssembleOptions { continuousMarkCrossSection?: number; }',
          `export interface ThemeSpec { ${members} }`,
          'export interface ChartAssemblyInput { data?: number; chart_spec?: { baseSize?: number }; }',
          'export interface ChartEncoding { x?: number; }',
          'export interface SemanticAnnotation { x?: number; }',
          ...declarations,
        ].join('\n'),
      );
      return extractTypes(dir);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  };
  const member = (types: Record<string, IrNamedType>, name: string) =>
    (types.ThemeSpec as { props: IrProp[] }).props.find((p) => p.name === name)!.type;

  // source: flint-chart 1.0.0 d.ts ThemeInk.series.categoricalByCount (`Record<number, string[]>`); the key kind is kept
  // so the map can be integer-keyed in C# (JSON keys stay strings on the wire)
  it('records a number-keyed Record as a record with number keys', () => {
    const types = extractTheme('byCount?: Record<number, string[]>;');
    assert.deepEqual(member(types, 'byCount'), { kind: 'record', key: 'number', value: { kind: 'array', element: { kind: 'string' } } });
  });

  // source: flint-chart 1.0.0 d.ts ThemeInk.series.byMark (`Partial<Record<SeriesMarkFamily, {...}>>`, SeriesMarkFamily a
  // string literal union): a map whose keys are a subset of the literals, kept in declaration order
  it('records a Partial Record keyed by string literals as a record with its keys', () => {
    const types = extractTheme("byMark?: Partial<Record<'line' | 'bar', { single?: string }>>;");
    assert.deepEqual(member(types, 'byMark'), {
      kind: 'record',
      keys: ['line', 'bar'],
      value: { kind: 'object', props: [{ name: 'single', optional: true, type: { kind: 'string' } }] },
    });
  });

  // source: TypeScript handbook, Record<Keys, Type> (a single literal is a one-key set)
  it('records a Record keyed by one string literal as a record with that key', () => {
    const types = extractTheme("only?: Record<'line', number>;");
    assert.deepEqual(member(types, 'only'), { kind: 'record', keys: ['line'], value: { kind: 'number' } });
  });

  // source: design §7.4 (ThemeCompileDefaults extends Partial<AssembleOptions> is extracted as one flattened object: the
  // inherited members and its own, sorted by wire name)
  it('flattens an interface that extends Partial<AssembleOptions> into the inherited and own members', () => {
    const types = extractTheme('compileDefaults?: ThemeCompileDefaults;', 'export interface ThemeCompileDefaults extends Partial<AssembleOptions> { baseSize?: number; }');
    assert.deepEqual(member(types, 'compileDefaults'), { kind: 'ref', name: 'ThemeCompileDefaults' });
    assert.deepEqual((types.ThemeCompileDefaults as { props: IrProp[] }).props.map((p) => `${p.name}${p.optional ? '?' : ''}`), [
      'baseSize?',
      'continuousMarkCrossSection?',
    ]);
  });

  // source: flint-chart 1.0.0 d.ts PointGeometry.ink (`'series' | 'text' | string`): any string is accepted, the literals
  // are the documented values, kept in declaration order
  it('records a union of string literals and string as an open literal union', () => {
    const types = extractTheme("ink?: 'series' | 'text' | string;");
    assert.deepEqual(member(types, 'ink'), { kind: 'literalUnion', values: ['series', 'text'], open: true });
  });
});
