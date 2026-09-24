# Local repository checks

**These are not PLATFORM_CORE checks.** `docs/PLATFORM_CORE_v1_9_FROZEN.md` §3.6
defines exactly ten CI checks, numbered 1–10, and they are built in T2. The checks
in this directory are local additions to this repository, decided by the project
owner. They are named, never numbered, so that "Check N" always means the
document's Check N.

They also do not belong in `Conformance`. Conformance measures any
implementation against the written contract (PROOF_SPEC §6). These checks
enforce this repository's own layout, which another implementation need not
share.

| Check | What it enforces | Added |
|---|---|---|
| `check-schema-allowlist` | The only non-system schemas are those in `schema-allowlist.txt` (`public`, `migrations_meta`), and `migrations_meta` contains only `__EFMigrationsHistory`. Drift is reported in both directions: anything extra, and anything allowlisted but missing. | T0 (PR #1) |

## check-schema-allowlist

**Why it exists:** Check 5 in the document covers `public` only
(`n.nspname = 'public'`). The EF history table lives in `migrations_meta` so that
`rls_exemptions` can stay empty, and that makes any other schema a place where
a table would escape Check 5's RLS coverage. This check closes that by making
the set of schemas, and the contents of `migrations_meta`, a declared list.

**What it inspects in `exact` schemas:** relations of every kind (tables, views,
materialized views, sequences, foreign and partitioned tables), functions and
procedures, and standalone types. Indexes and TOAST tables are treated as part
of their table.

**Running it** (repository root, after migrating):

```bash
checks/local/check-schema-allowlist.sh
checks/local/check-schema-allowlist.sh --self-test
```

`--self-test` plants a rogue schema and a rogue table inside a transaction that
is rolled back, and fails unless the check reports both. CI runs both modes.

**Changing the allowlist** is a project-owner decision made in review, like the
manifest.
