#!/usr/bin/env sh
# Restores the packed generator from a folder feed (design §12.9 step 1, §12.10 Packaging) into a scratch project that
# references it and a downstream project that references that project. The first gets the analyzer and
# build/Graspea.Flint.Generators.targets and generates its chart; the downstream project gets Graspea.Flint (the [X]
# dependency) and calls the first project's generated API, but does not import the build/ targets (default PrivateAssets),
# so it has no spec files and generates no chart. The SDK still passes the analyzer to the downstream compilation, which
# then holds only the generator's internal attributes. The feed must hold Graspea.Flint and Graspea.Flint.Generators at
# <version>.
# Usage: scripts/check-generator-flow.sh <feed folder> <version>
set -eu
[ "$#" -eq 2 ] || { echo "usage: $0 <feed folder> <version>" >&2; exit 2; }
feed=$(cd "$1" && pwd)
version=$2
# The physical path: NuGet drops a project reference whose path it resolves through a link (macOS /var is /private/var).
dir=$(cd "$(mktemp -d)" && pwd -P)
trap 'rm -rf "$dir"' EXIT
mkdir "$dir/Uses" "$dir/Downstream"
# A private package folder: NuGet never extracts a version again that the global folder already holds, so a second run with
# a repacked 0.0.0-ci would otherwise check the first build.
export NUGET_PACKAGES="$dir/packages"

cat > "$dir/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="feed" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
printf '{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }\n' > "$dir/global.json"

cat > "$dir/Uses/Uses.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>\$(NoWarn);FLINTEXP001</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Graspea.Flint.Generators" Version="$version" />
  </ItemGroup>
</Project>
XML
printf '{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":{"field":"region","type":"nominal"},"y":{"field":"units","type":"quantitative"}}}}\n' > "$dir/Uses/units.flint.json"
printf 'namespace Uses;\n[Graspea.Flint.FlintChart("units.flint.json")]\npublic static partial class UnitsChart;\n' > "$dir/Uses/UnitsChart.cs"

cat > "$dir/Downstream/Downstream.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>\$(NoWarn);FLINTEXP001</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Uses/Uses.csproj" />
  </ItemGroup>
</Project>
XML
printf '{"chart_spec":{"chartType":"Bar Chart","encodings":{"x":"a","y":"b"}}}\n' > "$dir/Downstream/ignored.flint.json"
cat > "$dir/Downstream/Use.cs" <<'CS'
namespace Downstream;

public static class Use
{
    public static Graspea.Flint.ChartType Type => Graspea.Flint.ChartType.BarChart;

    public static int Issues() => Uses.UnitsChart.Validate().Issues.Count;
}
CS

dotnet build "$dir/Downstream/Downstream.csproj" -nologo -v quiet -p:EmitCompilerGeneratedFiles=true
grep -q 'Graspea.Flint.Generators.targets' "$dir/Uses/obj/Uses.csproj.nuget.g.targets" || { echo "the referencing project does not import build/Graspea.Flint.Generators.targets" >&2; exit 1; }
if grep -q 'Graspea.Flint.Generators.targets' "$dir/Downstream/obj/Downstream.csproj.nuget.g.targets"; then
  echo "the downstream project imports build/Graspea.Flint.Generators.targets" >&2
  exit 1
fi
[ -n "$(find "$dir/Uses/obj" -name 'Uses.UnitsChart.flint.g.cs')" ] || { echo "the referencing project generated no chart" >&2; exit 1; }
[ -z "$(find "$dir/Downstream/obj" -name '*.flint.g.cs')" ] || { echo "the downstream project generated a chart" >&2; exit 1; }
echo "Graspea.Flint.Generators $version: the referencing project imports the build/ targets and generates its chart; the downstream project gets Graspea.Flint and the generated API, not the build/ targets, and generates no chart"
