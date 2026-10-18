# Security policy

## Reporting a vulnerability

Please do not report security problems in public issues, discussions or pull requests.

Report them privately through GitHub: on the repository page open **Security**, then **Report a vulnerability** ([direct link](https://github.com/graspea/flint-chart-dotnet/security/advisories/new)). Include the affected version, the steps to reproduce and the impact you expect. We acknowledge the report in the advisory, keep you informed there, and agree on a disclosure date with you once a fix is ready.

## Scope

- the `Graspea.Flint` NuGet package;
- the generator in `generator/` and the GitHub Actions workflows of this repository, including the upstream sync and release workflows.

Problems in `flint-chart` or `flint-chart-mcp` themselves belong to the upstream project; please follow its security policy at https://github.com/microsoft/flint-chart. If you are not sure where a problem belongs, report it here and we will help route it.

## Verifying a release

Releases are built only by `.github/workflows/release.yml`, from an annotated `v*` tag on `main` signed with the SSH key of a maintainer listed in `.github/allowed_signers`, and only after a maintainer approves the `nuget` deployment. The tag check inside the workflow runs from the tagged commit and is advisory; the security boundary is the repository's tag ruleset (only the maintainers team may push `v*` tags), write access limited to that team, and the required reviewers of the `nuget` environment, who check the tag signature, the tag name and the commit's place on `main` before they approve. Each release consists of three packages at one version, `Graspea.Flint`, `Graspea.Flint.Tool` and `Graspea.Flint.Generators`, packed in the same approved job. The workflow records Sigstore build provenance for every `.nupkg` and `.snupkg` it builds (the generator package has no `.snupkg`), attaches them to the GitHub release and publishes them to nuget.org, which adds its repository signature. There is no author certificate.

- Provenance (the file from the GitHub release, exactly as built):

  ```
  gh attestation verify Graspea.Flint.<version>.nupkg --repo graspea/flint-chart-dotnet \
    --signer-workflow graspea/flint-chart-dotnet/.github/workflows/release.yml \
    --source-ref refs/tags/v<version> --deny-self-hosted-runners
  ```

  It checks that the file was built by the release workflow, run for the tag `v<version>` on a GitHub-hosted runner. `--repo` alone accepts any workflow of the repository on any branch, and `--signer-workflow` without `--source-ref` also accepts that workflow file run from a branch. An equivalent exact check is `--cert-identity https://github.com/graspea/flint-chart-dotnet/.github/workflows/release.yml@refs/tags/v<version>` in place of `--signer-workflow` and `--source-ref`. The same command works for `Graspea.Flint.Tool.<version>.nupkg`, `Graspea.Flint.Generators.<version>.nupkg` and each `.snupkg`. The repository signature that nuget.org adds changes the file's digest, so this command does not match a copy downloaded from nuget.org.
- Repository signature (the file from nuget.org or your package cache):

  ```
  fingerprints=$(curl -s https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json \
    | jq -r '.signingCertificates[].fingerprints["2.16.840.1.101.3.4.2.1"] | "--certificate-fingerprint " + .')
  dotnet nuget verify --all $fingerprints Graspea.Flint.<version>.nupkg
  ```

  Without `--certificate-fingerprint`, `dotnet nuget verify --all` accepts any valid signature chain, not only nuget.org's. With the SHA-256 fingerprints of nuget.org's repository certificates (published in the index above) it passes only for a package nuget.org signed; the output names `Signature type: Repository` and `CN=NuGet.org Repository by Microsoft`. This shows where the file came from, not how it was built; the provenance check above covers that. The same command checks the tool and the generator package.

- Release tag: `git verify-tag` with `gpg.format=ssh` and `gpg.ssh.allowedSignersFile` pointing at `.github/allowed_signers` checks the maintainer signature of a tag in a clone.

## Supported versions

Fixes go into the newest release line and are released from `main`. Older lines receive no fixes; upgrade to the newest release.
