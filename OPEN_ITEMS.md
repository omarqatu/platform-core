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
- **The decision:** data moves, no code. The live system (SYSTEM_DESIGN 2.0) is read-only until the cutover, then
  switched off.
- **To build:** the import (§6), in the order of `docs/ROADMAP_SUBSCRIPTIONS.md`.

### 43. The operations procedure: a third provisioner path (D2)

- **Origin:** Amendment 01, D2, §5.4. It **amends** v1.16 §4.4/3 and §4.4/5 ("two paths, no third").
- **The decision:** tenants are created in production by `src/Ops` (the `provisioner` and `job_runner` strings only;
  it refuses to run if it sees `migrator`), with no HTTP path and no public self-registration: `create-tenant`,
  `mark-paid` (the only way back from suspension), `suspend-unpaid` (scheduled outside the platform; lists the
  active tenants as `job_runner`, then one `provisioner` transaction per tenant), and `announce-migration` (once,
  after the M3 cutover; item 47).
- **The owner invitation (decided 2026-10-17):** `provisioner` gets `INSERT` on `invitations`, and
  `invitations.invited_by` becomes nullable: `NULL` means a bootstrap invitation, with no human inviter (this
  **amends** the `invitations` definition in v1.16 §4.1). `invitations_provisioner_insert` requires
  `invited_by IS NULL` and `status = 'pending'`, so the provisioner path cannot forge an inviter; `invitations_insert`
  still requires `invited_by = app.user_id` for `app_user`. The tenant has no active owner until the invitation is
  accepted: this **amends** v1.16 §3.10 item a. Both are declared in the amendment's §0.
- **For the migration PR, not the amendment:** any code that reads `invited_by` (display, audit) must handle `NULL`.

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
- **The decision (revised 2026-10-17):** no `audit_log` entry for the import at all — `migrator` does not become a
  fourth audit writer (v1.16 §7 stays as it is). The record is the row-count report the import command writes outside
  the database, plus `legacy_id` for tracing until it is dropped.

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
- **Migrated users (decided 2026-10-17, §2.12, §6/3 and §6/7):** the import (`src/Ops.Import`, `migrator` only)
  creates a `user_password_credentials` row in a no-password state that no input matches and that the verifier
  rejects without throwing — a marked assumption, to be proven on PostgreSQL 18 in the code PR. So
  `password_self_update` stays valid and `provisioner` needs no new grant.
- **Their first password:** the import sends nothing, issues no tokens and does not call forgot; it creates the
  no-password row and stops. A separate `src/Ops` command, `announce-migration`, run only after the M3 cutover, sends
  each migrated user a plain announcement email carrying a link to the forgot page, and each user starts the flow
  themselves — no new token kind, no new lifetime, no per-IP ceiling reached (§5.2), and the 30-minute lifetime starts
  from the user's own request. `src/Ops.Import` holds no provider configuration.

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

### 62. The platform has no password rule at all — and that predates these screens

- **Origin:** the password screens (OPEN_ITEMS 59), found while looking for the rules the new screens were meant
  to match. **The gap is not these screens': it has shipped since invitation acceptance was built.**
- **The finding:** nothing anywhere checks a password beyond its being present.
  - The API accepts **1 to 1024 bytes**: every one of the three endpoints tests only
    `{ Length: > 0 and <= MaxPassword }` (`src/Api/Endpoints/AuthEndpoints.cs`), and so does bootstrap. There is
    no minimum length, no composition rule, no check against a common-password list, nothing.
  - No screen checks more than `required`: not the invitation-acceptance screen
    (`web/src/screens/invitations/AcceptInvitationScreen.tsx`), which is where accounts are actually created
    today, and not the two new ones.
  - So a one-character password is accepted by the platform, and has been since acceptance shipped.
- **What these screens did:** nothing, deliberately. They check that the two fields match and no more; inventing
  a minimum in the interface would be a product rule written in the wrong layer, and one that the API would not
  enforce for anyone who did not use the interface.
- **For the project owner:** the rule is yours to set — whether there is one, what it is, and whether it is
  checked in the API alone or in both layers (it has to be in the API either way). It touches the acceptance
  screen and bootstrap as much as recovery, so whatever is decided is one change across all of them, not a
  change to the recovery screens.

### 63. `POST /auth/password/reset` takes unlimited attempts against a token

- **Origin:** the password screens (OPEN_ITEMS 59), found while establishing which error codes the reset screen
  can actually receive.
- **The finding:** Amendment 01 §5.2 puts the attempt limiter on `login` and on `forgot` — "the same mechanism
  for `forgot`, by (the email + `ip`)" — and says nothing about `reset`. The endpoint matches the implementation:
  it reads the token, answers `invalid_token` or `invalid_request`, and never counts an attempt or writes an
  `auth_attempts` row. There is no ip ceiling on it and no delay, so a caller may try tokens against it as fast
  as the server answers. (The screen therefore does **not** handle `too_many_attempts`: it cannot arrive.)
- **Why it is probably fine:** the token is 32 random bytes and only its SHA-256 is stored; the live window is
  30 minutes and the newest request consumes the older tokens. Guessing one is not a practical attack.
- **Why it is written down anyway:** "probably fine" should be a decision somebody made, not a line nobody
  noticed was missing. The cheap version is the ip ceiling `forgot` already uses, which costs one
  `auth_attempts` read on a path that is otherwise unmetered.
- **For the project owner:** to be settled with the launch numbers of §5.2 (OPEN_ITEMS 16, 54).

### 64. `PUT /me/password` answers 401 for a wrong *current* password, which is the wrong code

- **Origin:** the password screens (OPEN_ITEMS 59), found while building the change-password screen.
- **The finding:** 401 is the interface's signal that the session is over — `api/client.ts` sends every 401 but
  login's to the session, which drops the state, aborts everything in flight and shows "Your session has ended".
  `PUT /me/password` returns `401 invalid_credentials` when the *current* password is wrong, which is not a
  statement about the session at all: the caller is signed in, and stays signed in. Taken at face value it signs
  a user out of a working session for a typo.
- **What this PR did:** the smallest correct client fix — a 401 ends the session unless the code is
  `invalid_credentials` (`web/src/api/client.ts`). A stale security stamp on the same request is
  `session_expired` and still ends the session, so the two cases stay apart. Covered by a unit test for each.
- **What should happen instead:** the endpoint should answer with a code that means "this credential is wrong",
  not "this session is over" — 403, or 422 as a validation refusal. Amendment 01 §5.1 does not name the status,
  so this is not a conflict with the document; it is a gap in it. When it is changed, the client's exception
  comes out with it, rather than becoming permanent.
- **For the project owner:** the status code, and whether the amendment should name it.

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
