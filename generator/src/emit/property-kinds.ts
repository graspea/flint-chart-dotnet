// Property kind rule (design §7.4): one key with different kinds across the backends of one template fails
// generation with both IR paths, unless overrides.json maps "<Template>/property/<key>" (to JsonNode).
import type { Ir } from '../ir.ts';
import { OverrideUse, type Overrides } from '../overrides.ts';

export function checkPropertyKinds(ir: Ir, overrides: Overrides): void {
  const use = new OverrideUse(overrides);
  const errors: string[] = [];
  for (const t of ir.catalog.chartTypes) {
    const seen = new Map<string, { kind: string; path: string }>();
    for (const backend of ir.catalog.backends.filter((b) => b in t.backends)) {
      for (const p of t.backends[backend].properties) {
        const path = `catalog.chartTypes[${t.name}].backends.${backend}.properties[${p.key}]`;
        const first = seen.get(p.key);
        if (!first) seen.set(p.key, { kind: p.type, path });
        else if (first.kind !== p.type && use.union(`${t.name}/property/${p.key}`) === undefined) {
          errors.push(`${first.path} is ${first.kind} but ${path} is ${p.type}`);
        }
      }
    }
  }
  if (errors.length > 0) {
    throw new Error(`Property kind mismatch (map "<Template>/property/<key>" in overrides.json unions): ${errors.join('; ')}.`);
  }
  use.assertAllUsed((key) => key.includes('/property/'));
}
