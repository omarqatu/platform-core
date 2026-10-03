// Fresh extraction with the formatjs CLI, into a temporary file: never the committed locales/en.json.
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const WEB = join(dirname(fileURLToPath(import.meta.url)), '..');

/** { id: defaultMessage } for the given globs or files (relative to web/), as `npm run i18n:extract` produces it. */
export function extract(sources, ignore = ['src/**/*.test.{ts,tsx}']) {
  const dir = mkdtempSync(join(tmpdir(), 'i18n-extract-'));
  try {
    const out = join(dir, 'out.json');
    const args = ['extract', ...sources, ...ignore.flatMap((g) => ['--ignore', g]), '--out-file', out, '--format', 'simple', '--throws'];
    execFileSync(join(WEB, 'node_modules/.bin/formatjs'), args, { cwd: WEB, stdio: ['ignore', 'ignore', 'inherit'] });
    return JSON.parse(readFileSync(out, 'utf8'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

export function readJson(path) {
  return JSON.parse(readFileSync(path, 'utf8'));
}

/** The keys of `a` not in `b`, sorted. */
export function missing(a, b) {
  const have = new Set(b);
  return [...new Set(a)].filter((k) => !have.has(k)).sort();
}
