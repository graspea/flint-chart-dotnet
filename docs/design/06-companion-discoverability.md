## Companion, part 3: discoverability (topics, tags, metadata)

### Discoverability (topics, tags, metadata)

The goal is that a .NET developer who has heard of Flint finds the package in one search, and that nobody who finds it mistakes it for a Microsoft product or a renderer. This section is the single owner of the package `Description`, `PackageTags`, GitHub topics, the README title line and first paragraph, the release title and body wording and the tag format. Everything below is driven by four verified facts (2026-10-17):

1. `flint-chart` is the only unambiguous name. It has 0 GitHub topic uses and 0 NuGet tag uses, while `flint` alone is crowded (FLINT number theory library, the `flint` npm linter, a smart-contract language, a router, and the NuGet packages `Flint` and `Flint.AI`).
2. GitHub repository search matches only the name, description and topics by default; the README needs `in:readme`, and the website field is not listed as searchable (GitHub docs, searching-for-repositories).
3. nuget.org requires every query term to match some field, applies no stemming ("flint chart" returns 1 hit, "flint charts" returns 0, verified live), prefix-matches only the last term, indexes tags as whole lowercase keywords and splits unscoped queries on punctuation, so hyphenated tags are only found through `tags:`. The field weights (PackageId 9, Tags 5, everything else 1) come from runtime configuration that is not in the NuGetGallery repository and are UNVERIFIED; downloads dominate between equal matches. The README is not indexed.
4. While only `-preview.N` versions exist, `dotnet package search`, the NuGet CLI, the Package Manager Console and the search API hide the package by default; the nuget.org web search still shows it (NuGet docs, finding-and-choosing-packages; the VS "Include prerelease" default is UNVERIFIED).

No .NET Flint SDK exists today on GitHub or NuGet, so the first-mover position on `flint-chart`, `vega-lite` (0 C# repos) and `Graspea.Flint` is free.

#### Where each item lives

| Item | Lives in | Set by |
|---|---|---|
| Repository description (About), website, topics | GitHub repository settings | `gh repo edit graspea/flint-dotnet -d "..." -h "..." --add-topic a,b,c` at repository creation (documented in `CONTRIBUTING.md` under "Repository settings") |
| Social preview image | GitHub repository settings (upload); source file committed at `docs/assets/social-preview.png` | Manual upload, once |
| PackageTags, Authors, Copyright, license, icon, README, project and repository URLs | `Directory.Build.props` (metadata half; the build half is SDK design §3) | Repository |
| PackageId, Title, Description | each `.csproj` (`Graspea.Flint.AspNetCore` needs its own) | Repository |
| README title line, first paragraph, install command, badges | `README.md`; the same file ships as `PackageReadmeFile`; the `flint-chart` badge version and the "tracks" sentence rewritten by the generator; the NuGet badge kind (`v`/`vpre`) and the `--prerelease` install line set in the release PR (SDK design §9.6 step 2) | Repository |
| Release title and body wording, prerelease flag | `release.yml` final job, specified in SDK design §9.6; wording per this section | Repository |
| Tag prefix | one `env: TAG_PREFIX` line in `release.yml`, read by the sync report | Repository |
| Generated-code handling for Linguist | `.gitattributes` (row owned by SDK design §3; reason stated here) | Repository |
| Docs keywords and headings | `README.md`, `docs/` | Repository |
| Organisation description | GitHub organisation settings (`graspea` is verified but has an empty description) | Manual, optional |

#### GitHub repository: description and website

**Repository name** `graspea/flint-dotnet` is itself searched and already contains the tokens `flint` and `dotnet`. Keep it.

**Description. Decided (decision 10 wording):**

> Unofficial .NET SDK for Flint (flint-chart), Microsoft's visualization language for AI agents; not affiliated with or endorsed by Microsoft. Build chart specs in C# and emit Flint JSON. Emits JSON only; it does not render charts.

229 characters (re-count before use). The 350-character limit is UNVERIFIED (desktop/desktop#19465 only), so staying under 250 is deliberate. The description carries every token that GitHub search would otherwise miss: `flint`, `flint-chart`, `.NET`, `SDK`, `C#`, `chart`, `charts`, `visualization`, `AI agents`, `JSON`. It states the non-renderer scope in the last sentence because the backend topics below would otherwise attract people looking for a Vega-Lite or ECharts renderer. "Microsoft" appears only as nominative use next to the disclaimer; it is never a topic, tag or logo.

**Website field:** `https://www.nuget.org/packages/Graspea.Flint`. The field is displayed and not listed as searchable, so it should be the call to action, not a keyword carrier. Set it right after the first publish (the URL 404s until then). If a docs site is ever created, point the field there and keep the NuGet link in the README badge row. Do not point it at upstream's homepage; that implies affiliation.

#### GitHub topics (20 of 20, ordered by value)

**Decided (20 topics, decision 11):**

```
flint-chart, flint, dotnet, csharp, data-visualization, charts, chart, charting-library, ai-agents, nuget, sdk, visualization, dataviz, vega-lite, echarts, chartjs, plotly, llm, fluent-api, code-generation
```

| Position | Topic | Reason (counts are all repos / C# repos; observed on 2026-10-17, unsourced) |
|---|---|---|
| 1 | `flint-chart` | Only unambiguous anchor; 0/0 today, so we own the topic page and every `topic:flint-chart` query |
| 2 | `flint` | What people type; 75/0, so `topic:flint language:C#` returns only us |
| 3-4 | `dotnet`, `csharp` | The two most common C# topics, needed for the `.NET` and `C#` topic pages |
| 5 | `data-visualization` | Shared with upstream (67669/374) |
| 6-7 | `charts`, `chart` | The two largest non-curated chart topics (4525/298 and 4055/292); ScottPlot and OxyPlot use both spellings |
| 8 | `charting-library` | Shared with upstream, apache/echarts and plotly.js; 473/126 |
| 9 | `ai-agents` | Shared with upstream; 120676/766, where "C# + Flint" is a distinct niche |
| 10-11 | `nuget`, `sdk` | Describe the artefact |
| 12-13 | `visualization`, `dataviz` | `visualization` 29551/332; `dataviz` has its own topic page |
| 14-17 | `vega-lite`, `echarts`, `chartjs`, `plotly` | The Flint backends; almost no C# competition (0, 8, 64, 6). These are the topics that mislead if the description does not say "emits JSON only" |
| 18 | `llm` | Upstream's npm keyword |
| 19 | `fluent-api` | Describes the builder API (M2); 424/224 |
| 20 | `code-generation` | The generator is a real part of the repo; 7228/370 |

`type-safe` (920/17) was swapped for `chart`, because `chart` and `charts` are separate topic pages with ten times more C# traffic. `mcp`, `mcp-server` and `model-context-protocol` are never used as topics or tags (no own MCP server, decision 9; upstream's `mcp-server` topic describes `flint-chart-mcp`, which this SDK does not replace). `microsoft` is never a topic, tag or logo (decision 10). Whether any of the topics above is a curated (github/explore) topic is UNVERIFIED and does not change the list. GitHub topic rules: at most 20, lowercase letters, numbers and hyphens, 50 characters.

#### NuGet package metadata

Facts that shape the fields: Title is "not used for any display purposes" (NuGet nuspec reference) but is still indexed; Summary is deprecated and unsupported by MSBuild, so it is not set; `PackageTags` is semicolon-separated, up to 4000 characters, no count limit; `Graspea.Flint` is tokenized into `graspea` and `flint` in the ID field, so "flint" is already covered by the ID and the tags only need to cover the other words people add.

**PackageId:** `Graspea.Flint` (free, verified). Later `Graspea.Flint.AspNetCore`, after the first stable release (not a gate for it).

**Title:** `Flint.NET` (searchable, never displayed; keep it to the product name so it does not drift from the README).

**Description. Decided (decision 10 wording); the text for releases before M2:**

> Unofficial, type-safe .NET SDK for Flint (flint-chart), Microsoft's visualization language for AI agents. Build Flint ChartAssemblyInput chart specs in C# with a typed model and serialize them to JSON with System.Text.Json; there are no other dependencies. flint-chart compiles that JSON to Vega-Lite, ECharts, Chart.js, Plotly or Excel charts. This package emits JSON only; it does not render charts. Not affiliated with or endorsed by Microsoft.

447 characters. With M2, "with a typed model" becomes "with a typed model or a fluent builder". The text deliberately contains both `chart` and `charts`, `ChartAssemblyInput`, `visualization`, `JSON`, `SDK`, `.NET`, `C#` and all five backend names, because any term the user types that matches nowhere removes the package from the result set and the README is not indexed.

**PackageTags. Decided (23 tags, decision 11), exact string:**

```
flint;flint-chart;flintchart;chartassemblyinput;chart;charts;charting;visualization;dataviz;data-visualization;vega;vegalite;vega-lite;echarts;chartjs;plotly;semantic;fluent;sdk;json;ai;llm;agent
```

23 tags. Rules applied: single-word tags first, because unscoped queries are split on punctuation and only `tags:vega-lite` finds a hyphenated tag (verified live with Syncfusion's `smith-chart`); both `chart` and `charts`, and both `vegalite` and `vega-lite`, because there is no stemming; `flintchart` catches the one-word spelling that neither the ID nor a hyphenated tag matches; `chartassemblyinput` is the one term an agent or a reader of the upstream docs will search for verbatim; `agent` rather than `agents` because the description already contains "agents" and a trailing "agent" prefix-matches. `dotnet` and `csharp` are left out: every package on nuget.org is .NET, and the description already contains ".NET" and "C#", which satisfies the all-terms-required rule. `excel`, `mcp` and `microsoft` are left out on purpose (huge mismatched pools, no own MCP server, trademark exposure). `fluent` stays in the tag list from the first release because tags are cheap and the builder arrives with M2. For `Graspea.Flint.AspNetCore` append `;aspnetcore;minimal-api` in its `.csproj`.

**Directory.Build.props (metadata half; the build properties are in SDK design §3):**

```xml
<Project>
  <!-- PackageId, Title and Description live in each .csproj (Graspea.Flint.AspNetCore needs its own). -->
  <PropertyGroup>
    <Authors>GRASPEA</Authors>
    <Company>GRASPEA s.r.o.</Company>
    <Copyright>Copyright (c) 2026 GRASPEA s.r.o.</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/graspea/flint-dotnet</PackageProjectUrl>
    <RepositoryUrl>https://github.com/graspea/flint-dotnet</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <PackageIcon>icon.png</PackageIcon>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageTags>flint;flint-chart;flintchart;chartassemblyinput;chart;charts;charting;visualization;dataviz;data-visualization;vega;vegalite;vega-lite;echarts;chartjs;plotly;semantic;fluent;sdk;json;ai;llm;agent</PackageTags>
  </PropertyGroup>
  <ItemGroup Condition="'$(IsPackable)' != 'false'">
    <None Include="$(MSBuildThisFileDirectory)icon.png" Pack="true" PackagePath="\" />
    <None Include="$(IntermediateOutputPath)README.pack.md" Pack="true" PackagePath="README.md" />
    <None Include="$(MSBuildThisFileDirectory)docs/package/AGENTS.md" Pack="true" PackagePath="\" />
    <None Include="$(MSBuildThisFileDirectory)src/Graspea.Flint/Generated/catalog.json" Pack="true" PackagePath="catalog/" />
  </ItemGroup>
</Project>
```

The packed README is the marker-free copy that an MSBuild target writes before pack (AI section §2); `AGENTS.md` and `catalog.json` are the two agent artifacts of AI section §2. `PackageLicenseExpression` and an embedded `PackageIcon` are best practices for packages under a reserved prefix, so they are set from the first publish.

#### README title line and first paragraph

The README is read in three places with different rules: GitHub (searched only with `in:readme`), nuget.org (Markdown only; badge images from img.shields.io; relative paths UNVERIFIED; a README fix needs a new package version), and web search engines, where "Flint.NET" alone returns FLINT and Flint, Michigan. So the `<h1>` must pair the product name with `flint-chart`, and the first paragraph must contain the terms, the scope statement and the exact install command.

```markdown
# Flint.NET: unofficial .NET SDK for Flint (flint-chart)

[![NuGet](https://img.shields.io/nuget/vpre/Graspea.Flint)](https://www.nuget.org/packages/Graspea.Flint)
[![flint-chart](https://img.shields.io/badge/flint--chart-0.5.1-blue)](https://github.com/microsoft/flint-chart/releases/tag/0.5.1)
[![CI](https://github.com/graspea/flint-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/graspea/flint-dotnet/actions/workflows/ci.yml)

Type-safe C# model for [Flint](https://github.com/microsoft/flint-chart) (`flint-chart`),
Microsoft's visualization language for AI agents. Flint.NET builds semantic chart specs in .NET and
emits Flint `ChartAssemblyInput` JSON; `flint-chart` then compiles that JSON to Vega-Lite, ECharts,
Chart.js, Plotly or Excel charts. This package emits JSON only and does not render charts. It is an
independent project, not affiliated with or endorsed by Microsoft.

    dotnet add package Graspea.Flint --prerelease

Flint.NET 0.5.1.x tracks flint-chart 0.5.1; the first three version parts always equal the pinned
upstream release.
```

Notes: with M2 the first line becomes "Type-safe C# model and fluent builder for ...". `vpre` is the shields.io badge that shows prerelease versions, which matters while only previews exist, and the `--prerelease` flag is the single most important line for discoverability while the package is preview-only (fact 4). The badge kind and the install line depend on the label of the tag, which is known only in the release PR (the maintainer may force preview or stable at tag time, decision 6), so they are set by hand in the release PR (SDK design §9.6 step 2) and never by the generator: after the first stable release the badge becomes `v` and `--prerelease` is dropped; while the newest line is a preview after a stable release exists, both install lines are shown. The generator rewrites only the `flint-chart` badge version and the "tracks" sentence from `generator/package.json` so they never drift; `verify-generated` does not cover the two hand-set lines. A brief legal sanity check of the trademark wording ("unofficial", "not affiliated with or endorsed by Microsoft", nominative use of "Microsoft") is advisable before the first publish (decision 10).

#### Social preview image and package icon

- **Social preview:** 1280x640 PNG under 1 MB on a solid background (GitHub docs, customizing-your-repositorys-social-media-preview; 640x320 minimum). Content: "Flint.NET" as the headline, "Unofficial .NET SDK for Flint (flint-chart)" as the subline, and a two-column visual of a short C# snippet on the left and the resulting Flint JSON on the right. Own artwork only: no Flint logo, no Microsoft logo, no upstream colours, because the upstream README's trademark section requires that use of Microsoft trademarks or logos "must not cause confusion or imply Microsoft sponsorship" (decision 10). Commit the source at `docs/assets/social-preview.png` (and the editable source next to it), upload it once in repository settings.
- **Package icon:** `icon.png`, 128x128 PNG at the repository root, the same mark as the social preview without text (NuGet recommends 128x128, max 1 MB; `iconUrl` is deprecated).

#### Release title, body and tag format

- **Release title:** `Flint.NET 0.5.1.1-preview.1 (flint-chart 0.5.1)`. The upstream version in the title is what a reader scanning the releases list needs, and it matches upstream's own habit of putting the version in the release name ("Flint 0.5: ThemeSpec").
- **Body:** first line `Tracks flint-chart 0.5.1 (npm).` with a link to the upstream release (or the `gitHead` commit when no tag exists), then the CHANGELOG entry for the release (no generated notes). The job that creates the release, with `--prerelease` when the tag carries a label, is specified in SDK design §9.6. Every preview release is therefore marked pre-release, which means the repository shows no "Latest" badge until the first non-prerelease (GitHub REST docs, get-the-latest-release). Link the releases page from the README install section so this does not confuse people.
- **Tag format (default applied): `v`-prefixed**, for example `v0.5.1.1-preview.1`. It is the dominant .NET convention, keeps our tags visually distinct from upstream's bare `0.5.1` when both appear in a sync PR, and `release.yml` strips it with `${GITHUB_REF_NAME#v}` before comparing the first three parts to `FlintUpstream.Version`. The prefix lives in one `env: TAG_PREFIX: v` line in `release.yml`, which the sync report reads for its suggested tag and after-merge command, so a change of style is a one-line change. The alternative, upstream's bare `0.5.1.1-preview.1`, would equal the NuGet version byte for byte but needs a version-pattern trigger glob instead of `v*`.
- Whether search engines index release pages usefully is UNVERIFIED; the README and the NuGet page are the pages to optimise, not releases.

#### Leaving the preview label

Rule: SDK design §8.1 (decided; `-preview.N` increments on the same four-part version, the stable exit needs M2 done plus two consecutive clean syncs, BREAKING rows restart a line at `-preview.1`, and the maintainer may force either label at tag time). Discoverability consequences: the awesome-dotnet listing requires "Stable", so it waits for the first non-preview release; the NuGet badge kind and the install line are set in the release PR (SDK design §9.6 step 2), as described in the README section above.

#### Docs keywords

Use one vocabulary everywhere (README, `docs/`, XML doc comments, release notes), taken from upstream's own terms so that people who read the Flint README find the same words here: "Flint", "flint-chart", "chart spec", "semantic types", "semantic chart", "ChartAssemblyInput", "visualization language", "AI agents", and the backend names "Vega-Lite", "ECharts", "Chart.js", "Plotly", "Excel". Add our own: ".NET", "C#", "SDK", "typed model", "fluent builder" (from M2), "System.Text.Json", "zero dependencies", "generated from flint-chart". Headings in the README are the phrases people search with `in:readme`: "Install", "Quick start", "Typed model", "Fluent builder", "Validate against flint-chart", "Version mapping to flint-chart", "Regenerate from a new flint-chart release". The arXiv paper (2607.20775) and the Microsoft Research blog post are linked once from "About Flint"; do not quote their text.

#### Repository language and generated C# (default applied)

Linguist excludes files marked `linguist-generated` from language statistics and `linguist-detectable` cannot bring them back; it does not detect `.g.cs` or `// <auto-generated>` on its own. The generated model and tables will be the bulk of the C# lines, so marking them generated could flip the repository language to TypeScript (the generator), which would drop the repository from `language:C#` searches and change the colour bar that .NET developers scan first. Default: do not mark generated `.cs` files; mark only the JSON data (`tests/fixtures/** linguist-generated=true` and `generator/ir/*.json linguist-generated=true`, the `.gitattributes` rows in SDK design §3). The cost is that upstream-sync pull requests show the full generated C# diff.

#### Preview-only visibility (follows from the decided version scheme)

Until the first non-prerelease, the package is invisible to `dotnet package search`, the NuGet CLI, the Package Manager Console and the search API default, and probably to the Visual Studio browse tab (UNVERIFIED). It stays visible on the nuget.org website. The hidden state ends with the first stable release (SDK design §8.1) and recurs only for a line restarted at `-preview.1` after a BREAKING sync. A SemVer 2 label such as `-preview.1` also hides the package from clients older than NuGet 4.3 / VS 2017 15.3, which no longer matters in practice. Mitigations already built in: the `--prerelease` install line at the top of the README, the `vpre` badge, and the exact ID in the description.

#### Prefix reservation and other one-off steps after the first release

- Reserve the `Graspea.` prefix (default applied): email account@nuget.org with the owner name and the prefix once `Graspea.Flint` is live. The criteria are owner identification, a non-generic prefix and confusion risk (NuGet docs, id-prefix-reservation); license metadata and an embedded icon are best practices for packages under the prefix, not hard conditions, and `Directory.Build.props` already sets both. The docs do not require the ID to exist first, but publishing first makes the request concrete. The existing package owned by the `graspea` account falls under the prefix too. `Flint.` cannot be reserved (generic word, upstream's name, not the owner's identity).
- Post once in upstream's Discussions "Show and tell" category (the category exists; the upstream README has no list of ports, so this is the only upstream-side pointer available). Wording: unofficial, independent, emits JSON only.
- Fill the `graspea` organisation description (currently empty) with one neutral line, so the owner card on the repository page is not blank.

#### Defaults applied in this section (say if you object)

- Tag format: `v`-prefixed, prefix held in one line of `release.yml`.
- Linguist: generated C# stays in language statistics; only `tests/fixtures/**` and `generator/ir/*.json` are marked generated.
- `Graspea.` prefix: reserve right after the first publish.
- Topics are set at repository creation; `mcp*` and `microsoft` are never topics or tags.
- Release title and body wording per this section; the `gh release create` job is specified in SDK design §9.6.
