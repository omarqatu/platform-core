import js from '@eslint/js';
import formatjs from 'eslint-plugin-formatjs';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

// Message ids are explicit and stable: <area>.<screen>.<element> (auth.login.title), and errors.<code> for the API's
// error codes. enforce-id accepts an id only if it matches one of these; an id that is missing fails too.
const MESSAGE_IDS = ['^[a-z][a-zA-Z0-9]*\\.[a-z][a-zA-Z0-9]*\\.[a-z][a-zA-Z0-9]*$', '^errors\\.[a-z][a-z0-9_]*$'];

// The direction is the document's: physical sides would be wrong in one of the two. Logical properties only.
const PHYSICAL_STYLE = '/^(margin|padding|border)(Left|Right)|^(left|right)$|^border(Top|Bottom)?(Left|Right)/';
const PHYSICAL_CLASS = '/(^|\\s)-?(ml|mr|pl|pr|left|right|border-l|border-r|rounded-l|rounded-r|text-left|text-right)-/';

export default tseslint.config(
  { ignores: ['dist', 'compiled-locales', 'node_modules'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: { globals: globals.browser },
    plugins: { formatjs, 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'formatjs/enforce-id': ['error', { idInterpolationPattern: '[sha512:contenthash:base64:6]', idWhitelist: MESSAGE_IDS }],
      'formatjs/enforce-default-message': ['error', 'literal'],
      'formatjs/no-literal-string-in-jsx': 'error',
      'formatjs/no-invalid-icu': 'error',
      'formatjs/enforce-placeholders': 'error',
      'no-restricted-syntax': [
        'error',
        {
          // An id built at run time (`errors.${code}`) escapes extraction: every id is a string literal.
          selector:
            'CallExpression[callee.name=/^define(Message|Messages)$/] Property[key.name="id"]:not([value.type="Literal"])',
          message: 'A message id must be a string literal (no id built at run time).',
        },
        {
          selector:
            'CallExpression[callee.property.name="formatMessage"] > ObjectExpression > Property[key.name="id"]:not([value.type="Literal"])',
          message: 'A message id must be a string literal (no id built at run time).',
        },
        {
          selector: 'JSXOpeningElement[name.name="FormattedMessage"] > JSXAttribute[name.name="id"][value.type!="Literal"]',
          message: 'A message id must be a string literal (no id built at run time).',
        },
        {
          // enforce-id compiles its allowlist case-insensitively: a segment starting with a capital is refused here.
          selector: 'CallExpression[callee.name=/^define(Message|Messages)$/] Property[key.name="id"] > Literal[value=/(^|\\.)[A-Z]/]',
          message: 'Each segment of a message id starts with a lower-case letter (<area>.<screen>.<element>).',
        },
        {
          selector: `JSXAttribute[name.name="style"] Property[key.name=${PHYSICAL_STYLE}]`,
          message: 'Use a logical property (marginInlineStart, insetInlineEnd, …), not a physical side.',
        },
        {
          selector: `JSXAttribute[name.name="className"] Literal[value=${PHYSICAL_CLASS}]`,
          message: 'Use a logical class (ms-/me-/ps-/pe-/start-/end-), not a physical side.',
        },
      ],
    },
  },
  {
    // The repository's own scripts (checks and their self-tests) run on Node.
    files: ['scripts/**/*.mjs', '*.config.{js,ts}', 'e2e/**/*.ts'],
    languageOptions: { globals: globals.node },
  },
  {
    // Tests build fixtures and assert on rendered text; the rules on shipped code do not apply to them.
    files: ['**/*.test.{ts,tsx}'],
    rules: { 'formatjs/no-literal-string-in-jsx': 'off' },
  },
);
