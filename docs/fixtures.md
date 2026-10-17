# Fixtures

The contract tests (`tests/Graspea.Flint.ContractTests`) run against a corpus generated from the pinned `flint-chart` package. Each upstream version has its own folder, and folders are append-only: `tests/fixtures/flint-chart@<version>/`. The design is in [part 2 §7.9](design/02-sdk-serialisation-validation-codegen.md) and [part 3 §10.3](design/03-sdk-versioning-runbook-tests-risks.md). The folder is generated; never edit it by hand.

```
cd generator
npm run fixtures            # this step only
npm run generate            # extract, emit, fixtures
npm run generate -- --check # writes nothing; fails when a generated file would change
```

## Provenance

The source is `TEST_GENERATORS` from `flint-chart/test-data` of the pinned package: 128 groups and 934 test cases, covering all 48 chart types. The tarball ships the generators, not their output. The generators call `Math.random` and read the local time zone, so the step:
- sets `TZ=UTC`;
- replaces `Math.random` with a Park-Miller generator (seed 42) before it imports `flint-chart` or `flint-chart/test-data`;
- runs in a process of its own, because both settings are process-wide.

`fixtures.test.ts` runs the step twice from a non-UTC shell and compares the hashes. `manifest.json` records the version, the integrity hash from the lock file, the case count, the time zone, the seed and `excludedNondeterministic` (empty).

## Mapping (TestCase to ChartAssemblyInput)

- `data = { values: data }`, with row keys in upstream's order.
- `semantic_types[f] = semanticAnnotations[f] ?? metadata[f].semanticType`, for every field in `metadata`. An empty name is dropped, and the key is omitted when no field has a type.
- `chart_spec.encodings[ch] = { field, type, aggregate, sortOrder, sortBy, scheme }` for each `encodingMap` entry that has a `fieldID`:
  - `field` is the name of the matching `fields` entry;
  - `type` is `dtype`;
  - empty values are dropped.

  On 0.5.1, only 4 of the 2430 encodings carry a `dtype`, and none carries the other four members.
- `chart_spec.chartProperties = chartProperties` and `options = assembleOptions`, both when present.
- Dropped: `title`, `description`, `tags`, `fields`, `metadata.type` and `metadata.levels`.

Upstream's own converters are not used, because they write an unread `semantic_annotations` key.

**`metadata.levels` (open question 3, decided).** `levels` is dropped. Upstream's own inputs, compared below, never turn `levels` into an encoding `sortOrder` or an annotation `sortOrder`: every matched input has the same `semantic_types`, and no upstream encoding has a `sortOrder`.

## Formats

| File | Content |
|---|---|
| `cases.jsonl` | One line per case: `{"id":"Semantic Context\|#0","title":"…","chartType":"Bar Chart","defaultBackend":"vegalite","input":{…}}`. The id is `<group>\|#<index>`. The title is there because the contract tests are named `<group>\|<title>` (unique on 0.5.1, as asserted by a generator test). `defaultBackend` is the backend the group belongs to in upstream's gallery: the group prefix (`ECharts: `, `Chart.js: `, `Plotly: `), else Vega-Lite; if that backend lacks the template, the first backend in the fixed order that has it. |
| `expectations.jsonl` | One line per case: `{"id":…,"backends":{"vegalite":{"throws":false,"warnings":[]},…,"excel":{"throws":true,"error":"…"}}}`, for every backend that has the template. `warnings` lists the distinct sorted codes of `_warnings` on the layout backends, and of the top-level `warnings` array on Excel. A throw records the error message, which names the Excel reason. |
| `probe-cases.jsonl` | The crafted cases of `generator/src/extract/probe-cases.ts`, in the `cases.jsonl` format with ids `Probe\|#<n>`: one small document per validator rule that the corpus never makes an assembler throw or warn on (each static-series rule, null and empty data, null and empty encodings, a null channel, an unknown chart type and one without a template on some backends, unknown and `Object.prototype`-named themes, null and primitive rows, invalid and label-valued options including `projectionCenter`, and `Object.prototype`-named semantic types). Only documents the SDK model can express are listed. A case is recorded on every backend that has the template; the unknown chart type and the missing template on every backend. |
| `probe-expectations.jsonl` | The outcomes of the crafted cases, in the `expectations.jsonl` format. |
| `manifest.json` | See Provenance; `probeCases` is the number of crafted cases. |
| `known-failures.txt` | Fixture ids, one per line, whose round-trip is a known failure (`#` starts a comment). The step creates the file empty and never rewrites it. |

## Counts on flint-chart 0.5.1

| Backend | Cases with the template | Throws | Recorded warnings |
|---|---|---|---|
| vegalite | 886 | 0 | `overflow` 76, `negative-values-pyramid` 1 |
| echarts | 866 | 0 | `overflow` 74 |
| chartjs | 743 | 0 | `overflow` 57 |
| plotly | 894 | 0 | `overflow` 63 |
| excel | 695 | 219 | `excel-radar-fill-unsupported` 13 |

The 219 Excel throws by reason:

| Count | Reason |
|---|---|
| 130 | faceting |
| 46 | quantitative x and y required (XY chart) |
| 7 | quantitative y required (line chart) |
| 7 | colour-grouped box and whisker |
| 5 | one quantitative measure axis required (bar chart) |
| 5 | non-initial total points (waterfall) |
| 3 | continuous colour (scatter) |
| 3 | continuous grouping |
| 3 | quantitative y required (box and whisker) |
| 2 | continuous colour (line chart) |
| 2 | final total (waterfall) |
| 1 each | shape encoding; continuous colour (stacked bar); unlegended detail series; negative pyramid values; quantitative y required (area chart); categorical radar x |

No corpus fixture produces `invalid-option-value`; the crafted cases do (`option value invalid`, `option array value`).

## Contract results on flint-chart 0.5.1

- `FixtureRoundTripTests`: 934 of 934 pass.
- `FixtureExpectationTests`: the 934 corpus cases and the 37 crafted cases pass. For each recorded backend: a throw is reported as an Error whose code explains the recorded message (a table maps message prefixes such as `Static series` or `Unknown theme` to codes, and an Excel `validate()` message to `excel_unsupported`, `data_empty`, `data_url_only` or the encodings codes; an unmapped message fails), no non-throwing case gets a throw-class Error, and `invalid_option_value` is reported exactly when upstream records `invalid-option-value`. `Validate(input, null).IsValid` holds exactly when no backend with the template throws. `FixtureSet_MatchesItsManifest` fails when a file is missing or truncated: the case counts must equal `manifest.json`, every case must have an expectation line, and `upstream` must equal `FlintUpstream.Version`.
- The crafted cases found one rule that was wrong: Excel was reported to reject every static series, but it reads an array encoding as an unbound channel and assembles a Line Chart with a static series on `color`.
- `known-failures.txt` is empty: no fixture needed a model or converter fix.

## Cross-check against upstream's shared test data

`generator/src/extract/crosscheck.ts` is a one-off step, not run in CI. It compares the mapped fixtures with the `shared/test-data/*/input.json` files of the upstream git tag `0.5.1`. Those files are not in the npm tarball. The step matches each file to a fixture by the generator group and the title in its `meta.json`.

```
curl -sL https://codeload.github.com/microsoft/flint-chart/tar.gz/refs/tags/0.5.1 | tar xz -C <dir>
cd generator && npm run crosscheck -- <dir>/flint-chart-0.5.1
```

Result of the run on 2026-10-17, against the tag tarball:

- Upstream has 705 `input.json` files; 645 match a fixture.
- 60 do not match:
  - 49 have no generator in `meta.json`: gallery pages for Bullet, Choropleth, Connected Scatter, ECDF, Gantt, Map, Range Area, Slope and Violin.
  - 11 have titles that no longer exist in the 0.5.1 generators: Area Chart 5, Streamgraph 3, Grouped Bar 1, Stacked Bar 1, Omni: Waterfall 1.

| Difference | Matched inputs |
|---|---|
| `chart_spec.baseSize` only upstream (always `{"width":400,"height":300}`) | 645 |
| `options` only upstream (always `{"addTooltips":true}`) | 631 |
| `options` differ (upstream adds `addTooltips: true` to the case's own options) | 14 |
| data values differ | 32 |
| data shape (row count or first-row keys) differs | 11 of those 32 |
| `chartType`, encoding fields, encoding types, `semantic_types`, `chartProperties` | identical in all 645 |

Reading:
- The mapping is semantically equal to upstream's inputs in everything the mapping writes.
- Upstream's converter adds two defaults that the mapping leaves out: `baseSize` and `addTooltips`.
- In 32 cases from 10 groups (Area, Bar, Candlestick, Dates: Hours, Dates: Year, Grouped Bar, Radar, Stacked Bar, Streamgraph, Waterfall), upstream's rows differ from a seeded UTC run. The cause was not investigated; the files were probably written with another random sequence or an earlier generator.
