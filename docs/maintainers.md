# Maintainers

One-off repository and NuGet settings, and the release checklist. The design is in [part 3 §9](design/03-sdk-versioning-runbook-tests-risks.md), [part 4](design/04-companion-upstream-watch-bot.md), [part 6](design/06-companion-discoverability.md) and [part 7 §12.9](design/07-spec-to-csharp-tool-and-generator.md). A release publishes three packages at one version: `Graspea.Flint` (the library), `Graspea.Flint.Tool` (the `flint-cs` tool) and `Graspea.Flint.Generators` (the source generator). Commands use the GitHub CLI (`gh`) with an account that administers `graspea/flint-chart-dotnet`.

## What the workflows need

| Workflow | Trigger | Environment | Repository variables | Secrets | `GITHUB_TOKEN` permissions |
|---|---|---|---|---|---|
| `ci.yml` | push to `main`, pull requests, called by `release.yml` | none | none | none | `contents: read` |
| `release.yml` | push of a tag `v*` (annotated, SSH-signed by a key in `.github/allowed_signers`) | `nuget` (job `publish`) | `NUGET_USER` | none: NuGet Trusted Publishing through OIDC | `contents: read`; `contents: read`, `id-token: write`, `attestations: write` in `publish`; `contents: write` in `github-release` |
| `codeql.yml` | push to `main`, pull requests, weekly | none | none | none | `contents: read`, `security-events: write` |
| `dependency-review.yml` | pull requests | none | none | none | `contents: read` |
| `scorecard.yml` | push to `main`, weekly, `workflow_dispatch` | none | none | none | `contents: read`, `actions: read`, `security-events: write`, `id-token: write` |
| `upstream-sync.yml` | daily at 05:17 UTC, `workflow_dispatch` with an optional `version` | `upstream-sync` (jobs `open-pr` and `close-stale`) | `UPSTREAM_SYNC_APP_ID`; optional `FLINT_M2_DONE` | `UPSTREAM_SYNC_APP_KEY` on the `upstream-sync` environment | `contents: read`, `pull-requests: read`, `issues: write` in `detect`; `contents: read` in `generate` and `open-pr`; `issues: write` in `report-failure` |

| Name | Kind | Value | Used by |
|---|---|---|---|
| `nuget` | environment | required reviewers `@graspea/flint-chart-dotnet-maintainers`; deployment policy "Selected tags: `v*`"; administrators cannot bypass | `release.yml`, job `publish` |
| `upstream-sync` | environment | deployment policy "Selected branches: `main`"; administrators cannot bypass | `upstream-sync.yml`, jobs `open-pr` and `close-stale` |
| `NUGET_USER` | repository variable | the nuget.org profile name that `NuGet/login` signs in as | `release.yml` |
| `UPSTREAM_SYNC_APP_ID` | repository variable | the client id of the upstream sync GitHub App | `upstream-sync.yml` |
| `UPSTREAM_SYNC_APP_KEY` | environment secret (`upstream-sync`) | the App's private key, the whole PEM | `upstream-sync.yml` |
| `FLINT_M2_DONE` | repository variable, optional | `true` once the fluent builder milestone is complete; without it the report suggests no stable tag before the first stable release | `upstream-sync.yml` (suggested tag) |

No workflow uses a personal access token or a NuGet API key secret. `codeql.yml`, `dependency-review.yml`, `scorecard.yml` and the provenance step of `release.yml` are free on a public repository only; on a private one they need GitHub Advanced Security (or GitHub Enterprise Cloud for attestations) and fail without it. The labels `upstream`, `breaking`, `needs-attention`, `upstream-skip`, `upstream-major`, `upstream-minor` and `upstream-patch` must exist (below).

## Repository settings (once, at creation)

**About.** Description, and the 20 topics (`microsoft` and `mcp*` are never topics):

```
gh repo edit graspea/flint-chart-dotnet \
  --description "Unofficial .NET SDK for Flint (flint-chart), Microsoft's visualization language for AI agents; not affiliated with or endorsed by Microsoft. Build chart specs in C# and emit Flint JSON. Emits JSON only; it does not render charts." \
  --add-topic flint-chart,flint,dotnet,csharp,data-visualization,charts,chart,charting-library,ai-agents,nuget,sdk,visualization,dataviz,vega-lite,echarts,chartjs,plotly,llm,fluent-api,code-generation
```

Set the website after the first publish, when the URL exists: `gh repo edit graspea/flint-chart-dotnet --homepage https://www.nuget.org/packages/Graspea.Flint`. Do not point it at the upstream site.

**Social preview.** Not created yet. Create a 1280x640 PNG under 1 MB, commit it as `docs/assets/social-preview.png` with its editable source next to it, then upload it once under Settings, General, Social preview. Content: "Flint.NET", the line "Unofficial .NET SDK for Flint (flint-chart)", and a short C# snippet next to the Flint JSON it produces. Own artwork only: no Flint or Microsoft logo and no upstream colours. The package icon (`icon.png`, 128x128) is the same mark without text.

**Team.** The organisation team `flint-chart-dotnet-maintainers` is the code owner of everything (`.github/CODEOWNERS`), the required reviewer of the `nuget` environment and the only actor that may create release tags:

```
gh api --method POST orgs/graspea/teams -f name=flint-chart-dotnet-maintainers -f privacy=closed -f description="Maintainers of graspea/flint-chart-dotnet"
gh api --method PUT orgs/graspea/teams/flint-chart-dotnet-maintainers/memberships/<login> -f role=member
gh api --method PUT orgs/graspea/teams/flint-chart-dotnet-maintainers/repos/graspea/flint-chart-dotnet -f permission=maintain
```

Run the membership line once per maintainer.

**Write access.** Only the maintainers team may write to the repository. Anyone with write access can push a branch whose workflow requests `id-token: write`, `attestations: write` and `contents: write` (the read-only default token permission does not limit what a workflow requests), record build provenance for any file as `graspea/flint-chart-dotnet` and replace the assets of a release. The organisation base permission applies to every repository of the organisation, so it is set to `read` there; direct collaborators and other teams with write access are removed:

```
gh api orgs/graspea --jq .default_repository_permission
gh api --method PATCH orgs/graspea -f default_repository_permission=read
gh api 'repos/graspea/flint-chart-dotnet/collaborators?affiliation=direct' --jq '.[] | "\(.login) \(.role_name)"'
gh api --method DELETE repos/graspea/flint-chart-dotnet/collaborators/<login>
gh api repos/graspea/flint-chart-dotnet/teams --jq '.[] | "\(.slug) \(.permission)"'
gh api --method DELETE orgs/graspea/teams/<team slug>/repos/graspea/flint-chart-dotnet
```

Run the `PATCH` when the first command prints `write` or `admin`, and the `DELETE` lines for each direct collaborator and each team other than `flint-chart-dotnet-maintainers` that the listings show. Afterwards the base permission is `read` (or `none`), the collaborator list is empty and the team list shows only `flint-chart-dotnet-maintainers`. Outside contributors work from forks.

**Immutable releases.** A published release's tag and assets can no longer be changed or replaced, so the attested files attached by `release.yml` stay the ones it built (`gh release create` uploads the assets before it publishes the release):

```
gh api --method PUT repos/graspea/flint-chart-dotnet/immutable-releases
gh api repos/graspea/flint-chart-dotnet/immutable-releases --jq .enabled
```

**Actions.** Read-only default token, no pull request approvals by Actions, every action pinned to a full-length commit SHA (Dependabot keeps the pins current, `.github/dependabot.yml`):

```
gh api --method PUT repos/graspea/flint-chart-dotnet/actions/permissions/workflow -f default_workflow_permissions=read -F can_approve_pull_request_reviews=false
gh api --method PUT repos/graspea/flint-chart-dotnet/actions/permissions -F enabled=true -f allowed_actions=all -F sha_pinning_required=true
```

Once the repository is public, require approval before workflows run for any outside contributor's pull request:

```
gh api --method PUT repos/graspea/flint-chart-dotnet/actions/permissions/fork-pr-contributor-approval -f approval_policy=all_external_contributors
```

**Rulesets.** Two repository rulesets, kept as files under `.github/rulesets/` and applied by hand:

- `main.json` (branch `main`): changes only through pull requests with one approving review by a code owner (stale approvals dismissed, the last push approved by someone else), only commits with a verified signature, the required status checks `verify-generated`, `dotnet`, `samples` and `contract-node` (the jobs of `ci.yml`) and `dependency-review`, merge or squash (no rebase merge, which GitHub cannot sign), no force push, no deletion, no bypass. Sync pull requests opened by the app run these checks without an approval click; their commits are signed by GitHub as the app's bot (`sign-commits` in `upstream-sync.yml`).
- `release-tags.json` (tags `refs/tags/v*`): creation, update and deletion restricted, with the team `flint-chart-dotnet-maintainers` as the only bypass actor. The upstream sync app has `contents: write` and must not be able to push a release tag. The file holds `"actor_id": null` because the team's numeric id exists only once the team does; the command below fills it in, and the API refuses the file as it is.

```
gh api --method POST repos/graspea/flint-chart-dotnet/rulesets --input .github/rulesets/main.json
team_id=$(gh api orgs/graspea/teams/flint-chart-dotnet-maintainers --jq .id)
jq --argjson id "$team_id" '.bypass_actors[0].actor_id = $id' .github/rulesets/release-tags.json \
  | gh api --method POST repos/graspea/flint-chart-dotnet/rulesets --input -
gh api repos/graspea/flint-chart-dotnet/rulesets --jq '.[] | "\(.id) \(.name) \(.target) \(.enforcement)"'
```

To change a ruleset later, edit the file in a pull request, then `gh api --method PUT repos/graspea/flint-chart-dotnet/rulesets/<id> --input <file>` (for `release-tags.json` through the same `jq` filter). A pull request author cannot approve their own pull request, so the `main` ruleset needs two maintainers. While there is only one, add the team as a bypass actor that may merge pull requests without the review, and remove it when a second maintainer joins:

```
jq --argjson id "$team_id" '.bypass_actors = [{"actor_type":"Team","actor_id":$id,"bypass_mode":"pull_request"}]' .github/rulesets/main.json \
  | gh api --method PUT repos/graspea/flint-chart-dotnet/rulesets/<main ruleset id> --input -
```

**Security features (once the repository is public).** All free on a public repository. Secret scanning with push protection, Dependabot alerts and security updates, private vulnerability reporting (`SECURITY.md` points reporters there), and code scanning through the advanced setup in `codeql.yml`, so the default setup stays off (with it on, GitHub rejects the uploads from `codeql.yml`):

```
gh api --method PATCH repos/graspea/flint-chart-dotnet --input - <<'EOF'
{"security_and_analysis":{"secret_scanning":{"status":"enabled"},"secret_scanning_push_protection":{"status":"enabled"}}}
EOF
gh api --method PUT repos/graspea/flint-chart-dotnet/vulnerability-alerts
gh api --method PUT repos/graspea/flint-chart-dotnet/automated-security-fixes
gh api --method PUT repos/graspea/flint-chart-dotnet/private-vulnerability-reporting
gh api --method PATCH repos/graspea/flint-chart-dotnet/code-scanning/default-setup -f state=not-configured
```

Check them:

```
gh api repos/graspea/flint-chart-dotnet --jq .security_and_analysis
gh api repos/graspea/flint-chart-dotnet/vulnerability-alerts --silent && echo "Dependabot alerts on"
gh api repos/graspea/flint-chart-dotnet/automated-security-fixes --jq .enabled
gh api repos/graspea/flint-chart-dotnet/private-vulnerability-reporting --jq .enabled
gh api repos/graspea/flint-chart-dotnet/code-scanning/default-setup --jq .state
```

The first push to `main` after the repository is public runs `codeql.yml` and `scorecard.yml`; the score then appears at `https://scorecard.dev/viewer/?uri=github.com/graspea/flint-chart-dotnet`.

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
2. Install it on `graspea/flint-chart-dotnet` only.
3. Generate a private key.
4. Create the environment `upstream-sync` with the deployment branch policy "Selected branches: `main`", and add the secret `UPSTREAM_SYNC_APP_KEY` (the private key, whole PEM):

   ```
   gh api --method PUT repos/graspea/flint-chart-dotnet/environments/upstream-sync --input - <<'EOF'
   {"can_admins_bypass":false,"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}
   EOF
   gh api --method POST repos/graspea/flint-chart-dotnet/environments/upstream-sync/deployment-branch-policies -f name=main -f type=branch
   gh secret set UPSTREAM_SYNC_APP_KEY --env upstream-sync --repo graspea/flint-chart-dotnet < <private key file>.pem
   ```
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

1. On nuget.org, under the account that owns `Graspea.Flint`, add a Trusted Publishing policy: owner = that account, repository `graspea/flint-chart-dotnet`, workflow file `release.yml`, environment `nuget`. No API key is stored in GitHub. The policy is scoped to the owner account, not to a package id, so the one policy covers every package the account owns: `Graspea.Flint`, `Graspea.Flint.Tool` and `Graspea.Flint.Generators`. All three ids must be owned by that account only; an id owned by another account is refused at the push.

   New package ids: whether nuget.org accepts the *first* push of an id that does not exist yet through a Trusted Publishing key is unverified. Before the first release that includes `Graspea.Flint.Tool` and `Graspea.Flint.Generators`, check it in the nuget.org Trusted Publishing documentation or with the nuget.org support, and record the answer and the date here:

   | Package id | First push through Trusted Publishing | Checked |
   |---|---|---|
   | `Graspea.Flint` | the first release | |
   | `Graspea.Flint.Tool` | not yet checked | |
   | `Graspea.Flint.Generators` | not yet checked | |

   Do not add an API key secret to the repository or the workflow to get around a refusal: every push goes through the approved `publish` job and its short-lived key.
2. Create the environment `nuget`: the maintainers team as required reviewers and the deployment policy "Selected tags: `v*`", so the job that packs and publishes runs only for a release tag and only after a maintainer approves it. `can_admins_bypass` is `false`: it defaults to `true`, which would let a repository administrator or organisation owner outside the team start the job without an approval. Set `prevent_self_review` to `true` once there are two maintainers, so the maintainer who pushed the tag cannot approve its publish alone.

   ```
   team_id=$(gh api orgs/graspea/teams/flint-chart-dotnet-maintainers --jq .id)
   gh api --method PUT repos/graspea/flint-chart-dotnet/environments/nuget --input - <<EOF
   {"reviewers":[{"type":"Team","id":$team_id}],"prevent_self_review":false,"can_admins_bypass":false,"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}
   EOF
   gh api --method POST repos/graspea/flint-chart-dotnet/environments/nuget/deployment-branch-policies -f name='v*' -f type=tag
   gh api repos/graspea/flint-chart-dotnet/environments/nuget --jq .can_admins_bypass
   gh api repos/graspea/flint-chart-dotnet/environments/upstream-sync --jq .can_admins_bypass
   ```

   Both checks must print `false`.

3. Add the repository variable `NUGET_USER` with the nuget.org profile name that `NuGet/login` signs in as.

## After the first publish (once)

- Set the repository website (above).
- Reserve the `Graspea.` package id prefix: email account@nuget.org from the owner account, naming the account and the prefix. The reservation covers `Graspea.Flint.Tool` and `Graspea.Flint.Generators` too. The packages already set an MIT license expression and an embedded icon.
- Optional: submit the repository to Context7 and DeepWiki; check their current configuration keys at submission time.
- Optional: one post in the upstream Discussions "Show and tell" category: unofficial, independent, emits JSON only.
- Optional: a one-line description for the `graspea` organisation.

Before the first tag, have the trademark wording checked once ("unofficial", "not affiliated with or endorsed by Microsoft", "Microsoft's visualization language for AI agents") in the README, the package description and the repository description.

## Release signing key (once per maintainer)

Release tags are annotated and signed with the maintainer's SSH key; `release.yml` stops before anything is built unless the key is listed with the tagger's email in `.github/allowed_signers` both of the tagged commit and of `main`. The file holds no key until the first maintainer adds theirs, which must happen before the first release.

1. A key for signing (an existing SSH key works; `-t ed25519-sk` puts it on a hardware key):

   ```
   ssh-keygen -t ed25519 -C "<email>" -f ~/.ssh/flint_signing
   ```

2. Sign with it, in your clone of the repository; `user.email` is the email of your line in `.github/allowed_signers`:

   ```
   git config gpg.format ssh
   git config user.signingkey ~/.ssh/flint_signing.pub
   git config user.email <email>
   git config commit.gpgsign true
   git config tag.gpgsign true
   git config gpg.ssh.allowedSignersFile "$PWD/.github/allowed_signers"
   ```

3. Add the public key to your GitHub account as a signing key, so your commits show as verified (`main` accepts signed commits only):

   ```
   gh auth refresh -s admin:ssh_signing_key
   gh ssh-key add ~/.ssh/flint_signing.pub --type signing --title "flint-chart-dotnet signing"
   ```

4. Add the line to `.github/allowed_signers` in a signed commit on a branch and open a pull request; another maintainer reviews it (`.github/CODEOWNERS` covers the file):

   ```
   echo "<email> namespaces=\"git\" $(cut -d' ' -f1,2 ~/.ssh/flint_signing.pub)" >> .github/allowed_signers
   ```

   The first key is added the same way before the rulesets are applied, or with the single-maintainer bypass above. To retire a key, remove its line in a pull request. Once that is merged the key is revoked: a tag it signed, also an earlier one whose run is repeated, no longer passes, because the key must also be listed in the file on `main`.

## Release checklist

1. **Sync pull request.** Review the bot's `Upstream: flint-chart <version>` pull request ([part 3 §9.5](design/03-sdk-versioning-runbook-tests-risks.md)) and merge it when CI is green. Never edit it by hand: generator fixes go to `main` in their own pull request, then run the workflow again.
2. **Release pull request** `release: v<version>`, with the tag suggested by the sync report or chosen by you:
   - move every line of `src/Graspea.Flint/PublicAPI.Unshipped.txt` and of `src/Graspea.Flint.Generators/PublicAPI.Unshipped.txt` (except `#nullable enable`) to the `PublicAPI.Shipped.txt` next to it;
   - move the rules of `src/Graspea.Flint.Generators/AnalyzerReleases.Unshipped.md` to `AnalyzerReleases.Shipped.md` under a heading `## Release <version>`, keeping their tables (`### New Rules`, `### Changed Rules`, `### Removed Rules`); the Unshipped file keeps only its header lines;
   - set `PackageValidationBaselineVersion` to the previous release of the same `X.Y` line (none for a new line);
   - add the `CHANGELOG.md` entry under the heading `## [<version>] - <YYYY-MM-DD>` (the version without the `v`; for the first release, rename `## [Unreleased]`): the IR diff table from the sync pull request and notes on changed defaults and behaviour. One entry covers the three packages: a line per change to the tool's or the generator's contract while it is in preview, and, in the release that declares one of them stable, the line that says so ([versioning.md](versioning.md#the-flint-cs-tool)). `release.yml` refuses a tag without this entry and uses it as the body of the GitHub release;
   - set the NuGet badge in `README.md`: `vpre` while the newest line is a preview, `v` once it is stable;
   - set both install lines in `README.md` (under the first paragraph and under "Install"): `dotnet add package Graspea.Flint --prerelease` while only previews exist; without the flag once a stable release exists; both lines while the newest line is a preview and a stable release exists.

   The drift check does not cover the badge and the install lines.
3. **Tag** the merge commit of the release pull request with a signed annotated tag, check it and push it (only the maintainers team may push `v*` tags):

   ```
   git fetch origin
   git tag -s v0.5.1.1-preview.1 -m "Flint.NET 0.5.1.1-preview.1" <merge commit>
   git verify-tag v0.5.1.1-preview.1
   git push origin v0.5.1.1-preview.1
   ```

4. **Publish.** `release.yml` first checks the tag (`generator/scripts/verify-release-tag.mjs`): annotated, its tag object naming the same tag, its SSH signature valid for a key in `.github/allowed_signers` of the tagged commit and of `main`, the tagger email equal to that key's email, the tagged commit on `main`. It then checks the tag against the version grammar (`v` + four parts, the fourth at least 1, optionally `-preview.N` with N at least 1), the first three parts against `FlintUpstream.Version` and the generator pin, the `CHANGELOG.md` entry and that the `flint-chart` version is still on npm; it then runs all of `ci.yml` without secrets and waits for approval in the `nuget` environment.

   The tag check runs from the tagged commit's own `release.yml`, so a tag on a commit with a changed workflow can leave it out: it is advisory. What a tagged commit cannot change is the tag ruleset and the reviewers of the `nuget` environment, so before approving, the reviewer runs the same checks locally against `main` (from a clone of the repository):

   ```
   tag=v0.5.1.1-preview.1
   git fetch origin --tags --force
   git show origin/main:.github/allowed_signers > "${TMPDIR:-/tmp}/flint_allowed_signers"
   git -c gpg.format=ssh -c gpg.ssh.allowedSignersFile="${TMPDIR:-/tmp}/flint_allowed_signers" verify-tag "$tag"
   test "$(git cat-file tag "$tag" | sed -n 's/^tag //p' | head -n 1)" = "$tag" && echo "tag object names $tag"
   git merge-base --is-ancestor "$tag^{commit}" origin/main && echo "on main"
   git diff --exit-code "$tag" origin/main -- .github/ && echo "workflows as on main"
   run=$(gh run list --repo graspea/flint-chart-dotnet --workflow release.yml --branch "$tag" --limit 1 --json databaseId --jq '.[0].databaseId')
   test "$(gh run view "$run" --repo graspea/flint-chart-dotnet --json headSha --jq .headSha)" = "$(git rev-parse "$tag^{commit}")" && echo "run $run is for $tag"
   ```

   Every line must succeed: a good signature by a key listed on `main`, the tag object naming the tag, the commit on `main`, and the run building that commit. `git diff` shows a difference only when `main` changed `.github/` after the tagged commit; `git log "$tag"..origin/main -- .github/` lists those commits, which were reviewed on `main`. Anything else: reject the deployment and delete the tag.

   Approve it (the run page, "Review deployments", or the commands below); only then does job `publish` pack the three packages with the tag's version, the library first (package validation runs on the library), and check each with `scripts/check-nupkg.sh` by its kind:

   | Package | Checked |
   |---|---|
   | `Graspea.Flint` | README, icon, `AGENTS.md`, `catalog/catalog.json`, `THIRD-PARTY-NOTICES.md`; no dependencies |
   | `Graspea.Flint.Tool` (`--tool`) | package type `DotnetTool`, command `flint-cs`, `rollForward` `Major`, the packed `Graspea.Flint.dll` of the tag's version and no other assembly; README, icon, license, notices; no dependencies (a tool carries its own) |
   | `Graspea.Flint.Generators` (`--generators`) | the analyzer under `analyzers/dotnet/roslyn4.14/cs/` and nothing else there, no compiler or System.Text.Json file, `build/` targets and no `buildTransitive/`, `lib/net8.0/_._` and nothing else under `lib/`, exactly one dependency `Graspea.Flint [<version>]`, not a development dependency; README, icon, license, notices |

   It then records Sigstore build provenance for every `.nupkg` and `.snupkg` (the generator package has no symbols) and pushes the library, the tool and the generator, in that order, through Trusted Publishing, each with `--skip-duplicate`. A GitHub release titled `Flint.NET <version> (flint-chart <upstream version>)` is created from the changelog entry with all attested files attached, marked pre-release when the tag has a label. A stable release becomes the repository's Latest release only when no higher stable tag exists.

   When a push fails after an earlier one succeeded (a refused new id, a nuget.org outage), fix the cause and re-run the failed `publish` job; it needs the approval again. It packs, checks and attests again, uploads the files as the artifact `package-<attempt>`, skips the versions already on nuget.org and pushes the rest, and the GitHub release attaches the files of that attempt. For a package the earlier attempt already pushed, the attached file is therefore a rebuild, not the build on nuget.org: that package's provenance is the earlier attempt's attestation, so say so in the release notes when it happens. Never delete or unlist a pushed version to repeat a push: a version number cannot be reused on nuget.org.

   ```
   env_id=$(gh api repos/graspea/flint-chart-dotnet/environments/nuget --jq .id)
   gh api --method POST "repos/graspea/flint-chart-dotnet/actions/runs/$run/pending_deployments" \
     -F "environment_ids[]=$env_id" -f state=approved -f comment="Release v0.5.1.1-preview.1"
   ```

   Afterwards, check what consumers check (`SECURITY.md`, "Verifying a release"):

   ```
   gh release download v0.5.1.1-preview.1 --repo graspea/flint-chart-dotnet --pattern '*.nupkg'
   for id in Graspea.Flint Graspea.Flint.Tool Graspea.Flint.Generators; do
     gh attestation verify "$id.0.5.1.1-preview.1.nupkg" --repo graspea/flint-chart-dotnet \
       --signer-workflow graspea/flint-chart-dotnet/.github/workflows/release.yml \
       --source-ref refs/tags/v0.5.1.1-preview.1 --deny-self-hosted-runners
   done
   ```

   Then check that nuget.org lists all three ids at the version and that the tool installs and runs: `dotnet tool install --global Graspea.Flint.Tool --version 0.5.1.1-preview.1` and `flint-cs --version`.
5. **Fixes.** A fix goes to `main` in a pull request, with a failing test first, and is released from `main` as the next fourth part (or the next `-preview.N`) of the current line. Every release tag is on `main`; `release.yml` refuses a tagged commit that `main` cannot reach, and there are no release branches. A release on an older line would first need a protected `release/X.Y.Z` branch (its own ruleset, with creation limited to the maintainers team) and a tag check that accepts that branch.
