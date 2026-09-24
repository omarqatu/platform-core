# Operations

Procedures that run outside the application. Each is run by `migrator`, the only role that
holds them.

## `auth_attempts` retention (PROOF_SPEC v1.1 T2, spec item b)

`auth_attempts` is append-only and records usernames and IP addresses with no cap
(PLATFORM_CORE §4.1, 13/b). Rows older than **90 days** are deleted by a `migrator`
procedure, created by the migration `20260924000005_SeedAndRetention`:

```sql
CALL prune_auth_attempts();
```

- **Who runs it:** `migrator`. `EXECUTE` is revoked from `PUBLIC`, and no application role
  holds it. `migrator` has `BYPASSRLS`, so the delete sees every row under `FORCE ROW LEVEL
  SECURITY`.
- **When:** daily, from a scheduler outside the application (for example cron, or the
  platform's job scheduler), for example:
  ```bash
  psql "$MIGRATOR_CONNECTION" -c 'CALL prune_auth_attempts();'
  ```
- **The figure:** 90 days is the project owner's decision and is changed only by that
  decision, through a new migration.
- **Production requirement (spec item k, not built in the proof):** the scheduler that holds
  the `migrator` connection runs on the operations network. The application network cannot
  reach the `migrator` account (`pg_hba.conf`).
