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
