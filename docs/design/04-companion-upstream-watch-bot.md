## Companion, part 1: upstream watch bot

### Upstream watch bot

#### Purpose and scope

The bot keeps the SDK one step behind the `flint-chart` npm package and nothing else. It polls the registry, regenerates the committed IR, C#, fixtures, the marked README sections and the whole `docs/package/AGENTS.md` for the next upstream version, runs the full test suite, writes a human-readable change report and opens a pull request. A maintainer merges, opens the release PR and pushes the tag; the tag-driven release workflow does the rest. The bot never publishes to NuGet, never pushes to `main` and never creates a tag (decision 7).

Scope boundary: this section covers the release tag only as the hand-off from the merged PR to the release workflow. The tag format itself, repository topics, NuGet tags and all other metadata are owned by the Discoverability section; the bot reads the tag prefix from `release.yml` and never defines it. The version grammar, the preview/stable rule and `release.yml` are owned by the SDK design (§8.1, §9.6); this section applies them and never restates them.

The bot is `upstream-sync.yml`, the only automated upstream watcher in the repository (decision 7: one workflow, not two). This section replaces SDK design §9.1 and owns version selection, branch and label names, identity and PR contents.

#### What it watches

The only reliable signal is the npm registry. Upstream GitHub tags cover six of the twelve published versions (`0.5.1`, `0.5` rather than `0.5.0`, `0.4.0`, `0.3.0`, `0.2.1`, `0.1.1`) and releases are created by hand with inconsistent names, so they are used for links only, and a generated "link at tag X.Y.Z" falls back to the `gitHead` commit URL when no tag exists. There is no separate spec version: `ChartAssemblyInput`, `theme_spec` and the upcoming `interaction_spec` carry no version or schema field, and the only schema id (`EXCEL_ARTIFACT_SCHEMA = 'flint.excel.chart/v1'`) labels the Excel backend's output. The package version is the spec version.

| Source | Used for |
|---|---|
| `GET https://registry.npmjs.org/flint-chart` (full packument; `max-age=300`, ETag) | `dist-tags`, `versions`, per-version `time`, `gitHead`, `dist.attestations` ([registry docs](https://github.com/npm/registry/blob/main/docs/responses/package-metadata.md)) |
| `GET /-/npm/v1/attestations/flint-chart@X` and `npm audit signatures` | provenance gate ([npm audit](https://docs.npmjs.com/cli/v11/commands/npm-audit)) |
| `generator/package.json` | the exact pin (`dependencies["flint-chart"]`), which is the SDK's current upstream version; the drift check keeps it equal to `FlintUpstream.Version` |
| `https://github.com/microsoft/flint-chart/compare/<gitHead old>...<gitHead new>` | compare link in the report |
| `https://raw.githubusercontent.com/microsoft/flint-chart/<gitHead>/CHANGELOG.md` and `.github/release-notes/<ver>.md` | changelog text in the report (the npm tarball ships no CHANGELOG; release notes exist only for 0.3.0, 0.5.0, 0.5.1) |
| `https://raw.githubusercontent.com/microsoft/flint-chart/<gitHead>/agent-skills/flint-chart-author/SKILL.md` | git blob SHA compared with the previous release for the "skill changed upstream" line in the report (input from the AI section) |

It does not watch `flint-chart-mcp` (its version comes from its own package.json and the upstream workflow can publish either package alone; the versions have matched 12 of 12 times so far, UNVERIFIED for the future; the gate script only resolves whether `flint-chart-mcp@<ver>` exists, for the README `mcp-commands` section, AI section §5), the Python port (not on PyPI) or upstream GitHub tags and releases. The CHANGELOG claims 0.2.1 and 0.2.2 were never published while the registry has both; the registry is the source of truth.

npm cannot push notifications (`npm hook` was removed in npm 11.0.0-pre.0, npm/cli PR #7835), so the bot polls.

#### Trigger and schedule

- `schedule`: once a day at an off-the-hour minute in UTC (`17 5 * * *`). Scheduled runs execute only on the default branch and may be delayed or dropped under load ([events reference](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)). The upstream cadence (12 versions in 7 weeks, then nothing for 64 days) does not justify a shorter interval; a burst of several versions within hours (0.1.0 to 0.1.2 took about 6 hours) is handled by the one-version-per-PR rule, which takes one day per queued version.
- `workflow_dispatch` with an optional `version` input, so a maintainer can run it on demand or target a specific version (for example after removing an `upstream-skip` label, or after landing a generator fix on `main`).
- `concurrency: { group: upstream-sync, cancel-in-progress: false }` so a manual run and a scheduled run never race. The group keeps one pending run, and a newer trigger replaces it, so a dispatch waiting behind a running sync can be cancelled by the next scheduled run; `docs/maintainers.md` says how to check. `queue: max` would keep it, once actionlint accepts the key. Dispatches do not get a group of their own: two runs would force-push the same `upstream/flint-chart-<ver>` branch.

#### Identity and token

Decided (2026-10-17): **the bot is a GitHub App**, authenticated through `actions/create-github-app-token` ([App tokens in workflows](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/making-authenticated-api-requests-with-a-github-app-in-a-github-actions-workflow)).

- The token is minted only in the `open-pr` job and narrowed to `permission-contents: write` and `permission-pull-requests: write` (labelling a pull request needs only the latter); `close-stale` mints one with `permission-pull-requests: write`. The `detect` and `report-failure` jobs keep the default `GITHUB_TOKEN` with `issues: write`; `open-pr` holds only `contents: read` on the default token, for the checkout, and every write goes through the App token.
- The App's client id is a repository variable (`UPSTREAM_SYNC_APP_ID`); the private key is a secret of the `upstream-sync` environment (`UPSTREAM_SYNC_APP_KEY`), whose deployment branch policy allows `main` only. In v3 of the action `app-id` is deprecated in favour of `client-id`.
- A PR opened or force-pushed with an App installation token starts the `pull_request` CI run immediately, with no approval click ([GITHUB_TOKEN docs](https://docs.github.com/en/actions/concepts/security/github_token)). The bot already runs `dotnet test` and the generator's `node:test` suite before opening the PR and prints the outcome in the PR body, so CI is the second opinion.
- The App never creates tags. The maintainer pushes the release tag by hand (decision 7). A tag ruleset on `v*` restricts tag creation to maintainers, so the App's `contents: write` cannot reach `release.yml`.
- Register the App once under the `graspea` organisation with the two permissions above, install it on `graspea/flint-chart-dotnet` only.

Rejected options (decision 2026-10-17): the default `GITHUB_TOKEN` with "Allow GitHub Actions to create and approve pull requests", because `opened`, `synchronize` and `reopened` events from a `GITHUB_TOKEN`-made PR create runs in an approval-required state, so every daily force-update would need another click ([GITHUB_TOKEN docs](https://docs.github.com/en/actions/concepts/security/github_token), [changelog 2026-06-11](https://github.blog/changelog/2026-06-11-bot-created-pull-requests-can-run-workflows-if-approved/)); and a fine-grained PAT, because the PR author becomes the token's owner, who then cannot approve the PR, and GitHub's own guidance prefers an App for long-lived integrations. The `UPSTREAM_SYNC_TOKEN` PAT from the earlier SDK design draft is gone.

Renovate and Dependabot were evaluated and rejected: neither can run the generator after the bump (UNVERIFIED details: Mend-hosted Renovate's `postUpgradeTasks` restriction on the free tier; Dependabot's lack of a post-update hook and read-only follow-up token), and both stop updating a PR once another author commits to it. If either is ever enabled for `generator/`, `flint-chart` must be ignored there, or they will open competing PRs.

#### End-to-end flow

Three jobs plus a failure reporter. The split is deliberate: job 2 executes code from the upstream package and runs with a read-only token and no secrets; job 3 opens the PR with the App token and runs no npm code.

1. **Detect** (job `detect`, `contents: read`, `pull-requests: read`, `issues: write`).
   `node generator/scripts/pick-upstream-version.mjs` fetches the packument, reads the pin from `generator/package.json`, lists the bot's own PRs once (`gh pr list --label upstream --state all --json number,headRefName,state,mergedAt,labels,author,isCrossRepository`, keeping only pull requests a bot opened from this repository, so a labelled human or fork pull request on a bot-like branch counts for nothing) and computes:
   - `S` = stable versions (no prerelease part, not deprecated) greater than the pin;
   - `skipped` = versions in `S` whose branch `upstream/flint-chart-<ver>` has a closed, unmerged PR or an open PR labelled `upstream-skip`;
   - `version` = `min(S \ skipped)` (or the `workflow_dispatch` input, which must be in `S`; a dispatched version overrides `skipped`);
   - `queued` = the rest of `S \ skipped`, for the PR body;
   - `stale` = open bot PRs whose version is no longer in `S` (deprecated or unpublished upstream), closed by number in `close-stale`;
   - `below_pin` = stable versions newer by publish time but lower by semver (the backport case). For each, the job searches for an issue in any state titled `upstream backport flint-chart <ver>` with label `upstream`, and creates it with labels `upstream`, `needs-attention` when none exists; a closed one records the maintainer's decision.
   Every version string is validated against `^\d+\.\d+\.\d+$` before it is exported as a job output. The step summary states the decision on every run, including "nothing to do" and the skipped list, so a silent day is distinguishable from a broken poll.

2. **Generate** (job `generate`, `contents: read`, `persist-credentials: false`, no secrets).
   1. Gate: `npm ci --ignore-scripts`, then `npm run install-upstream -- "$FLINT_VERSION"` in `generator/`. The gate is `scripts/install-upstream.mjs`, the single implementation shared with the manual path (SDK design §7.2, §9.2). It runs, in this order: `npm install --save-exact --ignore-scripts "flint-chart@<ver>"` (no package code runs with `--ignore-scripts`), then `npm audit signatures` (verifies the registry signature and the Sigstore provenance bundle), then `node scripts/verify-provenance.mjs <ver>`, which reads the now-verified attestation and requires repository `microsoft/flint-chart`, workflow `.github/workflows/npm-publish.yml` and a `gitCommit` equal to the packument's `gitHead` (true for every version from 0.1.1 to 0.5.1; 0.1.0 was a manual publish). The provenance `ref` is `refs/heads/main`, never a tag, so no tag ref is checked. Identity claims are checked only after signature verification, because before it they are unverified text. A gate failure stops the run; nothing from the package has executed yet. Last, the script resolves the `flint-chart-mcp` version for the README `mcp-commands` section (`npm view flint-chart-mcp@<ver> version`; when that version does not exist, the newest `flint-chart-mcp` version not above the pin) into `generator/mcp-pin.json`, so that `npm run generate` stays offline. Installing an upstream version is never part of `npm run generate`.
   2. After the gate, three steps:
      - (a) Forward-compat probe on the old model: `npm run fixtures`, which writes `tests/fixtures/flint-chart@$FLINT_VERSION`, then `dotnet test tests/Graspea.Flint.ContractTests --filter Category=RoundTrip`; the pass/fail counts go into the report. This is the first point where upstream code runs.
      - (b) `npm run generate`: extract, emit (IR, C#, `PublicAPI.Unshipped.txt`, the marked README sections and the whole `docs/package/AGENTS.md`, `catalog.json`) and the fixtures with expectations recorded from the five assemblers (SDK design §7.2).
      - (c) Generation failures (collision, unmapped construct, kind mismatch) are captured verbatim to `$OUT/generate.log` without aborting the job, so the PR carries the message.
   3. Build and test: `dotnet build`, `dotnet test` (unit, plus contract tests over `tests/fixtures`, offline: round-trip and recorded assembler outcomes; no Node, no `flint-chart-mcp`) and `npm test` in `generator/` (`node:test`). Both run with `continue-on-error: true`; their outcome is recorded, not fatal, so a breaking upstream change still produces a reviewable PR.
   4. Report: `node generator/scripts/upstream-report.mjs` writes `pr-body.md` and `result.json` (test outcomes, labels, queued versions, suggested tag) from `ir-diff` (`generator/src/diff.ts`) between `git show main:generator/ir/flint-ir.json` and the new IR, the new `retired.json` entries, the forward-compat counts, the tail of `generate.log` on failure (at most 200 lines and 20 000 characters, after the after-merge steps, because `create-pull-request` cuts a body above 65 536 characters without notice), the fixture set, the generated-file stats, the SKILL.md blob SHA comparison and the `flint-chart-mcp` row from `generator/mcp-pin.json` (`needs-attention` when `flint-chart-mcp@<ver>` does not exist and the `mcp-commands` section fell back to an older version). Severity rows map onto IR paths. Registry and CHANGELOG text are written to the file, never expanded inside `${{ }}` in a `run:` step.
   5. Package: all outputs go to `$RUNNER_TEMP/out`, outside the checkout, so they never enter the patch. `git add -N .` then `git diff --binary > "$RUNNER_TEMP/out/upstream.patch"`; upload the directory as one artifact.

3. **Open PR** (job `open-pr`, `environment: upstream-sync`, `contents: read` on the default token, for the checkout only; every write uses the App token).
   Mints the App token, checks out `main`, downloads the artifact to `$RUNNER_TEMP/out`, applies `git apply "$RUNNER_TEMP/out/upstream.patch"` to the working tree only (`create-pull-request` commits the whole index after adding `add-paths`, so a staged path would bypass the allowlist), runs `generator/scripts/check-sync-paths.mjs`, which fails on any changed or new path outside the allowlist, then runs `peter-evans/create-pull-request` with `sign-commits: true` (the commit is created through the API with the App token and signed by GitHub as the App's bot, because `main` requires signed commits; the action then ignores `author` and `committer`), `branch: upstream/flint-chart-<ver>`, `body-path`, `labels` from `result.json`, `draft` set when tests or generation failed, and `add-paths: generator/package.json,generator/package-lock.json,generator/mcp-pin.json,generator/ir/**,generator/retired.json,src/**,tests/fixtures/**,README.md,docs/package/AGENTS.md`, which is every path the gate and the generator write (`src/**` covers `PublicAPI.Unshipped.txt`); the job env `SYNC_PATHS` holds the list once for both. On an existing pull request the action updates only the title and body, so a last step removes `breaking` and `needs-attention` when `result.json` no longer has them and runs `gh pr ready` or `gh pr ready --undo` to match `draft`. No `npm` or `dotnet` command runs in this job. Job `close-stale` closes each PR in `stale` by number with a comment ("no longer a stable published version above the pin").

4. **Failure reporter** (job `report-failure`, `issues: write`, `if: failure()`), see Failure handling.

**Severity** is computed in the report, never read from upstream semver: registry diffs show that 0.2.1 to 0.2.2 (a patch) contained one BREAKING change, 0.2.2 to 0.3.0 four and 0.4.1 to 0.5.0 two, while 0.4.0 to 0.4.1 was 39 additive items. Rules the report applies per row:
- BREAKING: template, channel, property, option value or semantic type removed or renamed; input type narrowed; a property made required; an enum value removed.
- behavioural: a default value changed; validation tightened without a type change; `EXCEL_ARTIFACT_SCHEMA` or any other exported constant changed.
- additive: anything new and optional; a type widened.
- cosmetic: description or documentation text only.

The row-level severities roll up to the PR labels and feed the suggested tag. BREAKING rows choose the label (the line restarts at `-preview.1`) under SDK design §8.1; the release kind (REGENERATE, BREAKING, held back) is decided separately under SDK design §9.4, where removals are kept as `[Obsolete]` and the release is still a regenerate release.

**Labels**: `upstream` on every bot PR; `breaking` when any row is BREAKING; `needs-attention` when tests or generation failed, or when `flint-chart-mcp@<ver>` does not exist (AI section §5); `upstream-skip` on a held-back version (SDK design §9.4), applied by the owner; optionally `upstream-major`, `upstream-minor`, `upstream-patch` derived from the upstream semver bump, informational only. No other label (`upstream-sync`, `upstream-blocked`) exists. Pre-create all labels in the repository (UNVERIFIED whether adding a missing label through the API creates it).

**Suggested SDK tag.** The report builds it from the tag prefix (`TAG_PREFIX`, read from the single `env:` line in `release.yml` so the bot and the release trigger cannot disagree; `v` under the Discoverability default), the upstream version and the rule in SDK design §8.1, which it applies and does not restate. It computes three inputs: `stable_exists` (a tag without a label exists); `m2_done` (repository variable `FLINT_M2_DONE`; M2 also covers the `IDictionary<,>` freeze, so no file in the repository can stand for it); and `clean_sync_streak` (consecutive merged sync PRs with no change to `generator/src/**` or `generator/overrides.json` between them). It suggests `<prefix>X.Y.Z.1` when `stable_exists && !breaking`, or when `!stable_exists && m2_done && clean_sync_streak >= 2`; otherwise `<prefix>X.Y.Z.1-preview.1`. The fourth part always starts at 1. A fourth input, `restarted_line_clean` (a stable release exists, `<prefix>X.Y.Z.1` for the pin is not tagged yet, the current pin's sync PR had BREAKING rows and no change to `generator/src/**` or `generator/overrides.json` has landed on `main` since its merge), makes the report also print the stable tag `<prefix>X.Y.Z.1` for the current pin: a line restarted at `-preview.1` leaves preview at the same four-part version by the maintainer's tag-time decision (SDK design §8.1). The body prints "suggestion only; the maintainer may force preview or stable at tag time".

**Release hand-off.** After merge the maintainer opens the release PR (SDK design §9.6 step 2) and pushes the tag by hand on its merge commit; the PR body prints the command with the suggested tag. The `release.yml` guards (grammar, three-part match against `FlintUpstream.Version`, `npm view` existence check, OIDC publish through the `nuget` environment) are specified in SDK design §9.6.

#### Idempotency

- One fixed branch per upstream version: `upstream/flint-chart-<ver>`. A rerun with no changes exits silently; a rerun with changes (for example after a generator fix on `main`) force-updates the branch and the open PR (create-pull-request `concepts-guidelines.md`). Fixes are never committed to the bot branch: `overrides.json` and generator fixes land on `main` in their own PR, then the workflow is dispatched and regenerates.
- Skipping happens in `detect`, before the version is chosen, so a skipped or closed version never blocks the versions after it. A version skips only through its pull request: one that fails before a pull request exists (the gate, or a generator crash with no output) is picked again every day and blocks the queue until a maintainer dispatches the next version and merges it. Without this the bot would pick the same skipped version every day and sync nothing until the pin moved.
- `queued` versions are only listed; the next one is handled on the next run after the pin moves (that is, after merge). Every upstream version therefore gets its own PR, its own merge commit and its own tag, which is what the versioning policy requires.
- A bot PR for a version that upstream later deprecates or unpublishes is closed with a comment by `open-pr` (`stale` list), so no PR stays open without a note.
- The `concurrency` group serializes scheduled and manual runs.

#### The `upstream-skip` label

The constraint "every upstream release gets an SDK release" has one explicit exception, exercised only by the owner: a version that upstream has deprecated, unpublished, or that is broken in a way that no SDK release should mirror, or a held-back version under SDK design §9.4 (a patch that would need a breaking change; an issue labelled `upstream`, `needs-attention` tracks it for the next upstream minor). Adding `upstream-skip` to the bot's PR (or closing it unmerged) records that exception; `detect` then moves on to the next version. The run summary lists skipped versions on every run so the exception stays visible. Removing the label and dispatching the workflow with that version re-opens the path.

#### Prerelease and backport handling (defaults applied)

- **Upstream prereleases: ignored, listed in the run summary.** Upstream has never published one and has no `next` dist-tag. npm 11 refuses to publish a prerelease without an explicit `--tag` (npm/cli PR #7910) and only moves `latest` implicitly for a version above the current latest (PR #7939); the upstream workflow passes no `--tag`, so a prerelease would need a visible change to that workflow. This matches "the SDK follows npm releases only". A maintainer who wants to try one uses the manual path on a branch that is never merged (SDK design §9.4).
- **Backports** (a new stable version below the pin, for example 0.4.2 after 0.5.1): one deduplicated issue, no PR. The maintainer decides whether to create a `release/X.Y` branch and run the bot against it with `workflow_dispatch`. Nothing today suggests upstream will start maintenance branches (UNVERIFIED), so automation for this case is not built.
- **Deprecated versions** are treated as absent (and their open bot PR is closed, see Idempotency).

#### Failure handling

| Where it fails | What happens |
|---|---|
| Registry unreachable, version string invalid, `gh pr list` fails (job `detect`) | run fails; `report-failure` opens or comments on the issue `upstream sync failed: detect` (no version is known yet) |
| Provenance gate, `npm audit signatures`, a generator crash with no output, or a successful generation that left `src/Graspea.Flint/Generated` unchanged | `report-failure` opens or comments on one issue per version, found by title `upstream sync failed: flint-chart <ver>` and label `upstream`. The patch is never empty (the pin files and the new fixture folder always change), but every generated file names the upstream version in its header, so unchanged generated C# means the generator missed the new version |
| Generation fails with a named IR path (collision, unmapped construct, kind mismatch) | PR is still opened, as a draft, with `needs-attention`; the body holds `generate.log` verbatim. The fix is an `overrides.json` or generator change on `main` (decision 1), then a dispatch |
| Build or tests fail after generation | PR is still opened, as a draft, with `needs-attention`; the body shows the failing suites. The diff is reviewable, the CI checks are red |
| PR creation fails | `report-failure` issue, with the artifact name so the patch can be applied by hand |
| Scheduled run skipped by GitHub | nothing, the next day covers it; GitHub notifies the user who last edited the cron line about failed scheduled runs |

`report-failure` runs on `if: failure()` with no further condition; the title falls back to `detect` when the version output is empty. Issues are updated by comment, not duplicated: the job searches first and comments when one is open.

#### The 60-day auto-disable (default applied)

"In a public repository, scheduled workflows are automatically disabled when no repository activity has occurred in 60 days" ([disable and enable workflows](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/disable-and-enable-workflows)). Upstream has been quiet for 64 days as of today, so the watcher could switch off exactly while waiting for the next release. UNVERIFIED: what counts as "repository activity" and whether GitHub warns before disabling.

Default: rely on routine activity the repository wants anyway, Dependabot on the `github-actions` ecosystem (weekly, keeps the action SHAs and the workflow lockfile fresh) and on `nuget`/`npm` for the test and generator tooling, with `ignore: [{ dependency-name: "flint-chart" }]` on the `/generator` npm ecosystem so Dependabot never bumps the pin, plus a monthly `gh workflow list` glance by the maintainer. Alternatives: a keepalive marker commit ([efrecon/gh-action-keepalive](https://github.com/efrecon/gh-action-keepalive), which also notes that toggling the workflow's enabled state did not reset the deadline), or accepting manual `gh workflow enable` whenever it happens.

#### Security

Blast radius, stated plainly: code from the upstream package runs in two places, the `generate` job (read-only token, no secrets) and the PR's own CI, where the regenerated `tests/**` and `generator/package-lock.json` are executed and the contract-node job runs the new flint-chart under Node. PR CI starts immediately on PR creation and runs with a read-only `GITHUB_TOKEN` and no secrets; the only credentials with publish rights live in the `nuget` environment, reachable only from a tag on `main` after merge and after the required reviewer approves, and only maintainers may create tags. The App's private key lives in the `upstream-sync` environment, reachable only from `main`, and the token it mints has write access to contents and pull requests of this one repository. A compromised upstream release therefore gets code execution in CI with no secrets and a reviewable diff, never a publish and never a tag.

- Top-level `permissions: {}`; each job declares only what it needs. Keep the repository's default `GITHUB_TOKEN` read-only ([secure use](https://docs.github.com/en/actions/reference/security/secure-use)).
- Gate order, all inside `scripts/install-upstream.mjs`: install with `--ignore-scripts` (nothing runs), `npm audit signatures` (signature and Sigstore bundle verified), then the identity check on repository, workflow and `gitCommit`. The committed lockfile and `--save-exact` keep the result reproducible.
- The patch travels as an artifact to `open-pr`, which runs no npm code.
- No untrusted text inside `${{ }}` in `run:` steps. Version strings are regex-validated in `detect` and passed through `env`; the report goes to a file and into the PR via `body-path`. The `with:` inputs of the PR action receive only the validated version.
- Every action pinned to a full commit SHA with a version comment; turn on "Require actions to be pinned to a full-length commit SHA"; let Dependabot keep the SHAs and the `.github/workflows/actions.lock` lockfile current (public preview since 2026-10-14, [changelog](https://github.blog/changelog/2026-10-14-github-actions-workflow-lockfiles-in-public-preview/)). Pins as of 2026-10-17: `actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1` (v7.0.1), `actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1` (v7.1.0), `actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68` (v6.0.0), `peter-evans/create-pull-request@5f6978faf089d4d20b00c7766989d076bb2fc7f1` (v8.1.1), `actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9` (v7.0.2), `actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333` (v8.0.2), `actions/create-github-app-token@64ab729a6294000f5aa4a4bc0b73d1f32bc45510` (v3.3.0); release and scanning workflows: `actions/attest-build-provenance@4d101475d8b20a2381f78447822ac1eab6504dd8` (v4.2.2), `github/codeql-action@6f2ac81f5cd81fe351a197ba0b00d91d03cd2aee` (v4.39.0), `actions/dependency-review-action@a1d282b36b6f3519aa1f3fc636f609c47dddb294` (v5.0.0), `ossf/scorecard-action@fa7117db83c52d787ae5a89f350999701505d78e` (v2.5.0).
- No `pull_request_target`, no `workflow_run` with a PR-head checkout. checkout v7 blocks fork-head checkouts in those events ([changelog 2026-06-18](https://github.blog/changelog/2026-06-18-safer-pull_request_target-defaults-for-github-actions-checkout/); backported to other supported versions from 2026-07-20), but same-repo bot branches are not covered, so the rule has to be a convention in this repository.
- App configuration: client id in the repository variable `UPSTREAM_SYNC_APP_ID`, private key as the secret `UPSTREAM_SYNC_APP_KEY` on the `upstream-sync` environment whose deployment branch policy is `main`, token narrowed with `permission-contents: write` and `permission-pull-requests: write`; a tag ruleset on `v*` that only maintainers may create; bot commits signed through `sign-commits` (the action's GitHub API path; a git blob over 40 MiB would fail it, the largest fixture file is about 9 MB).

#### Report inputs from other sections

- From the AI section: the upstream `SKILL.md` blob SHA comparison, rendered as one line ("skill changed upstream: yes/no, old SHA -> new SHA, link at the new tag or commit") so the owner can re-check the hand-written agent rules.
- From the AI section: the marked README sections, the whole `docs/package/AGENTS.md` and the IR-derived `catalog.json` are regenerated in place by `npm run generate` in the `generate` job; no separate `--check` run is needed in this workflow, because the PR's own CI runs `verify-generated`.
- From the Discoverability section: the README `flint-chart` badge version and the "tracks flint-chart X.Y.Z" sentence are rewritten by the same generator step. The NuGet badge kind (`v`/`vpre`) and the `--prerelease` install line are not generated: they are set by hand in the release PR (SDK design §9.6 step 2), where the tag is known.

#### Relationship to upstream-sync.yml and release.yml

This section replaces SDK design §9.1. The skeleton ("daily npm check, regenerate and dump fixtures, PR, maintainer tags after merge") stays; this section adds the three-job split, the gate order, the version selection and idempotency rules, the computed-severity report, the labels, the failure issues, the backport and prerelease rules and the keepalive concern. The file keeps its name and remains the only scheduled workflow in the repository.

`release.yml` is specified in SDK design §9.6 (guards, publish, GitHub release job); this section contributes nothing to it beyond the suggested tag and the release title and body wording owned by Discoverability.

#### Workflow sketch

```yaml
name: upstream-sync
on:
  schedule:
    - cron: '17 5 * * *'
  workflow_dispatch:
    inputs:
      version:
        description: flint-chart version to sync (default: lowest stable version above the pin, excluding skipped ones)
        required: false
permissions: {}
concurrency:
  group: upstream-sync
  cancel-in-progress: false

jobs:
  detect:
    runs-on: ubuntu-latest
    permissions: { contents: read, pull-requests: read, issues: write }
    outputs:
      version: ${{ steps.pick.outputs.version }}
      queued: ${{ steps.pick.outputs.queued }}
      stale: ${{ steps.pick.outputs.stale }}
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with: { persist-credentials: false }
      - uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
        with: { node-version-file: .nvmrc }
      - id: pick
        env: { REQUESTED: "${{ inputs.version }}", GH_TOKEN: "${{ github.token }}" }
        # validates ^\d+\.\d+\.\d+$, reads bot PRs once (skip set, stale set),
        # files/comments the backport issue (search by title first), writes outputs + step summary
        run: node generator/scripts/pick-upstream-version.mjs

  generate:
    needs: detect
    if: needs.detect.outputs.version != ''
    runs-on: ubuntu-latest
    permissions: { contents: read }
    env: { FLINT_VERSION: "${{ needs.detect.outputs.version }}", OUT: "${{ runner.temp }}/out" }
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with: { persist-credentials: false }
      - uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
        with: { node-version-file: .nvmrc }
      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with: { global-json-file: global.json }
      - working-directory: generator
        run: |
          npm ci --ignore-scripts
          npm run install-upstream -- "$FLINT_VERSION"   # the gate: install exact, npm audit signatures, verify-provenance, resolve flint-chart-mcp
      - id: forward
        continue-on-error: true
        run: |
          npm --prefix generator run fixtures
          dotnet test tests/Graspea.Flint.ContractTests --filter Category=RoundTrip --nologo
      - id: gen
        run: |
          mkdir -p "$OUT"
          npm --prefix generator run generate 2>&1 | tee "$OUT/generate.log"
          echo "ok=${PIPESTATUS[0]}" >> "$GITHUB_OUTPUT"
      - id: tests
        continue-on-error: true
        run: |
          dotnet test --nologo
          npm --prefix generator test
      - env: { TESTS: "${{ steps.tests.outcome }}", FORWARD: "${{ steps.forward.outcome }}", GEN: "${{ steps.gen.outputs.ok }}", QUEUED: "${{ needs.detect.outputs.queued }}" }
        run: |
          git show main:generator/ir/flint-ir.json > "$OUT/flint-ir.old.json"
          node generator/scripts/upstream-report.mjs --old-ir "$OUT/flint-ir.old.json" --body "$OUT/pr-body.md" --result "$OUT/result.json"
          git add -N . && git diff --binary > "$OUT/upstream.patch"
      - uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2
        with: { name: upstream-${{ needs.detect.outputs.version }}, path: ${{ runner.temp }}/out }

  open-pr:
    needs: [detect, generate]
    runs-on: ubuntu-latest
    environment: upstream-sync
    permissions: { contents: read }   # checkout only; every write uses the App token
    env: { FLINT_VERSION: "${{ needs.detect.outputs.version }}", OUT: "${{ runner.temp }}/out" }
    steps:
      - id: app
        uses: actions/create-github-app-token@64ab729a6294000f5aa4a4bc0b73d1f32bc45510 # v3.3.0
        with:
          client-id: ${{ vars.UPSTREAM_SYNC_APP_ID }}
          private-key: ${{ secrets.UPSTREAM_SYNC_APP_KEY }}
          permission-contents: write
          permission-pull-requests: write
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with: { persist-credentials: false }
      - uses: actions/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333 # v8.0.2
        with: { name: upstream-${{ needs.detect.outputs.version }}, path: ${{ runner.temp }}/out }
      - env: { STALE: "${{ needs.detect.outputs.stale }}", GH_TOKEN: "${{ steps.app.outputs.token }}" }
        run: |
          for v in $STALE; do
            gh pr close "upstream/flint-chart-$v" --comment "flint-chart $v is no longer a stable published version; closing." || true
          done
      - id: meta
        run: |
          echo "draft=$(jq -r '(.tests != "success") or (.generation != "ok")' "$OUT/result.json")" >> "$GITHUB_OUTPUT"
          echo "labels=$(jq -r '.labels | join(",")' "$OUT/result.json")" >> "$GITHUB_OUTPUT"
      - run: |
          git apply "$OUT/upstream.patch"                 # working tree only: add-paths decides what is committed
          node generator/scripts/check-sync-paths.mjs     # fails on a path outside SYNC_PATHS
      - uses: peter-evans/create-pull-request@5f6978faf089d4d20b00c7766989d076bb2fc7f1 # v8.1.1
        with:
          token: ${{ steps.app.outputs.token }}
          sign-commits: true
          branch: upstream/flint-chart-${{ needs.detect.outputs.version }}
          commit-message: "Sync flint-chart ${{ needs.detect.outputs.version }}"
          title: "Upstream: flint-chart ${{ needs.detect.outputs.version }}"
          body-path: ${{ runner.temp }}/out/pr-body.md
          labels: ${{ steps.meta.outputs.labels }}
          draft: ${{ steps.meta.outputs.draft }}
          add-paths: generator/package.json,generator/package-lock.json,generator/mcp-pin.json,generator/ir/**,generator/retired.json,src/**,tests/fixtures/**,README.md,docs/package/AGENTS.md

  report-failure:
    needs: [detect, generate, open-pr]
    if: failure()
    runs-on: ubuntu-latest
    permissions: { issues: write }
    env: { GH_TOKEN: "${{ github.token }}", FLINT_VERSION: "${{ needs.detect.outputs.version }}", RUN_URL: "${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}" }
    steps:
      - run: |
          title="upstream sync failed: ${FLINT_VERSION:+flint-chart }${FLINT_VERSION:-detect}"
          n=$(gh issue list --search "\"$title\" in:title" --label upstream --state open --json number --jq '.[0].number')
          if [ -n "$n" ]; then gh issue comment "$n" --body "Failed again: $RUN_URL"; else gh issue create --title "$title" --label upstream,needs-attention --body "Run: $RUN_URL"; fi
```

`result.json` carries `tests`, `generation` (`ok` or `failed`), `forward` (round-trip counts), `mcp` (the resolved `flint-chart-mcp` version and whether it equals the pin), `labels` (`upstream` plus `breaking` and `needs-attention` as computed), `queued` and `suggested_tag`.

#### PR body template

```markdown
## flint-chart 0.5.1 -> 0.6.0

| | |
|---|---|
| Published | 2026-11-02 10:14 UTC (npm `latest`) |
| Upstream compare | https://github.com/microsoft/flint-chart/compare/<old gitHead>...<new gitHead> |
| Provenance | SLSA v1, repo microsoft/flint-chart, workflow npm-publish.yml, run <url> |
| flint-chart-mcp | 0.6.0 published; `mcp-commands` pins 0.6.0 (or: missing; pinned to 0.5.1, needs-attention) |
| Release notes | .github/release-notes/0.6.0.md (if present) |
| Agent skill | changed upstream (af1a890f -> 3c91d02e), see link at tag 0.6.0 (or commit <gitHead> when no tag exists) |
| Generation | OK (or: failed, see log below) |
| Forward-compat round-trip (old model, new fixtures) | 940 / 942 |
| Tests | dotnet test: PASS, generator node:test: PASS |
| Queued after this | 0.6.1 |
| Suggested SDK tag | `v0.6.0.1-preview.1` (suggestion; maintainer may override at tag time; rule: SDK design §8.1) |

### Severity summary
BREAKING 2 · behavioural 1 · additive 17 · cosmetic 9

### Chart types
| Change | Name | Severity | Detail |
|---|---|---|---|
| removed | `Stacked Waterfall Chart` | BREAKING | `catalog.chartTypes[...]` gone from the registry; kept as `[Obsolete] FLINT0001` |
| added | `Bump Chart` | additive | 4 channels, 11 properties |

### Channels
| Change | Template | Channel | Severity | Detail |
|---|---|---|---|---|
| removed | `Bar Chart` (vegalite) | `strokeDash` | BREAKING | `unsupported_channel` from ChartValidator; `retired.json` entry |

### Properties
| Change | Scope | Property | Severity | Detail |
|---|---|---|---|---|
| default changed | `Line Chart` | `showPoints` | behavioural | `true` -> `false` |
| options added | `Line Chart` | `interpolate` | additive | `+ "natural"` |

### Semantic types
| Change | Name | Severity |
|---|---|---|
| added | `YearQuarter` | additive |

### Input type changes (ChartAssemblyInput)
| Path | Old | New | Severity |
|---|---|---|---|
| `chart_spec.chartProperties` | `Record<string, unknown>` | `Record<string, unknown> \| undefined` | additive |

### Retired (retired.json)
| Entry | Vanished in | Class |
|---|---|---|
| `Bar Chart/vegalite/channel/strokeDash` | 0.6.0 | Warning (`unsupported_channel`) |

### Constants
`EXCEL_ARTIFACT_SCHEMA`: unchanged (`flint.excel.chart/v1`)

### Fixtures and generated files
fixtures: +6 / -1 / ~14 · generated C#: 3 files, +412 / -97 lines · README/AGENTS.md generated sections: updated

### Generation log
<verbatim generate.log, only when generation failed>

### Upstream CHANGELOG 0.6.0
<verbatim section from CHANGELOG.md at the new gitHead>

### After merge
1. Open the release PR `release: v0.6.0.1-preview.1` (SDK design §9.6 step 2: Shipped moved, baseline set, CHANGELOG entry, NuGet badge kind, install line) and merge it.
2. `git tag v0.6.0.1-preview.1 <release PR merge sha> && git push origin v0.6.0.1-preview.1`
Review checklist: SDK design §9.5.
```

The template text is produced by `upstream-report.mjs` from the structured IR diff; identifiers are in IR form (display names for chart types, camelCase keys, PascalCase semantic types). The CHANGELOG section and `generate.log` are copied into the file as-is and never pass through a shell expression.

#### Defaults applied in this section (say if you object)

- Upstream prereleases: ignored, listed in the run summary.
- Backports below the pin: one deduplicated issue, no PR.
- 60-day keepalive: Dependabot activity (with `flint-chart` ignored) plus a monthly `gh workflow list` check.
- Deprecated or unpublished versions: treated as absent; their open bot PR is closed with a comment.
- `upstream-skip`: the owner's explicit exception to "every upstream release gets an SDK release", also used for held-back versions.
- Branch `upstream/flint-chart-<ver>`, label `upstream`, title "Upstream: flint-chart <ver>"; these names are the only ones used anywhere.
