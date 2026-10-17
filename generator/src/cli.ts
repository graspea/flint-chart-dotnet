// npm run extract | emit | fixtures | generate [-- --check] | probes | ir-diff. `generate` = extract + emit + fixtures;
// emit also writes the marked README.md sections and docs/package/AGENTS.md.
// Paths are relative to generator/. With --check nothing is written: every file that would change is listed and the
// command fails (the drift check of design §7.7).
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { diffIr, diffMarkdown, retiredEntries, type RetiredEntry } from './diff.ts';
import { emitBuilders } from './emit/builders.ts';
import { mergeResults } from './emit/common.ts';
import { emitConstants } from './emit/constants.ts';
import { emitDocs, packageAgents, readmeSections, spliceSections, type McpPin } from './emit/docs.ts';
import { emitEntries } from './emit/entries.ts';
import { emitEnums } from './emit/enums.ts';
import { emitOptions } from './emit/options.ts';
import { checkPropertyKinds } from './emit/property-kinds.ts';
import { declaredPublicTypes, unshippedText } from './emit/public-api.ts';
import { emitTables } from './emit/tables.ts';
import { extractTypes } from './extract/dts.ts';
import { extractFixtures, fixtureFiles, fixtureFolder, KNOWN_FAILURES_FILE } from './extract/fixtures.ts';
import { flintPackageDir, lockedIntegrity, packageManifest, PACKAGE_NAME } from './extract/package.ts';
import { extractCatalog } from './extract/registry.ts';
import { canonicalJson, type Ir } from './ir.ts';
import { loadOverrides } from './overrides.ts';
import { runProbes } from './probes.ts';

const generatorDir = dirname(dirname(fileURLToPath(import.meta.url)));
const repoDir = dirname(generatorDir);
const IR_FILE = join(generatorDir, 'ir/flint-ir.json');
const RETIRED_FILE = join(generatorDir, 'retired.json');
const OVERRIDES_FILE = join(generatorDir, 'overrides.json');
const LIBRARY_DIR = join(repoDir, 'src/Graspea.Flint');
const GENERATED_DIR = join(LIBRARY_DIR, 'Generated');
const FIXTURES_DIR = join(repoDir, 'tests/fixtures');
const PROBES_FILE = join(generatorDir, 'verify/probes.json');
const MCP_PIN_FILE = join(generatorDir, 'mcp-pin.json');
const README_FILE = join(repoDir, 'README.md');
const PACKAGE_AGENTS_FILE = join(repoDir, 'docs/package/AGENTS.md');

const readJson = <T>(file: string): T => JSON.parse(readFileSync(file, 'utf8')) as T;

const check = process.argv.includes('--check');
const drift: string[] = [];
const wrote = check ? 'checked' : 'wrote';

function writeText(file: string, content: string): void {
  if (existsSync(file) && readFileSync(file, 'utf8') === content) return;
  if (check) {
    drift.push(relative(repoDir, file));
    return;
  }
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, content);
}

function removeFile(file: string): void {
  if (check) drift.push(relative(repoDir, file));
  else rmSync(file);
}

async function extract(): Promise<Ir> {
  process.env.TZ = 'UTC';
  const overrides = loadOverrides(OVERRIDES_FILE);
  const packageDir = flintPackageDir();
  const flint = (await import(PACKAGE_NAME)) as Record<string, unknown>;
  const ir: Ir = {
    upstream: {
      package: PACKAGE_NAME,
      version: packageManifest(packageDir).version,
      integrity: lockedIntegrity(join(generatorDir, 'package-lock.json')),
    },
    types: extractTypes(packageDir),
    catalog: extractCatalog(flint, packageDir, overrides),
  };

  if (existsSync(IR_FILE)) {
    const previous = readJson<Ir>(IR_FILE);
    const rows = diffIr(previous, ir);
    const retired = retiredEntries(rows, previous.upstream.version, ir.upstream.version);
    if (retired.length > 0) {
      const ledger = existsSync(RETIRED_FILE) ? readJson<RetiredEntry[]>(RETIRED_FILE) : [];
      const known = new Set(ledger.map((e) => e.path));
      writeText(RETIRED_FILE, canonicalJson([...ledger, ...retired.filter((e) => !known.has(e.path))]));
    }
    console.log(`extract: ${rows.length} IR change(s), ${retired.length} retirement(s)`);
  }
  writeText(IR_FILE, canonicalJson(ir));
  console.log(`extract: ${wrote} ${IR_FILE}`);
  return ir;
}

/**
 * Removes every `*.g.cs` under dir that is not in keep, and the folders that end up empty. Returns the public types the
 * removed files declared, whose PublicAPI lines then belong to the generator.
 */
function removeStale(dir: string, keep: Set<string>): string[] {
  if (!existsSync(dir)) return [];
  const types: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      types.push(...removeStale(path, keep));
      if (!check && readdirSync(path).length === 0) rmSync(path, { recursive: true });
    } else if (entry.name.endsWith('.g.cs') && !keep.has(path)) {
      types.push(...declaredPublicTypes(readFileSync(path, 'utf8')));
      removeFile(path);
    }
  }
  return types;
}

function emit(ir: Ir = readJson<Ir>(IR_FILE)): void {
  const overrides = loadOverrides(OVERRIDES_FILE);
  checkPropertyKinds(ir, overrides);
  const shipped = readFileSync(join(LIBRARY_DIR, 'PublicAPI.Shipped.txt'), 'utf8');
  const result = mergeResults([
    emitConstants(ir, overrides),
    emitOptions(ir, overrides),
    emitTables(ir),
    emitBuilders(ir, overrides),
    emitEnums(ir, overrides, shipped),
    emitEntries(ir, overrides),
    emitDocs(ir),
  ]);

  result.ownedTypes.push(...removeStale(GENERATED_DIR, new Set(result.files.map((f) => join(GENERATED_DIR, f.path)))));
  for (const file of result.files) writeText(join(GENERATED_DIR, file.path), file.content);

  const unshippedFile = join(LIBRARY_DIR, 'PublicAPI.Unshipped.txt');
  writeText(unshippedFile, unshippedText(shipped, readFileSync(unshippedFile, 'utf8'), result));

  const mcpPin = readJson<McpPin>(MCP_PIN_FILE);
  const readme = spliceSections(readFileSync(README_FILE, 'utf8'), readmeSections(ir, overrides, mcpPin), 'README.md');
  writeText(README_FILE, readme);
  writeText(PACKAGE_AGENTS_FILE, packageAgents(readme, ir, overrides, mcpPin));
  console.log(`emit: ${wrote} ${result.files.length} files, ${result.apiLines.length} API lines, README.md sections and docs/package/AGENTS.md`);
}

/** Writes tests/fixtures/flint-chart@<version>/ (or under --out). Must run in a process that has not imported flint-chart. */
async function fixtures(outRoot: string): Promise<void> {
  const version = packageManifest(flintPackageDir()).version;
  const set = await extractFixtures({ version, integrity: lockedIntegrity(join(generatorDir, 'package-lock.json')) });
  const folder = join(outRoot, fixtureFolder(version));
  for (const file of fixtureFiles(set)) writeText(join(folder, file.path), file.content);
  const knownFailures = join(folder, KNOWN_FAILURES_FILE);
  if (!existsSync(knownFailures)) writeText(knownFailures, '');
  console.log(`fixtures: ${wrote} ${set.cases.length} cases in ${folder}`);
}

/** extract and emit in this process; fixtures in a fresh one, because its seed must precede every flint-chart import. */
async function generate(): Promise<void> {
  const ir = await extract();
  emit(ir);
  const pin = readJson<{ dependencies: Record<string, string> }>(join(generatorDir, 'package.json')).dependencies[PACKAGE_NAME];
  if (pin !== ir.upstream.version) throw new Error(`generator/package.json pins ${PACKAGE_NAME}@${pin}, the installed package is ${ir.upstream.version}.`);
  const child = spawnSync(process.execPath, ['--import', 'tsx', fileURLToPath(import.meta.url), 'fixtures', ...(check ? ['--check'] : [])], {
    cwd: generatorDir,
    stdio: 'inherit',
  });
  if (child.status !== 0) {
    if (check) drift.push('tests/fixtures (see above)');
    else throw new Error(`fixtures failed with exit code ${child.status}`);
  }
}

/** Records the runtime probes (design §7.2); not part of generate. */
async function probes(): Promise<void> {
  process.env.TZ = 'UTC';
  writeText(PROBES_FILE, canonicalJson(await runProbes()));
  console.log(`probes: ${wrote} ${PROBES_FILE}`);
}

function argValue(args: string[], name: string): string | undefined {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : undefined;
}

function irDiff(args: string[]): void {
  const [before, after = IR_FILE] = args;
  if (!before) throw new Error('usage: npm run ir-diff -- <before.json> [after.json]');
  process.stdout.write(diffMarkdown(diffIr(readJson(before), readJson(after))));
}

const [command, ...args] = process.argv.slice(2);
switch (command) {
  case 'extract':
    await extract();
    break;
  case 'emit':
    emit();
    break;
  case 'fixtures':
    await fixtures(resolve(argValue(args, '--out') ?? FIXTURES_DIR));
    break;
  case 'generate':
    await generate();
    break;
  case 'probes':
    await probes();
    break;
  case 'ir-diff':
    irDiff(args);
    break;
  default:
    throw new Error(`Unknown command "${command}". Use extract, emit, fixtures, generate, probes or ir-diff.`);
}

if (check && drift.length > 0) {
  console.error(`check: ${drift.length} generated file(s) differ from a fresh run; run npm run generate:\n  ${drift.join('\n  ')}`);
  process.exit(1);
}
