## ADR 16: chart data delivery for fixed chart definitions

Status: **Accepted** (2026-11-08) by the owner, as owner decision 16 in the index's dated decision table (`00-index.md:32-38`); the owner settled D9.6 (streamed parts in v1, ADR-16.2) and D9.11 (no npm client, README guidance only, ADR-16.6) first, on the same day, and accepted the other outcomes as recorded below. Every decision is implemented on the development branch, as its outcome below states, and unreleased (`CHANGELOG.md`, section `[Unreleased]`). Decision ids ADR-16.1 to ADR-16.7 are numbered after that decision; each names the owner decision points of part 5 (§14.10, D9.n) it settles and the PRD requirements it serves. Design: part 5 of the SDK design ([09-browser-data-delivery.md](09-browser-data-delivery.md), §14). Requirements: the PRD ([10-prd-chart-data-delivery.md](10-prd-chart-data-delivery.md), N1-N11, FR-1 to FR-14, NFR-1 to NFR-9). Baseline: the SDK release `v1.0.1.1-preview.1` (`flint-chart` 1.0.1), plus M0a, the `scheme` union, of the 1.0.1 gaps it leaves; M0b, the Image-Charts backend, is outside this decision (§14.8.1). Paths follow the Paths paragraph of part 5.

### Context

- Owner decision 15 shipped late-bound, asynchronous and streamed rows: `ChartTemplate<TRow>`, `Bind`, `BindAsync`, `BindStream`, `RowSource.FromBatches` and `FlintJson.SerializeAsync` (§13; `CHANGELOG.md`, section `[0.5.1.1-preview.1]`).
- Hosts that render with `flint-chart` in the browser need the data-free specs of a page on mount and the rows of a chart group only when it is shown, under fixed definitions, on `flint-chart` 1.0.1, over HTTP and over a WebSocket RPC with their own envelope (PRD §4, N1-N11).
- The uncommitted 0.5.1 draft of part 5 added only `ChartTemplate<TRow>.ToJson()`, documented `template.Bind(page).Data!.Values!` as the page route, left the response shape to the host, and rejected `WriteRows` members, an SDK-owned envelope and a JavaScript test. The owner now asks for a data API, synchronous and asynchronous, considered as a separate package, with an integration plan, implemented on 1.0.0/1.0.1 for fixed definitions.
- Facts that bound the choice: everything a separate package would need is internal (§14.3); upstream derives types, sizes, series, categories and colours from the rows present, so a chart draws once on complete rows (§14.2.2); ECharts and Chart.js output, and in 1.0.1 the Vega-Lite output, hold functions, so assembly stays in the browser (§14.2.1); the bundle exports no version (§14.1); a 1.0.1 spec with an array `scheme` cannot be read before M0a (§14.1).

### Decision drivers

1. The host needs N1-N11 on 1.0.1 (PRD §4).
2. Zero runtime dependencies, `net8.0`, trimming and AOT safety (NFR-1; `00-index.md:20`).
3. One four-part version for every package of a release (`docs/versioning.md:42-49`).
4. No `InternalsVisibleTo` (decision 14 for the generator, assumed for any companion package).
5. `Build()`/`ToJson()` never validate (NFR-2; `00-index.md:12`).
6. Byte identity of delivered rows with the streamed document, and with `Bind` for rows whose converters write JSON tokens (FR-7).
7. The measured plumbing cost of a further package (§14.3, option B).
8. The neutral public audience of the SDK (`00-index.md:20`).

### Candidate designs

Three complete designs were compared against the drivers (scores 1-10):

| Criterion | Minimal core | Host ergonomics | Risk-first |
|---|---|---|---|
| Meets N1-N11 on 1.0.1 | 8 | 9 | 9 |
| Minimal surface and package count | 9 | 5 | 6 |
| Correctness against the verified facts | 8 | 8 | 9 |
| Contract stability and versioning | 7 | 8 | 9 |
| AOT, trimming and failure modes | 9 | 8 | 9 |
| Implementability in this repository | 9 | 6 | 6 |
| **Total** | **50** | **44** | **48** |

- **Minimal core** (the base): one package, nothing in `release.yml`, every failure before the first byte, a page that can be written again. Its weak calls were deferring `ToJson()`, a `JsonArray` per chart where internal access allows UTF-8 directly, and no cap option.
- **Host ergonomics**: the best host samples (HTTP and WebSocket) and a cap, at the cost of a fourth package with its whole plumbing and a 100,000-row default cap, which is policy.
- **Risk-first**: the most careful on facts (fail early for a template without row metadata, a null cap default, additive wire rules), at the same package cost; its `group/chart` ids made one chart shown in two groups two charts.

The outcome takes the minimal core design and grafts: `ToJson()` now (host ergonomics, risk-first and the 0.5.1 draft; ADR-16.7); an optional `MaxRows` with a null default and `ChartRowLimitException` (risk-first, host ergonomics; ADR-16.5); the missing-metadata failure in the handle constructor at start-up (risk-first's probe, moved earlier; ADR-16.2); the per-chart `input` wrapper (risk-first; ADR-16.3); `DataPage.WriteAsync(Stream)` as a buffered finisher (host ergonomics, risk-first; ADR-16.2); the opt-in streamed part written by `WriteAsync` only (owner decision D9.6; ADR-16.2); the reference merge in `tests/contract-node` (all three; ADR-16.6).

### Decisions

#### ADR-16.1 The data API lives in core, in namespace `Graspea.Flint.Delivery`

Settles D9.1, D9.4 and D9.12. Serves FR-1 to FR-6, NFR-1 and NFR-9. Design §14.3.

| Option | Buys | Costs |
|---|---|---|
| **A. Core namespace** | internal access (spec bytes, row writer, pooled buffer, `ValidationIssue` constructors); no spec parse and no `JsonArray` per chart; no public row writer; one release process and one AOT publish | about 9 types and 40 members in core; the C# surface freezes with core at stable unless marked `[Experimental]` |
| B. Public primitives in core + a `Graspea.Flint.Delivery` package | a package boundary and an independent stability declaration after core goes stable | public `WriteRows*` and an own result type; an exact `[X]` dependency through a nuspec or pack target; four `release.yml` edits, the workflow test, `docs/maintainers.md`, `SECURITY.md`, the README and the AOT sample; identical rebuilds on most upstream releases; package id availability and first Trusted Publishing push **UNVERIFIED** |
| C. `Graspea.Flint.AspNetCore` | minimal-API convenience | fails N6 |
| D. An npm client from this repository | a typed, tested client | the four-part version is not npm semver; a second lockfile and sync path; first publish **UNVERIFIED** |

**Outcome: A.** The C# surface follows the library's preview rule (D9.4 (a)); `[Experimental("FLINTEXP002")]` on every public type of `Graspea.Flint.Delivery` and on `ChartTemplate<TRow>.ToJson()` is the fallback if core goes stable before a host has run it (D9.4 (b)). `docs/versioning.md` ("The delivery contract") writes this as the preview exit gate: before the first stable release the maintainers decide whether the Delivery C# surface becomes stable API or gets the attribute. Public `WriteRows`/`WriteRowsAsync` stay internal (D9.12). Streamed parts in v1 (ADR-16.2) strengthen A: a separate package would also need a public streaming primitive with an after-row hook.

**Consequences.** No release plumbing changes (`release.yml`, `scripts/check-nupkg.sh`, nuspecs, `SECURITY.md` and `docs/maintainers.md` untouched, §14.8.2). Wire v1 becomes the third documented contract of `docs/versioning.md`, next to the tool's and the generator's. The delivery surface is 47 of the 59 `PublicAPI.Unshipped.txt` lines (46 in `Graspea.Flint.Delivery` and `ChartTemplate<TRow>.ToJson()`; the other 12 are the `scheme` union of M0a). The core `Description` gains one clause; "emits JSON only" stays true. **Split trigger** for B: a wire v2 is needed while core is stable, or a host needs the members on another cadence; the split then publishes `WriteRows*` next to `ToJson()`, moves the profile codes to an own result type and keeps the wire shape.

#### ADR-16.2 Rows are materialised per chart by default; a streamed part is opt-in in v1

Settles D9.6 (owner, 2026-11-08: streaming in v1). Serves FR-4, FR-5, FR-7, FR-11, FR-14, NFR-3, NFR-4, NFR-5 and NFR-6. Design §14.4.2, §14.4.6.

| Option | Buys | Costs |
|---|---|---|
| **(a) Per-chart UTF-8 through the internal row writer** (`StreamedRows` into a pooled buffer, copied to an exact array) | every failure before the page's first byte; rows-or-withheld decided per chart; one copy of the rows' JSON; byte identity through the shared `RowWriter<TRow>` | memory of about 1× the rows' JSON per chart plus the output |
| (b) Per-chart `JsonArray` (`Bind(rows).Data!.Values!`, the 0.5.1 draft's route) | no new internal code | one spec parse and a node tree per chart |
| (c) A streamed envelope | memory independent of the row count | a metadata, predicate or source failure lands after the first byte: truncated JSON, no withheld code, a second failure model |
| (d) Public `WriteRows*` into a host-owned writer | a host serializer writes the envelope | rules for the writer's position and options; a synchronous flush per row on a `Stream`-backed writer; an array left open when a row throws |

**Outcome: (a) by default, (c) as an opt-in part.** A template without row metadata fails in the `ChartHandle<TRow>` constructor, at start-up. `Rows`/`RowsAsync` materialise; `DataPage.WriteAsync(Stream)` writes such a page with `WriteAsync` and one `FlushAsync`, so no host needs a `Utf8JsonWriter` over a response stream. `ChartHandle<TRow>.Stream(IAsyncEnumerable<TRow>)` gives a single-use part that only `WriteAsync` writes, draining a pooled buffer whenever it holds at least 16 KiB, after each streamed row and after each materialised part, as `FlintJson.SerializeAsync` drains; the page claims itself and every streamed part before the first byte; the synchronous finishers refuse such a page; no public after-row hook.

**Consequences.** Every row, limit, invariant and cancellation failure of a page without streamed parts happens before the first byte (NFR-3); a transport failure, the stream's `WriteAsync` or `FlushAsync` throwing, can come at any time, including at the final flush, and propagates unchanged (the token passed to `WriteAsync` reaches only the stream, so a cancellation observed there is such a failure); the SDK writes nothing after a failure, and a host never appends an error document after its output has started. Such pages and their parts are immutable and written any number of times; one row writer per call (NFR-4); no synchronous I/O in `WriteAsync` (NFR-6). The memory bound per materialised page is the rows' JSON plus the output (NFR-5), bounded per part by `MaxRows` or by the host's cap; a streamed page holds its largest single row or materialised part plus about 32 KiB of buffer, whatever its row count. The rows of a part equal byte for byte the `data.values` elements of the streamed document, and those of `Bind` for rows whose converters write JSON tokens; a converter that writes raw JSON can differ from `Bind` in insignificant whitespace and is then JSON-equal (FR-7). A streamed part moves value, source, cap and cancellation failures into `WriteAsync`, possibly after the first byte: the stream holds a prefix of the page, empty before the first 16 KiB drain and otherwise truncated JSON with no withheld code, so the host aborts a started response and the client restarts from the first page (§14.4.6). It is a second failure model, confined to the parts that opt in.

#### ADR-16.3 Wire contract v1, owned by the SDK

Settles D9.2, D9.5 and D9.10. Serves FR-2, FR-3, FR-6, FR-9, FR-10, NFR-7 and NFR-8. Design §14.5.

| Option | Buys | Costs |
|---|---|---|
| **(a) SDK-owned v1**: manifest `contract`/`flint`/`charts`/`groups` with a per-chart `input`; page `contract`/`group`/`charts` with `rows` xor `withheld` and `next` only when another page follows; error `contract`/`error.code`; no counts | one tested shape for every host and transport; a version check; a strong validator for the manifest | a contract to keep frozen; one more level (`input`) for the client |
| (b) Host-owned shape with docs guidance (the 0.5.1 draft) | no contract in the SDK | every host re-invents the envelope, withholding and errors |
| (c) Group-scoped chart ids (`group/chart`) | ids unique by construction | one chart shown in two groups becomes two charts and two specs |
| (d) A version member inside each `ChartAssemblyInput` | no envelope member | a wire-format change for every document (`docs/versioning.md:35`) |

**Outcome: (a)**, with the noun `ChartGroup`/`groups`/`group` (D9.2), every chart of a group on every page (D9.5) and the `input` wrapper (D9.10). Chart ids are host-chosen, match `[A-Za-z0-9_.-]+` and are unique per manifest; one handle may sit in several groups. `next` is never empty.

**Consequences.** Additive-only once released; a removal, rename or change of meaning bumps `contract` (NFR-7). Clients ignore unknown members. `Hash` (the lowercase hex SHA-256 of the stored manifest bytes) stays outside the document; it is deterministic for the same definitions under the same SDK version and runtime serialisation (NFR-8), no canonical hash across runtimes or across semantically equal documents is promised, and it changes by design with every upstream version, because `flint` is in the bytes, and when a new SDK line moves spec bytes. The pages carry no manifest hash: the reference HTTP transport sends the manifest's `ETag` as a query parameter of every page request, next to the group and the cursor, which never go in a path segment because URL parsing collapses the legal group names `.` and `..`; the host answers its own error code on a mismatch, so a deploy under a live browser needs no wire change (§14.4.5, §14.6). The SDK version, counts, totals, more-pages flags, messages, timestamps, sizes and backend names are absent: the merge needs none of them, and the manifest stays byte-stable for caching. `docs/versioning.md` gains "The delivery contract"; the first release declares v1 stable in the CHANGELOG.

#### ADR-16.4 Fixed-definition checks are Info codes of `ChartGroup.Validate`

Settles D9.3. Serves FR-8 and NFR-2. Design §14.7.

| Option | Buys | Costs |
|---|---|---|
| **(a) Five Info codes (`fixed_type_missing`, `fixed_order_missing`, `fixed_aggregate`, `fixed_host_override`, `fixed_size`) from `ChartGroup.Validate` only** | one result type; constructible issues (core); manifest paths; no new output for existing consumers | a host fails start-up on the `fixed_` prefix with a line of LINQ |
| (b) An own result type | separation from upstream-mirroring codes | a second result type |
| (c) Host-owned checks | nothing in the SDK | every host re-derives the 1.0.1 `sortBy`, type and size rules |
| (d) Warnings in `ChartValidator` | visible everywhere | breaks the severity rule: upstream drops nothing here; changes existing output |

**Outcome: (a).** `IsValid` is unchanged; `ValidationResult.ThrowIfAny(params string[] codes)` is deferred.

**Consequences.** `ChartValidator.Validate` and `template.Validate` report none of the five. The codes join `docs/validation-codes.md` under their own heading, each citing its 1.0.1 fact. Known gaps stay documented host rules: an ordinal field with a built-in sequence, or a date drawn as a category, is a false positive of `fixed_order_missing`, with no exemption in this release, and following the code switches that inferred order off (`src/core/resolve-semantics.ts:739`), so an explicit order lists every category and the server writes rows in that order (ECharts Heatmap); Heatmap implicit aggregation (one row per cell) and ECharts row order are not checkable from the spec. Template-dependent facts wait for a public catalog API.

#### ADR-16.5 Row cap: optional, no default

Settles D9.8. Serves FR-11 and N4. Design §14.4.2, §14.2.3.

| Option | Buys | Costs |
|---|---|---|
| (a) None | no surface | the memory of an asynchronous source is unbounded |
| **(b) Optional `MaxRows`, null by default** | the host chooses its policy per chart, enforced per part; the failure comes before any byte from `Rows`/`RowsAsync`, and out of `WriteAsync` for a streamed part (ADR-16.2) | one constructor parameter and one exception type |
| (c) Default 10,000 | safe by default | throws in production for a host that never chose the figure |
| (d) Default 100,000 | matches `assembleForBackend` | policy in the SDK, above the documented guidance |

**Outcome: (b).** `MaxRows` caps one part, the rows of one `Rows`, `RowsAsync` or `Stream` call, not a chart across a paging sequence; a value below 1 throws `ArgumentOutOfRangeException`. An internal counting wrapper around the source throws `ChartRowLimitException` (an `InvalidOperationException`, carrying `Chart` and `Limit`) when the source yields row `MaxRows + 1`, before that row is serialised, so the limit wins over a bad excess row, and the count cannot overflow; from `Rows`/`RowsAsync` nothing is kept, from a streamed part the rows already drained stay in the stream (ADR-16.2).

**Consequences.** Policy stays with the host, which maps the exception to its own error code and, when it pages, caps the whole chart across its pages itself (`docs/delivery.md` shows a two-page budget). The 10,000-row figure is documentation unless a host configures `MaxRows`.

#### ADR-16.6 JavaScript: README guidance, tested, no package

Settles D9.7 and D9.11 (owner, 2026-11-08: no package, README guidance only). Serves FR-13 and N8, N9, N11. Design §14.6, §14.8.5 step 12.

| Option | Buys | Costs |
|---|---|---|
| **(a) Browser guidance in the README, the same code as `tests/contract-node/delivery-merge.mjs`, tested by `delivery.test.js` on 1.0.1** | the guidance is pinned against real 1.0.1 output; no package | the client copies about 40 lines |
| (b) An npm package | a typed client | version mapping, a second lockfile, an **UNVERIFIED** first publish |
| (c) No JavaScript and no test (the 0.5.1 draft) | nothing to maintain | the envelope the SDK now owns goes untested in the browser's language |

**Outcome: (a).** No npm client is planned.

**Consequences.** "This package emits JSON only" holds; the 0.5.1 draft's "no JavaScript delivery test" is reversed, because the SDK now owns the envelope. The code has no `flint-chart` version literal, which would go stale outside the README's generated sections: the host injects the version at build time and passes it to `fetchManifest`, and `undefined` skips the check. `delivery.test.js` pins the README block as equal to `delivery-merge.mjs`; it assembles each chart on the four layout backends that have its template, asserts that set is non-empty, compares warnings, not output, and covers the reference merge with synthetic pages (prototype-like ids, `.` and `..` as group names, withheld after rows, unknown members, mismatches, errors after an earlier page, transport and parse failures).

#### ADR-16.7 `ChartTemplate<TRow>.ToJson()` ships now

Settles D9.9. Serves FR-12 and FR-3. Design §14.4.3.

| Option | Buys | Costs |
|---|---|---|
| **(a) `ToJson()` now** | the data-free document for any host, file or `flint-chart-mcp` check; mirrors `ChartBuilder.ToJson()` | one decode per call |
| (b) Defer; the manifest only | no new member | hosts outside the manifest keep the `Bind` plus `Data = null` workaround |
| (c) A public UTF-8 form on the template | no decode | a second name frozen for a small value the manifest already holds as bytes |

**Outcome: (a).** The manifest writes the same stored bytes raw and never decodes them. A `ChartHandle<TRow>` refuses a template whose document has a top-level `data` member, which a `Configure` action that writes `Extensions["data"]` can produce, so the manifest stays data-free.

**Consequences.** One more template member in `PublicAPI.Unshipped.txt`; `Template.ToJson()` reaches source-generated types with no new generated member.

### Outcome summary

| Id | Decision | Outcome | Settles | Requirements | Design |
|---|---|---|---|---|---|
| ADR-16.1 | Where the data API lives | core namespace `Graspea.Flint.Delivery`; preview rule, `FLINTEXP002` as fallback; no public `WriteRows*` | D9.1, D9.4, D9.12 | FR-1 to FR-6, NFR-1, NFR-9 | §14.3 |
| ADR-16.2 | Row path and failure model | per-chart materialised UTF-8 by default; opt-in streamed part written only by `WriteAsync` (owner) | D9.6 | FR-4, FR-5, FR-7, FR-11, FR-14, NFR-3 to NFR-6 | §14.4.2, §14.4.6 |
| ADR-16.3 | Wire contract | SDK-owned v1 as §14.5 | D9.2, D9.5, D9.10 | FR-2, FR-3, FR-6, FR-9, FR-10, NFR-7, NFR-8 | §14.5 |
| ADR-16.4 | Fixed-definition checks | five Info codes from `ChartGroup.Validate` only | D9.3 | FR-8, NFR-2 | §14.7 |
| ADR-16.5 | Row cap | optional `MaxRows` per part, null by default | D9.8 | FR-11 | §14.4.2 |
| ADR-16.6 | JavaScript | README guidance, tested; no package (owner) | D9.7, D9.11 | FR-13 | §14.6, §14.8.5 |
| ADR-16.7 | Spec accessor | `ToJson()` now | D9.9 | FR-12, FR-3 | §14.4.3 |

### Consequences

- **Positive.** One documented envelope for every host and transport; specs cached and revalidated by hash; every row, limit, invariant and cancellation failure of a materialised page before the first byte, and flat memory for streamed parts; rows byte-identical to the streamed document, and to `Bind` for rows whose converters write JSON tokens; no change to existing output, wire bytes for documents the library could read before, or release plumbing; the one existing member that changes is `ChartEncoding.Scheme`, deprecated under `FLINT0002` by M0a; trimming- and AOT-safe without new annotations.
- **Negative.** 47 public API lines in core for delivery (and 12 for the `scheme` union of M0a) that freeze with core at stable unless the preview exit gate marks them experimental; a frozen wire contract to maintain; memory per page of about the rows' JSON plus the output; definitions that rely on row order or one row per Heatmap cell depend on host discipline.
- **Neutral.** The hash moves with every upstream version and when a new SDK line changes spec bytes (`v1.0.1.1-preview.1` did); hosts that draw per page stay outside the documented flow; a host that streams a part accepts truncated output on a late failure.

### Confirmation

The decisions are implemented when the release criteria of §14.8.6 hold: the tests of §14.8.5 green on `net8.0` and `net10.0`, the AOT publish clean, `delivery.test.js` green on 1.0.1, the PublicAPI drift check green, and `docs/delivery.md`, the versioning section, the validation-code rows and README rule 9 in place. The code, the tests and the docs are on the development branch; the `net8.0` runs and the Native AOT publish are checked by CI (`.github/workflows/ci.yml:89-90`) before the release.

### Follow-ups

- The preview exit gate of the Delivery C# surface: stable API or `[Experimental("FLINTEXP002")]`, before the first stable release (D9.4).
- The removal of `ChartEncoding.Scheme` in the release after `1.0.1.1-preview.2` (`docs/deprecations.md`, `FLINT0002`).
- `fixed_order_missing` exemptions for built-in sequences and dates drawn as categories (§14.11).
- M4 `Graspea.Flint.AspNetCore`, on the trigger of §14.11.
- `ValidationResult.ThrowIfAny`, `RowSource.FromPages` (D8.5 (c)), `Rows(JsonArray)`, a page for several groups, the manifest hash inside pages, per-chart error-to-withheld mapping, and a public catalog API for template-dependent profile facts (§14.11).
- The upstream proposals of §14.9 stand: declared category domains (U1.1), pins on every backend (U1.3), value colours on ECharts and Chart.js (U1.4), JSON-safe ECharts output (U2.1), escaping in ECharts formatters (U2.2), a runtime version export (U4) and large-data safety (U5).
