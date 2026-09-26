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

## For the project owner — a decision after T8

### 27. The measurement threshold is exceeded: choose between the two declared alternatives

- **Origin:** T8 (spec item j). The threshold, written before the measurement: p95 of the reference query under both
  policies ≤ 1.5 × under `tenant_isolation` alone, under 20 concurrent requests.
- **The result:** exceeded — 4 runs of 5 above 1.5, median ratio 1.574 (1.491–1.704); about 3.5 ms at p95 in absolute
  terms; the whole transaction's ratio stays within 1.5 (median 1.386). `docs/measurements/README.md` has every run.
- **Not fixed in T8** (the spec). **The decision:** a materialized assignment table, or a `SECURITY DEFINER` function
  for `client_scope` (§4.8, 13-i) — or a measurement on a dedicated database host first; the threshold itself stays.

## For the real subscriptions module — outside the proof

### 25. Module activation per tenant is not checked

- **Origin:** T5, decided by the project owner (rule 10: the bare minimum).
- **As built:** the module's endpoints check the caller's permission (5) and the scope beneath, not whether the
  module is active for the tenant (`tenant_modules.is_active`, §5); no tenant has a `tenant_modules` row.
- **For the real module:** each endpoint refuses when the module is not active for the tenant, and bootstrap (or an
  activation path) writes `tenant_modules`.

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

### 16. Login attempt limiting (§4.3-a/2)

- **Origin:** T3, decision 43; decided by the project owner.
- **The direction:** an escalating delay per (username + IP), with **no account lockout** (a
  lockout lets anyone lock out a known user), and a ceiling per IP. The checks read
  `auth_attempts`, as §4.3-a/2 says.
- **The numbers** (delays, windows, ceilings) are settled at launch.

## For production — outside the proof

### 18. Persistent Data Protection keys

- **Origin:** T3, decision 32; decided by the project owner.
- **The gap:** the session cookie is encrypted with ASP.NET Core Data Protection, whose keys now
  live inside the container: a restart or a second instance invalidates every session.
- **For production:** persist the key ring, protected at rest, shared by every Api instance.

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
