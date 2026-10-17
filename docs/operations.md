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

## The platform's delivery account (Amendment 01 §2.6)

Nothing is sent until `provider_accounts` holds the platform's row for the channel, and no application
role can write one: the rows are `migrator`'s, seeded. A deployment seeds its own once, as `migrator`:

```sql
INSERT INTO provider_accounts (id, tenant_id, channel, provider_code, sender_id, credential_ref, is_active, created_at)
VALUES (uuidv7(), NULL, 'email', 'smtp', 'no-reply@<your domain>', 'platform-email', true, now());
```

- **`tenant_id IS NULL` is the platform's row**, one per channel — `UNIQUE NULLS NOT DISTINCT` refuses a second.
  The per-tenant override is a reserved slot at this stage; no path writes one.
- **`credential_ref` is a settings key name, never a secret.** The row above names `platform-email`, and the Api
  reads the credentials from `Notifications:Email:Providers:platform-email` — `Host`, `Port`, `UseStartTls`,
  `Username`, `Password` — wherever that deployment keeps its settings, beside the connection strings. Put a
  secret in this table and every member of every tenant is one vulnerability away from it (§2.6).
- **Turning a channel off** is `UPDATE provider_accounts SET is_active = false …` as `migrator`. The sender then
  reports every send as failed and logs it; nothing throws, and no path that sends is broken by it.
- **After changing a row**, the Api holds it for `Notifications:ProviderAccounts:CacheFor` (5 minutes by
  default) before reading it again.

## `password_reset_tokens` retention (Amendment 01 §2.2)

The recovery tokens follow the same policy as `auth_attempts`: rows older than **90 days** are
deleted by a `migrator` procedure, created by the migration `20261017000010_PasswordReset`:

```sql
CALL prune_password_reset_tokens();
```

- **Who runs it:** `migrator` alone, exactly as above — `EXECUTE` is revoked from `PUBLIC`, and no
  application role holds `DELETE` on the table either (§3: `authenticator` has `SELECT`, `INSERT`
  and `UPDATE (used_at)`, nothing more).
- **When:** daily, beside the procedure above:
  ```bash
  psql "$MIGRATOR_CONNECTION" -c 'CALL prune_password_reset_tokens();'
  ```
- **What it does not do:** a token is consumed by being used (`used_at`), never by being deleted;
  this only clears the history afterwards. An unused token is already dead to the reset path once
  `expires_at` has passed.
