// check-api-error-codes, the interface's half (checks/local/check-api-error-codes.sh runs the backend's half first).
// Every code in checks/local/api-error-codes.json — the export of src/Core/ApiErrorCodes.cs — has its entry
// errors.<code> in apiErrors (src/i18n/apiErrors.ts), and every entry but errors.unknown has its code. An errors.* id
// anywhere else in src/ fails too: apiErrors is the only place for them.
//
// Usage (from web/): node scripts/check-api-error-codes.mjs [--self-test]
import { join } from 'node:path';
import { extract, missing, readJson, WEB } from './extract.mjs';

const CODES = join(WEB, '../checks/local/api-error-codes.json');
const API_ERRORS = 'src/i18n/apiErrors.ts';

export function problems(codes, entryIds, allErrorIds) {
  const out = [];
  if (!entryIds.includes('errors.unknown')) out.push('no entry errors.unknown in apiErrors');
  const expected = codes.map((code) => `errors.${code}`);
  for (const id of missing(expected, entryIds)) out.push(`code without an entry in apiErrors: ${id.slice('errors.'.length)}`);
  for (const id of missing(entryIds, [...expected, 'errors.unknown'])) out.push(`entry without a code in the backend's list: ${id}`);
  for (const id of missing(allErrorIds, entryIds)) out.push(`errors.* id outside ${API_ERRORS}: ${id}`);
  return out;
}

const codes = readJson(CODES).codes;
const entryIds = Object.keys(extract([API_ERRORS], []));
const allErrorIds = Object.keys(extract(['src/**/*.{ts,tsx}'])).filter((id) => id.startsWith('errors.'));

if (process.argv.includes('--self-test')) {
  // Plant a backend code with no entry, and drop a backend code so its entry has none.
  const [dropped, ...rest] = codes;
  const found = problems([...rest, 'planted_code'], entryIds, [...allErrorIds, 'errors.planted_elsewhere']);
  console.log(found.join('\n'));
  const expected = [
    'code without an entry in apiErrors: planted_code',
    `entry without a code in the backend's list: errors.${dropped}`,
    `errors.* id outside ${API_ERRORS}: errors.planted_elsewhere`,
  ];
  if (found.length === expected.length && expected.every((line) => found.includes(line))) {
    console.log('PASS (self-test): check-api-error-codes (interface) reports the code without an entry, the entry without a code, and the stray id.');
    process.exit(0);
  }
  console.log('FAIL (self-test): check-api-error-codes (interface) did not report exactly the planted differences.');
  process.exit(1);
}

const found = problems(codes, entryIds, allErrorIds);
if (found.length) {
  console.log(found.join('\n'));
  console.log('FAIL: check-api-error-codes — apiErrors and the backend\'s codes differ.');
  process.exit(1);
}
console.log(`PASS: check-api-error-codes — ${codes.length} codes, each with its entry in apiErrors (and errors.unknown).`);
