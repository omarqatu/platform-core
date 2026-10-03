// check-logical-css — the interface is laid out in both directions, so its style sheets use logical properties only
// (margin-inline-start, inset-inline-end, text-align: start, …), never a physical side. Scans every .css file under
// src/. (Inline styles and class names in TSX are ESLint's: eslint.config.js.)
//
// Usage (from web/): node scripts/check-logical-css.mjs [--self-test]
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { WEB } from './extract.mjs';

const PHYSICAL = [
  /\b(margin|padding|border|scroll-margin|scroll-padding)-(left|right)\b/,
  /\bborder-(top|bottom)-(left|right)-radius\b/,
  /(^|[\s;{])(left|right)\s*:/,
  /\b(text-align|float|clear)\s*:\s*(left|right)\b/,
];

export function scan(name, text) {
  const hits = [];
  text.split('\n').forEach((line, i) => {
    const code = line.replace(/\/\*.*?\*\//g, '');
    if (PHYSICAL.some((p) => p.test(code))) hits.push(`${name}:${i + 1}: ${line.trim()}`);
  });
  return hits;
}

function* cssFiles(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) yield* cssFiles(path);
    else if (entry.name.endsWith('.css')) yield path;
  }
}

if (process.argv.includes('--self-test')) {
  const plant = '.a {\n  margin-left: 1rem;\n  padding-inline-start: 1rem;\n  text-align: right;\n  right: 0;\n  border-top-left-radius: 2px;\n}\n';
  const hits = scan('plant.css', plant);
  console.log(hits.join('\n'));
  const lines = hits.map((h) => Number(h.split(':')[1]));
  if (JSON.stringify(lines) === JSON.stringify([2, 4, 5, 6])) {
    console.log('PASS (self-test): check-logical-css reports the four physical properties and not the logical one.');
    process.exit(0);
  }
  console.log('FAIL (self-test): check-logical-css did not report exactly the planted physical properties.');
  process.exit(1);
}

const hits = [...cssFiles(join(WEB, 'src'))].flatMap((f) => scan(relative(WEB, f), readFileSync(f, 'utf8')));
if (hits.length) {
  console.log(hits.join('\n'));
  console.log('FAIL: check-logical-css — physical sides in style sheets; use logical properties.');
  process.exit(1);
}
console.log('PASS: check-logical-css — logical properties only.');
