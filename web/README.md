# web — the web interface

React + TypeScript, built with Vite. Text through FormatJS (`react-intl`), ICU messages; two languages, Arabic
(right-to-left, the default) and English.

```
web/
  index.html
  src/
    main.tsx                 the root: I18nProvider around App
    App.tsx
    api/client.ts            fetch under /api; a refusal is ApiError(status, code)
    i18n/
      locale.ts              the languages, the Intl locale per language, dir, the "lang" cookie, the initial choice
      messages.ts            the compiled catalogues (compiled-locales/)
      I18nProvider.tsx       IntlProvider, document lang/dir and the cookie; useLocale() → { lang, setLang }
      apiErrors.ts           one message per API error code; errorMessage(code)
    screens/subscriptions/   the T8 screen (the i18n proof)
  locales/
    en.json                  generated: npm run i18n:extract — never edited by hand
    ar.json                  edited by hand
  compiled-locales/          generated: npm run i18n:compile (--ast); not committed
  scripts/                   the checks' logic (see checks/local/README.md) and the ESLint self-test
```

## Running

```bash
npm ci
npm run dev        # Vite on :5173; /api/* is forwarded to Api (API_URL, default http://127.0.0.1:5080)
npm test           # vitest (jsdom)
npm run lint
npm run build      # dist/
```

`dev`, `build`, `typecheck` and `test` compile the messages first.

## How it is served

Same origin as the API, always: no CORS, and the session stays Api's HTTP-only cookie.

- **Production:** the Api image builds `web/` in its own stage and copies `dist/` to `wwwroot` beside `Api.dll`
  (`src/Api/Dockerfile`). Api (`src/Api/WebInterface.cs`) removes the `/api` prefix before routing — `/api/x` is
  routed as `/x`, so every API route keeps its path and behaviour — and, only for a GET no route matched outside
  `/api`, serves a file of the build, or `index.html` for a page (`Accept: text/html`). Anything else is answered as
  before. Without a build (`Web:Root` absent), nothing changes.
- **Development:** Vite's proxy forwards `/api/*` to Api with the prefix removed, and the browser's `Origin` as it
  is. Open the interface at `http://localhost:5173` exactly: it is the one origin Api's Development configuration
  allows.

## CSRF

Api refuses every unsafe request (POST, PUT, PATCH, DELETE — login and invitation acceptance included) unless both
hold, with 403 `csrf_rejected`, before any database command (`src/Api/CsrfProtection.cs`):

- `Origin` equals, character for character, one of `Security:AllowedOrigins`. No `Origin`: refused (never a fallback
  to `Referer`). The browser sends it itself on every unsafe request.
- `X-Requested-With: platform-web`. `src/api/client.ts` — the one way the interface reaches the API — sends it on
  every request. Another origin cannot send a custom header without a CORS preflight, which Api never grants.

`Security:AllowedOrigins` is a list of serialized origins (`https://app.example.com`: scheme, host, port if not the
default; no path, no trailing slash). Api refuses to start with none, or with an entry a browser could never send. Each
environment sets its own: Development has `http://localhost:5173`; production sets its public origin
(`Security__AllowedOrigins__0=https://…`); CI sets the Api container's own (`.github/workflows/ci.yml`).

## Messages

- Every message has an explicit, stable `id`: `<area>.<screen>.<element>` (`auth.login.title`), or
  `errors.<code>` for an API error code. Never an id built at run time (`` `errors.${code}` ``): it escapes extraction.
- `defaultMessage` is the English text, in the code. Arabic only in `locales/ar.json`.
- Counts are ICU `plural`; in Arabic with all six forms (zero, one, two, few, many, other).
- Arabic is formatted as `ar-u-nu-latn`: Latin digits in every Intl format (numbers, plurals, dates).
- What users entered (client and service names) is shown as is, never translated, in `<bdi dir="auto">`.
- Style sheets use logical properties only (`margin-inline-start`, `text-align: start`, …). No Tailwind.

## ESLint

- `formatjs/enforce-id`: the plugin's rule checks ids against a content hash (`idInterpolationPattern`) unless the id
  matches its `idWhitelist`. The allowlist is our two patterns — `^[a-z][a-zA-Z0-9]*\.[a-z][a-zA-Z0-9]*\.[a-z][a-zA-Z0-9]*$`
  and `^errors\.[a-z][a-z0-9_]*$` — so an explicit id in that shape passes, and a missing id, or one in any other
  shape, fails (its "expected" hash in the message is never meant to be used). The plugin compiles the allowlist
  case-insensitively, so a `no-restricted-syntax` rule refuses a segment starting with a capital.
- `formatjs/no-literal-string-in-jsx`: no text in JSX, nor in `aria-*`, `alt`, `title`, `placeholder`.
- `no-restricted-syntax`: an `id` that is not a string literal (in `defineMessage(s)`, `formatMessage({...})`,
  `<FormattedMessage>`); physical sides in inline styles (`marginLeft`, `right`, …) and in class names (`ml-`, `pr-`,
  `left-`, `text-right`, …).
- Also `enforce-default-message`, `no-invalid-icu`, `enforce-placeholders`. Tests are exempt from
  `no-literal-string-in-jsx`.
- `npm run lint:self-test` lints planted code in memory and fails unless each rule reports its plant.
