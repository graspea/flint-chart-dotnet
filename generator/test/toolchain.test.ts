import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Drift guards: they hold today and fail when a later edit lets the pins drift apart; they were never red first.
describe('toolchain', () => {
  // source: design §3 repository files (.nvmrc holds the exact Node version, engines the matching major)
  it('keeps the .nvmrc major equal to the engines major', () => {
    const nvmrc = readFileSync(new URL('../../.nvmrc', import.meta.url), 'utf8').trim();
    const engines = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8')).engines.node as string;
    const major = nvmrc.replace(/^v/, '').split('.')[0];
    assert.equal(engines, `>=${major} <${Number(major) + 1}`);
  });

  // source: design §7.1 (flint-chart pinned exactly)
  it('pins flint-chart exactly', () => {
    const pkg = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
    assert.match(pkg.dependencies['flint-chart'], /^\d+\.\d+\.\d+$/);
  });
});
