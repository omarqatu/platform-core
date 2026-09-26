# T8 — the measurement (PROOF_SPEC v1.3, spec item j)

**The threshold, written in the spec before the measurement and not adjusted:**

```
The reference query: a sorted list of 50 subscriptions, for an assigned member assigned to 20 clients, in a tenant
with 500 clients and 20,000 subscriptions.
Acceptance: p95 for the query under both policies (tenant_isolation + client_scope)
            ≤ 1.5 × p95 for the same query under tenant_isolation alone, under 20 concurrent requests.
```

## Verdict: EXCEEDED

Four runs of five exceed 1.5; the median ratio is **1.574**. The first run passed at 1.491, within 0.6% of the limit.
A single run that close to the limit decides nothing, so the same procedure was run four more times, each on a fresh
database. Every run is reported below; none is left out.

| Run | p95 query, both policies (ms) | p95 query, tenant_isolation alone (ms) | Ratio | Within 1.5 |
|---|---|---|---|---|
| 1 | 9.371 | 6.285 | 1.491 | yes |
| 2 | 9.878 | 6.425 | 1.537 | no |
| 3 | 9.983 | 6.136 | 1.627 | no |
| 4 | 9.451 | 6.005 | 1.574 | no |
| 5 | 8.890 | 5.217 | 1.704 | no |
| **Median** | | | **1.574** | |

**Under the spec, this is not fixed in T8.** It is recorded with its numbers (OPEN_ITEMS 27), and the project owner
decides between the two alternatives the document declares: a materialized assignment table, or a `SECURITY DEFINER`
function for `client_scope` (§4.8 / 13-i: the single declared exception to Check 10's limit).

## What the numbers say — context for the decision, not a change to the verdict

- **The absolute cost:** at p95 the second axis adds about 3.5 ms to the query (≈ 9.5 ms against ≈ 6 ms). The
  database's own execution time is 0.45 ms with both policies against 0.04 ms without (EXPLAIN ANALYZE, run 1): the
  member sees 20 of 500 clients, so the index scan in `id` order removes about 1,226 rows by the filter to find 50,
  each checked against the hashed assignments. The rest of each sample is the round trip and the contention of 20
  concurrent workers on 4 CPUs.
- **The whole transaction** — begin, context, resolution a-b-c, the query, commit — has a median p95 ratio of **1.386**
  (1.336–1.466), inside 1.5 in every run. Not the criterion: the spec measures the query.
- **The environment:** a 4-CPU container, PostgreSQL 18.6 and the client on the same host. A dedicated database host
  would change the absolute numbers; whether it would change the ratio is not known without measuring there.

## How it was measured

`tests/Measurement` (not a CI step):

```bash
# a clean, migrated database (the CI steps up to Migrate), then:
dotnet run --project tests/Measurement -c Release -- docs/measurements/runs/<name>
```

- The dataset, as migrator, in a tenant of its own: 500 clients, 40 subscriptions each, and an operator member with
  scope `assigned`, assigned to 20 clients (every 25th), so 800 subscriptions are visible. `ANALYZE` afterwards.
- The reference query is the module's `GET /subscriptions` query — `subscriptions` ordered by `id`, 50 rows — through
  EF, as `app_user`, inside the unit of work (its resolution a-b-c and SET LOCALs first, 3.5/6). "Query" is the
  stopwatch around the query alone; "transaction" is the whole unit of work.
- 20 concurrent workers; per phase 200 warm-up iterations (discarded), then 100 per worker = 2,000 samples.
- "tenant_isolation alone": `client_scope` on `subscriptions` neutralized as migrator (`USING (true) WITH CHECK
  (true)`) for the second phase, then restored from the template; the tool compares its deparse before and after,
  and fails if it differs.
- Afterwards the dataset's membership is disabled, so the tenant is inert.

`runs/runN_report.md` holds each run's full report — every percentile, both measures, both query plans — and
`runs/runN_samples.csv` every sample.
