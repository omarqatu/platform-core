# web — the web interface

React + TypeScript, built with Vite. Text through FormatJS (`react-intl`), ICU messages; two languages, Arabic
(right-to-left, the default) and English.

```
web/
  index.html
  GLOSSARY.md                the interface's terms (Organization / الجهة, …) — check-i18n-parity enforces them
  src/
    main.tsx                 I18nProvider → BrowserRouter → SessionProvider → App
    App.tsx                  the routes
    api/client.ts            request(): /api, X-Requested-With, the error code of any status; 401 → the session;
                             abortAll() for every request in flight
    session/
      SessionProvider.tsx    the session from GET /me (no storage); signIn, selectTenant, switchTenant, signOut,
                             revalidate
      guards.tsx             RequireAuth (→ /login?next=…), RequireTenant (→ /organizations; one subtree per tenant)
      next.ts                safeNext(): the return path after signing in — internal paths only
    components/              IdentityBar, LanguageSwitcher, ErrorAlert (role="alert", focused)
    i18n/                    locale, compiled messages, I18nProvider, apiErrors
    screens/
      auth/                  the login screen
      tenants/               the organization selection (and "no active membership")
      status/                loading, not permitted, not found
      subscriptions/         the T8 screen
  e2e/                       Playwright [B] against a real Api (npm run e2e)
  locales/en.json            generated: npm run i18n:extract — never edited by hand
  locales/ar.json            edited by hand
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
npm run e2e        # Playwright against a running Api serving dist/ (E2E_BASE_URL, default http://127.0.0.1:5080)
```

The E2E tests sign in as the seed contract's users (passwords from `Seed:PasswordFormat`, as Conformance) and reach
the database as migrator for their fixtures (`ConnectionStrings__migrator`), restoring what they change. CI runs them
after Conformance, against the Api container. Locally: an Api with `Web__Root` pointing at `dist/`, and
`PW_CHROMIUM_PATH` if Chromium is installed elsewhere.

## Routing and the session

- **react-router** (library mode: `BrowserRouter`, `Routes`): the simplest router that gives deep links and history.
- **Paths:** `/login`, `/organizations`, and the organization's screens under `/app/` (`/app/subscriptions`). An
  interface path never equals an API path: Api serves `index.html` only for a page no API route matches, so a reload of
  `/subscriptions` would be the API's JSON (OPEN_ITEMS 37).
- **The session** is the HTTP-only cookie and `GET /me` — nothing in `localStorage` or `sessionStorage` (the `lang`
  cookie aside). After signing in: no active membership → a status screen with "Sign out"; one → entered directly; more
  → the selection. Only active memberships in active organizations count.
- **Switching or leaving an organization:** the state changes first, so the organization's subtree (keyed by it) is gone
  in the same render, and every request in flight is aborted. There is no data cache beyond that subtree's state.
- **An ended session** (any 401 but login's — expiry, or Api restarted, OPEN_ITEMS 18): the login screen with a
  message and the way back (`?next=`, internal paths only). **A lost organization** (`/me` names none — a membership
  disabled, an organization suspended): checked on each navigation, when the window comes back (focus, visible), and
  when a data request is refused with `not_a_member`, `no_active_tenant` or `tenant_not_active`; everything is dropped
  and the selection shows.
- **Selection refusals** (`membership_scope_missing`, `step_up_required`, `tenant_not_active`, `not_a_member`, …) are
  shown as they are, focused, and the screen stays. The client reads the code of a 500 too.

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
