// check-i18n-parity — a local repository check (checks/local/README.md). The Arabic catalogue holds exactly the
// messages of the code: the keys of locales/ar.json are the keys of a fresh extraction of src/ — none missing, none
// extra. Also: locales/en.json is that extraction (committed, never edited by hand), and every plural in ar.json gives
// all six Arabic forms (zero, one, two, few, many, other).
//
// Usage (from web/): node scripts/check-i18n-parity.mjs [--self-test]
import { parse, TYPE } from '@formatjs/icu-messageformat-parser';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { extract, missing, readJson, WEB } from './extract.mjs';

const ARABIC_PLURAL_FORMS = ['zero', 'one', 'two', 'few', 'many', 'other'];

/** Every problem between the extracted messages, the committed en.json and ar.json, as lines. */
export function problems(extracted, en, ar) {
  const out = [];
  const ids = Object.keys(extracted);
  for (const id of missing(ids, Object.keys(ar))) out.push(`missing in ar.json: ${id}`);
  for (const id of missing(Object.keys(ar), ids)) out.push(`in ar.json, not in the code: ${id}`);
  const enStale = missing(ids, Object.keys(en)).length > 0 || missing(Object.keys(en), ids).length > 0 ||
    ids.some((id) => en[id] !== extracted[id]);
  if (enStale) out.push('locales/en.json is not the current extraction: run `npm run i18n:extract` and commit it');
  for (const [id, message] of Object.entries(ar)) {
    let ast;
    try {
      ast = parse(message);
    } catch (error) {
      out.push(`ar.json ${id}: not valid ICU (${error.message})`);
      continue;
    }
    for (const plural of plurals(ast)) {
      const absent = ARABIC_PLURAL_FORMS.filter((form) => !(form in plural.options));
      if (absent.length) out.push(`ar.json ${id}: {${plural.value}, plural} lacks ${absent.join(', ')}`);
    }
  }
  return out;
}

function* plurals(elements) {
  for (const element of elements) {
    if (element.type === TYPE.plural && element.pluralType !== 'ordinal') yield element;
    if (element.options) for (const option of Object.values(element.options)) yield* plurals(option.value);
    if (element.children) yield* plurals(element.children);
  }
}

function selfTest(extracted, en, ar) {
  // Plant one key missing from ar.json, one extra key, and a plural with two Arabic forms only.
  const [removed] = Object.keys(ar);
  const planted = { ...ar, 'planted.extra.key': 'زائد', 'planted.plural.key': '{n, plural, one {واحد} other {#}}' };
  delete planted[removed];
  const found = problems(extracted, en, planted);
  console.log(found.join('\n'));
  const expected = [
    `missing in ar.json: ${removed}`,
    'in ar.json, not in the code: planted.extra.key',
    'in ar.json, not in the code: planted.plural.key',
    'ar.json planted.plural.key: {n, plural} lacks zero, two, few, many',
  ];
  if (found.length === expected.length && expected.every((line) => found.includes(line))) {
    console.log('PASS (self-test): check-i18n-parity reports the missing key, the extra key and the incomplete plural.');
    return 0;
  }
  console.log('FAIL (self-test): check-i18n-parity did not report exactly the planted differences.');
  return 1;
}

const extracted = extract(['src/**/*.{ts,tsx}']);
const en = readJson(join(WEB, 'locales/en.json'));
const ar = JSON.parse(readFileSync(join(WEB, 'locales/ar.json'), 'utf8'));
if (process.argv.includes('--self-test')) process.exit(selfTest(extracted, en, ar));

const found = problems(extracted, en, ar);
if (found.length) {
  console.log(found.join('\n'));
  console.log(`FAIL: check-i18n-parity — ${found.length} difference(s) between the code's messages and locales/.`);
  process.exit(1);
}
console.log(`PASS: check-i18n-parity — ar.json has exactly the ${Object.keys(extracted).length} extracted messages.`);
