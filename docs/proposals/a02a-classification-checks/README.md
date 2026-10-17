# Amendment 02, stage (a) — the classification, proven on PostgreSQL 18

Two things this PR's description claims, and the files that show them. Both ran on **PostgreSQL 18.6** (the
`postgres:18` image of `docker-compose.yml`), against the real migrations 0001–0012.

## 1. Checks 2, 3, 5 and 6 see the new catalog — not assumed, planted

`categories_read` and `subcategories_read` are the first Amendment 02 policies to enter the manifest
(`checks/core/amendment-policies-built.json`, `"02"`). That the manifest lists them proves the generator read them;
it does not prove the checks compare against them. `run.sh` plants one violation per case on the catalog inside a
transaction that is rolled back, runs one check with exactly the inputs `run-checks.sh` gives it, and prints what the
check reported. **An empty report would be a check that does not see the object** (`MISS`); there is none.

```bash
PSQL='docker compose exec -T postgres psql -U migrator -d platform' docs/proposals/a02a-classification-checks/run.sh
```

`result-2026-10-17.txt` is its output: a dropped policy fails Check 3 (A, B); a narrowed role list, a changed
`USING` and an extra policy fail Check 2 (C, D, E); a write granted, a read revoked, and the deferred `job_runner`
read granted fail Check 6 (F, G, I — OPEN_ITEMS 76); `NO FORCE` fails Check 5 (H).

## 2. The four backfills, on a table that has rows

CI migrates a clean database, so there the four `NOT NULL` columns land on an empty `subscriptions` and the
backfills update nothing. `backfill-2026-10-17.txt` is the other case, run by hand: migrations 0001–0011, the
**previous** seed contract (26 subscriptions with only T5's columns), then 0012. Every row came out
`subscription` / `other` / `yearly` / `false`, the optional columns `NULL`, and no column has a `DEFAULT`. Its last
block shows a row-count check failing when one row does not carry the backfilled value
(`billing_cycle backfill: 25 of 26 rows`), so the check is not one that cannot fail.
