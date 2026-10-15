## What and why

<!-- The task (T-n, OPEN_ITEMS n, or the request) and what this PR changes. -->

## Decisions and discrepancies

<!-- Every decision you made that PLATFORM_CORE / PROOF_SPEC didn't, and every conflict you found between the spec,
the document and the code. "None" if none. The project owner decides these. -->

## Checklist

Data access
- [ ] Every database access runs inside `UnitOfWork` / `ProvisionerUnitOfWork` / `SystemContext`
- [ ] No tenant or scope filter added in code to hide or fix visibility — RLS is the filter
- [ ] No scope, membership or permission read from a request, header, query, token or cookie
- [ ] Each new endpoint checks its permission before any work; each new list returns a scope declaration (`ScopedList`)
- [ ] Writes that must happen use `CriticalWrite`; no `ExecuteUpdate` / `ExecuteDelete`
- [ ] Keys (`Guid.CreateVersion7()`) and timestamps set in code

Schema (skip if unchanged)
- [ ] New SQL migration + its `M00NN_*` class; forward-only
- [ ] New tables: RLS `ENABLE` + `FORCE`, policies with explicit `FOR`/`TO` and `NULLIF`, composite FKs on `tenant_id`, grants
- [ ] `checks/core/grants.json` / `policy-chains.json` updated with it (flagged above as a project-owner decision)
- [ ] `docs/schema.sql` regenerated (`scripts/dump-schema.sh`) and `docs/DATABASE.md` updated

API and interface (skip if unchanged)
- [ ] New error codes: `ApiErrorCodes.cs`, the export, `apiErrors.ts`, Arabic
- [ ] New UI text: explicit ids, Arabic in `ar.json`, glossary terms, logical CSS

Tests and docs
- [ ] [B] tests in Conformance for the behavior; [W] tests for internal guarantees
- [ ] No existing test edited, skipped or weakened to pass
- [ ] Each new check has a `--self-test` that fails on its plant
- [ ] `OPEN_ITEMS.md`: closed items removed, new ones added
- [ ] `docs/DEVELOPER_GUIDE.md` / `docs/DATABASE.md` updated where this change shows
- [ ] CI green
