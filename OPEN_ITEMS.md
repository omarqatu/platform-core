# Open items

Items carried between tasks so nothing is lost when a task is built. Each one
names the task it belongs to. An item is removed only by the PR that closes it.
Numbers are kept stable, so earlier PRs still refer to the right item; new items take the next number.

Closed by the T2 PR:
- **1.** `check-schema-allowlist` runs in the catalog-check steps beside Checks
  1–10, check and self-test, as a named local check.
- **2.** No application role has USAGE or CREATE on any schema except `public`.
  Granted by name in `0003_grants.sql`, enforced by Check 6.
- **3.** CONNECT and TEMP on the database: nothing through PUBLIC, CONNECT for
  the four application roles, TEMP for none. Enforced by Check 6.
- **4.** T1.2, T1.3 and T1.6 re-proven in Conformance against a real table (`roles`,
  with the seed contract's tenants); `t1_probe`, its fixtures and the residue step
  removed.
- **5.** The catalog checks run before every test step in CI.

Closed by the T2b PR:
- **10.** Attribution on the scope surface: PLATFORM_CORE v1.15 dropped `membership_scope.updated_by`
  and `updated_at`, and `scope_assignments.granted_by` and `granted_at`; migration 0006 drops them.
  The audit log is the sole source of attribution.

Closed by the T3 PR:
- **7.** Test 18's second part (T3.8): a membership with no `membership_scope` row → a loud
  error at tenant selection (`membership_scope_missing`, the tenant not entered); beneath it,
  with the membership set and no scope resolved, zero rows.
- **11.** Test 17-c: a scope variable from a header, the query string, or the payload → no
  effect; an `assigned` member stays `assigned` and cannot manage.
- **6, the memberships part** (T3.7): on a reused connection, `app.user_id` alone → the caller's
  own memberships only, with no error, after COMMIT or ROLLBACK, with or without DISCARD ALL.
  The `client_scope` part stays open below, for T5.

Closed by the T4 PR:
- **8.** Zero RETURNING across the full paths (T4.10): bootstrap, a new-account acceptance and a return, through
  the real EF commands with a command recorder — every table they write, and no command containing RETURNING.
- **17.** Automatic auditing (T4.15): `AutomaticAuditInterceptor` captures at `SavingChanges` and writes the entries
  by a second save from `SavedChanges`, in the same transaction, under a recursion flag; T3's explicit audit rows are
  gone, and `ExecuteUpdate`/`ExecuteDelete` are forbidden in `src/` by `check-no-bulk-writes` (T4.16).

Closed by the T5 PR:
- **6, the client_scope part** (T5.8): on a reused connection where all six variables were set, a query with no
  context on `subscriptions` and `clients` → zero rows, no error, after COMMIT or ROLLBACK, with or without
  DISCARD ALL.
- **9.** Test 21, the `scope_ref_id` part (T5.9): a subscription's `scope_ref_id` changed to another tenant's client →
  `23503` from the module's composite FK, as migrator and as `app_user`.
- **13.** Test 20, the visible rows: a revoked assignment and a lowered mode during a live session → the next
  request lists zero subscriptions of that client, with no re-login.

Closed by the CSRF PR:
- **28.** CSRF: `CsrfProtection` (Api) refuses every unsafe request (POST, PUT, PATCH, DELETE — login and
  invitation acceptance included) unless its `Origin` equals one of `Security:AllowedOrigins` (no `Referer`
  fallback) and it carries `X-Requested-With: platform-web`: 403 `csrf_rejected`, before routing and any database
  command. Conformance `CsrfTests` [B]; `CsrfNoDatabaseCommandTests` [W].

Closed by the identity screens PR:
- **38.** "Switch organization", then a reload, no longer returns to the previous organization: `POST
  /tenants/deselect` (the owner's decision) issues the cookie again with the user alone — no database command, CSRF
  applies — and the interface calls it before showing the selection. Conformance `Deselect_*`, WhiteBox
  `SessionEndpointCommandTests`, E2E 7.

Closed by the API-path PR:
- **37.** The API lives under `/api` only (the owner's decision): every route is registered in that group; the bare
  routes and the prefix stripping of #11 (`/api/x` and `/x` both answering — the double path, which had no number of
  its own) are gone. A bare API path and any unknown path under `/api` are a plain 404, never `index.html`; any other
  page path is the interface. The one documented exception is the original T8 screen, `/subscriptions/screen`
  (`WebInterface.ServerRenderedScreens`). The Conformance harness takes its base path from configuration
  (`Api:BasePath`, `Api:RootPaths`); the WhiteBox tests call `/api/...`. Conformance `ApiPathTests`, WhiteBox
  `ApiRoutesTests`.

Closed by the manifest PR:
- **50.** The manifest reads the amendments (approved by the project owner as a change to check inputs, this change
  alone). `checks/core/generate-manifest.py` reads v1.16, then its amendments by number, each in Arabic and English:
  the two must state the same `CREATE POLICY` statements (comments aside), and the English text lands in the
  manifest. Amendment 01 §4.1 is now `CREATE POLICY` in an sql block in place of its table. An amendment policy
  enters the manifest only once it is built — listed in `checks/core/amendment-policies-built.json` by the code PR
  that creates it (empty today, so the manifest's policies are unchanged); a name never leaves that file except by
  the project owner's decision. The local check `check-manifest-extraction` (CI, with a self-test of four plants)
  fails while the manifest is not the extraction. The 17 policies were run on PostgreSQL 18.6, both ways, against
  stand-in tables. **The record:** the amendment's own line — "تعديل المولّد شرط لأول PR كود" — was incomplete. The
  manifest was all-or-nothing (Checks 2 and 3 require every manifest table and policy to exist), while the amendment
  is built across several PRs; changing the generator was not enough without a way to adopt its policies one PR at a
  time.

Closed by the A3 PR:
- **53.** `POST /api/auth/password/forgot`: the address, and who may read it. Amendment 01 contradicted itself — §5.1
  makes `forgot` an `authenticator` unit of work keyed by the address, while its §3 matrix carried v1.16's `persons`
  row verbatim, giving `authenticator` nothing on the table where the address lives. Resolved in the governing text,
  before any code, because the manifest is extracted from it: §3 grants `authenticator` `SELECT (id, email)` on
  `persons` — those two columns alone — with a justification line naming why no other role can serve a sessionless
  lookup (`app_user` needs `app.user_id`, every `provisioner` policy needs `app.tenant_id`, `job_runner` has no
  grant, `migrator` is forbidden to the application); §4.1 states `persons_authenticator_read` as a `CREATE POLICY`
  in the sql block, matching Arabic and English; and §0 records the amended v1.16 paragraphs honestly — the §4.3-a/2
  row is widened to its full 1.16 text, because that text enumerates the role's reads as a closed list, and a
  **ninth row** is added for §4.3-a/3, whose "three resolution tables" become four. §4.3-a/4 is untouched: this is a
  read, not a write. The policy is extracted as pending until the code PR lists it in
  `checks/core/amendment-policies-built.json`.
- **51.** Forgot-password: the response time reveals whether an account exists. **Closed, and by a change of
  shape rather than by padding.** The leak was a provider round trip on one branch and nothing on the other; a
  floor alone would have had to hide a latency nobody controls, which is the half-fix. So the send left the
  request: `Core.Notifications.INotificationDispatch` takes the message and the sender runs outside the
  response, leaving the two branches differing only by a token insert and the consumption of any outstanding
  token. A floor (`Identity:ForgotPassword:MinimumResponseTime`, 1.5 s by default, a marked assumption) then
  covers that difference, and the limiter's escalating delay is added identically either way because **every**
  request writes its `auth_attempts` row, found or not. Proven in `[W]`
  (`T3_ForgotPasswordTests.TheAnswerTakesTheSameTime_…`: each answer at or above the floor, the two medians
  within half of it) and in `[B]` (the uniform 204 and the token that is minted only where there is an account).
  **The residue, stated:** the floor is honest while the work stays well under it; if the database slowed past
  it, the difference would show again. The floor overruns nothing today and the work on the path is two small
  statements. **For the project owner:** Amendment 01 §5.1's table still says "the response time is not"
  uniform, with a reference to this item. That line is now false; whether the amendment records the closure is
  a text decision, not one this PR took.
- **58 was raised and then withdrawn, so it is not an item.** It asked which role resolves the provider account
  for a sessionless send, the amendment naming only `app_user`. The first answer here was to read the platform
  row as `app_user` with neither a user nor a tenant set. The project owner rejected it: an application role
  running contextless on a sessionless path is a surface no document describes, and it was a reading of §2.6
  rather than its text — the same lesson as item 53. So the amendment says it instead (`SELECT` on
  `provider_accounts` to `authenticator`, `provider_accounts_authenticator_read` `USING (tenant_id IS NULL)`,
  §0's tenth amended paragraph), and there is nothing left to carry. The number is retired, not reused.

Closed by the password-screens PR:
- **59.** The three screens of password recovery and change — `/app/password/forgot`, `/app/password/reset` and
  `/app/password/change` — in both languages, with their message ids and the Arabic in `web/locales/ar.json`.
  The link the A3 e-mail sends now reaches a page: `Identity:ForgotPassword:ResetLinkTemplate` already pointed at
  `/app/password/reset#token=…`, and no backend change was needed to make it real. The token is handled exactly
  as the invitation's — `web/src/password/pendingResetToken.ts`, into memory before anything renders, out of the
  address, `hashchange` watched — so it never reaches a server log or a referrer. Forgot shows one panel in one
  wording for every address, with no redirect and nothing timed in the browser, which is what keeps A3's uniform
  204 uniform where the user can see it. Three items were raised in the building of it: **62** (no password rule
  exists anywhere, and that predates these screens), **63** (`reset` is unlimited against a token) and **64**
  (`PUT /me/password` answers 401 for a wrong *current* password). It also found, through CI rather than through
  reading, that `session_expired` never reached the user on a cold load — the client read every boot 401 as "not
  signed in" — and fixed it; and it established that a container with no .NET SDK can still run the whole stack
  through Docker, which DEVELOPER_GUIDE §16 now says in the place the next session will look.
Closed by the password-rule PR:
- **62.** The password rule — **minimum length 12, no composition rules** — built as decided. One place holds the
  number, `PasswordPolicy` in `src/Core/Identity`, and all four entry points that write a password check it and
  answer `400 password_too_short`: `POST /auth/password/reset`, `PUT /me/password`, `POST /invitations/accept`
  (the new-account branch) and `POST /provision/tenants`. Each is checked **before** the work it guards, which is
  not cosmetic: a refused reset leaves the link's one use unspent, and a refused change costs no `auth_attempts`
  row and no step of the limiter's escalating delay. `POST /auth/login` does **not** check it — existing
  passwords are not forced to rotate, and that half is proven by a test of its own. The four screens say the
  rule with the field and check it before sending; invitation acceptance, where accounts are actually created,
  has the rule but deliberately no confirmation field, because the mistyped-first-password risk a confirmation
  guards against is answered by `/app/password/forgot`, which now exists.
  The item's three notes held: the seed contract never passes the API, the e2e fixtures were all over twelve
  (checked, not assumed — one Conformance fixture was not, see the PR), and the error code took all four files.
- **65.** The rule's number is repeated for the interface's sake, and `check-password-policy` now holds those
  copies to the server's. Raised and closed in the same PR, by the project owner's decision to approve a new
  check for it: drift would not weaken the rule — the server still refuses — but a screen stating one number
  while the server enforces another reads as a broken product rather than as a policy, and that is the worse
  failure. The check asserts the occurrences agree and nothing more, with a `--self-test` that plants a
  mismatch. It compares **six**, not the four this item first counted: the Arabic sentences in
  `web/locales/ar.json` carry the number too, and a drift there is wrong to Arabic users alone.

Closed by the test-server deployment PR:
- **85.** The client's address behind the proxy — raised and closed in the same PR, by the project owner's decision
  that the nginx file is incomplete without it: the limiter keys on the address, so behind Cloudflare and nginx
  every user would have shared one ceiling, and 50 failures by anyone would have refused every sign-in. Two links,
  each believing only the one before it. nginx takes `CF-Connecting-IP` only from Cloudflare's published ranges
  (`set_real_ip_from`, the list as of 2026-10-19) and **replaces** `X-Forwarded-For` with it. Api
  (`ReverseProxy`, first in the pipeline) believes `X-Forwarded-For` only from `Network:TrustedProxies` — exact
  addresses, never a network; the defaults' loopback trust cleared; one hop — and with none configured (development,
  CI) reads no header at all; a malformed entry stops it from starting. On the server the proxy is the gateway of
  Api's network, pinned to `10.213.47.0/29`. [W] `T3_ClientAddressTests` (trusted, untrusted, none, mapped IPv6,
  one hop, the ceiling per client behind one proxy, the startup guard); [B] a client's own `X-Forwarded-For` is not
  the recorded address. **Not proven until the runbook's step 4 is run on the server:** that nginx's connections
  arrive from that gateway there — proven here on Docker 29 only.

Closed by the create-tenant PR:
- **86.** `src/Ops` is built, with `create-tenant`: a deployed server gets its first tenant and owner. The command
  writes the tenant, its roles from the templates and one `owner` invitation with `invited_by` NULL (Amendment 01
  §2.11, migration `0014`); the owner accepts in person through the existing path, which creates the account. It
  runs as a one-shot container from the Api image (`/app/ops`, its own entry point; T0.3 scans all of `/app`), as
  the `ops` service of `deploy/docker-compose.prod.yml`, with the `provisioner` string alone, and prints the
  acceptance link on `Ops:PublicOrigin`, which it refuses to run without. The runbook's step 5 is written for it,
  including that the link lives only in the container's output, that `--rm` destroys it, and that a lost link
  means running the command again. **In part:** item 89.

## For the project owner — a decision after T8

### 27. The measurement threshold is exceeded: choose between the two declared alternatives

- **Origin:** T8 (spec item j). The threshold, written before the measurement: p95 of the reference query under both
  policies ≤ 1.5 × under `tenant_isolation` alone, under 20 concurrent requests.
- **The result:** exceeded — 4 runs of 5 above 1.5, median ratio 1.574 (1.491–1.704); about 3.5 ms at p95 in absolute
  terms; the whole transaction's ratio stays within 1.5 (median 1.386). `docs/measurements/README.md` has every run.
- **Not fixed in T8** (the spec). **The decision:** a materialized assignment table, or a `SECURITY DEFINER` function
  for `client_scope` (§4.8, 13-i) — or a measurement on a dedicated database host first; the threshold itself stays.
- **Deferred (Amendment 01, D5 — the project owner's decision of 2026-10-17):** measure again after ten real tenants;
  the threshold and the two alternatives stay as they are.

## From the i18n task — the web interface (web/)

### 29. Two T8 screens: the original server-rendered one stays outside i18n

- **Origin:** i18n task, decided by the project owner: `SubscriptionScreen.cs` (`GET /subscriptions/screen`) is part
  of a merged proof (T8.2) and is not edited.
- **Now:** the server screen stays Arabic-only, its text in C#; it keeps the one API error code written as a literal
  in `src/` (`no_active_tenant`, the same value as `ApiErrorCodes.NoActiveTenant`), allowlisted by name in
  `checks/local/api-error-codes-literal-allowlist.txt`. The React screen (`web/src/screens/subscriptions`) is the
  i18n proof, from `GET /subscriptions` and `GET /subscriptions/clients`.
- **The decision:** retire the server screen and restate T8.2 against the React screen (a browser test, or the
  component tests as they are) — or keep both.

### 30. The unified view's per-tenant statuses are not in ApiErrorCodes

- **Origin:** i18n task. `GET /unified/subscriptions` declares each tenant's `status`: `ok`, `not_permitted`,
  `failed`, `exhausted`. These are statuses in a successful response, not error bodies, so `ApiErrorCodes` (and the
  `check-api-error-codes` list) does not hold them — except `not_permitted`, which is the same refusal and now uses
  the constant (`TenantFanOut`).
- **When the unified screen is built:** a second source (e.g. `TenantStatuses`), exported and checked like the
  error codes, with its messages in the interface.

### 31. The React T8 screen: order, and client names past 200

- **Origin:** i18n task, which changes no API behaviour.
- **Order:** `GET /subscriptions` pages by id; the server screen orders by `ends_on`. The React screen shows the API's
  order: sorting one page on the client would misorder across pages.
- **Client names:** joined from `GET /subscriptions/clients?limit=200` (the API's maximum page). A subscription
  whose client is past the first 200 shows "Not shown". **For the real module:** the client's name on the
  subscription item, or a sort parameter — an API change, decided there.

### 32. The initial language does not read Accept-Language on the server

- **Origin:** i18n task. The SPA has no server render, so it reads `navigator.languages` — the list the browser sends
  as `Accept-Language` — after the `lang` cookie and before the Arabic default. The result is the same for a
  browser; only a server-side choice (Api writing `lang`/`dir` into `index.html` before the first paint) would differ,
  and it would avoid one frame in the default direction when the cookie is absent and the browser prefers English.
  Not needed now.

## From the identity screens — the web interface

### 34. The organization cards show the name only

- **Origin:** the identity screens. `GET /tenants` (app.user_id alone, T3.7) returns `tenant_id`, `name`,
  `tenant_status`, `membership_status` — no role, no scope mode — and the task added no field to it. The selection
  shows the name alone.
- **If wanted:** the role and the scope mode of each membership, read on the same path. Each needs its own decision:
  under app.user_id alone the roles and `membership_scope` of other tenants are not visible today (the policies are
  per tenant), so showing them means a new read path, reviewed like T3.7.
- **The same gap on the acceptance screen** (invitation acceptance, decided by the project owner):
  `/app/invitations/accept` names neither the inviting organization nor the role offered — nothing read before
  accepting returns them, and `POST /invitations/accept` answers 204 with no body. Showing them means a read of the
  invitation by its token before acceptance (or a body on the 204, which existing tests assert), decided with the
  selection's.

### 35. No step-up path in the interface

- **Origin:** the identity screens, decided by the project owner. `POST /tenants/{id}/select` refuses with
  `step_up_required` a membership whose tenant accepts no provider the session used (4.2; `password` is the only
  provider today). The selection shows the message ("requires a stronger sign-in method, which is not available
  yet") and does not enter. **To build:** the step-up flow itself, with the provider it needs.

### 36. Logging out does not revoke the session on the server

- **Origin:** the identity screens, decided by the project owner. `POST /auth/logout` expires the cookie in the
  browser; the cookie is self-contained (encrypted user and tenant ids), so a copy kept elsewhere stays valid until it
  expires.
- **When needed:** server-side revocation — a session id or a security stamp in the cookie, checked on every request
  against a store (and a "log out everywhere"). Not built.
- **Partly closed, built in A1 (D6, §2.1, §5.2):** `users.security_stamp` is in the cookie and compared with the
  user's row on every request, so changing a password ends every session. Logging out does not change the stamp,
  so a copied cookie still survives logout until it expires: **this item stays open for that part**, and for
  "log out everywhere" as a deliberate action rather than a side effect of a password change.

## From invitation acceptance

### 39. No membership without its scope, enforced in the database

- **Origin:** invitation acceptance (option C), decided by the project owner. P4 of the v1.17 proposal
  (`docs/proposals/v1.17-acceptance/p4-membership-has-scope.sql`) — a deferred constraint trigger refusing at commit a
  membership with no `membership_scope` row, in any path and for any role, `migrator` included — was run both ways
  on PostgreSQL 18 and **not adopted**: it breaks two existing tests whose setup commits such a membership on purpose,
  to prove what the application does with one (T3.8): `Conformance.T3_SessionTests.T3_8_Test18_MembershipWithoutScopeRow_LoudAtTenantSelection`
  and `Core.WhiteBoxTests.T3_Test26.Test26_Rule7_MembershipWithoutScopeRow_ResolverThrows`.
- **Today:** the paths write the scope in the membership's transaction (bootstrap, acceptance, return); nothing in the
  database forbids a membership without one.
- **To build:** the database rule, together with new setup for those two tests (e.g. deleting the scope row of a
  committed membership, which the trigger does not see) — a change to existing tests, for the owner to approve.

### 40. If the email match must one day be enforced in the database

- **Origin:** invitation acceptance (option C), decided by the project owner. The match is the application's
  (§4.5), normalized. Two database variants were run both ways on PostgreSQL 18 and rejected
  (`docs/proposals/v1.17-acceptance/README.md`): policies over `invitations → users → persons` (new chains, §3.9;
  bootstrap through a founding invitation, §4.4), and a `SECURITY DEFINER` function owned by `migrator` (BYPASSRLS
  reached from application code, §3.4; a fourth audit writer outside the interceptor, §7).
- **The direction, if it becomes necessary:** a function owned by a dedicated role **without `BYPASSRLS`**, so the
  policies still apply inside it, and whose writes pass through the automatic audit interceptor — not either variant
  tried here.

## For the real subscriptions module — outside the proof

### 25. Module activation per tenant is not checked

- **Origin:** T5, decided by the project owner (rule 10: the bare minimum).
- **As built:** the module's endpoints check the caller's permission (5) and the scope beneath, not whether the
  module is active for the tenant (`tenant_modules.is_active`, §5); no tenant has a `tenant_modules` row.
- **For the real module:** each endpoint refuses when the module is not active for the tenant, and bootstrap (or an
  activation path) writes `tenant_modules`.
- **Deferred (Amendment 01, D8 — the project owner's decision of 2026-10-17):** until the second module.

### 26. Spec item i — what disabling an assignment does beyond visibility

- **Origin:** PROOF_SPEC v1.3 T5, spec item i.
- **In the proof:** disabling an assignment only cuts off visibility (Test 20 above); the proof has no tasks or
  notifications for it to affect.
- **For the real module:** decide its other effects (open tasks, notifications, ownership of in-flight work).

## Before launch — outside the proof

### 22. Invitation delivery — a launch condition

- **Accepted by the project owner as recorded (T4, before merge).**

- **Origin:** T4. The document says the invitation message carries the token (§4.5/2) and names no channel; the
  proof sends no message.
- **As built:** `POST /members/invitations` returns the token to the inviter, with `expires_at` — the same response
  whether a person with the email exists or not (Test 11). The lifetime is 7 days (`MemberAdministration`), a value
  the document does not set.
- **Condition before launch (the project owner's, explicit):** the token appears in **no** API response, and is
  sent **by email alone**, to the invitation's address. Otherwise the decision that "the token and a matching email
  are enough" (§3.10, acceptance with no account) falls: a manager holding the token could accept in the invitee's
  name — creating the account with the invitee's email — and bring someone into the tenant without their consent.
  The lifetime also becomes a decision of the document.
- **The link the email carries** (invitation acceptance, decided by the project owner): the token in the
  **fragment**, never the query string — `/app/invitations/accept#tenant=<tenant id>&token=<token>` — as the
  acceptance screen is built: a fragment reaches no server log and no referrer, and the interface takes it out of the
  address before anything renders (`web/src/invitations/pendingInvitation.ts`).

### 16. Login attempt limiting (§4.3-a/2)

- **Origin:** T3, decision 43; decided by the project owner.
- **The direction:** an escalating delay per (username + IP), with **no account lockout** (a
  lockout lets anyone lock out a known user), and a ceiling per IP. The checks read
  `auth_attempts`, as §4.3-a/2 says.
- **The numbers** (delays, windows, ceilings) are settled at launch.
- **Amendment 01 §5.2** keeps this direction and extends it: the same limiter also covers the forgot-password path
  (`POST /api/auth/password/forgot`), by (email + IP). The numbers stay open.
- **Built in A1 (the mechanism, not the numbers):** `Authenticator` reads `auth_attempts` — the window's failures
  from the caller's address at or above the ceiling → 429 `too_many_attempts` before the password is read; below
  it, an escalating delay from the window's failures for that (username + address), awaited after the transaction
  commits and only on a failure. No lockout, no new table. The numbers live in Api configuration
  (`Identity:LoginLimits`: `Window`, `FreeFailures`, `DelaySecondsByFailure`, `PerIpCeiling`) and **stay open**
  until launch; the Development and CI values are deliberately lenient (see item 54). The forgot path's half of
  §5.2 travels with that path (item 53).

### 52. No audit trail for a password change until the identity audit log exists

- **Origin:** the review of Amendment 01 (§2.1, §5.1), recorded by the project owner on 2026-10-17. A pre-launch item.
- **The gap:** a password reset or change writes identity state — `user_password_credentials` (never audited, v1.16
  §7), `users.security_stamp` (on the exception list of automatic auditing, Amendment 01 §2.1) and
  `password_reset_tokens` (outside automatic auditing, §2.2). None of these writes is recorded anywhere today:
  `audit_log` is a tenant log, and these are not tenant data.
- **Before launch:** the identity audit log v1.16 §7 already lists as a pre-launch item, covering at least password
  resets and changes.

## For production — outside the proof

### 18. Persistent Data Protection keys

- **Origin:** T3, decision 32; decided by the project owner.
- **The gap:** the session cookie is encrypted with ASP.NET Core Data Protection, whose keys now
  live inside the container: a restart or a second instance invalidates every session.
- **For production:** persist the key ring, protected at rest, shared by every Api instance.

### 33. Machine clients are refused by the CSRF protection

- **Origin:** the CSRF PR, decided by the project owner. `CsrfProtection` refuses every unsafe request without an
  allowed `Origin` and `X-Requested-With: platform-web`, whoever sends it: a machine client — a cron job calling an
  endpoint internally, an integration — sends neither, and gets 403 `csrf_rejected`. None exists today (the
  background jobs run in the Worker, against the database, not through Api).
- **When one is needed:** a separate authentication path — a token in a request header (e.g.
  `Authorization: Bearer`), never the session cookie — with its own scheme and its own grants. CSRF protection stays
  mandatory for every request that carries the session cookie: a request is exempt only because it authenticates
  without a cookie, never by its path, its caller or a configuration switch.
- **Not built now.**

## From Amendment 01 — product readiness (the project owner's decisions of 2026-10-17)

### 42. Succession: the platform replaces the live subscription system (D1)

- **Origin:** Amendment 01, D1. It settles the condition v1.16 §0 left open ("two separate products until an explicit
  succession decision").
- **The decision:** the succession stands. Data moves, no code.
- **Corrected by Amendment 02 (2026-10-17) — the live system was described wrongly in every document until that
  day:** it is not SYSTEM_DESIGN 2.0 on TypeScript and PG16. It is **an Excel workbook + n8n + GreenAPI**. And the
  "three accounts" of the roadmap are **four clients inside one tenant**, not three tenants — Amendment 01 §6/3's
  "each live account = a tenant" was wrong (Amendment 02 §0, §1).
- **Withdrawn with it:** the import. There is no automated migration — the data is entered by hand through the
  interface, and the switch-off is disabling a workflow in n8n (Amendment 02 §0, E7; roadmap M3, rewritten).
- **Still open under this item:** nothing to build. It closes when M3's manual entry is done and the n8n workflow
  is disabled.

### 43. The operations procedure: a third provisioner path (D2)

- **Origin:** Amendment 01, D2, §5.4. It **amends** v1.16 §4.4/3 and §4.4/5 ("two paths, no third").
- **The decision:** tenants are created in production by `src/Ops` (the `provisioner` and `job_runner` strings only;
  it refuses to run if it sees `migrator`), with no HTTP path and no public self-registration: `create-tenant`,
  `mark-paid` (the only way back from suspension), and `suspend-unpaid` (scheduled outside the platform; lists the
  active tenants as `job_runner`, then one `provisioner` transaction per tenant).
- **Withdrawn by Amendment 02 (2026-10-17):** `announce-migration`, the fourth command. It existed to tell migrated
  users to set a password, and there are no migrated users (Amendment 02 §0). `src/Ops` keeps its `migrator` guard;
  `src/Ops.Import` is never built.
- **The owner invitation (decided 2026-10-17):** `provisioner` gets `INSERT` on `invitations`, and
  `invitations.invited_by` becomes nullable: `NULL` means a bootstrap invitation, with no human inviter (this
  **amends** the `invitations` definition in v1.16 §4.1). `invitations_provisioner_insert` requires
  `invited_by IS NULL` and `status = 'pending'`, so the provisioner path cannot forge an inviter; `invitations_insert`
  still requires `invited_by = app.user_id` for `app_user`. The tenant has no active owner until the invitation is
  accepted: this **amends** v1.16 §3.10 item a. Both are declared in the amendment's §0.
- **For the migration PR, not the amendment:** any code that reads `invited_by` (display, audit) must handle `NULL`.
- **As built (the create-tenant PR, 2026-10-19):** the owner invitation — migration `0014` (`invited_by` nullable,
  `INSERT` for `provisioner`, `invitations_provisioner_insert` verbatim) — and `create-tenant` (item 86, closed).
  `Invitation.InvitedBy` is `Guid?`; no code displays it, and the acceptance path reads the entity, which a
  non-nullable property would have refused to materialize. **Still open under this item:** `mark-paid` and
  `suspend-unpaid`, which need `platform_subscriptions` (item 89), and with them `provisioner`'s `SELECT` and
  `UPDATE (status)` on `tenants` (Amendment 01 §3), which no built command needs yet.

### 44. Delivery channels (D3)

- **Origin:** Amendment 01, D3.
- **The decision:** email, WhatsApp (a hosted gateway such as GreenAPI), SMS (an API), Telegram (a bot); email and
  WhatsApp first, built inside M1 before the cutover (option A). Tables in §2.5–2.7, grants in §3.
- **Related:** item 22 (the invitation is sent by email alone) is met through the same sender (§5.5).
- **As built (A3):** the interface and the **email** channel. `Core.Notifications.INotificationSender` with one
  implementation per channel; `EmailNotificationSender` over SMTP, the account from the platform's
  `provider_accounts` row (§2.6) and the secret from the settings key its `credential_ref` names. A delivery that
  cannot happen is reported, never thrown — a channel failing must not stop the others (§5.3) nor turn `forgot`'s
  uniform 204 into a 500. `INotificationDispatch` hands a message to the sender outside the request that asked
  for it, which is what makes a constant-time `forgot` reachable (item 51).
- **Still open:** WhatsApp, SMS and Telegram — the three remaining channels of D3, in that order; the outbox of
  §5.5 (`notification_log`, the delivery job's `pending → sent|failed`), which has no table yet; and invitation
  delivery by e-mail (M2/b), which this task left alone — see item 60.

### 45. Three layers of delivery; provider secrets outside the database (D3′)

- **Origin:** Amendment 01, D3′, §2.6.
- **The decision:** the platform's provider account by default, a per-tenant override later; per-tenant channels and
  timing; per-client contact details. `provider_accounts.credential_ref` names a key in the Worker/Api settings — no
  secret in the database.
- **Not built:** per-tenant `provider_accounts` rows are a reserved slot with no path that writes them (§7).

### 46. Audit of the migrated rows (D4)

- **Origin:** Amendment 01, D4, answered 2026-10-17.
- **Closed as withdrawn, not as done (Amendment 02 §0, 2026-10-17):** there is no import, so there are no migrated
  rows to audit and the question has no subject. `migrator` did not become a fourth audit writer and will not
  (v1.16 §7 stays as it is); the row-count report and `legacy_id` fall with Amendment 01 §6.
- **And what replaces it is better than what was designed:** manual entry goes through the ordinary `app_user`
  path, so every row is audited automatically (v1.16 §7) — which the import, as `migrator`, could not have been.

### 47. Password recovery and change (D6) — see items 36 and 16

- **Built in A1 (PR for this item):** `users.security_stamp` with its backfill, `password_reset_tokens` with the
  three §4.1 policies and the §3 grants, `POST /api/auth/password/reset`, `PUT /api/me/password`, the stamp in the
  cookie and its comparison in `UnitOfWorkMiddleware` (401 `session_expired`), and the limiter of §5.2.
- **Still open under this item:** `POST /api/auth/password/forgot` — item 53 — and the three screens, which follow
  it. The migration's "no password" row (§2.12) belongs to the import command and is not built either.
- **Origin:** Amendment 01, D6, §2.1, §2.2, §5.1. It **amends** v1.16 §4.3-a/2 and §4.3-a/4 (`authenticator` writes
  `password_reset_tokens`) and §4.3-b (in `POST /api/auth/password/reset`, UoW-2 takes `app.user_id` from the
  consumed token).
- **The security stamp:** item 36 — it closes that item only in part.
- **The attempt limiter:** item 16 — the same direction, extended to the forgot-password path.
- **Migrated users — withdrawn, not built (Amendment 02 §0, 2026-10-17):** both the no-password state (§2.12) and
  `announce-migration` (§5.4) existed for migrated users alone, and there are none. The only user is created by a
  `create-tenant` invitation and sets their password on acceptance. **The marked assumption of §2.12 falls
  unproven** — nothing is built on it any more, so nothing needs proving on PostgreSQL 18. Were a no-password state
  ever needed again, the assumption comes back with it.

### 48. The platform subscription register — layer 1 (D7)

- **Origin:** Amendment 01, D7, §2.3, §2.4.
- **The decision:** plans without prices, one `platform_subscriptions` row per tenant with `paid_until`, suspension by
  the scheduled `suspend-unpaid` command (§5.4), read by the owner and admin (`core.billing.read`). Billing engine and
  gateways later, built on top of this, not instead of it.
- **Statuses:** `active` and `suspended` only; `grace_days` carries the grace (`past_due` dropped 2026-10-17 — a status
  nobody writes is worse than one that does not exist).
- **Assumptions still marked:** the plan codes [`trial`, `standard`], `trial` length [30] days, `grace_days` [7].

### 49. The amendment stands apart from v1.16 (D9)

- **Origin:** Amendment 01, D9.
- **For the document:** merge the amendments into a v2.0 when they accumulate or the second module starts. Until then
  the authority is v1.16 plus its amendments in order (`CLAUDE.md`).

### 54. The limiter's numbers, and T3.5's two arms

- **Origin:** A1 (the code PR for item 47), recorded by the project owner on 2026-10-17.
- **The finding:** PROOF_SPEC v1.3's T3.5 timing test interleaves 12 failures against one **existing** username with
  12 against a **fresh random missing** username each round, and requires the medians to be within 1.5×. Under the
  escalating delay of Amendment 01 §5.2 the first arm escalates and the second does not — not because one account
  exists and the other does not, but because one username repeats and the other never does. The two arms are not
  symmetric; the property T3.5 proves is not what breaks.
- **The decision (2026-10-17):** **PROOF_SPEC stays untouched** and no test is edited. The Development and CI
  configuration leaves the first 40 failures per (username + address) free and sets a high per-address ceiling, so
  T3.5 passes as written; the mechanism is proven in `[W]` (`T3_LoginLimiterTests`) with numbers of its own.
- **The consequence, stated plainly:** CI does not exercise the deployed numbers. **At launch**, with the numbers of
  item 16, restating T3.5's timing arm (both arms repeating one username) is a PROOF_SPEC decision to take at the
  same time.

### 55. A self-service identity write under a grant that withholds SELECT must carry no WHERE

- **Origin:** A1 (the code PR for item 47), found by CI on 2026-10-17 and recorded by the project owner.
- **The finding:** `app_user` holds `UPDATE (password_hash, updated_at)` on `user_password_credentials` and **no
  `SELECT`** (v1.16 §3.8; §1.11 forbids even a column `SELECT` on it). PostgreSQL requires `SELECT` on every column
  a statement *reads*, and a `WHERE user_id = …` reads one — so the obvious implementation is refused with `42501`,
  and EF Core always writes that `WHERE` for a tracked update. **Neither v1.16 nor Amendment 01 says this anywhere.**
- **The rule it implies** (now in `DEVELOPER_GUIDE` §6): such a write is raw SQL with **no `WHERE` at all**, and the
  policy (`password_self_update`) chooses the row — the same rule as "never filter in code what RLS filters",
  reaching the user axis. The count is then the guard: **exactly one row**, never "not zero", so that widening the
  policy later cannot turn the statement into a silent mass update. Proven in `tests/Conformance/T3_PasswordTests`
  (the caller's row alone while other rows exist; nothing at all with no `app.user_id`) and in `[W]`
  (zero rows → `CriticalWriteException`).
- **Where it bites next:** A3 (`forgot`, which writes nothing on this table but shares the role's shape) and
  anything else that ever writes `user_password_credentials` — the import's "no password" row (§2.12) is created by
  `migrator`, so it is not affected.
- **For the project owner:** whether v1.16 §3.8 or an amendment should state the rule beside the grant itself,
  rather than leaving it in the guide, is a documentation decision — the code and the tests do not wait on it.

### 56. `authenticator` reads all of `persons`; the risk is accepted, not mitigated by a policy

- **Origin:** A3 (the amendment text for item 53), stated by the project owner on 2026-10-17 as a condition of
  approving the grant.
- **The finding:** `persons_authenticator_read` is `USING (true)` (Amendment 01 §4.1). The grant
  `SELECT (id, email)` narrows the **columns**; nothing narrows the **rows**. `authenticator` can therefore read
  every address on the platform, not just the one `forgot` is resolving — and `persons.email` is a globally unique
  identity identifier (v1.16 §3.3), so a compromise of that role is an enumeration of every address we hold.
- **Why no row-narrowing policy:** a policy would have to name the subject row, and the subject is not known until
  the lookup the policy itself governs. No session variable identifies it beforehand — that is the whole point of a
  sessionless path. The condition is not expressible, not merely unwritten.
- **Accepted by the project owner (2026-10-17):** the login path has stood on the same ground since v1.16 —
  `users`, `user_password_credentials` and `membership_auth` are all `USING (true)` to this role (§4.3-a/3) — so
  `persons` is a fourth resolution table, not a new kind of exposure. **The mitigation is the role's own
  containment:** a separate connection with a separate role (§4.3-a/1), no session, a surface structurally confined
  to the authentication path, and the column grant that withholds the name and the phone. Check 6 fails if the
  column grant ever widens.
- **If it is ever to be narrowed:** the only shapes that would work are outside this amendment — resolving against
  a separate, derived address-to-user table that holds nothing else, or moving the lookup behind a rate-limited
  intermediary that never returns rows. Both are a design change, not a policy edit.

### 57. One address, two active accounts: which one does recovery mean?

- **Origin:** A3 (`forgot`), found while building the lookup.
- **The finding:** `persons.email` is unique, but `users.person_id` is **not** — one person may hold an
  `employee` account and a `customer` one (`users.user_type`), both active. Neither v1.16 nor Amendment 01 says
  which of them `POST /api/auth/password/forgot` recovers, and the path cannot ask: it answers 204 and says
  nothing.
- **As built:** exactly one active account → a token. Two or more → **nothing is minted**, and the answer is the
  same 204 as for an address nobody holds. Not an error, because an error would be the oracle the uniform answer
  prevents; and not a token for each, because the e-mail could not say which account it was for.
- **The project owner's decision (2026-10-17):** the behaviour above is **approved as it stands** — silence beats
  an oracle — and the item **stays open by that decision**, not for want of one. What is still to be settled is
  the eventual answer: a token per account with the account named in the e-mail, recovery keyed by username for
  such a person, or a rule that one address holds one account. Nothing is built until then; the seed and the live
  data have no such person today.

### 60. Invitation delivery by e-mail is not built (M2/b)

- **Origin:** A3, scope. The sender it needs now exists.
- **The finding:** an invitation's link is still copied by hand from the response (DEVELOPER_GUIDE §9). With
  `INotificationSender` in place, sending it is a small change — but not a free one: it decides what the e-mail
  says in both languages, and it must keep the link out of `audit_log` (M2/b), which is a surface this task did
  not open. Left out deliberately rather than half-done.
- **Related:** items 22 and 44.

### 61. `Test28c_EveryEntityTypeInsert_SendsNoReturning` covers 19 of 21 entity types — and the real hole is A1's

- **The shape it belongs to: item 83** — a verifier that verifies nothing reports success. This is sighting one
  of three; read 83 before fixing this, because the fix below (assert on the set, not on a count) is 83's first rule.
- **Origin:** A3, found while reading why the entity-count tripwires failed. **The gap itself is A1's**, merged
  2026-10-17 in PR #22: A1 added `password_reset_tokens` and the application inserts into it, and this test has
  never covered that insert. A3 found it; A3 did not cause it, and did not fix it (below).
- **The finding:** the test's name promises every entity type. What it does is insert a hand-written list of
  entities and assert `Assert.Equal(19, tablesInserted.Count)` — where `tablesInserted` counts the tables that
  **received** an insert. A new entity therefore cannot fail it: it is simply absent from both sides of the
  comparison, and the count stays 19. It stayed 19 across A1 and A3 with nobody noticing.
- **Which absence matters, and which does not:**
  - `provider_accounts` (A3): **defensible.** No application role holds `INSERT` on it — the rows are
    `migrator`'s, seeded (§2.6) — so there is no command of ours that could carry a `RETURNING`. Nothing to
    cover.
  - `password_reset_tokens` (A1): **a real hole.** `authenticator` holds `INSERT` and the application uses it —
    `PasswordPaths.BeginResetAsync` mints a token on every `forgot`. That insert has never been checked for
    `RETURNING`, which is the one thing this test exists to prevent (§3.5, Test 28).
- **What it would take to make the test live up to its name:** derive the list from the model instead of writing
  it by hand — enumerate `db.Model.GetEntityTypes()` and require every one of them either to have received an
  insert in the test or to be named in a short, declared exclusion list with its reason ("no application role
  inserts it"). Then the assertion is `Assert.Empty(entityTypes - inserted - excluded)` rather than a count, a
  new entity fails the test until somebody places it on one side or the other, and the name becomes true.
  **The reason it was hand-written is real and has to be designed around:** the rows are not arbitrary — foreign
  keys, policies and the `app_id`/`app_ts` domains mean each entity needs a valid parent graph and a context
  that admits the write, which is why the existing test builds a throwaway tenant, person, module and template
  first. The exclusion list is what keeps that from becoming unbounded.
- **The trap to close either way:** a test whose name claims "every" while a hardcoded count says otherwise reads
  as coverage that does not exist — worse than no test, because nobody looks again.
- **What does cover the new entity meanwhile:** `T2_Test28.Test28b_Model_EveryPropertyIsValueGeneratedNever` and
  `T5_ModuleModelTests.Test28b_ModuleModel_EveryPropertyIsValueGeneratedNever` build their set by `SelectMany`
  over every entity type's properties, so `provider_accounts` is genuinely inside those two.
- **For the project owner:** whether the test should assert over every entity type or only over every entity the
  application inserts is yours to settle; and A1's hole is not A3's to close inside A3.

### 63. `POST /auth/password/reset` takes unlimited attempts against a token — accepted, by decision

- **Origin:** the password screens (OPEN_ITEMS 59), found while establishing which error codes the reset screen
  can actually receive. **Left open by the project owner's decision on 2026-10-17, not by omission.**
- **The finding:** Amendment 01 §5.2 puts the attempt limiter on `login` and on `forgot` — "the same mechanism
  for `forgot`, by (the email + `ip`)" — and says nothing about `reset`. The endpoint matches the document: it
  reads the token, answers `invalid_token` or `invalid_request`, and never counts an attempt or writes an
  `auth_attempts` row. There is no ip ceiling on it and no delay. (The screen therefore does **not** handle
  `too_many_attempts`: it cannot arrive.)
- **The decision:** a 32-byte single-use token with a 30-minute life makes guessing impractical, so the absence
  of a limiter here is accepted. The limiter question is **settled with the launch numbers, alongside item 54**
  (and item 16's).
- **Why it stays written down:** so that "probably fine" is a decision somebody made and dated, not a line
  nobody noticed was missing. The cheap version, if the launch numbers want it, is the ip ceiling `forgot`
  already uses — one `auth_attempts` read on a path that is otherwise unmetered.

### 64. `PUT /me/password` answers 401 for a wrong *current* password, which is the wrong code

- **Origin:** the password screens (OPEN_ITEMS 59), found while building the change-password screen.
  **Decided by the project owner on 2026-10-17: 403 is the right code; fixed in the first backend PR that
  touches this area and is not carrying a specification of its own, not in a PR of its own.** (The wording
  first written here excepted nothing, and so read as if it claimed the password-rule PR — item 62 — whose
  whole point was to stand alone. Corrected by that PR.)
- **The finding:** 401 is the interface's signal that the session is over — `api/client.ts` sends 401s to the
  session, which drops the state, aborts everything in flight and shows "Your session has ended".
  `PUT /me/password` returns `401 invalid_credentials` when the *current* password is wrong, which says nothing
  about the session: the caller is signed in and stays signed in. Taken at face value it signs a user out of a
  working session for a typo.
- **What the password-screens PR did:** the smallest correct client fix — a 401 ends the session unless the code
  is `invalid_credentials` (`web/src/api/client.ts`). A stale security stamp on the same request is
  `session_expired` and still ends the session, so the two cases stay apart. One unit test each.
- **What the fixing PR must do — both halves, in the same PR:** change the status to **403**, *and* remove the
  client's `invalid_credentials` exception with it. Left behind, that exception becomes dead code nobody can
  explain: it guards a 401 the server no longer sends, and the comment explaining it points at an item that is
  closed. It is referenced from `web/src/api/client.ts`, `docs/DEVELOPER_GUIDE.md` §15 and `web/README.md` —
  all three mention this item by number, and all three come out together.
- **Amendment 01 §5.1 names no status for this case**, so this is a gap in the document rather than a conflict
  with it; whether the amendment should name it is the project owner's call.

### 66. Three tests assert on timing, or on a render that is normally complete — the fix is always in the test

- **Origin:** the password-rule PR (OPEN_ITEMS 62), which went red three times without causing any of them.
  **Decided by the project owner on 2026-10-17**, as a specification decision rather than tidying.
- **It is one pattern, not three coincidences,** which is why it is one item. Each of these asserts against a
  deadline, or against a render the system is only *usually* finished with by the time the assertion runs. A
  separate item per flake would read as a maintenance list; named once, with the rule, it gets read and fixed
  once.
- **The rule: the fix is always in the test, never in production code.** A millisecond or two of tolerance
  where the assertion is a deadline; a `waitFor` around the assertion where it is a render. No floor moves, no
  timing code changes, nothing in `src/` is touched. Leaving them flaky is the worse option: noise teaches
  people to ignore red, and a red signal nobody trusts is worth less than a slightly less precise one.

| Test | What it asserts, and why it races | Seen as |
|---|---|---|
| `T3_ForgotPasswordTests.TheAnswerTakesTheSameTime_…` [W] | each answer took **at least** the floor, exactly. `UniformTiming.HoldAsync` awaits `Task.Delay(remaining)`; `Task.Delay` sits on a timer whose due time is rounded and can return a fraction of a millisecond early relative to `Stopwatch`, and the work before the handler's `Stopwatch.GetTimestamp()` does not always cover it. An exact comparison against an inexact sleep. | `answered in 00:00:00.3999202, below the floor 00:00:00.4000000` — **79.8 µs** — then passed on a re-run of the **same commit** |
| `T3_LoginLimiterTests.TheDelayEscalates…` [W] | the first failure waited **under** 500 ms, on a path that derives a password hash (210,000 PBKDF2 iterations). A fixed threshold against work whose cost depends on the machine. | `the first failure waited 585 ms`, reproduced identically on clean `main` (`629 ms`), again at `657 ms` in the stage (b) session (item 82), and at **`802 ms` under load** there — a Conformance run on the same machine — which is the clearest evidence yet that the threshold races the machine and not the clock |
| `App.test.tsx > a 401 during the session` (and its siblings asserting `probe.location`) | awaits `findByText(…)`, then reads `probe.location` **synchronously**. The probe is written by a render-time effect, so the message appearing and the redirect being observed are two different commits; between them the assertion reads the old path. | `expected '/app/subscriptions' to be '/login?next=%2Fapp%2Fsubscriptions'`; 20 runs gave **2/20** failures on the PR branch and **1/20** on clean `main` |

- **How often it bites, measured (item 82, 2026-10-18):** the limiter test alone accounted for an intermittent
  `[W]` failure at roughly **one run in seven** on one machine, and was caught on run 1 of a 20-run capture. That
  is the first rate anyone has recorded for any of the three, and it is high enough that leaving it is already
  costing red CI runs that mean nothing.

- **Whoever takes `T3_ForgotPasswordTests` reads item 51 first.** That is not a courtesy reference. The floor
  this test measures (`Identity:ForgotPassword:MinimumResponseTime`) is what stops the forgot-password
  response time from saying whether an address has an account, and **this test is the only evidence the floor
  works**. The tolerance is traded against exactly that evidence, so the trade should be made by someone who
  knows what is on the other side of it. The tolerance stays far below the 400 ms the test configures and far
  below the half-floor difference its next assertion already allows, so what item 51 is actually protected by
  does not change.
- **Also seen (the stage (a) PR, 2026-10-17):** `App.test.tsx > changing one's own password > a reload after the
  password changed elsewhere still says why` — under the full suite only, **1/20 on clean `main` and 1/20 on the
  branch**, never in 25 isolated runs of the file on either. The same pattern; reported, not changed.
- **Also seen (the create-tenant PR, 2026-10-19):** the limiter test at `the first failure waited 2504 ms`, and
  `2390 ms` on clean `main` in a worktree, the same run — the SDK in a container on a slow machine. Not changed.
- **Scope:** A3's tests and the web suite, not the PR that found them. The password-rule PR reported all three
  with their provenance and changed none of them, which is why this item exists.

## From Amendment 02 — the reminder engine (the project owner's decisions of 2026-10-17)

### 67. The classification cannot carry `tenant_id`: Check 7 against a nullable tenant column

- **Origin:** Amendment 02, found in the first review of its draft, corrected by the project owner before merge
  (Amendment 02 §0, §3.1).
- **The defect in the draft:** `categories`/`subcategories` were defined with a `tenant_id NULL` column, in the
  `provider_accounts` pattern (Amendment 01 §2.6), so the platform's rows would sit in the `NULL` rows and a tenant
  would add its own beside them. But `subscriptions` carries `tenant_id`, so the FK toward `categories` would be a
  link between two `tenant_id` tables. **`checks/core/check-07.sql` is purely structural** — it joins on the
  existence of a `tenant_id` attribute on both ends and never looks at whether it is nullable — so it demands a
  composite key; and a composite key from a non-null `tenant_id` toward a `tenant_id IS NULL` row never matches
  under MATCH SIMPLE. The platform's seven categories would have been unreferenceable from any subscription.
- **As decided:** a global catalog with **no `tenant_id` at all**, exactly as `plans`, `modules` and `permissions`
  (Amendment 02 §3.1). Check 7 then does not apply, and `categories_read` is `USING (true)` like `plans_read`.
- **The general lesson, for the document:** a nullable `tenant_id` is a usable pattern only for a table that
  **nothing with a `tenant_id` points at**. `provider_accounts` is such a table; a classification is not. v1.16
  §3.3 does not say this, and it is worth one sentence there.

### 68. Check 8 passes silently on a table that declares `client_scope` but lacks `scope_ref_id`

- **The shape it belongs to: item 83** — a verifier that verifies nothing reports success. This is sighting two
  of three; 83's second rule (derive the set from the system, never by hand) is what an eleventh check would have
  to satisfy.
- **Origin:** Amendment 02, found in the first review of its draft (Amendment 02 §0, §3.3).
- **The gap:** Check 8 requires the `client_scope` template on every table **carrying** `scope_ref_id`. A table that
  is meant to be scoped but names its column something else — the draft's `client_notification_settings(client_id)`,
  with "client_scope (scope_ref_id = client_id)" in its prose — carries no `scope_ref_id`, so the check never looks
  at it and reports nothing. The policy itself would fail at creation, since the template's text reads
  `<t>.scope_ref_id` literally; but nothing in the ten checks says the table should have been scoped at all.
- **The consequence if it were missed:** an `operator` in `assigned` mode reads and edits every client's settings in
  the tenant. The second axis is "an explicit opt-in by carrying the column" (v1.16 §4.8), and the failure mode of an
  opt-in is a silent omission.
- **Corrected in Amendment 02 itself:** the column is `scope_ref_id`, with the composite FK toward `clients`, as
  `subscriptions` and `notification_log` do.
- **Still open — the check, not this table:** nothing detects the next table that should have opted in and did not.
  The candidates are an eleventh check or a manifest entry listing the tables that must be scoped. **A new check is a
  change to the check inputs, so it is the project owner's decision**, and it is not made here.

### 69. A tenant adding its own categories — the second part of E3, deferred

- **Origin:** Amendment 02, E3 and §3.1, deferred by the project owner 2026-10-17.
- **What is built:** the platform's closed catalog alone — `domains, hosting, security, software, backup, ai,
  other`, seeded by `migrator`, with no application role writing it.
- **What is deferred and why:** how a tenant adds its own categories. It is **not** a `tenant_id` column on the
  global catalog — item 67 says why — most likely a separate table for the tenant's categories, read alongside the
  platform's. Its shape is decided by the first real request, not guessed now.
- **Deferred with it:** the permission `subscriptions.categories.manage`, which the draft carried. With no
  application write on a global catalog it governs nothing; it returns with the separate table if that is built.
- **Opens it:** the first tenant that asks for a category outside the seven.

### 70. Appointments as a second reminder kind — deferred

- **Origin:** Amendment 02, E6 and §6, deferred by the project owner 2026-10-17.
- **Why it is safe to defer:** `reminder_type` is built now with one seeded value, `subscription`, and the financial
  fields are optional — so adding a second kind adds a value and a row, and reshapes no table (Amendment 02 §2).
- **Opens it:** the first client from outside the technical sector who asks for it (a clinic, maintenance).

### 71. One reminder for subscriptions that expire together — deferred

- **Origin:** Amendment 02, §6, deferred by the project owner 2026-10-17.
- **Why it is deferred and not cancelled:** the live system sent 14 messages in one minute to the same recipient
  about seven subscriptions expiring together (2026-09-15). The **operational** half of that — the number being
  blocked — is handled now by the spacing in Amendment 02 §4.3 (2); what grouping adds beyond it is an improvement
  to the experience.
- **What it will cost when it comes:** grouping changes the de-duplication key
  (`UNIQUE (tenant_id, subscription_id, channel, reminder_day)`) and the message template together — which is why it
  waits until one reminder works in production.

### 72. The expense report — deferred

- **Origin:** Amendment 02, §6, deferred by the project owner 2026-10-17.
- **What it is:** what the subscriptions cost monthly, and their spread across providers. The columns it needs
  (`cost`, `currency`, `billing_cycle`, `provider`) are built now (Amendment 02 §3.2), so there is no retrofit.
- **Why it waits:** it is a screen and an aggregation over data that has not been entered yet.
- **And it is not the cross-tenant dashboard** that the roadmap's M4 warns about: it reads inside one tenant, under
  `tenant_isolation`, and crosses no boundary. The M4 warning stands for the aggregated metrics dashboard alone.
- **Opens it:** after the manual entry of M3 and a first month of use.

### 73. The spacing between sends to the same recipient — `[2 s]`, a marked assumption

- **Origin:** Amendment 02 §4.3 (2), 2026-10-17.
- **Why it exists:** on 2026-09-15 the live system sent 14 messages in one minute to the same number. On unofficial
  WhatsApp (GreenAPI) that is what a number gets blocked for — a stoppage of the service, not an annoyance.
- **What is assumed:** a gap of **2 seconds** between two consecutive sends to the same recipient on the same
  channel. The number is a guess; nothing in the live system or the provider's terms fixes it.
- **To settle before stage (d) is built:** the provider's own published rate limit, where there is one, in place of
  the guess; otherwise the number is the project owner's decision, recorded in the code PR.

### 74. The retry ceiling — `[5]`, a marked assumption

- **Origin:** Amendment 02 §4.3 (4), 2026-10-17.
- **Why it exists:** a `pending` row is retried in every run, and the run became hourly (§4.2) — so a message whose
  provider is down is retried 24 times a day with no ceiling, which is the same behaviour item 73 exists to prevent.
- **What is assumed:** an `attempt_count` column, an escalating gap between attempts, and a ceiling of **5** after
  which the status becomes `failed` for good and the administrator alert of §4.3 (1) fires.
- **To settle before stage (d) is built:** the ceiling and the escalation curve. A ceiling too low drops a reminder
  over a transient provider error; too high is item 73 again by another route.

### 75. The daylight-saving proof — owed on PostgreSQL 18 before stage (d)

- **Origin:** Amendment 02 §4.3 (5), 2026-10-17.
- **The problem:** on the night the clocks change an hour repeats or is removed, so a client's send window is seen
  twice or not at all — a duplicated or a missed reminder, twice a year, in the zones that transition.
- **`Asia/Amman` is not one of them:** Jordan has been fixed at UTC+3 since 2022. So the proof must **not** be
  written against the zone the live system uses — it would pass while proving nothing. The zone is a per-client
  setting (§3.3) and clients will not all be in Jordan.
- **What is owed, on real PostgreSQL 18 (v1.16 rule: prove engine assumptions before building on them):** computing
  `reminder_day` in the zone of a client that does transition (`Europe/Berlin`, say) across both transitions, and
  the behaviour of `send_from`/`send_to` in the repeated hour and in the removed one.
- **Why it is an item and not only a line in the amendment:** whoever builds stage (d) needs to know a proof is
  owed **before** the code, not after. It is the one assumption of the three that a wrong guess hides for six
  months and then breaks twice a year.

### 76. `job_runner` and the catalog: Amendment 02 §5 grants a read that is deferred, and the question it leaves for stage (d)

- **Origin:** the stage (a) PR (the classification), 2026-10-17. **Decided by the project owner** in that PR,
  against the PR's own recommendation.
- **What happened:** Amendment 02 §5 grants `job_runner` `SELECT` on `categories` and `subcategories` (as
  Amendment 01 §3 does on `plans`, not built yet). Built as written, it fails `T7_4_JobRunner_ReadsActiveTenantsOnly_
  AndNoOtherTable` — PROOF_SPEC v1.3 T7.4, "`job_runner` reads from `tenants` only the active ones, and reads no other
  table", which guards v1.16 §8. Neither amendment's §0 names T7.4 as amended.
- **As decided:** `SELECT` for `app_user` and `provisioner` only; `job_runner` is dropped from `grants.json`, and
  T7.4 is untouched. No background job reads the catalog in stage (a), (b) or (c), so the grant would widen a role's
  surface and weaken its guard in exchange for nothing.
- **Amendment 02 §5 is not wrong.** `grants.json` lags it by one role, on purpose, until something needs that role.
  This is a deferral of a grant, not a correction of the amendment.
- **The gap between the policy and the grant is deliberate — do not "fix" it.** `categories_read` and
  `subcategories_read` keep naming `job_runner` (`FOR SELECT TO app_user, provisioner, job_runner`), verbatim from
  §5.1, because the manifest is extracted from the amendment and Check 2 compares against it. The policy permits and
  the grant withholds, and the grant is the real guard: without it the role is refused (`42501`) before any policy is
  consulted. `T5_ClassificationTests.TheCatalog_IsNotReadByTheOtherRoles` asserts exactly that. A reader who sees
  `job_runner` in the policy and not in `grants.json` is looking at this item, not at a transcription error.
- **The question for stage (d), when the send job first needs the catalog** — two roads, and whoever takes it should
  see both:
  1. **Amend T7.4** (a PROOF_SPEC change, the project owner's) to allow the global catalogs, and add the grant.
  2. **Read the catalog in the `app_user` context the job already runs in per tenant** (v1.16 §8: `job_runner`
     lists the active tenants, then each tenant is processed by `app_user` in a system context). `app_user` already
     holds the read. **The project owner leans to this one**, in which case T7.4 is never touched and the
     `job_runner` grant may never be needed at all.
- **The same question will meet `plans_read`** (Amendment 01 §3, §4.1) when `plans` is built: it names `job_runner`
  too, and the same two roads apply.

### 77. The subscription list offers "New" and "Edit" to members who may not write

- **Origin:** the stage (a) PR, 2026-10-17.
- **What is true:** the interface does not know the caller's permissions — no endpoint tells it, and the session
  carries none by design (its cookie holds the user and the tenant only — DEVELOPER_GUIDE §7). So the list shows
  "New subscription" and each row's "Edit" to every member who can list, a viewer included; the viewer's attempt
  is refused by the server (`403 not_permitted`) and the form says so. Nothing is written, and the server is the
  guard — this is a courtesy gap, not a security one.
- **What would close it:** a read of the caller's own permissions in the active tenant (e.g. on `GET /me/scope`,
  which already resolves `can_manage_scope`), so the screens can hide what the server would refuse. It is a contract
  addition, so it is not made inside a stage (a) PR.

### 78. The subscription form's end date is a text field, not a date picker

- **Origin:** the stage (a) PR, 2026-10-17.
- **Why text:** the interface shows Western digits in Arabic (`ar-u-nu-latn`, web/README). A native
  `<input type="date">` draws its value in the browser's own locale, which the page does not control, so in an
  Arabic browser it may show Arabic-Indic digits. The field is therefore `yyyy-MM-dd` text written left-to-right,
  with its example under it, a check that the day exists, and Arabic-Indic digits converted as they are typed.
- **What would close it:** a picker the interface draws itself, in `ar-u-nu-latn`, with Gregorian month names — a
  component decision for the design system, not for this form alone.

### 79. `subscriptions.clients.write` is narrowed at creation by the second axis, not by the permission

- **Origin:** the stage (b) PR, 2026-10-18. Proven on PostgreSQL 18.6 **before** the stage was designed around it.
- **The finding:** a client is its own scoped entity (`scope_ref_id = id`), so a **new** row has no
  `scope_assignments` row by virtue of being new, and `client_scope`'s `WITH CHECK` refuses its `INSERT` from a
  member whose scope mode is `assigned` — `42501`, which `ErrorResponses` already answers as `403 not_permitted`.
  Amendment 01 §4.2 gives `subscriptions.clients.write` to `operator`, and every seeded operator is `assigned`, so
  in practice an operator creates a client only when their mode is `all`. The same member **edits** the clients
  assigned to them with no trouble: the narrowing is at creation and only there.
- **The decision (2026-10-18, the project owner):** accept it and describe it — a **one-line clarification beside
  the permission in Amendment 01 §4.2**, in both languages, because the amendment is the thing people read and a
  permission row that reads broader than it is will mislead the next reader. No `§0` row was added for it: §0 lists
  what an amendment **amends**, and this describes behaviour v1.16 §3.1 already has rather than changing any
  paragraph. Proven in `T5_ClientLifecycleTests`.
- **Rejected, and why:** writing a `scope_assignments` row for the creating member in the same transaction would
  let a member widen their own scope without `core.scope.manage` — a hole, not a convenience.
- **What it does not affect:** the manual entry of M3 is done by the owner, whose mode is `all`.

### 80. The subscription's status gate is the database's; the renewal's "once" is a UNIQUE

- **Origin:** the stage (b) PR, 2026-10-18.
- **What was built:** `subscriptions_status_gate` (Amendment 02 §5.1) — `AS RESTRICTIVE FOR UPDATE TO app_user
  USING (status = 'active') WITH CHECK (true)`. No `UPDATE` reaches a row that has left `active`, so a cancelled or
  renewed subscription is not edited, not revived and not renewed again, whatever the code does. Beside it,
  `UNIQUE (tenant_id, renewed_from_id)` means one subscription is renewed **once**.
- **The engine finding, which changed the policy's text:** PostgreSQL uses the `USING` expression as the
  `WITH CHECK` expression when the latter is omitted. The policy as first dictated carried no `WITH CHECK`, which
  would have required the **new** row to be active too and refused the cancellation itself (`42501`) — the very
  thing the gate exists for. Proven on PostgreSQL 18.6, then `WITH CHECK (true)` was written out, with the reason
  beside it in the amendment, in the migration and in `DATABASE.md` §7.7.
- **The consequence for the API:** a failing `USING` reaches no row rather than erroring, so the endpoints read
  `status` first and answer `409 subscription_not_active`; without that the caller would see `403 not_permitted`
  on a row they can plainly read. `PUT /api/subscriptions/{id}` answers the same code for the same reason — a
  change to stage (a)'s endpoint that the gate made necessary.

### 81. `ExpiringSubscriptionsJob` counts cancelled subscriptions — for stage (د) to settle

- **Origin:** the stage (b) PR, 2026-10-18, by the project owner's decision not to touch it now.
- **What is true:** the job counts every subscription ending within 30 days regardless of `status`, so a cancelled
  one is still counted — and, when stage (د) sends, would still be reminded about.
- **Why it was not changed here:** PROOF_SPEC v1.3 T7 defines the count as "subscriptions ending within 30 days",
  and narrowing it would change a definition from inside a PR that is not about the job. The filter belongs with
  the sender.
- **For stage (د):** the sender reads `clients.is_active` (the project owner's decision of 2026-10-17) **and**
  `subscriptions.status`; whether T7's own count narrows with it is a PROOF_SPEC decision to take at the same time.

### 82. ~~An unidentified intermittent failure in `[W]`~~ — **identified: it is item 66's limiter test. Closed into 66.**

- **Origin:** the stage (b) PR, 2026-10-18. Opened when `tests/Core.WhiteBoxTests` reported `Failed: 1,
  Passed: 111` twice in about fifteen runs — once after Conformance, once on a clean migrated-and-seeded database,
  which is CI's own starting state — with no test named in the console summary.
- **Captured, by the project owner's condition (2026-10-18): the name, or a run configured to take it.** `[W]`
  alone, in a loop on a clean seeded database, every run keeping its own `trx`. It failed on **run 1 of 20**:

  ```
  FAILED TEST: T3_LoginLimiterTests.TheDelayEscalatesWithTheFailures_TheCeilingRefusesBeforeThePassword_AndNoAccountIsLockedOut
  MESSAGE:     the first failure waited 657 ms
  STACK:       tests/Core.WhiteBoxTests/T3_LoginLimiterTests.cs:48
  ```

- **It is item 66's second row, verbatim** — the same test, the same assertion (`first < 500 ms` on a path that
  derives a 210,000-iteration PBKDF2 hash), the same failure shape. Item 66 records it at `585 ms` and at `629 ms`
  on clean `main`; this is a third sighting at `657 ms`. **The rate this item measured — about one run in seven,
  and 1/20 in the capture — belongs to item 66 now**, and is the first number anyone has for how often it bites.
- **What this closes, and what it does not.** It closes the question this item existed to ask: there is no unknown
  intermittent failure in `[W]`. It does **not** close item 66, whose fix — a threshold that does not race
  PBKDF2 on a loaded machine — is still owed and is still "the fix is in the test, never in production code".
- **Not caused by the stage (b) PR**, and nothing in that PR touches it: item 66 already records this test failing
  on clean `main`. It was not fixed inside that PR either, because the fix is item 66's and widening a threshold
  from inside an unrelated change is how a test quietly stops testing.
- **For whoever takes item 66:** the capture recipe is worth keeping. `[W]` alone, clean seeded database, each run
  `--logger "trx;LogFileName=<run>.trx"`, every `trx` kept, read the `UnitTestResult` with `outcome="Failed"`. And
  the harness lesson: **a loop that reports green must prove it ran.** Twenty "green" runs here proved nothing
  until a run with no `Passed!`/`Failed!` summary line was made to fail the loop — the Docker daemon had died
  underneath it and every run was a no-op. That is sighting three of **item 83**, which names the shape these
  share with items 61 and 68; the loop's fix is the pattern 83 points at.

### 83. A verifier that verifies nothing must fail, not pass — one shape, three sightings in two days

- **Origin:** named by the project owner on 2026-10-18, after the third sighting in two days. The three were
  found separately, by different work, and read as three unrelated bugs. They are one.
- **The shape.** A verifier — a test, a check, a loop — is given a set to look at. The set comes out empty, or
  smaller than it should be, for a reason the verifier never examines. It finds no violation, because there was
  nothing to find a violation in. **It reports success.** Every one of these is green in exactly the case it
  exists to catch, and the greenness is what hides it: nobody investigates a passing check.

- **The fourth sighting is first here, because it hides every sighting after it.** The others are in the code;
  this one was in the **diagnostic**. `Api logs on failure` ran `docker logs api` unbounded, which after 237
  tests at EF `Information` level appended tens of thousands of lines **after** the test output and pushed it
  past what the logs API will return — so a CI failure could not be read at all, and the step reported success
  while destroying the evidence it existed to preserve (OPEN_ITEMS 84). A verifier that silently verifies
  nothing costs you one bug. **A diagnostic that silently reports nothing costs you every bug it was supposed to
  describe**, including the three below. Fixed in `.github/workflows/ci.yml`: both dumps bounded to
  `--tail 200`, and a `.trx` uploaded on failure so a failing run names its test. That is a defect fix, not an
  improvement.

| Sighting | What it was given | Why it found nothing | What it reported |
|---|---|---|---|
| **Item 61** — `Test28c_EveryEntityTypeInsert_SendsNoReturning` | a hand-written list of entity types | a new entity is absent from **both** sides of `Assert.Equal(19, …)`, so it cannot move the count | passing, at 19 of 21, across two PRs |
| **Item 68** — Check 8 | the tables carrying `scope_ref_id` | a table meant to be scoped that named its column `client_id` is not in the set at all | passing, on a table whose own prose claimed `client_scope` |
| **Item 82** — the `[W]` capture loop (this session) | twenty runs of the suite | the Docker daemon had died, so every run was a no-op and produced no summary line; the `grep` matched nothing and the loop read empty as "not failed" | **"20 runs, all green"** — having run the suite zero times |
| **Item 84** — `Api logs on failure` in CI | the Api container's log, unbounded | the dump buried the test output past the logs API's ~103 KB tail, so the failing test could not be named | the step **succeeded**, and the CI failure it was reporting on became unreadable |
| **Fifth — caught before commit** (the test-server deployment PR): a `[W]` test, "`X-Forwarded-Host` is not honoured" | a request with a refused `Host` and an allowed `X-Forwarded-Host` | host filtering runs before the middleware it meant to test, so the 400 came from the filter whatever that middleware did | it would have **passed with `XForwardedHost` enabled** — seen by asking what would turn it red, and removed rather than committed |
| **Item 88** — the documented local Conformance run, its own shape: **not a silent verifier but a baseline that is red by default** | the 238 tests, run as `DEVELOPER_GUIDE` §16 says | one test is red on `main` on every such run (`dev-up`'s Api has no interface to serve) | **"237/238 — the usual one"**: a real break in the local run looks exactly like every other run, and people who see red every day stop reading it |

- **The rule: a verifier must fail when it verifies nothing, and a diagnostic must not destroy what it reports
  on.** Not warn, not report zero — fail. Passing is a
  claim about the thing under test, and a verifier that found nothing to test has no grounds to make it. Three
  ways to hold it, in the order they are worth reaching for:
  1. **Assert on the set, not on a count.** `Assert.Empty(expected - covered - declared)` fails on a new member;
     `Assert.Equal(19, covered)` cannot (item 61).
  2. **Derive the set from the system, never by hand** — the model, the catalog, `pg_policy`. A hand-written list
     goes stale silently; a derived one grows and the assertion notices (items 61, 68).
  3. **Make "no evidence" an error.** A step that produces no output where output is the proof of work is a
     failure of the harness, and must stop the run (item 82).
  4. **A documented baseline is green, or it is a defect.** A run that is red by default teaches its readers to
     stop reading red; a known failure in the path the guide documents is fixed in the path (or the guide), never
     left as "the usual one" (item 88).
- **The fix made this session, as the pattern to copy:** the capture loop now treats a run that emits no
  `Passed!`/`Failed!` summary line as fatal — `if [ -z "$line" ]; then echo "FATAL: …"; exit 2; fi` — so a dead
  daemon stops the loop instead of being counted twenty times as a pass. Three lines, and the difference between
  a report and a lie.
- **This item fixes nothing on its own.** Items 61 and 68 each hold their own fix and their own decision (68's is
  a change to the check inputs, so it is the project owner's). This exists so that whoever picks up either one —
  or writes the next check, or the next loop — finds the shape named in one place rather than meeting it a fourth
  time as something new. **Count as of 2026-10-19: six sightings** — four in the code and CI (61, 68, 82, 84), one
  caught before commit, and item 88.
- **Where it is worth looking next, unaudited:** any assertion in the suites of the form "count equals N" over a
  set that can grow; any check whose input is a hand-maintained file (`grants.json`, `policy-chains.json`,
  `rls-exemptions.json`, `scope-variable-writers.json`) and which reports nothing when a table is simply absent
  from it; and the `--self-test` of each check, which is the one mechanism the project already has against this
  shape — it proves the check fails on a planted violation, but not that the check *looked at anything* on a
  clean run.

### 84. An unidentified intermittent failure in Conformance — and CI's failure path destroys the evidence

- **Origin:** the stage (b) PR, 2026-10-18. Two items in one, because the second is why the first is still open.
- **What happened:** CI's `proof` job went red on the Conformance step at `0fd48fa` and green on the runs either
  side of it. **Four CI runs — `f3788f0`, `0fd48fa`, `138f55e`, `ed1b0de` — ran code that is byte-identical**;
  every one of those commits differs from the stage's build commit `7e0a748` in `OPEN_ITEMS.md` and nothing else
  (verified with `git diff --name-only`). So the suite is intermittently failing, and the denominator is four,
  not two:

  | run | commit | `proof` | |
  |---|---|---|---|
  | 90 | `f3788f0` | success | |
  | 91 | `0fd48fa` | **failure — Conformance** | unnamed; before the `.trx` artefact existed |
  | 92 | `138f55e` | success | |
  | 93 | `ed1b0de` | success | |
  | 94 | `62f8032` | success | first run with the artefact and the bounded log dumps |
  | 95 | `851d355` | success | |
  | 96 | `63333cb` | success | |

  **Precisely what "identical" means here:** runs 90–93 differ only in `OPEN_ITEMS.md`; run 94 also changed
  `ci.yml`. **Nothing under `src/` or `tests/` changed across any of the seven**, so the code under test is
  identical — which is the claim the rate rests on, and it is weaker than "byte-identical" would have been.

  Locally, on a clean migrated-and-seeded database running CI's exact sequence, **twenty rounds, all green,
  237/237 each** — twenty result lines, no `Failed!`, no aborted round, and the loop's own guard (a round
  emitting no summary line is fatal) never fired, so the twenty is twenty runs that ran.

- **The observed rate, as of 2026-10-18 08:08 UTC: 1 red in 7 CI runs, 0 in 20 local rounds.** Recorded
  separately from the decision below on purpose: the project owner decided at **one in six**, which is what the
  history showed at the moment of the call, and the seventh run landed green while the local rounds finished.
  **The record carries the rate that was measured; the decision carries the rate it was taken on.** They are not
  the same number and should not be collapsed. **No test was ever named.**
- **Why no test was named — and this is the part to fix.** CI's own failure path buries the failure it exists to
  report:
  - `Api logs on failure` runs `docker logs api` with **no `--tail`**. After 237 tests at EF `Information` level
    that is tens of thousands of lines, appended **after** the Conformance output.
  - The logs API returns at most ~103 KB from the tail (confirmed: identical cap at 120 and 260 requested lines),
    so the Conformance summary can never be reached by tailing.
  - The full-log blob URL is refused by the development container's egress proxy, and its `gh` token is invalid.
  **This is item 83's shape, in CI itself:** the step whose whole purpose is to preserve evidence of a failure
  made that evidence unreadable, and reported success while doing it.
- **The fix, which is a change to CI and therefore the project owner's decision:**
  1. `--logger "trx;LogFileName=conformance.trx"` on the Conformance step (and on `White-box tests`), plus
     `actions/upload-artifact` for the `.trx` on failure. Then a failing run names the test, its message and its
     stack, as an artefact rather than as scrollback.
  2. `docker logs --tail 200 api` instead of unbounded, and the same for `docker compose logs postgres`. A
     diagnostic step that destroys the diagnosis is worse than no step.
  **Both were approved by the project owner and are in `.github/workflows/ci.yml` as of this PR**, scoped to
  those steps and nothing else.
- **What is ruled out so far:** not the seed state (reproduced attempts were on a clean migrated-and-seeded
  database, CI's own); not the extra tenants this PR's tests create seen by a cross-tenant assertion
  (`T7_BackgroundJobTests` asserts per-seed-tenant counts and is robust to them — read and checked). Not ruled
  out: anything else, including whether the step **failed an assertion or aborted** — CI's Conformance step ran
  71 s against 82–105 s locally, which is short enough to raise the question and not enough to answer it.
- **What a missing `.trx` artefact will mean — read this before concluding anything from the next red run.** An
  **abort** and a **failed assertion** are different problems with different causes, and the artefact
  distinguishes them: a run that failed an assertion uploads a complete `.trx` naming the test; a run that
  aborted — test host crash, the Api container dying, the runner losing the process — uploads a **truncated
  `.trx`, or none at all**, because the logger never got to write it. **So the absence of the artefact is not a
  gap in the evidence; it is the evidence**, and it points at the 71 s gap above rather than at any test. Do not
  read "no artefact" as "nothing to see" — that would be this list's own item 83 all over again, at the level of
  reading the report.
- **The merge is taken with this item OPEN, by the project owner's decision of 2026-10-18 — do not read the
  merge as the item having been resolved.** The decision was recorded **before** the merge and conditional on the
  two CI runs then in flight coming back green, on these terms: **one red in six runs** with the code under test
  identical, zero in twenty local rounds, **no test ever named**, and the suite now configured to name it the
  next time it bites. Those two runs (94, 95) and the head run (96) all came back green, and the rate as finally
  measured was one in seven — see above. (A red run would have been the better outcome: it would have produced
  the name, and both of us said so.) The reasoning is that waiting longer buys nothing — it samples the same code hoping
  for a failure we have *made legible* rather than one we cannot read. What makes it acceptable is the CI change
  in this PR and nothing else: before it a recurrence told us nothing. **This item is open because the failure is
  unexplained, not because it was forgotten**, and the first red `proof` run after the merge should be brought
  straight back here.
- **Why it is not dismissed as a flake:** "flake" is a conclusion about a named test, not a substitute for naming
  one. Until it has a name nobody can say whether it is a racing assertion or a real defect in what this stage
  built, and the suite it is in is the one that proves the tenant-isolation contract.

### 87. The test server's Api runs as root, and its Data Protection keys sit unencrypted on a volume

- **Origin:** the test-server deployment PR, 2026-10-18.
- **The finding:** Api persists nothing of ASP.NET's Data Protection keys (no `AddDataProtection`), so in a
  container they are lost on every redeploy and every session with them. The deployment mounts a volume at the
  default location, `/root/.aspnet/DataProtection-Keys`, which keeps sessions across redeploys without a code
  change. Two consequences: the path is root's, because the images run as root (the .NET images' `app` user is not
  used); and the keys are stored unencrypted (ASP.NET logs a warning saying so) — anyone with Docker access to the
  server can read them and forge a session cookie. On that server Docker access is root anyway.
- **For its own PR:** run the images as the non-root `app` user (`USER $APP_UID`), with the keys' path set in code
  (`PersistKeysToFileSystem`) on a volume that user owns; whether to encrypt them at rest is the project owner's
  decision.

### 88. One Conformance test is red on every local run the guide documents — and so nobody would see it break

- **The shape it belongs to: item 83** — this is its sixth sighting, in a shape of its own: not a verifier that
  passes on nothing, but a documented baseline that is red by default. Read 83 (its fourth rule) before fixing it.
- **Origin:** the test-server deployment PR, 2026-10-19, asked for by the project owner while reviewing it.
- **The finding:** `Conformance.ApiPathTests.InterfacePath_Html_TheInterface` (`GET /app/anything` as HTML → the
  interface, 200) **fails on `main` itself** (`f91ff65`, checked in a clean worktree) when Conformance runs the way
  `docs/DEVELOPER_GUIDE.md` §16 says to — against the `scripts/dev-up.sh` stack. That stack runs Api with
  `dotnet run`, where there is no `wwwroot` and no `Web:Root` (the interface is Vite's, on :5173), so Api answers
  404. CI is green because CI's Api is the image, which holds the built interface. Measured locally: 237 of 238,
  that one red, on `main` and on this PR alike.
- **Why it matters more than one test:** everyone who runs the suite locally learns that one red is normal. The day
  this test — or any other — breaks for real, the local run looks exactly as it always did, and only CI says so.
- **What closes it (not done here):** the documented local path gives a green suite — for example `dev-up` building
  the interface and starting Api with `Web__Root=web/dist` (the guide's Docker path already does this), or §16
  naming the one step that makes it pass. Never by skipping or weakening the test.

### 89. `create-tenant` implements Amendment 01 §5.4 in part — no platform subscription, no notification settings

- **Origin:** the create-tenant PR, 2026-10-19, by the project owner's decision in that PR: build the command now,
  without the two tables it was to write, rather than widen the PR and delay the first real tenant.
- **What §5.4 says:** `create-tenant` = bootstrap from the templates **+ `platform_subscriptions` + the default
  `tenant_notification_settings`** + an `owner` invitation; and `[--plan trial]` in its signature.
- **What is built:** the templates and the invitation. **What is not:** the `platform_subscriptions` row (plan
  `trial`, `paid_until` = today + 30 — Amendment 01 §2.4), the default `tenant_notification_settings` rows
  (Amendment 02 §4.4's numbers), and `--plan`, which the command refuses by name. None of the three tables exists
  (`plans`, `platform_subscriptions` — M2/d; `tenant_notification_settings` — Amendment 02 stage (c)).
- **The amendment and the code deliberately disagree until then.** This is not a reading of §5.4; it is a
  recorded gap, and the amendment's text stands.
- **How it closes:** the migration that creates each table **backfills every existing tenant** — the tenants
  `create-tenant` made before it — the way `0013` gave existing tenants' roles the new permissions: the rows in the
  same migration, then a row-count check (`DO $$ … RAISE EXCEPTION`). And in the same PR `CreateTenant` starts
  writing the table for new tenants (with `provisioner`'s grants and policies from Amendment 01 §3, §4.1), and
  `--plan` stops being refused. `mark-paid` and `suspend-unpaid` come with `platform_subscriptions` (item 43).
- **What depends on it today:** nothing on the screens. `GET /api/tenant/subscription` and the settings screens are
  not built either.

### 90. A lost invitation link leaves a tenant behind with no member

- **Origin:** the create-tenant PR, 2026-10-19. The runbook's instruction for a lost link, decided by the project
  owner: run `create-tenant` again rather than hunt for the token — it is in the container's output alone, and
  `--rm` destroys it.
- **The consequence:** the first tenant stays: its roles, and a pending invitation no one holds the token for,
  which expires after 7 days. It has no member, so no one sees it; `job_runner` lists it among the active tenants,
  and the reminder job finds nothing in it.
- **Not built:** a way to re-issue the owner invitation of an existing tenant, or to remove one that never had a
  member. Neither is in Amendment 01 §5.4; either needs a decision before it is built. Until then the runbook says
  what is left behind, and what the operator sees (`docs/operations.md`, step 5): two tenants, the first with no
  member and a pending invitation.
- **Left open by the project owner's decision (2026-10-19):** nothing is built for it now. A second tenant is
  visible, recoverable by a later decision, and costs nothing while there is one user; re-issuing an invitation and
  deleting a tenant are both new write paths the amendment does not describe.

## For the next version of the document — not for the code

### 12. Test 17-a's wording: restate it as Test 23 was restated in 1.12

- **Origin:** T2b (PR #4), decision 26, accepted by the project owner.
- **The discrepancy:** §3.7 Test 17-a says an admin's UPDATE of their own
  `membership_scope` "fails under the restrictive policy on that table". But
  `membership_scope_admin_update`, as §4.8 writes it, is **permissive**, with the
  condition `membership_id <> app.membership_id`. The own row is filtered out, so
  the UPDATE affects **zero rows, silently**. The protection holds; the text
  describes a different mechanism.
- **For the document:** restate 17-a in two layers, as 1.12 restated Test 23:
  - **The database (silent):** the admin's UPDATE of their own mode → zero rows,
    the row untouched.
  - **The API (loud):** changing `scope_mode` is a **critical write** under the
    rows-affected guard (§3.5/5), so the same attempt through the API → an explicit
    error. (Since T3 the API layer exists and is tested: `Test17a_Api_*` in Conformance.)
  Then run the new text in isolation before building (PROOF_SPEC rule 11).

### 14. membership_auth_authenticator_read and its grant have no consumer — delete them

- **Origin:** T3, found in the rule-11 run, decided by the project owner.
- **The finding:** §4.3-a gives `authenticator` `SELECT` on `membership_auth` with a policy
  `USING (true)`. But `membership_auth` has no `user_id`, and `authenticator` has no grant on
  `memberships`, so it cannot find a given user's rows — it can only read **every tenant's**
  rows. The provider check therefore happens at **tenant selection**, as `app_user` through
  `membership_auth_self_read` (§3.9), where the membership is known. The authenticator path
  never reads `membership_auth`.
- **For the document:** delete `membership_auth_authenticator_read` (§3.9 / the manifest) and
  `GRANT SELECT ON membership_auth TO authenticator` (§4.3-a/2, the §3.8 matrix): a cross-tenant
  read with no need. Then a migration drops both, with the manifest, `grants.json` and Checks 2,
  3 and 6 updated, after the text is run in isolation (PROOF_SPEC rule 11).

### 19. Every policy inside a sql block

- **Origin:** T4, found while generating the manifest from v1.16 (`checks/core/generate-manifest.py`).
- **The finding:** the extractor reads every `CREATE POLICY` inside a ```sql block of the document's body — 62 in
  v1.16, each written once. Five policies the manifest holds are stated **outside** one, so the extractor does not
  see them: `tenants_for_jobs` (§8, inside a plain block) and `tenant_isolation` stated explicitly for
  `membership_roles`, `role_permissions`, `roles`, `tenant_modules` (1.13's decision 1). The generator keeps them
  and reports them, but they are the only entries not generated from the text.
- **For the document:** write those five as `CREATE POLICY` statements inside sql blocks, so the whole manifest is
  extracted from the text.

### 21. Item g's count reads membership_scope, which a members-only manager cannot see

- **Accepted by the project owner as recorded (T4, before merge).**

- **Origin:** T4, found building item g (§3.10) for disabling an `all` member.
- **The finding (run on PostgreSQL 18):** item g counts the active memberships whose mode is `all` through
  `membership_scope`. `membership_scope_read` shows a member only their own row unless they hold
  `core.scope.manage`. A manager holding `core.members.manage` **without** `core.scope.manage` therefore counts
  **0** `all` members where there are 4 (Al-Amin, as Khaled with only `app.can_manage_members = true`), and the
  lock over them locks 0 rows — with no error. The owner and admin templates hold both permissions, so it bites
  only a custom role with the members permission alone.
- **As built:** disabling an active member reads their scope row first; unreadable → refused loudly
  (`not_permitted`, `core.scope.manage`), never guarded silently. A members-only manager can therefore not disable
  anyone.
- **The proposed fix (the project owner's):** widen `membership_scope_read` to admit `app.can_manage_members` beside
  `app.can_manage_scope` — a read, not a write: `membership_scope_admin_update` stays the scope permission's alone.
  Before it is adopted: run it in isolation both ways on PostgreSQL 18 (PROOF_SPEC rule 11) — the members-only
  manager then counts every `all` member — and re-run Check 2 and the manifest. Then the loud refusal above goes.

### 23. Item g's text is incomplete: it must cover departure

- **Origin:** T4, decided by the project owner.
- **The gap:** §3.10 lists item g for "a downgrade, or disabling an `all` member" — not a departure. The last active
  `all` membership could therefore leave, leaving the tenant with no one who sees everything.
- **As built:** a departure takes item g too — the same lock and count (`MemberAdministration.LeaveAsync`), after
  item a. Covered by `T4_Departure_TheLastAllMembership_Refused` and the race
  `T4_6_AnAllMemberLeaving_WhileAnotherIsDowngraded`, seen failing without item g on departure and without the lock.
- **Found with it (run on PostgreSQL 18):** a leaver without `core.scope.manage` counts and locks their own row
  alone — Layla (Al-Amin, viewer, `all`) counts 1 `all` membership where there are 3, and `membership_lock` admits
  no row of hers to lock, since it needs a manager permission.
- **Decided by the project owner (T4, before merge): consent prevails (§0).** A member holding neither
  `core.scope.manage` nor `core.members.manage` always leaves; item g on departure applies only to a holder of one
  of the two. Layla (viewer, `all`) leaves → succeeds — even as the last `all` membership
  (`T4_Departure_TheLastAllMember_WithNoManagerPermission_Leaves`: zero `all` left, the owners remain). The
  managers' races stay as they are. Until item 21 is settled, a manager holding `core.members.manage` alone, `all`,
  is refused departure loudly: item g would count blind.
- **For the document:** add departure to item g's text, for holders of either manager permission, and the consent
  rule for everyone else.

### 24. The owner's scope is always 'all' — item a would then cover item g

- **Origin:** T4, proposed by the project owner.
- **The proposal:** fix an owner's scope mode at `all` (bootstrap already gives the first owner `all`). An active
  owner is then always an active `all` membership, so item a — at least one active owner — guarantees item g — at
  least one active `all` membership — and the consent decision of item 23 can never leave a tenant with no one who
  sees everything.
- **For the document:** state it, with what enforces it (e.g. an `owner` invitation only with `intended_scope_mode
  = 'all'`, no downgrade of an owner, granting `owner` only to an `all` member), each run in isolation both ways on
  PostgreSQL 18 before adoption (PROOF_SPEC rule 11).

### 41. One account per address, and the normalized match

- **Origin:** invitation acceptance (option C), decided by the project owner.
- **As built:** migration 0009 adds `persons_email_normalized_key`, a unique index on `lower(btrim(email))`, beside
  the exact `persons_email_key` (§3.3). The application compares addresses under the same rule
  (`EmailAddress.Normalize`) on every acceptance path — a new account, a return, an existing account — and an
  address whose account exists refuses the new-account path under it (§4.5).
- **For the document:** §3.3 (the constraint) and §4.5 ("authenticated email ≠ the invitation's", normalized).

## For the next version of PROOF_SPEC — not for the code

### 20. T4.13: split into its [B] and [W] parts

- **Origin:** T4, decided by the project owner.
- **The discrepancy:** T4.13 is tagged [B], but one of its parts — "refusal is seen from the application with no
  database command" — is not observable from outside the implementation.
- **As built:** the refusal from the API is [B] (Conformance, 403); the absence of any database command is [W]
  (`Core.WhiteBoxTests`, the command recorder, as in T3).
- **For PROOF_SPEC:** state T4.13 as two criteria, one [B] and one [W].


### 15. The seed contract names the seed users' passwords, as test values only

- **Origin:** T3, decided by the project owner. §7 names no passwords, yet every [B] login test
  needs them, on any implementation.
- **Now:** `tests/seed/seed-contract.sql` documents them in its header — each seed user's password
  is `<username>-seed-password`, **test values only** — and Conformance reads the pattern from
  configuration (`Seed:PasswordFormat`).
- **For PROOF_SPEC §7:** state the same, so another implementation seeds the same passwords.
