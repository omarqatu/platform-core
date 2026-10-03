// The ESLint rules on messages and direction, seen failing (PROOF_SPEC T2.2's rule for checks): planted code is linted
// in memory with the repository's own eslint.config.js, and every plant must be reported by its rule. No file is written.
//
// Usage (from web/): node scripts/lint-self-test.mjs
import { ESLint } from 'eslint';

const PLANT = `import { defineMessages, useIntl } from 'react-intl';
const m = defineMessages({ a: { defaultMessage: 'No id' }, b: { id: 'not a pattern', defaultMessage: 'x' } });
const n = defineMessages({ c: { id: 'Auth.login.title', defaultMessage: 'y' } });
export function Plant({ code }: { code: string }) {
  const intl = useIntl();
  return (
    <div style={{ marginLeft: 4 }} className="pr-2">
      Literal text
      {intl.formatMessage({ id: \`errors.\${code}\`, defaultMessage: 'x' })}
      {intl.formatMessage(m.a)}
      {intl.formatMessage(m.b)}
      {intl.formatMessage(n.c)}
    </div>
  );
}
`;

// [line, rule]: each must be reported.
const EXPECTED = [
  [2, 'formatjs/enforce-id'], // no id
  [2, 'formatjs/enforce-id'], // an id outside the allowlisted patterns
  [3, 'no-restricted-syntax'], // a segment with a capital, which enforce-id's case-insensitive allowlist accepts
  [7, 'no-restricted-syntax'], // marginLeft
  [7, 'no-restricted-syntax'], // pr-2
  [7, 'formatjs/no-literal-string-in-jsx'], // the text node begins on the line of the tag before it
  [9, 'no-restricted-syntax'], // an id built at run time
];

const eslint = new ESLint();
const [result] = await eslint.lintText(PLANT, { filePath: 'src/Plant.tsx' });
const reported = result.messages.map((m) => [m.line, m.ruleId]);
for (const m of result.messages) console.log(`${m.line}:${m.column} ${m.ruleId} — ${m.message.split('\n')[0]}`);

const remaining = [...reported];
const absent = EXPECTED.filter(([line, rule]) => {
  const i = remaining.findIndex(([l, r]) => l === line && r === rule);
  if (i < 0) return true;
  remaining.splice(i, 1);
  return false;
});
if (absent.length) {
  console.log(`FAIL (self-test): not reported: ${absent.map(([l, r]) => `${l} ${r}`).join(', ')}`);
  process.exit(1);
}
console.log('PASS (self-test): enforce-id, no-literal-string-in-jsx, the run-time id and id-case rules and the logical-direction rules each report their plant.');
