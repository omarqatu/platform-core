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

## For T4 — the rest of Test 28 (c)

### 8. Zero RETURNING across a full bootstrap

- **Origin:** T2 (PR #3), decision 20. T2 inserts every one of the 19 entity types
  through EF with no RETURNING; the bootstrap path itself is built in T4.
- **What T4 must do:** run the full bootstrap (a new tenant, its roles and role
  permissions from the templates, the first member as owner with scope `all`,
  the audit entry) through the real EF commands, and assert that no command
  contains RETURNING (PLATFORM_CORE §3.7 Test 28 c).

## For T5 — the scope_ref_id part of Test 21

### 9. Changing scope_ref_id to another tenant's entity → rejected by the composite FK

- **Origin:** T2 (PR #3), decision 20. The core's `scope_assignments.scope_ref_id`
  has no FK, the declared gap (PLATFORM_CORE §3.3, 9), so T2 covers Test 21's other
  columns only.
- **What T5 must do:** once the module's composite FK exists
  (`subscriptions (tenant_id, scope_ref_id) → clients (tenant_id, id)`), an UPDATE of
  `scope_ref_id` to another tenant's client → rejected by the constraint
  (§3.7 Test 21).

## For T5 — the rest of Test 27 (PLATFORM_CORE v1.10)

### 6. Test 27's client_scope part

- **Origin:** T1 (PR #2). T1 covers the first-template part of Test 27 as T1.6; T3 covers
  the memberships part as T3.7 (closed above).
- **Still to cover — T5.8, a table under `client_scope`:** where the first such table
  appears.

## For T5 — the visible-rows part of Test 20

### 13. A revoked assignment, or a lowered mode → the next request sees zero rows of that entity

- **Origin:** T3, accepted by the project owner. T3.3 proves propagation to the **next
  request, with no re-login**, on what T3 can observe: the resolved mode (`all` →
  `assigned`) and the member's own active assignments (B gone after its revocation).
- **What T5 must do:** with the first scoped table, the same two changes during a live
  session → the next request returns zero rows of the entity (the revoked client's
  subscriptions; the tenant's other clients after lowering the mode), with no re-login
  (§3.7 Test 20).

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

## For the next version of PROOF_SPEC — not for the code

### 15. The seed contract names the seed users' passwords, as test values only

- **Origin:** T3, decided by the project owner. §7 names no passwords, yet every [B] login test
  needs them, on any implementation.
- **Now:** `tests/seed/seed-contract.sql` documents them in its header — each seed user's password
  is `<username>-seed-password`, **test values only** — and Conformance reads the pattern from
  configuration (`Seed:PasswordFormat`).
- **For PROOF_SPEC §7:** state the same, so another implementation seeds the same passwords.
