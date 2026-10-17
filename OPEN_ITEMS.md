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

### 51. Forgot-password: the response time reveals whether an account exists

- **Origin:** the review of Amendment 01 (§5.1), recorded by the project owner on 2026-10-17. A pre-launch item.
- **The finding:** `POST /api/auth/password/forgot` always answers 204, but not in the same time: for an existing
  account it hashes and inserts a token and sends an email; for a missing one it returns at once. The latency tells
  an attacker which addresses have accounts — the enumeration the uniform 204 is there to prevent.
- **Before launch:** a constant-time response path — the response time must not depend on whether the account
  exists. The amendment's design does not change.

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

### 53. `POST /api/auth/password/forgot`: the address, and who may read it

- **Origin:** A1 (the code PR for item 47), recorded by the project owner on 2026-10-17 when the path was deferred.
- **The conflict found:** Amendment 01 §5.1 makes `forgot` an `authenticator` unit of work and §5.2 keys its limiter
  by (email + IP), but the amendment's own grants matrix (§3) carries v1.16's `persons` row verbatim —
  `authenticator` has no read on `persons`, where the address lives, and no policy on it either. No other role can
  resolve an address with no session: `app_user` sees only its own row (`user_self_read`), and every `provisioner`
  policy requires `app.tenant_id`.
- **The decision (2026-10-17):** `forgot` is deferred to **A3, with the e-mail sender** — a token nobody receives is
  not a feature — and the conflict is resolved there **by the narrow grant**: `GRANT SELECT (id, email) ON persons
  TO authenticator` plus a new §4.1 policy, written into the amendment **first**, because the manifest is extracted
  from its text (`checks/core/generate-manifest.py`).
- **Until then:** nothing mints a recovery token; `POST /api/auth/password/reset` consumes one that exists, and the
  `[B]` tests write their own as `authenticator`. Item 51 (the response time of `forgot`) stays open behind this one.

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
