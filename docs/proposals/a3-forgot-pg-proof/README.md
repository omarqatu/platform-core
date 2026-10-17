# A3 — the sessionless address lookup and the delivery account, run on a real engine

CLAUDE.md: prove engine assumptions before building on them. These ran before the migration and the code were
written, and the recorded output is `result-2026-10-17.txt`.

**On PostgreSQL 16.15, not 18.** The session this was built in had no Docker and no PostgreSQL 18 — the network
policy refuses `postgresql.org`, so PGDG could not be installed, and the `postgres:18` image of
`amendment-01-pg18-proof/run.sh` could not be pulled. Everything below uses features that predate 18
(column-level `SELECT`, `FORCE ROW LEVEL SECURITY`, `UNIQUE NULLS NOT DISTINCT` — PostgreSQL 15), and CI runs the
same statements on PostgreSQL 18 as part of the catalog checks and the tests. **Re-run `02-behaviour.sql` on 18
before relying on this file as the proof** (`docs/proposals/amendment-01-pg18-proof/run.sh` is the pattern).

- `01-setup.sql` — stand-in tables carrying only the columns these paths read, with Amendment 01's grants.
- `02-behaviour.sql` — fourteen cases, each run as the role that takes the path, each printing the rows it
  affected or the error it raised. What they establish:
  - **The policy is what turns the grant into rows** (1a): the column grant alone, under `FORCE RLS`, gives
    zero rows and no error — the silent failure mode the architecture is built to avoid.
  - **`persons_authenticator_read` + `SELECT (id, email)` is exactly as narrow as the amendment claims** (2a–2e):
    the lookup works and uses `persons_email_normalized_key`, a stored address with a capital letter and a
    trailing space is matched, and `SELECT *`, a projection of `full_name`, and a `WHERE` over `full_name` are
    each refused with `permission denied` — **the column grant, not the policy**. This is why the lookup in
    `PasswordPaths.BeginResetAsync` is raw SQL: a tracked EF read of `persons` reads every mapped column, so it
    is refused.
  - **The whole path runs in one statement** (2f–2g): address → person → user, with a row for an account that
    exists and zero rows, no error, for an address that does not.
  - **The role still cannot write `persons`** (2j), and it does read **every** row of it (2i) — the accepted
    risk of OPEN_ITEMS 56, measured rather than assumed.
  - **`provider_accounts_read` admits the platform row with no tenant set** (3a–3c): zero rows before the policy
    exists, the platform row after it with no `app.tenant_id` at all, and still one row with a tenant set. This
    is what lets a sessionless send resolve its provider as `app_user` (OPEN_ITEMS 58).
  - **`app_user` cannot write it** (3d), and **`UNIQUE NULLS NOT DISTINCT` refuses a second platform row per
    channel** (3e) — the constraint §2.6 calls for, failing as it should.
- `03-on-the-migrated-schema.sql` — the same lookup against the **real** migrated schema (migrations 0001–0011)
  with the seed contract loaded, connected as the real `authenticator` role: the exact statement the code sends
  returns the seeded account, an unknown address returns nothing, `EXPLAIN` shows the normalized unique index,
  and a read of every column of `persons` is refused. It also runs the **supersession** statement twice: it
  consumes whatever is live and then affects zero rows without raising, which is what keeps two requests racing
  on one address from answering 500 where an unknown address answers 204 — the oracle that would have undone
  OPEN_ITEMS 51. Writing any other column of the table is still refused.

Also run on the same server, and not recorded here: migrations 0001–0011 applied in order from clean, and
`checks/core/run-checks.sh` — Checks 1–10 all green with the new grants, policies and manifest.
