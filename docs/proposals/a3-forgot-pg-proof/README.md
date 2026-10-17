# A3 — the sessionless lookup and the delivery account, run on PostgreSQL 16 (pending 18)

## What this is, and what it is not

CLAUDE.md requires engine assumptions to be proven on **real PostgreSQL 18** before anything is built on them.
**This is not that.** The session this was built in had no Docker and no PostgreSQL 18 — the network policy
refuses `postgresql.org`, so PGDG could not be installed and the `postgres:18` image of
`amendment-01-pg18-proof/run.sh` could not be pulled. What ran was **PostgreSQL 16.15**.

So everything below is an **indication, not a proof**, until CI runs the same statements on 18. Read every
result here as "behaved this way on 16, pending 18". The features used all predate 18 — column-level `SELECT`,
`FORCE ROW LEVEL SECURITY`, `UNIQUE NULLS NOT DISTINCT` (PostgreSQL 15) — which is the reason to expect 18 to
agree, not a reason to skip asking it.

**Re-confirm before merging**, from the CI log rather than from this file:

1. the detached dispatch holding the timing floor — `[W]`
   `T3_ForgotPasswordTests.TheAnswerTakesTheSameTime_ForAnAddressWithAnAccountAndOneWithout`;
2. the idempotent supersession `UPDATE` — case B/C of `03-on-the-migrated-schema.sql`, and `[B]`
   `T3_ForgotPasswordTests.AsecondRequest_LeavesTheFirstTokenConsumed`.

`docs/proposals/amendment-01-pg18-proof/run.sh` is the pattern for re-running this on 18 in one command.

## The files

- `01-setup.sql` — stand-in tables carrying only the columns these paths read, with Amendment 01's grants.
- `02-behaviour.sql` — nineteen cases, each run as the role that takes the path, each printing the rows it
  affected or the error it raised.
- `03-on-the-migrated-schema.sql` — the same statements against the **real** migrated schema (migrations
  0001–0011) with the seed contract loaded, connected as the real `authenticator` role.
- `result-2026-10-17.txt` — the recorded output of all three.

## What they showed (on 16)

**The policy is what turns a grant into rows** (1a, 3a, 4a): each grant alone, under `FORCE RLS`, gives zero rows
and no error — the silent failure mode the architecture exists to avoid.

**`persons_authenticator_read` + `SELECT (id, email)` is as narrow as §3 claims** (2a–2e): the lookup works and
uses `persons_email_normalized_key`, a stored address with a capital letter and a trailing space is matched, and
`SELECT *`, a projection of `full_name`, and a `WHERE` over `full_name` are each refused with `permission
denied` — **the column grant, not the policy**. This is why the lookup in `PasswordPaths.BeginResetAsync` is
written out rather than left to EF: a tracked read of `persons` reads every mapped column, so it is refused
(confirmed again on the real schema, `03`).

**The whole lookup path runs in one statement** (2f–2g): address → person → user, a row for an account that
exists and zero rows, no error, for an address that does not. The role still cannot write `persons` (2j), and it
does read **every** row of it (2i) — the accepted risk of OPEN_ITEMS 56, measured rather than assumed.

**`provider_accounts_authenticator_read` confines the role to the platform's row** (4b–4e): with a per-tenant
row present in the table, the role sees the platform's one and not the tenant's — **and setting `app.tenant_id`
to that tenant changes nothing**, because the policy never reads a context variable. That is the shape the
amendment settled on: the grant covers the table, the policy confines the rows, so a per-tenant row written some
day is out of reach with no grant to revisit. The role cannot write the table either.

**`provider_accounts_read` admits the platform row to `app_user`** (3b–3c) and `app_user` cannot write it (3d);
**`UNIQUE NULLS NOT DISTINCT` refuses a second platform row per channel** (3e), the constraint §2.6 calls for.

**The supersession statement is idempotent** (`03`, B–D): it consumes whatever is live and then affects zero rows
without raising. That is what keeps two `forgot` requests racing on one address from answering 500 where an
unknown address answers 204 — the oracle that would have undone OPEN_ITEMS 51. Writing any other column of
`password_reset_tokens` is still refused.

## Also run on the same server, not recorded here

Migrations 0001–0011 applied in order from clean, the seed contract loaded, and `checks/core/run-checks.sh` —
**Checks 1–10 all green** with the new grants, policies and manifest. On 16, with a stand-in `uuidv7()`, since
that function is 18's.
