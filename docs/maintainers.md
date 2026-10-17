# Maintainers

One-off repository and NuGet settings, and the release checklist. The design is in [part 3 §9](design/03-sdk-versioning-runbook-tests-risks.md), [part 4](design/04-companion-upstream-watch-bot.md) and [part 6](design/06-companion-discoverability.md). Commands use the GitHub CLI (`gh`) with an account that administers `graspea/flint-dotnet`.

## What the workflows need

| Workflow | Trigger | Environment | Repository variables | Secrets | `GITHUB_TOKEN` permissions |
|---|---|---|---|---|---|
| `ci.yml` | push to `main`, pull requests, called by `release.yml` | none | none | none | `contents: read` |
| `release.yml` | push of a tag `v*` | `nuget` (job `publish`) | `NUGET_USER` | none: NuGet Trusted Publishing through OIDC | `contents: read`; `id-token: write` in `publish`; `contents: write` in `github-release` |
| `upstream-sync.yml` | daily at 05:17 UTC, `workflow_dispatch` with an optional `version` | `upstream-sync` (jobs `open-pr` and `close-stale`) | `UPSTREAM_SYNC_APP_ID`; optional `FLINT_M2_DONE` | `UPSTREAM_SYNC_APP_KEY` on the `upstream-sync` environment | `contents: read`, `pull-requests: read`, `issues: write` in `detect`; `contents: read` in `generate` and `open-pr`; `issues: write` in `report-failure` |

| Name | Kind | Value | Used by |
|---|---|---|---|
| `nuget` | environment | required reviewer `@graspea/maintainers`; deployment policy "Selected tags: `v*`" | `release.yml`, job `publish` |
| `upstream-sync` | environment | deployment policy "Selected branches: `main`" | `upstream-sync.yml`, jobs `open-pr` and `close-stale` |
| `NUGET_USER` | repository variable | the nuget.org profile name that `NuGet/login` signs in as | `release.yml` |
| `UPSTREAM_SYNC_APP_ID` | repository variable | the client id of the upstream sync GitHub App | `upstream-sync.yml` |
| `UPSTREAM_SYNC_APP_KEY` | environment secret (`upstream-sync`) | the App's private key, the whole PEM | `upstream-sync.yml` |
| `FLINT_M2_DONE` | repository variable, optional | `true` once the fluent builder milestone is complete; without it the report suggests no stable tag before the first stable release | `upstream-sync.yml` (suggested tag) |

No workflow uses a personal access token or a NuGet API key secret. The labels `upstream`, `breaking`, `needs-attention`, `upstream-skip`, `upstream-major`, `upstream-minor` and `upstream-patch` must exist (below).

## Repository settings (once, at creation)

**About.** Description, and the 20 topics (`microsoft` and `mcp*` are never topics):

```
gh repo edit graspea/flint-dotnet \
  --description "Unofficial .NET SDK for Flint (flint-chart), Microsoft's visualization language for AI agents; not affiliated with or endorsed by Microsoft. Build chart specs in C# and emit Flint JSON. Emits JSON only; it does not render charts." \
  --add-topic flint-chart,flint,dotnet,csharp,data-visualization,charts,chart,charting-library,ai-agents,nuget,sdk,visualization,dataviz,vega-lite,echarts,chartjs,plotly,llm,fluent-api,code-generation
```

Set the website after the first publish, when the URL exists: `gh repo edit graspea/flint-dotnet --homepage https://www.nuget.org/packages/Graspea.Flint`. Do not point it at the upstream site.

**Social preview.** Not created yet. Create a 1280x640 PNG under 1 MB, commit it as `docs/assets/social-preview.png` with its editable source next to it, then upload it once under Settings, General, Social preview. Content: "Flint.NET", the line "Unofficial .NET SDK for Flint (flint-chart)", and a short C# snippet next to the Flint JSON it produces. Own artwork only: no Flint or Microsoft logo and no upstream colours. The package icon (`icon.png`, 128x128) is the same mark without text.

**Team.** Create the organisation team `maintainers` (`@graspea/maintainers`), which `.github/CODEOWNERS` names, and give it the Maintain role on the repository.

**Actions.**
- Settings, Actions, General: workflow permissions "Read repository contents"; leave "Allow GitHub Actions to create and approve pull requests" off.
- Turn on "Require actions to be pinned to a full-length commit SHA". Dependabot keeps the pins current (`.github/dependabot.yml`).

**Branch protection for `main`** (a ruleset): pull request required, one approving review, required status checks `verify-generated`, `dotnet`, `samples` and `contract-node` (the jobs of `ci.yml`), no force push, no deletion. Sync pull requests opened by the app run these checks without an approval click.

**Tag ruleset.** A ruleset on tags matching `refs/tags/v*` that restricts creation, update and deletion, with `@graspea/maintainers` as the only bypass. The upstream sync app has `contents: write` and must not be able to push a release tag.

**Security.** Turn on private vulnerability reporting (Settings, Security, Private vulnerability reporting); `SECURITY.md` points reporters there.

**Labels.** The upstream sync workflow applies these; create them up front:

```
gh label create upstream         --color 0e8a16 --description "Upstream flint-chart sync"
gh label create breaking         --color b60205 --description "The upstream release has breaking changes"
gh label create needs-attention  --color d93f0b --description "The sync needs a maintainer"
gh label create upstream-skip    --color 6a737d --description "This upstream version gets no release"
gh label create upstream-major   --color c5def5 --description "Upstream major version bump"
gh label create upstream-minor   --color c5def5 --description "Upstream minor version bump"
gh label create upstream-patch   --color c5def5 --description "Upstream patch version bump"
```

## Upstream sync app (once)

The daily workflow `upstream-sync.yml` opens one pull request per new flint-chart release, authenticated as a GitHub App.

1. Register a GitHub App under the `graspea` organisation (Settings, Developer settings, GitHub Apps): no webhook, no callback URL, installable only on this account. Repository permissions: Contents read and write, Pull requests read and write (Metadata read is implied). Labelling a pull request needs only Pull requests; check on the first run that the labels are applied. No Workflows permission: a sync pull request never touches `.github/`. No organisation or account permissions.
2. Install it on `graspea/flint-dotnet` only.
3. Generate a private key.
4. Create the environment `upstream-sync` (Settings, Environments) with the deployment branch policy "Selected branches: `main`", and add the secret `UPSTREAM_SYNC_APP_KEY` (the private key, whole PEM).
5. Add the repository variable `UPSTREAM_SYNC_APP_ID` with the app's client id.
6. Optional: the repository variable `FLINT_M2_DONE` = `true` once the fluent builder milestone is complete; the sync report uses it to suggest a stable tag.
7. Run the workflow once by hand: `gh workflow run upstream-sync.yml`.

GitHub disables a scheduled workflow after 60 days without repository activity. The weekly Dependabot updates (`.github/dependabot.yml`: actions, NuGet and the generator's npm tooling, never `flint-chart`) are the routine activity; what GitHub counts as activity is not documented, so also check `gh workflow list` once a month and run `gh workflow enable upstream-sync.yml` when it shows as disabled.

## Running the upstream sync

- Each run's summary states the decision: the version it syncs, or "nothing to do", plus the queued, skipped and stale versions and the backport issues.
- One pull request per upstream version, `Upstream: flint-chart <version>` on branch `upstream/flint-chart-<version>`, labelled `upstream` and, as computed, `breaking`, `needs-attention` and `upstream-major`/`-minor`/`-patch`. It is a draft when generation or tests failed. The body is the change report with the suggested tag.
- After a generator fix on `main`, run it again: `gh workflow run upstream-sync.yml -f version=<version>`. The branch and the pull request are force-updated, labels that no longer apply (`breaking`, `needs-attention`) are removed and the draft state follows the new result.
- A dispatch that waits behind a running sync can be replaced by the next scheduled run; check `gh run list --workflow upstream-sync.yml` and dispatch again if it shows as cancelled.
- To skip a version, add `upstream-skip` to its pull request or close it unmerged; the next run moves on. To take it back, remove the label and dispatch with that version.
- A failed run opens or comments on the issue `upstream sync failed: flint-chart <version>` (or `... : detect`).
- A version that fails before a pull request exists (the gate, or a generator crash) blocks the queue: it is picked again every day. Dispatch the next version with `-f version=<next>` and merge it to move past it.
- A stable version published below the pin gets one issue, `upstream backport flint-chart <version>`, and no pull request. Closing the issue records the decision; it is not filed again.

## NuGet publishing (once)

1. On nuget.org, under the account that owns `Graspea.Flint`, add a Trusted Publishing policy: owner = that account, repository `graspea/flint-dotnet`, workflow file `release.yml`, environment `nuget`. No API key is stored in GitHub.
2. Create the environment `nuget` with a required reviewer (`@graspea/maintainers`) and the deployment policy "Selected tags: `v*`".
3. Add the repository variable `NUGET_USER` with the nuget.org profile name that `NuGet/login` signs in as.

## After the first publish (once)

- Set the repository website (above).
- Reserve the `Graspea.` package id prefix: email account@nuget.org from the owner account, naming the account and the prefix. The package already sets an MIT license expression and an embedded icon.
- Optional: submit the repository to Context7 and DeepWiki; check their current configuration keys at submission time.
- Optional: one post in the upstream Discussions "Show and tell" category: unofficial, independent, emits JSON only.
- Optional: a one-line description for the `graspea` organisation.

Before the first tag, have the trademark wording checked once ("unofficial", "not affiliated with or endorsed by Microsoft", "Microsoft's visualization language for AI agents") in the README, the package description and the repository description.

## Release checklist

1. **Sync pull request.** Review the bot's `Upstream: flint-chart <version>` pull request ([part 3 §9.5](design/03-sdk-versioning-runbook-tests-risks.md)) and merge it when CI is green. Never edit it by hand: generator fixes go to `main` in their own pull request, then run the workflow again.
2. **Release pull request** `release: v<version>`, with the tag suggested by the sync report or chosen by you:
   - move every line of `src/Graspea.Flint/PublicAPI.Unshipped.txt` (except `#nullable enable`) to `PublicAPI.Shipped.txt`;
   - set `PackageValidationBaselineVersion` to the previous release of the same `X.Y` line (none for a new line);
   - add the `CHANGELOG.md` entry under the heading `## [<version>] - <YYYY-MM-DD>` (the version without the `v`; for the first release, rename `## [Unreleased]`): the IR diff table from the sync pull request and notes on changed defaults and behaviour. `release.yml` refuses a tag without this entry and uses it as the body of the GitHub release;
   - set the NuGet badge in `README.md`: `vpre` while the newest line is a preview, `v` once it is stable;
   - set both install lines in `README.md` (under the first paragraph and under "Install"): `dotnet add package Graspea.Flint --prerelease` while only previews exist; without the flag once a stable release exists; both lines while the newest line is a preview and a stable release exists.

   The drift check does not cover the badge and the install lines.
3. **Tag** the merge commit of the release pull request and push the tag:

   ```
   git tag v0.5.1.1-preview.1 <merge commit>
   git push origin v0.5.1.1-preview.1
   ```

4. **Publish.** `release.yml` checks the tag against the version grammar (`v` + four parts, the fourth at least 1, optionally `-preview.N` with N at least 1), the first three parts against `FlintUpstream.Version` and the generator pin, the `CHANGELOG.md` entry and that the `flint-chart` version is still on npm; it then runs all of `ci.yml`, packs with the tag's version and package validation, checks the package contents (README, icon, `AGENTS.md`, `catalog/catalog.json`, `THIRD-PARTY-NOTICES.md`, no dependencies) and waits for approval in the `nuget` environment. Approve it; the `.nupkg` and its `.snupkg` are pushed through Trusted Publishing and a GitHub release titled `Flint.NET <version> (flint-chart <upstream version>)` is created from the changelog entry, marked pre-release when the tag has a label. A stable release becomes the repository's Latest release only when no higher stable tag exists.
5. A fix inside a line: branch from the line's latest tag (or `main` while it is still on that line), fix with a failing test first, and tag the next fourth part (or the next `-preview.N`). A fix on an older line does not take the Latest release from a newer line.
