import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { canonicalJson } from '../src/ir.ts';

describe('ir', () => {
  // source: design §7.2 and §10.4 test 2 (sorted keys, two-space indent, LF, trailing newline)
  it('writes sorted keys with two-space indent, LF and a trailing newline', () => {
    const text = canonicalJson({ b: 1, a: { d: [3, { z: true, y: null }], c: 'x' } });
    assert.equal(
      text,
      '{\n  "a": {\n    "c": "x",\n    "d": [\n      3,\n      {\n        "y": null,\n        "z": true\n      }\n    ]\n  },\n  "b": 1\n}\n',
    );
  });

  // source: design §10.4 test 2 (array order is data, only object keys are sorted)
  it('keeps array order', () => {
    assert.equal(canonicalJson(['b', 'a']), '[\n  "b",\n  "a"\n]\n');
  });

  // source: design §10.4 test 2 (idempotent)
  it('is idempotent', () => {
    const once = canonicalJson({ z: { y: [1, 2], x: 'é' }, a: [] });
    assert.equal(canonicalJson(JSON.parse(once)), once);
  });

  // source: design §7.2 (LF only, no CR anywhere)
  it('never writes a carriage return', () => {
    assert.ok(!canonicalJson({ a: 'line\r\nbreak' }).includes('\r'));
  });
});
