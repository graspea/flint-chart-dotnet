// Locates the installed flint-chart package and its pinned metadata.
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const PACKAGE_NAME = 'flint-chart';

/** Directory of the installed flint-chart package (package.json is not in its exports map, so walk up). */
export function flintPackageDir(): string {
  let dir = dirname(fileURLToPath(import.meta.resolve(PACKAGE_NAME)));
  for (;;) {
    const manifest = join(dir, 'package.json');
    if (existsSync(manifest) && JSON.parse(readFileSync(manifest, 'utf8')).name === PACKAGE_NAME) return dir;
    const parent = dirname(dir);
    if (parent === dir) throw new Error(`Cannot locate the ${PACKAGE_NAME} package directory.`);
    dir = parent;
  }
}

export function packageManifest(packageDir: string): { name: string; version: string; types: string } {
  return JSON.parse(readFileSync(join(packageDir, 'package.json'), 'utf8'));
}

/** The integrity hash recorded for the installed package in the generator lock file. */
export function lockedIntegrity(lockFile: string): string {
  const lock = JSON.parse(readFileSync(lockFile, 'utf8'));
  const entry = lock.packages?.[`node_modules/${PACKAGE_NAME}`];
  if (!entry?.integrity) throw new Error(`${lockFile} records no integrity for ${PACKAGE_NAME}.`);
  return entry.integrity;
}
