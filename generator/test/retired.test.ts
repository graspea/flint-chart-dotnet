import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { breakingLine, facadeCatalog, obsoleteIn, retirementStatus, type RetiredEntry } from '../src/retired.ts';
import { syntheticIr } from './support/synthetic-ir.ts';

const entry = (retiredIn: string, extra: Partial<RetiredEntry> = {}): RetiredEntry => ({ path: 'catalog.channels[angle]', lastSeenIn: '0.0.1', retiredIn, was: 'angle', ...extra });

describe('retired', () => {
  // source: part 3 §8.4 (breaking windows: pre-1.0 upstream minors, post-1.0 upstream majors)
  it('names the breaking line of a version', () => {
    assert.deepEqual(['0.5.1', '0.6.0', '1.0.0', '1.9.3', '2.0.0'].map(breakingLine), ['0.5', '0.6', '1', '1', '2']);
  });

  // source: part 3 §8.4 (kept until the next breaking window, never shorter than one upstream minor line) and the
  // maintainer decision recorded in docs/deprecations.md (members deleted in a breaking window carry deletedIn)
  it('keeps a member obsolete within its breaking line and removes it after it or when deleted', () => {
    assert.equal(retirementStatus(entry('0.5.2'), '0.5.9'), 'obsolete');
    assert.equal(retirementStatus(entry('0.5.2'), '0.6.0'), 'removed');
    assert.equal(retirementStatus(entry('1.2.0'), '1.9.0'), 'obsolete');
    assert.equal(retirementStatus(entry('1.9.5'), '2.0.0'), 'removed');
    assert.equal(retirementStatus(entry('2.0.0'), '2.0.0'), 'obsolete');
    assert.equal(retirementStatus(entry('1.0.0', { deletedIn: '1.0.0' }), '1.0.0'), 'removed');
  });

  // source: design §7.8 (emitters keep emitting retired members; the ledger keys them by template and backend)
  it('puts obsolete members back into the facade catalog and records where', () => {
    const ir = syntheticIr();
    const base = 'catalog.chartTypes[Line Chart].backends.vegalite';
    ir.retired = [
      { path: `${base}.channels[size]`, lastSeenIn: '0.5.0', retiredIn: '0.5.1', was: 'size' },
      { path: `${base}.properties[smooth]`, lastSeenIn: '0.5.0', retiredIn: '0.5.1', was: { key: 'smooth', label: 'Smooth', type: 'binary' } },
      { path: `${base}.properties[interpolate].options[="step"]`, lastSeenIn: '0.5.0', retiredIn: '0.5.1', was: { label: 'Step', value: 'step' } },
      { path: 'catalog.themePresets[dark]', lastSeenIn: '0.5.0', retiredIn: '0.5.1', was: { id: 'dark', label: 'Dark', description: 'Night.' } },
      { path: `${base}.properties[gone]`, lastSeenIn: '0.4.0', retiredIn: '0.4.1', was: { key: 'gone', label: 'Gone', type: 'binary' } },
    ];
    const { catalog, obsolete } = facadeCatalog(ir);
    const vegalite = catalog.chartTypes[0].backends.vegalite;
    assert.deepEqual(vegalite.channels, ['x', 'y', 'color', 'size']);
    assert.deepEqual(vegalite.properties.map((p) => p.key), ['interpolate', 'showPoints', 'strokeWidth', 'smooth']);
    assert.deepEqual(vegalite.properties[0].options?.map((o) => o.label), ['Default', 'Monotone', 'Step']);
    assert.deepEqual(catalog.themePresets.map((p) => p.id), ['powerbi-light', 'dark']);
    assert.equal(ir.catalog.chartTypes[0].backends.vegalite.channels.length, 3);
    assert.equal(obsoleteIn(obsolete, `${base}.properties[smooth]`), '0.5.1');
    assert.equal(obsoleteIn(obsolete, `${base}.properties[gone]`), undefined);
    assert.equal(obsoleteIn(obsolete, `${base}.properties[showPoints]`), undefined);
  });

  // source: design §7.8 (a retired template keeps everything it declared)
  it('reads a member of an obsolete template or backend as obsolete', () => {
    const obsolete = new Map([['catalog.chartTypes[Pie].backends.excel', '0.5.1']]);
    assert.equal(obsoleteIn(obsolete, 'catalog.chartTypes[Pie].backends.excel.properties[k].options[="a"]'), '0.5.1');
    assert.equal(obsoleteIn(obsolete, 'catalog.chartTypes[Pie].backends.vegalite.channels[x]'), undefined);
    // A backend id with a hyphen (image-charts) is a backend path too.
    const hyphen = new Map([['catalog.chartTypes[Pie].backends.image-charts', '1.0.1']]);
    assert.equal(obsoleteIn(hyphen, 'catalog.chartTypes[Pie].backends.image-charts.channels[color]'), '1.0.1');
  });

  // source: design §7.8 (an entry within its lifetime is emitted from its snapshot)
  it('fails on an obsolete entry without a snapshot', () => {
    const ir = syntheticIr();
    ir.retired = [{ path: 'catalog.channels[angle]', lastSeenIn: '0.5.0', retiredIn: '0.5.1' }];
    assert.throws(() => facadeCatalog(ir), /catalog\.channels\[angle\] is within its lifetime but has no "was" snapshot/);
  });
});
