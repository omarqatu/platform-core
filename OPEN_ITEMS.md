# Open items

Items carried between tasks so nothing is lost when a task is built. Each one
names the task it belongs to. An item is removed only by the PR that closes it.
Numbers are kept stable, so earlier PRs still refer to the right item.

Closed by the T2 PR:
- **1.** `check-schema-allowlist` runs in the catalog-check steps beside Checks
  1–10, check and self-test, as a named local check.
- **2.** No application role has USAGE or CREATE on any schema except `public`.
  Granted by name in `0003_grants.sql`, enforced by Check 6.
- **3.** CONNECT and TEMP on the database: nothing through PUBLIC, CONNECT for
  the four application roles, TEMP for none. Enforced by Check 6.
- **5.** The catalog checks run before every test step in CI.

## For T2 — the core schema and the ten CI checks

### 4. Re-prove T1.2, T1.3 and T1.6 in Conformance against a real table

- **Origin:** T1 (PR #2), decision 8; PROOF_SPEC v1.1, T1 build. T1.2, T1.3
  and T1.6 run against `t1_probe`, a test-only table, so today they are
  harness proofs, not tests that run unmodified against another implementation.
- **What T2 must do:** once the first real tenant table exists, point T1.2,
  T1.3 and T1.6 at it in Conformance.
  Then remove `t1_probe` along with its fixtures (`tests/fixtures/t1_probe.sql`,
  both `T1ProbeFixture` classes) and the "No test residue" CI step.

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
