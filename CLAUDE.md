# CLAUDE.md

Platform Core: a multi-tenant SaaS core. Tenant isolation is enforced by PostgreSQL itself (RLS + grants + composite
foreign keys); the application adds a loud layer on top. .NET 10 / EF Core / PostgreSQL 18; React + TypeScript in `web/`.

**Before your first change in a session, read `docs/DEVELOPER_GUIDE.md`** (§5 request lifecycle, §6 data-access
layer, §18 rules). For anything touching the schema, `docs/DATABASE.md`. The whole current schema is `docs/schema.sql`
(generated — never edit it).

## Authority

1. `docs/PLATFORM_CORE_v1_16_FROZEN.md` (Arabic, authoritative; `_EN.md` is a translation) — the architecture,
   **plus its amendments in order**: `docs/PLATFORM_CORE_AMENDMENT_01.md`, then
   `docs/PLATFORM_CORE_AMENDMENT_02.md` (Arabic, authoritative in both; `_EN.md`). An amendment adds to 1.16 and
   edits only the paragraphs its §0 names as amended; any other conflict between the two is an error in the
   amendment. A later amendment may withdraw what an earlier one added: Amendment 02 §0 withdraws Amendment 01 §6
   (the import) in full, so the earlier text that §0 names is history and not in force.
2. `docs/PROOF_SPEC_v1_3.md` (Arabic, authoritative; `_EN.md`) — tasks and acceptance criteria.
3. The code. 4. The guides.

The current stage and its order of work: `docs/ROADMAP_SUBSCRIPTIONS.md`.

A conflict between them → **stop and report it** in the PR description. The project owner decides; you don't.
Anything in neither document → **ask before building it**. No business logic beyond what the task needs.

## Rules that are never broken

- **No database access outside an explicit transaction.** Use `UnitOfWork.RunAsync` / `RunReadOnlyAsync`,
  `ProvisionerUnitOfWork.RunAsync`, or `SystemContext` (jobs). `TransactionRequiredInterceptor` throws otherwise.
- **Never filter by tenant or scope in code to "fix" visibility** — RLS does it. Endpoints check the permission
  (`Permissions.RequireAsync`, or `db.Scope.CanManageScope/CanManageMembers`) and declare scope (`ScopedList`).
- **Never read scope or permissions from a request, header, query, token or cookie.** The second-axis variables
  (`app.membership_id`, `app.scope_all`, `app.can_manage_scope`, `app.can_manage_members`) are set only in
  `src/Core/Data/ScopeResolver.cs` and `src/Core/Data/SystemContext.cs` (Check 9). The cookie holds `user_id` and
  `tenant_id` only — plus the security stamp once Amendment 01 §2.1 is built.
- **No `BYPASSRLS` except `migrator`. No `SECURITY DEFINER` anywhere.**
- **Every policy has explicit `FOR` and `TO`;** a narrowing condition is `AS RESTRICTIVE`; every `current_setting`
  read is `(SELECT NULLIF(current_setting('app.x', true), '')::type)`.
- **Every FK between two `tenant_id` tables is composite on `tenant_id`.**
- **The app generates every key and timestamp** (`Guid.CreateVersion7()`, `DateTime.UtcNow`). No `DEFAULT`,
  identity or generated column. Save parents before children (no relationships are mapped in EF).
- **No `ExecuteUpdate` / `ExecuteDelete` in `src/`** — they bypass the automatic audit.
- **Writes that must happen use `CriticalWrite`** (zero rows affected = error).
- **Api never holds the `migrator` connection string;** Worker holds only `job_runner` and `app_user`.
- **Never edit, skip, or weaken a test to make it pass.** A test that looks wrong → stop and explain in the PR.
- **Prove engine/framework assumptions on real PostgreSQL 18 (and real EF commands) before building on them.**
- **One task = one branch = one PR.** No force-push to `main` or a merged branch; on an unmerged PR branch only
  `--force-with-lease`, declared in the PR.

## Changing the schema

New file `src/Migrations/Sql/00NN_<what>.sql` + class `src/Migrations/Migrations/M00NN_<What>.cs`
(`SqlFileMigration`). Forward-only. A new table gets all three layers in the same file (table + constraints,
`ENABLE`+`FORCE` RLS + policies, grants); data migrations end with a row-count `DO $$ … RAISE EXCEPTION`.
Update `checks/core/grants.json` / `policy-chains.json` with it — changing them is a project-owner decision, say so in
the PR. The manifest is extracted from PLATFORM_CORE and its amendments (`checks/core/generate-manifest.py`), never
hand-edited; a new amendment policy enters it when its PR lists it in `checks/core/amendment-policies-built.json`.
Then regenerate `docs/schema.sql` (`scripts/dump-schema.sh`, see `docs/DATABASE.md` §10) and update `docs/DATABASE.md`.

## Running and checking

```bash
scripts/dev-up.sh                     # PostgreSQL 18 + migrations + seed + Api :5080 + web http://localhost:5173
scripts/dev-down.sh
dotnet build PlatformCore.slnx
PSQL='docker exec -i platform-core-dev-postgres psql -U migrator -d platform' checks/core/run-checks.sh [--self-test]
PG_DUMP='docker exec -i -e PGTZ=UTC platform-core-dev-postgres pg_dump -U migrator -d platform' scripts/dump-schema.sh
```

Tests: `tests/Conformance` [B] (SQL + HTTP only, no project references — keep it that way), `tests/Core.WhiteBoxTests`
[W], `web/e2e`. How to run them locally: `docs/DEVELOPER_GUIDE.md` §16. Every new check needs a `--self-test` that
fails on a planted violation.

## Conventions

- API routes under `/api` only; JSON `snake_case`; errors `{ "error": "<code>" }` with codes from
  `src/Core/ApiErrorCodes.cs` (never a string literal elsewhere; a new code also needs
  `checks/local/export-api-error-codes.py`, `web/src/i18n/apiErrors.ts` and Arabic in `web/locales/ar.json`).
- Unsafe requests need `Origin` (allowed) + `X-Requested-With: platform-web`.
- UI text: explicit message ids, English `defaultMessage`, Arabic in `web/locales/ar.json`, glossary terms
  (`web/GLOSSARY.md` — never "tenant" / "مستأجر" in visible text), logical CSS only, Western digits in Arabic.

## Finishing a task

Close the `OPEN_ITEMS.md` entries you closed; add what you found and did not do (new number, never renumber).
Update the guides where your change shows. Fill the PR template's checklist honestly. Commit and push; open a PR —
the project owner reviews and merges.
