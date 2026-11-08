## PRD: chart data delivery for fixed chart definitions

Status: Accepted (2026-11-08) with ADR 16; implemented on the development branch and unreleased (`CHANGELOG.md`, section `[Unreleased]`). Settled by the owner on 2026-11-08: streamed parts in v1 (D9.6) and no npm client, README guidance only (D9.11), then the other decision points as recommended, with ADR 16. It states what the delivery data API must do; part 5 of the SDK design ([09-browser-data-delivery.md](09-browser-data-delivery.md), §14) designs it and the ADR ([11-adr-chart-data-delivery.md](11-adr-chart-data-delivery.md)) records the decisions. Baseline: the SDK release `v1.0.1.1-preview.1`, which tracks `flint-chart` 1.0.1 (1.0.0 behaves the same), plus M0a, the `scheme` union, of the 1.0.1 gaps it leaves (§14.8.1); M0b, the Image-Charts backend, which delivery does not need, landed after it (§14.8.1). Ids: host needs N1-N11, stories S1-S6, functional requirements FR-1 to FR-14, non-functional requirements NFR-1 to NFR-9; "§14.x" points into part 5, "ADR-16.n" into the ADR and "D9.n" to the owner decision points of §14.10. Paths follow the Paths paragraph of part 5: repository paths, and upstream paths relative to the `flint-chart` 1.0.1 package.

### 1. Problem

Hosts that render with `flint-chart` in the browser need the data-free specs of a page's charts on mount, and the rows of a chart group only when the group is shown, in a shape that embeds in HTTP responses and in WebSocket RPC envelopes, with per-chart withholding and code-only errors. Core gives `ChartTemplate<TRow>` (§13.3.3) but no public spec accessor, no multi-chart envelope, no withheld state and no check that a definition is independent of the rows present (§14.1, "Today"), so every host re-invents the envelope, and the upstream facts that decide whether a spec stays stable without data (§14.2.2) are rediscovered host by host.

### 2. Assumptions

| Assumption | Consequence |
|---|---|
| **Fixed definitions**: charts are declared in code or in spec files at build time or start-up, immutable at run time and the same for every request of the users who see them | specs are computed once, cached and content-hashed, never composed per request; a host whose users see different charts (for example by role) builds one manifest per variant at start-up |
| The work is implemented on `flint-chart` 1.0.0/1.0.1 | every upstream fact is re-based on 1.0.1 (§14.1, §14.2) |
| M0a (the `scheme` union, one of the 1.0.1 gaps of `v1.0.1.1-preview.1`) lands first | blocking: on that release a 1.0.1 spec with an array `encoding.scheme` cannot be read (§14.1); done as `ChartEncoding.ColorScheme`, while M0b (Image-Charts) is done too, outside delivery, which does not use it (§14.8.1) |
| Rows are final values aggregated on the server, small enough to assemble in the browser (about 10,000 per chart, the measured guidance of §14.2.3) | the host enforces its cap and its own error code |
| The wire contract is frozen as v1 at its first release | additive changes only; the C# surface follows the library's preview rule (`docs/versioning.md:38`) until the preview exit gate decides between stable API and `[Experimental("FLINTEXP002")]` (D9.4) |

### 3. Users and stories

| Id | Persona | Story |
|---|---|---|
| S1 | .NET backend developer | declares chart handles and groups in static fields and gets a hashed manifest once, at start-up |
| S2 | .NET backend developer | answers a group request from an `IAsyncEnumerable<TRow>` and withholds one chart of the group with a code |
| S3 | .NET backend developer | embeds a page in an RPC reply of the host's own envelope as one raw JSON value |
| S4 | .NET backend developer | fails start-up when a definition depends on the rows present |
| S5 | Front-end developer | merges the pages of a group with about 40 lines of JavaScript and draws each chart once |
| S6 | AI coding agent | copies one documented pattern per side from README rule 9 and `docs/delivery.md` |

### 4. Host needs

Generic needs of hosts that render with `flint-chart` in the browser. N9 and N11 are client guidance; the SDK documents them and does not implement them.

| Id | Need |
|---|---|
| N1 | **Preload.** On page mount, one request returns the data-free specs of every chart of the page, or of a named chart group; cacheable until the next deploy, identical for every user who sees those charts |
| N2 | **Lazy rows.** A chart group (charts shown together, such as a panel or a tab) requests its rows only when it is shown; no prefetch; a closed group requests nothing |
| N3 | **One data response per group** carries the rows of several named charts; each chart is either rows or withheld under a host-defined code; no totals, counts or more-pages flags, which the merge does not need |
| N4 | **Final rows.** Rows are final values aggregated on the server; the client never aggregates; a result stays small enough to assemble in the browser (about 10,000 rows per chart, §14.2.3); `MaxRows` caps one part (one `Rows`, `RowsAsync` or `Stream` call), so a host that pages caps the whole chart across its pages itself and a host that caps a whole page sums its parts; a host that caps rows answers a larger result with its own error code and aggregates further |
| N5 | **Paging, additive and later.** An opaque cursor; the host authorizes every page and decodes the cursor only after authorization; one consistent result per sequence; the client draws once after the last page, because upstream derives types, sizes and categories from the rows present (§14.2.2) |
| N6 | **Transport-neutral.** Hosts use HTTP and also a WebSocket RPC with their own message envelope; the output embeds as a raw JSON value or as UTF-8 bytes in any transport; the core data API does not depend on ASP.NET Core |
| N7 | **Errors** are a code-only object, with no message text and no counts |
| N8 | **Version.** The browser knows its `flint-chart` version only at build time (1.0.1 exports none); hosts that deploy server and client from one commit may skip the check |
| N9 | The client keeps rows in memory only and clears them on sign-out |
| N10 | **Specs stable without data**: an explicit `encoding.type` on every channel, an explicit category order (an explicit-order `sortBy`), no `aggregate`, no `chartType` or `arrange` override, no size in the spec (the layout follows the container); checkable at start-up |
| N11 | After assembly the client post-processes: HTML-escaping ECharts tooltips (upstream interpolates raw values) and colours from CSS |

### 5. Goals and non-goals

**Goals**

1. One cacheable request per page for the specs (N1).
2. Lazy rows per group (N2).
3. Rows or withheld per chart, with no counts (N3, N7).
4. Transport-neutral output (N6).
5. Fixed definitions checkable at start-up (N10).
6. Paging reserved in the contract and additive (N5).
7. Byte identity with the streamed document and with `Bind` for rows whose converters write JSON tokens, and trimming and AOT safety.

**Non-goals**: rendering or assembling in .NET; progressive drawing (§14.2.2); HTTP helpers (M4, `Graspea.Flint.AspNetCore`); a published JavaScript client (the browser side is README guidance, D9.11); per-request spec composition; totals or counts; escaping or theming on the server (N11 is client work); SDK-defined withheld or error codes.

### 6. Functional requirements

| Id | Requirement | Acceptance | Design | ADR |
|---|---|---|---|---|
| FR-1 | `ChartHandle<TRow>(id, template, maxRows?)` binds an id to a template; the constructor refuses a bad id, a template without row metadata, a template whose document has a top-level `data` member and a `maxRows` below 1 | `ArgumentException` (`ArgumentOutOfRangeException` for `maxRows`) at construction, before any row | §14.4.1, §14.4.2 | ADR-16.1, ADR-16.2 |
| FR-2 | `ChartGroup(name, charts)` with a checked name, at least one chart and unique ids | invariant tests (§14.8.5 step 6) | §14.4.1 | ADR-16.3 |
| FR-3 | `ChartManifest.Create(groups)` writes the bytes once and exposes `Utf8Bytes`, `Hash`, `ToJson()` and `WriteTo(Utf8JsonWriter)`; a handle may sit in several groups; duplicate ids or group names throw; groups and charts are read-only snapshots | `input` equals the template's stored bytes; the bytes equal the expected manifest text with the version spliced in; `Hash` is the lowercase hex SHA-256 of `Utf8Bytes` | §14.4.1, §14.5 | ADR-16.3, ADR-16.7 |
| FR-4 | `Rows`, `RowsAsync`, `Stream` (FR-14) and `Withheld` produce parts; `DataPage` holds every chart of its group exactly once, in group order | a foreign, missing or repeated chart and an empty `next` throw | §14.4.1, §14.4.2 | ADR-16.2, ADR-16.3 |
| FR-5 | Pages write with `WriteTo(Utf8JsonWriter)`, `ToUtf8Bytes()`, `ToJson()` and `WriteAsync(Stream)`; errors with `WriteTo`, `ToUtf8Bytes()` and `ToJson()` | the outputs agree; `WriteTo` under an indented writer is JSON-equal; a failure of the stream propagates unchanged and nothing more is written | §14.4.1, §14.4.5 | ADR-16.1, ADR-16.2 |
| FR-6 | `DeliveryError(code)` writes `{"contract":1,"error":{"code":"…"}}` | an empty code throws | §14.5 | ADR-16.3 |
| FR-7 | The `rows` elements are byte-identical to the `data.values` elements of the streamed document (`FlintJson.SerializeAsync` over `BindStream` with the same rows), and to those of `Bind(rows)` for rows whose converters write JSON tokens; a converter that writes raw JSON is JSON-equal to `Bind` | §14.8.5 steps 3, 4 and 7 | §14.4.2 | ADR-16.2 |
| FR-8 | `ChartGroup.Validate(backend)` reports the template rules plus five `fixed_*` Info codes at manifest paths, from this entry point only | positive and negative specs per code; `ChartValidator.Validate` reports none of them | §14.7 | ADR-16.4 |
| FR-9 | A `next` cursor, written only when another page follows; the client concatenates `rows` per chart in arrival order | uneven pages concatenate to `Bind(all)`'s values | §14.5, §14.6 | ADR-16.3 |
| FR-10 | `flint` in the manifest; `contract` in every document | `flint` equals `FlintUpstream.Version` and the version the contract harness reports | §14.5 | ADR-16.3 |
| FR-11 | `MaxRows` caps one part (one `Rows`, `RowsAsync` or `Stream` call, not a chart across pages) and throws `ChartRowLimitException` when the source yields row `MaxRows + 1`, before that row is serialised: from `Rows`/`RowsAsync` before any byte of the page, from a streamed part out of `WriteAsync` (FR-14) | synchronous, asynchronous and streamed tests; no overflow at `int.MaxValue` (§14.8.5 steps 5 and 8a) | §14.4.2, §14.4.6 | ADR-16.5 |
| FR-12 | `ChartTemplate<TRow>.ToJson()` returns the stored data-free document | equal to `FlintJson.Serialize` of the data-free document and to the deferred builder's `ToJson()` | §14.4.3 | ADR-16.7 |
| FR-13 | A browser flow in the README, with a transport function (HTTP with the group, the manifest tag and the cursor in query parameters, and RPC), the version injected at build time, the drawing policy and the post-processing guidance (N9, N11); no package; the same code is tested on 1.0.1 | `delivery.test.js` green on 1.0.1, and the README block equal to `delivery-merge.mjs` | §14.6, §14.8.5 step 12 | ADR-16.6 |
| FR-14 | `ChartHandle<TRow>.Stream(IAsyncEnumerable<TRow>)` gives a single-use part whose rows go from the source to the output while `DataPage.WriteAsync` writes; the synchronous finishers refuse such a page | streamed bytes equal the `RowsAsync` page; memory flat in the row count; mid-stream failures propagate after a prefix | §14.4.6, §14.8.5 step 8a | ADR-16.2 |

### 7. Non-functional requirements

| Id | Requirement | Design | ADR |
|---|---|---|---|
| NFR-1 | Zero runtime dependencies, `net8.0`, `IsAotCompatible`, no new trimming or AOT annotations | §14.3, §14.4.2 (AOT) | ADR-16.1 |
| NFR-2 | Writers never validate; `Build()`/`ToJson()` unchanged | §14.4.2 | ADR-16.4 |
| NFR-3 | Every row, limit, invariant and cancellation failure of a page without streamed parts happens before its first byte; a streamed part may fail after it, leaving a prefix of the page (empty before the first 16 KiB drain, otherwise truncated JSON) as documented (FR-14); transport failures can come at any time, including at the final flush (the token passed to `WriteAsync` reaches only the stream, so a cancellation observed there is one); the SDK writes nothing after a failure | §14.4.2 (Errors), §14.4.6 | ADR-16.2 |
| NFR-4 | Declarations immutable and thread-safe; a new row writer per call; streamed parts single-use | §14.4.2 (Threads) | ADR-16.2 |
| NFR-5 | Memory per page about the rows' JSON plus the output, no `JsonArray`; a streamed page the largest single row or materialised part plus about 32 KiB of buffer, whatever its row count | §14.4.2 (Memory) | ADR-16.2 |
| NFR-6 | `WriteAsync` performs no synchronous I/O | §14.4.2 (Flushing) | ADR-16.2 |
| NFR-7 | Wire v1 additive-only once released; `contract` bumps on an incompatible change | §14.5 rule 1 | ADR-16.3 |
| NFR-8 | The manifest hash is deterministic for the same definitions under the same SDK version and runtime serialisation; no canonical hash across runtimes or semantically equal documents, and it changes with every upstream version (`flint`) | §14.4.2 (Hash) | ADR-16.3 |
| NFR-9 | PublicAPI analyzers (RS0016 as an error), package validation, English only, no AI attribution | §14.8.4, §14.8.6 | ADR-16.1 |

### 8. Success metrics

- A host replaces a hand-written envelope with at most 30 server lines and the 40-line client of §14.6.
- Manifest requests after the first revalidate with 304.
- Zero mid-envelope failures in tests for pages without streamed parts: every such failure case of §14.4.2 fails before the first byte.
- `delivery.test.js` green on 1.0.1; the AOT sample publishes with 0 trimming and AOT warnings.

### 9. Milestones and release criteria

| Milestone | Content | Status |
|---|---|---|
| M0a | the `scheme` union of 1.0.1, as `ChartEncoding.ColorScheme` with `ChartEncoding.Scheme` deprecated under `FLINT0002` (§14.8.1); `FlintUpstream.Version = "1.0.1"`, the typed 1.0.1 members and the aligned `sort_by_invalid` shipped in `v1.0.1.1-preview.1` | done, unreleased |
| M0b | `FlintBackend.ImageCharts` and six contract-node backends (Image-Charts has no template-definition export, and its result has no warnings member, §14.8.1) | done, unreleased; not a delivery requirement (owner decisions of 2026-11-08, §14.8.1) |
| M1 | `ToJson()`, the `Graspea.Flint.Delivery` namespace with the streamed part (§14.4.6, D9.6), the five codes, the .NET tests, `docs/delivery.md`, the AOT sample | done, unreleased |
| M2 | the samples subfolder, `delivery.test.js`, README rule 9 and the browser section, the CHANGELOG entry | done, unreleased |
| M4 | `Graspea.Flint.AspNetCore` | deferred |

No npm client is planned (D9.11). M3 (streaming) moved into M1 by D9.6 and M5 (the npm client) was dropped by D9.11; the numbers stay so that cross-references hold.

Release criteria for M1 and M2 are those of §14.8.6.

### 10. Risks

| Risk | Mitigation |
|---|---|
| Core reaches stable before a host has run the delivery surface | the preview exit gate of `docs/versioning.md` ("The delivery contract"): before the first stable release the maintainers decide between stable API and `[Experimental("FLINTEXP002")]` on every public type of `Graspea.Flint.Delivery` and on `ChartTemplate<TRow>.ToJson()` (D9.4 (b)) |
| The manifest bytes, and so the hash, change with every upstream version (`flint` is in them) and when a new SDK line moves spec bytes (`v1.0.1.1-preview.1` typed new members) | by design; documented in `docs/versioning.md`. The test pins the expected manifest text with the version spliced in, not a frozen hash, so a sync pull request stays green unless the minimal test specs serialise differently (§14.8.5 step 6) |
| The `scheme` union blocks 1.0.1 spec files | resolved by M0a: `ChartEncoding.ColorScheme`; code that reads `ChartEncoding.Scheme` keeps compiling with the `FLINT0002` warning and reads null for a list of stops (§14.8.1) |
| Heatmap implicit aggregation and ECharts row order are not checkable from the spec | documented host rules: one row per Heatmap cell, rows in the declared order (§14.6, §14.7) |
| Browser stack limits are **UNVERIFIED** | the 10,000-row guidance, `MaxRows`, server aggregation (§14.2.3) |
| The bundle exports no version | the host injects the version at build time and passes it to the optional check; no version literal in the README code, which would go stale (§14.5 rule 2, §14.6) |
| A browser keeps an old manifest across a deploy | the transport sends the manifest's `ETag` with every page request; the host answers its own error code (`stale` in the samples) and the client restarts from the manifest (§14.4.5, §14.6) |
| The legal group names `.` and `..` collapse in a URL path | the group, the manifest tag and the cursor go in query parameters (§14.4.5) |
| A host writes with a `Utf8JsonWriter` over a response stream | `WriteAsync`, and the XML docs and `docs/delivery.md` say "over a buffer" (§14.4.1) |
| A host draws per page | the drawing policy and its evidence (§14.2.2) |
| A streamed page fails after its first byte | opt-in per chart; the host aborts the response and the client restarts; materialised parts where a clean error document matters (§14.4.6) |
| A host appends an error document after its output has started (a streamed failure or a transport failure) | the docs say it never does: it checks `HasStarted` and aborts (§14.4.2, §14.4.5) |

### 11. Dependencies

M0a; `flint-chart` 1.0.1 pinned in `generator/package-lock.json`; the contract-node harness (`tests/contract-node/flint.js`); the samples artifact flow (`.github/workflows/ci.yml:111-129`).

### 12. Open decisions

The owner decision points are D9.1-D9.12 (§14.10); the ADR records the outcome of each. The owner settled D9.6 (streamed parts in v1) and D9.11 (no npm client, README guidance only) and accepted the recommendation of every other point with ADR 16 on 2026-11-08, so none is open. D9.4 keeps its gate: before the first stable release the preview exit gate of `docs/versioning.md` ("The delivery contract") decides whether the Delivery C# surface becomes stable API or gets `[Experimental("FLINTEXP002")]`.

### 13. Traceability

| Need or goal | Requirements | Design | ADR |
|---|---|---|---|
| N1 | FR-3, FR-10, FR-12, NFR-8 | §14.4.1, §14.4.5, §14.5 | ADR-16.3, ADR-16.7 |
| N2 | FR-2, FR-4, FR-13 | §14.4.1, §14.6 | ADR-16.3, ADR-16.6 |
| N3 | FR-4, FR-5 | §14.4.2, §14.5 rules 4 and 7 | ADR-16.2, ADR-16.3 |
| N4 | FR-7, FR-11, FR-14 | §14.2.3, §14.4.2, §14.4.6 | ADR-16.2, ADR-16.5 |
| N5 | FR-9 | §14.5 rule 5, §14.6 | ADR-16.3 |
| N6 | FR-5, NFR-1, NFR-6 | §14.3, §14.4.5 | ADR-16.1, ADR-16.2 |
| N7 | FR-6 | §14.5 | ADR-16.3 |
| N8 | FR-10 | §14.5 rule 2, §14.6 | ADR-16.3 |
| N9 | FR-13 | §14.6 | ADR-16.6 |
| N10 | FR-8, NFR-2 | §14.7 | ADR-16.4 |
| N11 | FR-13 | §14.6, §14.9 (U2.2) | ADR-16.6 |
| Goal 7 | FR-7, NFR-1, NFR-3, NFR-4, NFR-5 | §14.4.2 | ADR-16.1, ADR-16.2 |
