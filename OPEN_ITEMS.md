# Open items

Items carried between tasks so nothing is lost when a task is built. Each one
names the task it belongs to. An item is removed only by the PR that closes it.

## For T2 — the core schema and the ten CI checks

### 1. Wire `check-schema-allowlist` in next to the ten checks, without renumbering

- **Origin:** T0 (PR #1), a project-owner decision.
- **State:** built and running in CI (`checks/local/`), separate from the
  document's checks.
- **What T2 must do:**
  - Keep it as a **local check with its own name**. Do not fold it into Check 5
    or Check 6, and do not give it a number. "Check N" always means
    PLATFORM_CORE §3.6 Check N.
  - When the real CI catalog-check step replaces today's placeholder, run it in
    the same step as Checks 1–10, both the check and `--self-test`, and keep it
    labeled as local.
  - Include it in T2.2's negative-test discipline alongside the ten.
    `--self-test` already covers it.
  - If T2 adds a schema or an object to `migrations_meta`, the allowlist change
    goes to the project owner for decision. Don't just extend the list to make
    the check pass (rule 3).

### 2. Check 6 must deny application roles USAGE/CREATE on every schema except `public`

- **Origin:** T0 (PR #1). This is how `migrations_meta` is handled at the
  grant layer.
- **Why:** Check 5 covers `public` only. The grant layer is what keeps a table
  outside `public` unreachable, and `check-schema-allowlist` is what keeps such
  tables from appearing at all. Both are needed.
- **State:** true today, not yet enforced. app_user, authenticator, job_runner
  and provisioner have no USAGE on `migrations_meta`, verified on the local
  database.

### 3. Database-level CONNECT/TEMP

- **Origin:** T0 (PR #1). Left at PostgreSQL defaults.
- **What T2 must do:** settle both in Check 6's approved list (§3.6, Check 6:
  "CONNECT and TEMP on the database").

### 4. Re-prove T1.2 and T1.3 in Conformance against a real table

- **Origin:** T1 (PR #2), decision 8. T1.2 and T1.3 run against `t1_probe`, a
  test-only table, so today they are harness proofs, not tests that run
  unmodified against another implementation.
- **What T2 must do:** once the first real tenant table exists, point T1.2
  (including Test 27's first-template part) and T1.3 at it in Conformance.
  Then remove `t1_probe` along with its fixtures (`tests/fixtures/t1_probe.sql`,
  both `T1ProbeFixture` classes) and the "No test residue" CI step.

### 5. The catalog checks run before any test that creates tables

- **Origin:** T1 (PR #2). The `t1_probe` fixtures create and drop a table in
  `public` during the test steps.
- **What T2 must do:** when the real catalog-check step replaces the
  placeholder, keep it before the white-box and Conformance steps, so it can
  never see a test table. Until item 4 removes `t1_probe`, the "No test
  residue" step guards the other side.

## For T3 and T5 — the rest of Test 27 (PLATFORM_CORE v1.10)

### 6. Test 27's memberships and client_scope parts

- **Origin:** T1 (PR #2). T1 covers the first-template part of Test 27 (a
  reused connection with all five variables set, then COMMIT/ROLLBACK and
  optionally DISCARD ALL: zero rows, no error).
- **Still to cover:**
  - **memberships**, with `app.user_id` alone, returning only the caller's own
    memberships: in T3, next to Test 7, whose path it guards.
  - **a table under `client_scope`**: in T5, where the first such table
    appears.
