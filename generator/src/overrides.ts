// overrides.json: the only hand-maintained generator input, validated against overrides.schema.json.
import { readFileSync } from 'node:fs';
import { Ajv2020 } from 'ajv/dist/2020.js';

export interface Overrides {
  unions: Record<string, string>;
  flatten: string[];
  dedup: Record<string, string[]>;
  renames: Record<string, string>;
  docs: Record<string, string>;
  backendsApplyingTheme: string[];
  hostStateKeys: string[];
  ignoredOptions: Record<string, string[]>;
  keepObsolete: string[];
}

export function emptyOverrides(): Overrides {
  return {
    unions: {},
    flatten: [],
    dedup: {},
    renames: {},
    docs: {},
    backendsApplyingTheme: [],
    hostStateKeys: [],
    ignoredOptions: {},
    keepObsolete: [],
  };
}

const schema = JSON.parse(readFileSync(new URL('../overrides.schema.json', import.meta.url), 'utf8'));
const validate = new Ajv2020({ allErrors: true, strict: true }).compile(schema);

export function parseOverrides(value: unknown): Overrides {
  if (!validate(value)) {
    const errors = (validate.errors ?? []).map((e) => `${e.instancePath || '/'} ${e.message}${e.params && 'additionalProperty' in e.params ? ` (${e.params.additionalProperty})` : ''}`);
    throw new Error(`overrides.json does not match overrides.schema.json: ${errors.join('; ')}`);
  }
  const { $schema: _schema, ...rest } = value as Partial<Overrides> & { $schema?: string };
  return { ...emptyOverrides(), ...rest };
}

export function loadOverrides(file: string): Overrides {
  return parseOverrides(JSON.parse(readFileSync(file, 'utf8')));
}

/** Records which override entries were applied, so that an entry matching nothing fails generation. */
export class OverrideUse {
  private readonly used = new Set<string>();

  constructor(private readonly overrides: Overrides) {}

  union(path: string): string | undefined {
    return this.take('unions', path, this.overrides.unions[path]);
  }

  flatten(path: string): boolean {
    return this.take('flatten', path, this.overrides.flatten.includes(path) ? true : undefined) ?? false;
  }

  dedup(path: string): string | undefined {
    const target = Object.keys(this.overrides.dedup).find((t) => this.overrides.dedup[t].includes(path));
    return this.take('dedup', path, target);
  }

  rename(path: string): string | undefined {
    return this.take('renames', path, this.overrides.renames[path]);
  }

  docs(path: string): string | undefined {
    return this.take('docs', path, this.overrides.docs[path]);
  }

  /** Throws for every entry of the given sections whose key matches `scope` and was never applied. */
  assertAllUsed(scope: (key: string) => boolean): void {
    const all = [
      ...Object.keys(this.overrides.unions).map((k) => `unions:${k}`),
      ...this.overrides.flatten.map((k) => `flatten:${k}`),
      ...Object.values(this.overrides.dedup).flatMap((paths) => paths.map((k) => `dedup:${k}`)),
      ...Object.keys(this.overrides.renames).map((k) => `renames:${k}`),
      ...Object.keys(this.overrides.docs).map((k) => `docs:${k}`),
    ];
    const stale = all.filter((entry) => scope(entry.slice(entry.indexOf(':') + 1)) && !this.used.has(entry));
    if (stale.length > 0) throw new Error(`overrides.json entries that match nothing: ${stale.join(', ')}.`);
  }

  private take<T>(section: string, path: string, value: T | undefined): T | undefined {
    if (value !== undefined) this.used.add(`${section}:${path}`);
    return value;
  }
}
