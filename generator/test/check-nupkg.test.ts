import { after, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// scripts/check-nupkg.sh over crafted packages: a .nupkg is a zip with the nuspec at its root, so each case writes a file
// tree, zips it without directory entries (as NuGet writes it) and runs the script on it, as release.yml and ci.yml do on
// the packed files.
const script = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'scripts', 'check-nupkg.sh');
const root = mkdtempSync(join(tmpdir(), 'check-nupkg-'));
after(() => rmSync(root, { recursive: true, force: true }));

const version = '0.5.1.1-preview.1';
let count = 0;

function nupkg(id: string, files: Record<string, string>): string {
  const dir = join(root, `case-${++count}`);
  for (const [path, content] of Object.entries(files)) {
    mkdirSync(dirname(join(dir, path)), { recursive: true });
    writeFileSync(join(dir, path), content);
  }
  const file = join(root, `${id}.${count}.nupkg`);
  execFileSync('zip', ['-q', '-r', '-D', file, '.'], { cwd: dir });
  return file;
}

function check(...args: string[]): { status: number | null; out: string } {
  const result = spawnSync('sh', [script, ...args], { encoding: 'utf8' });
  return { status: result.status, out: result.stdout + result.stderr };
}

function nuspec(id: string, extra = '', dependencies = ''): string {
  return (
    `<?xml version="1.0" encoding="utf-8"?>\n<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">\n` +
    `  <metadata>\n    <id>${id}</id>\n    <version>${version}</version>\n` +
    `    <license type="expression">MIT</license>\n${extra}` +
    (dependencies ? `    <dependencies>\n      <group targetFramework="net8.0">\n${dependencies}      </group>\n    </dependencies>\n` : '') +
    `  </metadata>\n</package>\n`
  );
}

const common = { 'README.md': '# readme\n', 'icon.png': 'png', 'THIRD-PARTY-NOTICES.md': 'notices\n' };

function library(overrides: Record<string, string | undefined> = {}): Record<string, string> {
  return withOverrides(
    {
      ...common,
      'Graspea.Flint.nuspec': nuspec('Graspea.Flint'),
      'AGENTS.md': '# agents\n',
      'catalog/catalog.json': '{}\n',
      'lib/net8.0/Graspea.Flint.dll': 'dll\n',
    },
    overrides,
  );
}

const tools = 'tools/net8.0/any/';

function tool(overrides: Record<string, string | undefined> = {}): Record<string, string> {
  return withOverrides(
    {
      ...common,
      'Graspea.Flint.Tool.nuspec': nuspec('Graspea.Flint.Tool', '    <packageTypes>\n      <packageType name="DotnetTool" />\n    </packageTypes>\n'),
      [`${tools}DotnetToolSettings.xml`]: '<DotNetCliTool Version="1"><Commands><Command Name="flint-cs" EntryPoint="flint-cs.dll" Runner="dotnet" /></Commands></DotNetCliTool>\n',
      [`${tools}flint-cs.dll`]: 'dll\n',
      [`${tools}flint-cs.runtimeconfig.json`]: '{\n  "runtimeOptions": {\n    "tfm": "net8.0",\n    "rollForward": "Major"\n  }\n}\n',
      // `strings` reads the informational version from the assembly; a text file stands in for it.
      [`${tools}Graspea.Flint.dll`]: `binary\n${version}+0123abcd\n`,
    },
    overrides,
  );
}

const analyzer = 'analyzers/dotnet/roslyn4.14/cs/Graspea.Flint.Generators.dll';

function generators(overrides: Record<string, string | undefined> = {}, dependencies?: string): Record<string, string> {
  return withOverrides(
    {
      ...common,
      'Graspea.Flint.Generators.nuspec': nuspec('Graspea.Flint.Generators', '', dependencies ?? `        <dependency id="Graspea.Flint" version="[${version}]" />\n`),
      [analyzer]: 'dll\n',
      'build/Graspea.Flint.Generators.targets': '<Project />\n',
      'lib/net8.0/_._': '',
    },
    overrides,
  );
}

function withOverrides(files: Record<string, string>, overrides: Record<string, string | undefined>): Record<string, string> {
  const result = { ...files };
  for (const [path, content] of Object.entries(overrides)) {
    if (content === undefined) delete result[path];
    else result[path] = content;
  }
  return result;
}

describe('check-nupkg: library', () => {
  // source: design part 3 §9.6 and companion part 2 §2 (README, icon, AGENTS.md, catalog and notices; no dependencies)
  it('accepts the library package', () => {
    const { status, out } = check(nupkg('Graspea.Flint', library()));
    assert.equal(status, 0, out);
  });

  it('refuses a library package without AGENTS.md or the catalog', () => {
    assert.match(check(nupkg('Graspea.Flint', library({ 'AGENTS.md': undefined }))).out, /missing AGENTS\.md/);
    assert.match(check(nupkg('Graspea.Flint', library({ 'catalog/catalog.json': undefined }))).out, /missing catalog\/catalog\.json/);
  });

  it('refuses a library package with a dependency', () => {
    const files = library({ 'Graspea.Flint.nuspec': nuspec('Graspea.Flint', '', '        <dependency id="Some.Package" version="1.0.0" />\n') });
    const { status, out } = check(nupkg('Graspea.Flint', files));
    assert.equal(status, 1);
    assert.match(out, /declares package dependencies/);
  });
});

describe('check-nupkg: tool', () => {
  // source: design §12.9 step 1 (DotnetTool, flint-cs, rollForward Major, Graspea.Flint.dll of the same version and no
  // other assembly; a tool package carries its dependencies, so it declares none)
  it('accepts the tool package', () => {
    const { status, out } = check('--tool', nupkg('Graspea.Flint.Tool', tool()));
    assert.equal(status, 0, out);
  });

  it('refuses another package type', () => {
    const files = tool({ 'Graspea.Flint.Tool.nuspec': nuspec('Graspea.Flint.Tool') });
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', files)).out, /the package type is not DotnetTool/);
  });

  it('refuses a Graspea.Flint.dll of another version', () => {
    const files = tool({ [`${tools}Graspea.Flint.dll`]: 'binary\n0.5.1.2+0123abcd\n' });
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', files)).out, /Graspea\.Flint\.dll is not version 0\.5\.1\.1-preview\.1/);
  });

  // source: the version is a literal; a dot in it matches only a dot
  it('compares the Graspea.Flint.dll version literally', () => {
    const files = tool({ [`${tools}Graspea.Flint.dll`]: 'binary\n0x5x1x1-preview.1+0123abcd\n' });
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', files)).out, /Graspea\.Flint\.dll is not version/);
  });

  it('refuses a missing Graspea.Flint.dll, another assembly or a dependency', () => {
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', tool({ [`${tools}Graspea.Flint.dll`]: undefined }))).out, /missing tools\/net8\.0\/any\/Graspea\.Flint\.dll/);
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', tool({ [`${tools}Other.dll`]: 'dll\n' }))).out, /assemblies other than/);
    const dependency = tool({
      'Graspea.Flint.Tool.nuspec': nuspec(
        'Graspea.Flint.Tool',
        '    <packageTypes>\n      <packageType name="DotnetTool" />\n    </packageTypes>\n',
        `        <dependency id="Graspea.Flint" version="[${version}]" />\n`,
      ),
    });
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', dependency)).out, /declares package dependencies/);
  });

  it('refuses another command or roll-forward policy', () => {
    const command = tool({ [`${tools}DotnetToolSettings.xml`]: '<DotNetCliTool><Commands><Command Name="flint" /></Commands></DotNetCliTool>\n' });
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', command)).out, /the command is not flint-cs/);
    const rollForward = tool({ [`${tools}flint-cs.runtimeconfig.json`]: '{ "runtimeOptions": { "tfm": "net8.0" } }\n' });
    assert.match(check('--tool', nupkg('Graspea.Flint.Tool', rollForward)).out, /rollForward is not Major/);
  });
});

describe('check-nupkg: generators', () => {
  // source: design §12.9 step 1 and §12.6.1 (the analyzer under roslyn4.14 only, build/ targets, lib/net8.0/_._, exactly
  // one dependency Graspea.Flint [<version>], not a development dependency)
  it('accepts the generator package', () => {
    const { status, out } = check('--generators', nupkg('Graspea.Flint.Generators', generators()));
    assert.equal(status, 0, out);
  });

  // source: design §12.6.1 (the package brings no run-time assembly: lib/net8.0/_._ only keeps NU5128 away, so nothing
  // of the generator leaks into a consumer's references)
  it('refuses any file under lib/ but lib/net8.0/_._', () => {
    const { status, out } = check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'lib/net8.0/Graspea.Flint.Generators.dll': 'dll\n' })));
    assert.equal(status, 1);
    assert.match(out, /other files under lib\//);
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'lib/netstandard2.0/_._': '' }))).out, /other files under lib\//);
  });

  it('refuses another analyzer, compiler or System.Text.Json files', () => {
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'analyzers/dotnet/cs/Graspea.Flint.Generators.dll': 'dll\n' }))).out, /other files under analyzers\//);
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'tools/Microsoft.CodeAnalysis.CSharp.dll': 'dll\n' }))).out, /compiler or System\.Text\.Json files/);
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'tools/System.Text.Json.dll': 'dll\n' }))).out, /compiler or System\.Text\.Json files/);
  });

  it('refuses buildTransitive/ or a missing build/ targets file', () => {
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'buildTransitive/Graspea.Flint.Generators.targets': '<Project />\n' }))).out, /buildTransitive/);
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({ 'build/Graspea.Flint.Generators.targets': undefined }))).out, /missing build\/Graspea\.Flint\.Generators\.targets/);
  });

  // source: design §12.6.1 and D7.6 (exactly the same Graspea.Flint version, [X])
  it('refuses a Graspea.Flint requirement other than [<version>]', () => {
    for (const range of ['0.5.1.1-preview.1', '[0.5.1.2]', '[0.5.1.1-preview.1, )', '[0x5x1x1-preview.1]']) {
      const files = generators({}, `        <dependency id="Graspea.Flint" version="${range}" />\n`);
      assert.match(check('--generators', nupkg('Graspea.Flint.Generators', files)).out, /the dependency is not Graspea\.Flint \[0\.5\.1\.1-preview\.1\]/, range);
    }
  });

  it('refuses no dependency, a second one or a development dependency', () => {
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', generators({}, ''))).out, /expected one dependency, found 0/);
    const two = generators({}, `        <dependency id="Graspea.Flint" version="[${version}]" />\n        <dependency id="Other" version="1.0.0" />\n`);
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', two)).out, /expected one dependency, found 2/);
    const development = generators({
      'Graspea.Flint.Generators.nuspec': nuspec('Graspea.Flint.Generators', '    <developmentDependency>true</developmentDependency>\n', `        <dependency id="Graspea.Flint" version="[${version}]" />\n`),
    });
    assert.match(check('--generators', nupkg('Graspea.Flint.Generators', development)).out, /is a development dependency/);
  });
});
