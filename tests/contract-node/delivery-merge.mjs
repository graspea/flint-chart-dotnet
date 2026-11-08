// The README's browser guidance ("Charts in the browser"); the README block equals this file.
// transport(request, signal) returns a parsed document. request: { kind: 'manifest' } | { kind: 'page', manifest, group, next }
// A WebSocket host sends the manifest's hash in its own envelope.
export const httpTransport = (base) => {
  const tags = new WeakMap();                           // manifest object -> its ETag; pages carry it as `manifest`
  return async (req, signal) => {
    let url;
    if (req.kind === 'manifest') url = `${base}/manifest`;
    else {
      url = `${base}/page?group=${encodeURIComponent(req.group)}`;
      const tag = tags.get(req.manifest);
      if (tag) url += `&manifest=${encodeURIComponent(tag)}`;
      if (req.next) url += `&next=${encodeURIComponent(req.next)}`;
    }
    const res = await fetch(url, { signal, cache: req.kind === 'manifest' ? 'default' : 'no-store' });
    const doc = await res.json();                       // an error document arrives with its status
    if (req.kind === 'manifest' && doc !== null && typeof doc === 'object') tags.set(doc, res.headers.get('etag') ?? undefined);
    return doc;
  };                                                    // an aborted streamed page fails to parse: restart
};

// flint: the flint-chart version the client was built with; undefined skips the check
export async function fetchManifest(transport, signal, flint) {
  const m = await transport({ kind: 'manifest' }, signal);
  if (m.error) throw Object.assign(new Error('delivery'), { code: m.error.code });
  if (m.contract !== 1) throw new Error(`delivery contract ${m.contract}`);
  if (flint !== undefined && m.flint !== flint) throw new Error(`charts for flint-chart ${m.flint}, client built with ${flint}`);
  return m;
}

export async function collectGroup(transport, manifest, group, signal) {
  const rows = Object.create(null), withheld = Object.create(null);   // ids are host-chosen keys
  for (let next; ; ) {
    const page = await transport({ kind: 'page', manifest, group, next }, signal);
    if (page.error) throw Object.assign(new Error('delivery'), { code: page.error.code });   // restart from the manifest
    if (page.contract !== 1 || page.group !== group) throw new Error('unexpected page');
    for (const [id, part] of Object.entries(page.charts)) {
      if (part.withheld !== undefined) { withheld[id] ??= part.withheld; delete rows[id]; }
      else if (!(id in withheld)) rows[id] = (rows[id] ?? []).concat(part.rows);   // in order; never a spread
    }
    next = page.next;
    if (next == null) break;
  }
  return { rows, withheld };                            // memory only; clear on sign-out
}

export const inputFor = (manifest, id, rows) => ({ ...manifest.charts[id].input, data: { values: rows } });
