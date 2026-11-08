// The browser merge of the README ("Charts in the browser", delivery-merge.mjs) against the delivery samples
// (samples/out/delivery, written by `dotnet run --project samples/Graspea.Flint.Samples`) and synthetic pages.
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { root, version, backendsWith, assembleWarnings } from './flint.js';
import { httpTransport, fetchManifest, collectGroup, inputFor } from './delivery-merge.mjs';

const dir = join(process.env.FLINT_SAMPLES_DIR ?? join(root, 'samples', 'out'), 'delivery');
const read = (name) => JSON.parse(readFileSync(join(dir, `${name}.json`), 'utf8'));
const LAYOUT = ['vegalite', 'echarts', 'chartjs', 'plotly'];

// in-memory transport keyed by next ('' for the first page)
const pagesTransport = (pages) => async (req) => structuredClone(req.kind === 'manifest' ? pages.manifest : pages[req.next ?? '']);
const sampleTransport = () => pagesTransport({ manifest: read('manifest'), '': read('overview'), p2: read('overview-2') });
const page = (charts, extra = {}) => ({ contract: 1, group: 'g', charts, ...extra });
const manifestOf = (flint = '1.0.1') => ({ contract: 1, flint, charts: {}, groups: {} });
const M = manifestOf();   // a manifest object for transports that ignore it

describe('delivery samples', () => {
  // source: samples.test.js pattern; docs/delivery.md client section
  it('writes exactly the expected files', () => {
    assert.deepEqual(readdirSync(dir).sort(), [
      'bind-revenue-by-month.json', 'bind-stock-by-warehouse.json', 'bind-traffic-by-source.json',
      'manifest.json', 'overview-2.json', 'overview-withheld.json', 'overview.json',
    ]);
  });

  // source: design 14.5 rules 1-3
  it('has the contract, the pinned flint version and the groups', () => {
    const m = read('manifest');
    assert.equal(m.contract, 1);
    assert.equal(m.flint, version);
    assert.deepEqual(Object.keys(m.groups), ['overview', 'inventory']);
    assert.deepEqual(m.groups.overview, ['revenue-by-month', 'traffic-by-source']);
  });

  // source: design 14.5 rule 2
  it('fetchManifest refuses a different flint and accepts any when undefined', async () => {
    const t = sampleTransport();
    await assert.rejects(fetchManifest(t, undefined, '0.0.0'), /client built with 0\.0\.0/);
    assert.equal((await fetchManifest(t, undefined, version)).flint, version);
    assert.equal((await fetchManifest(t)).flint, version);
    // undefined opts out of the check: any flint is accepted, there is no built-in default
    assert.equal((await fetchManifest(async () => manifestOf('9.9.9'))).flint, '9.9.9');
    assert.equal((await fetchManifest(async () => manifestOf('9.9.9'), undefined, undefined)).flint, '9.9.9');
  });

  // source: design 14.5 rules 4-5, 14.8.5 step 12
  it('collectGroup pages merge to the Bind input', async () => {
    const t = sampleTransport();
    const m = await fetchManifest(t);
    const { rows, withheld } = await collectGroup(t, m, 'overview');
    assert.deepEqual({ ...withheld }, {});
    assert.deepEqual(Object.keys(rows), m.groups.overview);
    for (const id of Object.keys(rows)) assert.deepEqual(inputFor(m, id, rows[id]), read(`bind-${id}`));
    assert.equal(rows['revenue-by-month'].length, 18);
  });

  // source: design 14.2.3 (layout backends only), samples.test.js; §14.6 merge
  it('assembles every merged chart on the layout backends without warnings', async () => {
    const t = sampleTransport();
    const m = await fetchManifest(t);
    const { rows } = await collectGroup(t, m, 'overview');
    assert.deepEqual(Object.keys(rows), m.groups.overview);
    for (const id of Object.keys(rows)) {
      const input = inputFor(m, id, rows[id]);
      const backends = LAYOUT.filter((b) => backendsWith(input.chart_spec.chartType).includes(b));
      assert.ok(backends.length > 0, id);
      for (const b of backends) assert.deepEqual(assembleWarnings(b, input), [], `${id} on ${b}`);
    }
  });

  // source: design 14.5 rules 4 and 6
  it('a withheld chart has no rows and an empty chart is not assembled', async () => {
    const t = pagesTransport({ '': read('overview-withheld') });
    const { rows, withheld } = await collectGroup(t, M, 'overview');
    assert.equal(withheld['traffic-by-source'], 'restricted');
    assert.ok(!('traffic-by-source' in rows));
    const t2 = sampleTransport();
    const r2 = (await collectGroup(t2, M, 'overview')).rows;
    assert.deepEqual(r2['revenue-by-month'].length > 0, true);
    const empty = await collectGroup(pagesTransport({ '': page({ a: { rows: [] } }) }), M, 'g');
    assert.deepEqual(empty.rows.a, []);   // the caller shows the empty state for length 0
  });
});

describe('collectGroup', () => {
  // source: design 14.6 collectGroup
  it('an error page rejects with its code', async () => {
    const t = pagesTransport({ '': { contract: 1, error: { code: 'stale' } } });
    await assert.rejects(collectGroup(t, M, 'g'), (e) => e.code === 'stale');
  });

  // source: design 14.6 collectGroup
  it('an error page after an earlier page rejects with its code', async () => {
    const t = pagesTransport({ '': page({ a: { rows: [{ v: 1 }] } }, { next: 'p2' }), p2: { contract: 1, error: { code: 'stale' } } });
    await assert.rejects(collectGroup(t, M, 'g'), (e) => e.code === 'stale');
  });

  // source: design 14.6 collectGroup
  it('rejects a page of another group or contract', async () => {
    await assert.rejects(collectGroup(pagesTransport({ '': { ...page({}), group: 'other' } }), M, 'g'), /unexpected page/);
    await assert.rejects(collectGroup(pagesTransport({ '': { ...page({}), contract: 2 } }), M, 'g'), /unexpected page/);
  });

  // source: design 14.5 rule 3
  it('merges ids such as __proto__, constructor and numbers safely', async () => {
    const doc = JSON.parse('{"contract":1,"group":"g","charts":{"__proto__":{"rows":[{"v":1}]},"constructor":{"rows":[{"v":2}]},"7":{"withheld":"x"}}}');
    const { rows, withheld } = await collectGroup(pagesTransport({ '': doc }), M, 'g');
    assert.deepEqual(Object.keys(rows).sort(), ['__proto__', 'constructor']);
    assert.deepEqual(rows['__proto__'], [{ v: 1 }]);
    assert.deepEqual(rows.constructor, [{ v: 2 }]);
    assert.equal(withheld['7'], 'x');
    assert.equal(Object.getPrototypeOf(rows), null);
    assert.equal({}.v, undefined);
  });

  // source: design 14.5 rule 4
  it('withheld after earlier rows drops the rows, keeps the first code and discards later rows', async () => {
    const t = pagesTransport({
      '': page({ a: { rows: [{ v: 1 }] } }, { next: 'p2' }),
      p2: page({ a: { withheld: 'first' } }, { next: 'p3' }),
      p3: page({ a: { withheld: 'second' } }, { next: 'p4' }),
      p4: page({ a: { rows: [{ v: 9 }] } }),
    });
    const { rows, withheld } = await collectGroup(t, M, 'g');
    assert.equal(withheld.a, 'first');
    assert.ok(!('a' in rows));
  });

  // source: design 14.5 rule 1
  it('ignores unknown additive members at every level', async () => {
    const t = pagesTransport({
      '': { ...page({ a: { rows: [{ v: 1 }], extra: 1 } }, { more: true }), unknown: {} },
    });
    const { rows } = await collectGroup(t, M, 'g');
    assert.deepEqual(rows.a, [{ v: 1 }]);
    const m = { ...manifestOf(), extra: 1 };
    assert.equal((await fetchManifest(async () => m)).extra, 1);
  });

  // source: design 14.6 fetchManifest
  it('rejects a wrong contract, an error and a flint mismatch', async () => {
    await assert.rejects(fetchManifest(async () => ({ contract: 2 })), /delivery contract 2/);
    await assert.rejects(fetchManifest(async () => ({ contract: 1, error: { code: 'forbidden' } })), (e) => e.code === 'forbidden');
    await assert.rejects(fetchManifest(async () => manifestOf('1.0.2'), undefined, '1.0.1'), /1\.0\.2/);
  });

  // source: design 14.6 client code
  it('propagates a transport rejection and a parse failure', async () => {
    await assert.rejects(collectGroup(async () => { throw new Error('offline'); }, M, 'g'), /offline/);
    await assert.rejects(collectGroup(async () => JSON.parse('{"contract":'), M, 'g'), SyntaxError);
  });

  // source: design 14.6 client code
  it('assembles nothing before it resolves', async () => {
    const seen = [];
    let n = 0;
    const t = async (req) => { seen.push(req.kind); return n++ ? page({ a: { rows: [] } }) : page({ a: { rows: [{ v: 1 }] } }, { next: 'x' }); };
    const p = collectGroup(t, M, 'g');
    assert.deepEqual(seen, ['page']);   // synchronous start: one request, no assembly API is involved
    await p;
    assert.deepEqual(seen, ['page', 'page']);
  });
});

describe('httpTransport', () => {
  const real = globalThis.fetch;
  const run = async (fn, etag = '"abc"') => {
    const urls = [];
    globalThis.fetch = async (url, init) => {
      urls.push({ url, init });
      return { headers: { get: (h) => (h.toLowerCase() === 'etag' ? etag : null) }, json: async () => ({ ok: true }) };
    };
    try { await fn(urls); } finally { globalThis.fetch = real; }
  };

  // source: design 14.5 rules 5-8 (the manifest tag is the quoted hash, as served in the ETag header)
  it('keeps group, manifest tag and cursor in query parameters', () => run(async (urls) => {
    const t = httpTransport('/api');
    const manifest = await t({ kind: 'manifest' });
    await t({ kind: 'page', manifest, group: 'overview' });
    await t({ kind: 'page', manifest, group: 'overview', next: 'p 2' });
    assert.equal(urls[0].url, '/api/manifest');
    assert.equal(urls[1].url, '/api/page?group=overview&manifest=%22abc%22');
    assert.equal(urls[2].url, '/api/page?group=overview&manifest=%22abc%22&next=p%202');
    assert.equal(urls[1].init.cache, 'no-store');
  }));

  // source: design 14.5 rules 5-8; §14.6 client code
  it('keeps the group names "." and ".." out of the path', () => run(async (urls) => {
    const t = httpTransport('/api');
    for (const group of ['.', '..']) await t({ kind: 'page', group });
    assert.equal(urls[0].url, '/api/page?group=.');
    assert.equal(urls[1].url, '/api/page?group=..');
    for (const { url } of urls) assert.equal(new URL(url, 'http://h').pathname, '/api/page');
  }, null));

  // source: design 14.5 rules 5-8 (the manifest tag is the quoted hash)
  it('takes the manifest tag from the manifest response only', async () => {
    const urls = [];
    globalThis.fetch = async (url, init) => {
      urls.push(url);
      const etag = url.endsWith('/manifest') ? '"m"' : '"page"';
      return { headers: { get: (h) => (h.toLowerCase() === 'etag' ? etag : null) }, json: async () => ({}) };
    };
    try {
      const t = httpTransport('/api');
      const manifest = await t({ kind: 'manifest' });
      await t({ kind: 'page', manifest, group: 'g' });
      await t({ kind: 'page', manifest, group: 'g', next: 'x' });
      assert.equal(urls[1], '/api/page?group=g&manifest=%22m%22');
      assert.equal(urls[2], '/api/page?group=g&manifest=%22m%22&next=x');
    } finally { globalThis.fetch = real; }
  });

  // source: design 14.6 transport (each manifest keeps its own ETag; two overlapping loads must not mix tags)
  it('sends the tag of the manifest the caller holds when two manifest loads overlap', async () => {
    const urls = [];
    let releaseOlder;
    const olderJson = new Promise((r) => { releaseOlder = r; });
    let manifests = 0;
    globalThis.fetch = async (url) => {
      urls.push(url);
      if (!url.endsWith('/manifest')) return { headers: { get: () => null }, json: async () => page({}) };
      const older = manifests++ === 0;
      // the older response arrived first, but its JSON resolves after the newer response arrived
      return { headers: { get: () => (older ? '"old"' : '"new"') }, json: async () => (older ? olderJson : manifestOf('2')) };
    };
    try {
      const t = httpTransport('/api');
      const olderLoad = t({ kind: 'manifest' });
      const newer = await t({ kind: 'manifest' });
      releaseOlder(manifestOf('1'));
      const older = await olderLoad;
      await collectGroup(t, older, 'g');
      await collectGroup(t, newer, 'g');
      assert.equal(urls[2], '/api/page?group=g&manifest=%22old%22');
      assert.equal(urls[3], '/api/page?group=g&manifest=%22new%22');
    } finally { globalThis.fetch = real; }
  });

  // source: design 14.6 transport(request, signal)
  it('passes the abort signal to fetch', () => run(async (urls) => {
    const t = httpTransport('/api');
    const { signal } = new AbortController();
    await t({ kind: 'manifest' }, signal);
    await t({ kind: 'page', group: 'g' }, signal);
    assert.equal(urls[0].init.signal, signal);
    assert.equal(urls[1].init.signal, signal);
  }));

  // source: design 14.5 rules 5-8 (the manifest tag is the quoted hash)
  it('sends no manifest parameter before a manifest was fetched', () => run(async (urls) => {
    await httpTransport('/api')({ kind: 'page', group: 'g' });
    assert.equal(urls[0].url, '/api/page?group=g');
  }));
});

describe('README', () => {
  // source: design 14.6 first comment (the same code is delivery-merge.mjs)
  it('README browser block equals delivery-merge.mjs', () => {
    const readme = readFileSync(join(root, 'README.md'), 'utf8').replace(/\r\n/g, '\n');
    const at = readme.indexOf('\n## Charts in the browser\n');
    assert.ok(at >= 0, 'README.md has no "Charts in the browser" section');
    const block = /^```js\n([\s\S]*?)^```$/m.exec(readme.slice(at));
    assert.ok(block, 'the section has no js block');
    const file = readFileSync(join(root, 'tests', 'contract-node', 'delivery-merge.mjs'), 'utf8').replace(/\r\n/g, '\n');
    assert.equal(block[1], file);
  });
});
