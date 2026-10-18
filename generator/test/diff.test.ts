import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { diffIr, diffMarkdown, retiredEntries } from '../src/diff.ts';
import { syntheticIr } from './support/synthetic-ir.ts';

describe('diff', () => {
  const pair = () => {
    const before = syntheticIr();
    const after = syntheticIr();
    after.upstream.version = '0.6.0';
    return { before, after };
  };

  // source: design §7.2 (ir-diff: added / removed / changed, with IR paths)
  it('reports the root-most added and removed nodes and changed values by IR path', () => {
    const { before, after } = pair();
    after.catalog.chartTypes[0].backends.vegalite.channels = ['x', 'y'];
    after.catalog.chartTypes[0].backends.vegalite.properties[0].label = 'Interpolation';
    after.catalog.themePresets.push({ id: 'nyt', label: 'New York Times', description: 'News.' });
    assert.deepEqual(diffIr(before, after), [
      {
        change: 'removed',
        path: 'catalog.chartTypes[Line Chart].backends.vegalite.channels[color]',
        before: '"color"',
      },
      {
        change: 'changed',
        path: 'catalog.chartTypes[Line Chart].backends.vegalite.properties[interpolate].label',
        before: '"Curve"',
        after: '"Interpolation"',
      },
      { change: 'added', path: 'catalog.themePresets[nyt]', after: '{…}' },
      { change: 'changed', path: 'upstream.version', before: '"0.5.1"', after: '"0.6.0"' },
    ]);
  });

  // source: design §7.2 (the table feeds the sync PR body)
  it('renders a markdown table and a no-change line', () => {
    const { before, after } = pair();
    assert.equal(
      diffMarkdown(diffIr(before, after)),
      '| Change | IR path | Before | After |\n|---|---|---|---|\n| changed | `upstream.version` | `"0.5.1"` | `"0.6.0"` |\n',
    );
    assert.equal(diffMarkdown([]), 'No IR changes.\n');
  });

  // source: design §7.8 (vanished chart types, channels, properties, option values, semantic types, presets are retired,
  // keyed by template and backend, with the version in which they vanished)
  it('turns catalog removals into retirement entries and ignores metadata', () => {
    const { before, after } = pair();
    after.catalog.chartTypes[0].backends.vegalite.properties[0].options!.pop();
    delete after.catalog.chartTypes[0].backends.vegalite.category;
    after.catalog.semanticTypes = [];
    assert.deepEqual(retiredEntries(diffIr(before, after), '0.5.1', '0.6.0'), [
      {
        path: 'catalog.chartTypes[Line Chart].backends.vegalite.properties[interpolate].options[="monotone"]',
        lastSeenIn: '0.5.1',
        retiredIn: '0.6.0',
      },
      { path: 'catalog.semanticTypes[YearMonth]', lastSeenIn: '0.5.1', retiredIn: '0.6.0' },
    ]);
  });
});
