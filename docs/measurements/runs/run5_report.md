# T8 — the measurement (PROOF_SPEC v1.3, spec item j)

**The threshold (written in the spec before the measurement, not adjusted):** p95 of the reference query under both policies ≤ 1.5 × p95 under `tenant_isolation` alone, under 20 concurrent requests.

**Verdict: EXCEEDED** — p95 ratio 1.704 (both 8.890 ms / alone 5.217 ms; the limit is 1.5).

## The setup

- Measured: 2026-09-26 00:25 UTC. PostgreSQL: `PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit`. .NET 10.0.12, 4 logical CPUs; the database and the client on the same host.
- The tenant: 500 clients, 20,000 subscriptions (40 per client). The member: operator, scope `assigned`, assigned to 20 clients (800 subscriptions visible).
- The reference query: the module's `GET /subscriptions` query — `subscriptions` ordered by `id`, 50 rows — through EF, as `app_user`, inside the unit of work (its resolution a-b-c and SET LOCALs run first, 3.5/6).
- Load: 20 concurrent workers, 200 warm-up iterations per phase (discarded), then 100 iterations per worker = 2000 samples per phase. Pool size 25.
- "Query" is the query alone (the stopwatch around its `ToListAsync`); "transaction" is the whole unit of work (begin, context, resolution, query, commit).
- `tenant_isolation` alone: `client_scope` on `subscriptions` neutralized as migrator (`USING (true) WITH CHECK (true)`) for the second phase, then restored from the template; its deparse compared equal before and after.

## Raw numbers (milliseconds)

| Phase | Measure | n | min | p50 | p90 | p95 | p99 | max | mean |
|---|---|---|---|---|---|---|---|---|---|
| both policies (tenant_isolation + client_scope) | query | 2000 | 1.152 | 4.270 | 7.394 | 8.890 | 13.627 | 23.479 | 4.752 |
| both policies (tenant_isolation + client_scope) | transaction | 2000 | 6.017 | 35.318 | 46.608 | 51.080 | 80.393 | 114.388 | 36.029 |
| tenant_isolation alone (client_scope neutralized) | query | 2000 | 0.466 | 2.347 | 4.181 | 5.217 | 7.799 | 31.870 | 2.664 |
| tenant_isolation alone (client_scope neutralized) | transaction | 2000 | 4.143 | 23.474 | 31.207 | 34.836 | 41.542 | 79.627 | 24.044 |

Transaction p95 ratio (for information, not the criterion): 1.466.

## The plans (EXPLAIN ANALYZE, one execution in the member's context)

Both policies:
```
SELECT s.id AS "Id", s.scope_ref_id AS "ScopeRefId", s.service_name AS "ServiceName", s.ends_on AS "EndsOn" FROM subscriptions AS s ORDER BY s.id LIMIT 50

Limit  (cost=0.33..318.02 rows=50 width=46) (actual time=0.062..0.414 rows=50.00 loops=1)
  Buffers: shared hit=23
  InitPlan 1
    ->  Result  (cost=0.00..0.02 rows=1 width=1) (actual time=0.001..0.001 rows=1.00 loops=1)
  InitPlan 12
    ->  Result  (cost=0.00..0.02 rows=1 width=16) (actual time=0.004..0.004 rows=1.00 loops=1)
  ->  Index Scan using subscriptions_pkey on subscriptions s  (cost=0.29..31807.85 rows=5006 width=46) (actual time=0.062..0.409 rows=50.00 loops=1)
        Filter: ((tenant_id = (InitPlan 12).col1) AND (COALESCE((InitPlan 1).col1, false) OR (ANY ((tenant_id = (hashed SubPlan 11).col1) AND (scope_ref_id = (hashed SubPlan 11).col2)))))
        Rows Removed by Filter: 1226
        Index Searches: 1
        Buffers: shared hit=23
        SubPlan 11
          ->  Seq Scan on scope_assignments sa  (cost=0.08..1.48 rows=3 width=32) (actual time=0.021..0.024 rows=20.00 loops=1)
                Filter: (active AND (COALESCE((InitPlan 9).col1, false) OR (membership_id = (InitPlan 10).col1)) AND (tenant_id = (InitPlan 8).col1) AND (membership_id = (InitPlan 7).col1))
                Rows Removed by Filter: 3
                Buffers: shared hit=1
                InitPlan 7
                  ->  Result  (cost=0.00..0.02 rows=1 width=16) (actual time=0.001..0.001 rows=1.00 loops=1)
                InitPlan 8
                  ->  Result  (cost=0.00..0.02 rows=1 width=16) (actual time=0.001..0.001 rows=1.00 loops=1)
                InitPlan 9
                  ->  Result  (cost=0.00..0.02 rows=1 width=1) (actual time=0.001..0.001 rows=1.00 loops=1)
                InitPlan 10
                  ->  Result  (cost=0.00..0.02 rows=1 width=16) (actual time=0.001..0.001 rows=1.00 loops=1)
Planning Time: 0.197 ms
Execution Time: 0.490 ms
```

`tenant_isolation` alone:
```
SELECT s.id AS "Id", s.scope_ref_id AS "ScopeRefId", s.service_name AS "ServiceName", s.ends_on AS "EndsOn" FROM subscriptions AS s ORDER BY s.id LIMIT 50

Limit  (cost=0.31..7.18 rows=50 width=46) (actual time=0.016..0.026 rows=50.00 loops=1)
  Buffers: shared hit=3
  InitPlan 1
    ->  Result  (cost=0.00..0.02 rows=1 width=16) (actual time=0.003..0.003 rows=1.00 loops=1)
  ->  Index Scan using subscriptions_pkey on subscriptions s  (cost=0.29..917.74 rows=6675 width=46) (actual time=0.015..0.023 rows=50.00 loops=1)
        Filter: (tenant_id = (InitPlan 1).col1)
        Rows Removed by Filter: 26
        Index Searches: 1
        Buffers: shared hit=3
Planning Time: 0.059 ms
Execution Time: 0.037 ms
```

Every sample is in `T8_samples.csv` (phase, measure, milliseconds).
