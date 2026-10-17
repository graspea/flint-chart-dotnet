# Security policy

## Reporting a vulnerability

Please do not report security problems in public issues, discussions or pull requests.

Report them privately through GitHub: on the repository page open **Security**, then **Report a vulnerability** ([direct link](https://github.com/graspea/flint-dotnet/security/advisories/new)). Include the affected version, the steps to reproduce and the impact you expect. We acknowledge the report in the advisory, keep you informed there, and agree on a disclosure date with you once a fix is ready.

## Scope

- the `Graspea.Flint` NuGet package;
- the generator in `generator/` and the GitHub Actions workflows of this repository, including the upstream sync and release workflows.

Problems in `flint-chart` or `flint-chart-mcp` themselves belong to the upstream project; please follow its security policy at https://github.com/microsoft/flint-chart. If you are not sure where a problem belongs, report it here and we will help route it.

## Supported versions

Fixes go into the newest release line. Older lines receive security fixes only when the newest line cannot be used as a replacement.
