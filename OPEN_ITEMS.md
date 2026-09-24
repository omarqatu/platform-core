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

## For T3 — the second part of Test 18

### 7. A membership with no membership_scope row → a loud error at tenant selection

- **Origin:** T2 (PR #3), decision 20. T2 covers Test 18's first part (assigning to
  another tenant's membership → the composite FK rejects it).
- **What T3 must do:** when a tenant is selected, a membership with no
  `membership_scope` row is a thrown error, not a silent fallback (PLATFORM_CORE
  §3.5/7, §3.7 Test 18). Beneath it, if bypassed, zero rows.

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

## For T3 and T5 — the rest of Test 27 (PLATFORM_CORE v1.10)

### 6. Test 27's memberships and client_scope parts

- **Origin:** T1 (PR #2). T1 covers the first-template part of Test 27 as
  T1.6 (a reused connection with all five variables set, then COMMIT/ROLLBACK
  and optionally DISCARD ALL: zero rows, no error).
- **Still to cover — now acceptance criteria in PROOF_SPEC v1.1:**
  - **T3.7, memberships:** with `app.user_id` alone, only the caller's own
    memberships, next to Test 7, whose path it guards.
  - **T5.8, a table under `client_scope`:** where the first such table
    appears.
