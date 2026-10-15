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
| `check-schema-allowlist` | The only non-system schemas are those in `schema-allowlist.txt` (`public`, `migrations_meta`), and `migrations_meta` contains only `__EFMigrationsHistory`. Drift is reported in both directions: anything extra, and anything allowlisted but missing. | T0 (PR #1); runs beside Checks 1–10 since T2 |
| `check-no-bulk-writes` | No `ExecuteUpdate`/`ExecuteDelete` call in `src/` except the paths in `bulk-writes-allowlist.txt` (empty). | T4 — PLATFORM_CORE v1.16 §3.10, PROOF_SPEC v1.3 T4.16 |
| `check-i18n-parity` | The keys of `web/locales/ar.json` are exactly those of a fresh extraction of `web/src/`; `web/locales/en.json` is that extraction; every plural in `ar.json` gives the six Arabic forms. | i18n task |
| `check-api-error-codes` | `api-error-codes.json` is the current export of `src/Core/ApiErrorCodes.cs`; no code is a string literal elsewhere in `src/` (except `api-error-codes-literal-allowlist.txt`); every code has its entry `errors.<code>` in `web/src/i18n/apiErrors.ts`, and every entry but `errors.unknown` has its code. | i18n task |
| `check-schema-dump` | `docs/schema.sql` equals a fresh `pg_dump --schema-only` of the migrated database (`scripts/dump-schema.sh`). | Developer documentation |

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

## check-no-bulk-writes

**Why it exists:** auditing is automatic (PLATFORM_CORE v1.16 §7): the audit
interceptor captures tracked changes at `SavingChanges`. A bulk command —
`ExecuteUpdate` or `ExecuteDelete` — goes straight to the database and bypasses
the change tracker, so it would write with no audit entry (proven in the
document: 1 row updated, 0 audit entries). §3.10 forbids both in application
code; this check enforces it.

**What it inspects:** every `.cs` file under `src/` (not `bin/` or `obj/`) for a
call of `ExecuteUpdate`, `ExecuteUpdateAsync`, `ExecuteDelete` or
`ExecuteDeleteAsync`. Tests are not application code and are not scanned.

**The declared exception** is the password command (§3.8). It is a raw SQL
command, not an EF bulk command, so it never matches, and the allowlist is
empty. Adding a path to `bulk-writes-allowlist.txt` is a project-owner decision.

**Running it** (repository root; no database needed):

```bash
checks/local/check-no-bulk-writes.sh
checks/local/check-no-bulk-writes.sh --self-test
```

`--self-test` copies `src/` to a temporary directory, plants one
`ExecuteUpdateAsync` and one `ExecuteDelete` call there, and fails unless the
check reports exactly those two. The source tree is never edited. CI runs both
modes.

## check-i18n-parity

**Why it exists:** the interface's text lives in the code (`defaultMessage`, English) and in `web/locales/ar.json`
(Arabic, edited by hand). A message added to the code and not translated would show English inside the Arabic
interface; a translation left behind after its message is gone is dead weight that hides the first case.

**What it inspects:** it extracts `web/src/**/*.{ts,tsx}` (tests excluded) afresh with the formatjs CLI, into a
temporary file, and compares the ids with the keys of `ar.json` both ways. It also fails when the committed `en.json`
is not that extraction (run `npm run i18n:extract`), and when a `plural` in `ar.json` lacks one of zero, one, two,
few, many, other.

**Running it** (repository root; `npm ci` in `web/` first; no database):

```bash
checks/local/check-i18n-parity.sh
checks/local/check-i18n-parity.sh --self-test
```

`--self-test` removes one key from a copy of `ar.json` in memory, adds an extra key and a plural with two forms, and
fails unless exactly those are reported. No file is written. CI runs both modes (the `web` job).

## check-api-error-codes

**Why it exists:** the API returns codes, never text (`{ "error": "<code>" }`), and the interface translates each.
A code with no message would reach the user as "unknown"; a message with no code is dead.

**What it inspects:**
1. `api-error-codes.json` is the export of `src/Core/ApiErrorCodes.cs`, the codes' one source
   (`export-api-error-codes.py --check`; the exporter refuses any line of that file that is not a code constant).
2. No code from the export is written as a `"literal"` in a `.cs` file under `src/` outside `ApiErrorCodes.cs`, except
   the paths in `api-error-codes-literal-allowlist.txt` — today only the original T8 screen, which is not edited
   (OPEN_ITEMS 29).
3. The ids of `web/src/i18n/apiErrors.ts` are exactly `errors.<code>` for every code, plus `errors.unknown`, and no
   `errors.*` id exists anywhere else in `web/src/`.

**Running it** (repository root; python3, and `npm ci` in `web/` first; no database):

```bash
checks/local/check-api-error-codes.sh
checks/local/check-api-error-codes.sh --self-test
```

`--self-test` plants a literal code in a copy of `src/`, then, in memory, a backend code with no entry, an entry
whose code is dropped, and an `errors.*` id outside `apiErrors`; it fails unless each is reported. The source tree is
never edited. CI runs both modes (the `web` job).

**Adding a code:** a constant in `ApiErrorCodes.cs`, then `checks/local/export-api-error-codes.py`, then its entry in
`apiErrors` and its translation in `ar.json` — the check fails until all three are done.

## check-schema-dump

**Why it exists:** `docs/schema.sql` gives a reader the whole schema in one file — every table, constraint, index,
policy and grant — instead of nine migrations to replay in their head (`docs/DATABASE.md`). A schema file written by
hand drifts from the migrations within weeks and then misleads more than it helps, so this one is generated, and this
check fails while the committed file is not the current dump.

**What it compares:** the committed `docs/schema.sql` with `scripts/dump-schema.sh` run against the database CI has
just migrated from clean — `pg_dump --schema-only`, `migrations_meta` excluded, session time zone UTC, a fixed
`\restrict` key, and the "Dumped from/by" version lines removed, so the result does not depend on the PostgreSQL 18
minor version. A difference is printed as a unified diff.

**Running it** (repository root, after migrating a clean database; the default reaches `pg_dump` through the
`docker compose` service, `PG_DUMP` overrides it):

```bash
checks/local/check-schema-dump.sh
checks/local/check-schema-dump.sh --self-test
# against the scripts/dev-up.sh container:
PG_DUMP='docker exec -i -e PGTZ=UTC platform-core-dev-postgres pg_dump -U migrator -d platform' checks/local/check-schema-dump.sh
```

`--self-test` compares the fresh dump with an identical copy (must pass) and with a copy whose first `CREATE POLICY`
line is renamed (must fail and show that line). No file is written outside a temporary directory. CI runs both modes.

**Fixing a failure:** run `scripts/dump-schema.sh` (with the same `PG_DUMP`) on a database migrated from clean, and
commit `docs/schema.sql` with the migration that changed it.
