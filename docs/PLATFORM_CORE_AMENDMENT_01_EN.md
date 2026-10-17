# PLATFORM_CORE — Amendment 01: Product readiness

> **This file is a translation.** The authoritative text is the Arabic, `docs/PLATFORM_CORE_AMENDMENT_01.md`; where
> the two differ, the Arabic governs. Section numbers, table rows and marked assumptions follow the Arabic exactly.

**Status:** Approved by the project owner — 2026-10-17. Attached to PLATFORM_CORE v1.16 (frozen): it adds to it, and amends only the paragraphs of it named in §0. `_EN.md` is a translation; the Arabic governs.

## §0 Status and authority

**In force:** from the date this file is merged. The corresponding items in `OPEN_ITEMS.md`: 42–52.

**What it amends:** the frozen PLATFORM_CORE v1.16. The rule is **addition**: tables, columns, grants, policies and paths, and recorded decisions; each item here names the paragraph it attaches to. **And it amends** the following paragraphs of 1.16 — no others — by the project owner's decision of 2026-10-17:

| 1.16 paragraph | Its text in 1.16 | The amendment |
| --- | --- | --- |
| §4.3-a/2 | `INSERT` on `auth_attempts`, "its only write", for `authenticator` | `INSERT` and `UPDATE (used_at)` on `password_reset_tokens` are added (§2.2, §3): recovery starts with no session, and no other role works without a session |
| §4.3-a/4 | "It writes nothing to the identity tables themselves" | It writes `password_reset_tokens` alone; it touches neither `users` nor `user_password_credentials` |
| §4.3-b | `app.user_id` is set from the authenticated session | One named exception: in `POST /api/auth/password/reset`, UoW-2 takes `app.user_id` from the owner of the token that UoW-1 verified and consumed (§5.1) |
| §4.4/3 | "Exactly two confined paths, no more" | A third path: the operations procedure `src/Ops`, as `provisioner` (§5.4) |
| §4.4/5 | "No third path writes across isolation boundaries" | The operations procedure is the named third: one tenant per transaction, and its audit in the same transaction, like the two paths (§4.4/4) |
| §3.4 (the `migrator` row) | "migrations only" | And with them the import command in `src/Ops.Import` (§6) — a data-migration tool for a single purpose, under the three conditions of §3.4 |
| §3.10 item a | "The last active owner": no tenant is left without an active owner | A tenant created by `create-tenant` has no active owner until an `owner` invitation is accepted (§5.4); item a applies to it from the first acceptance |
| §4.1 (the `invitations` definition) | `invited_by` is the inviting user's id (`NOT NULL` in the schema) | `invited_by` accepts `NULL`, meaning "a bootstrap invitation, with no human inviter" (§2.11) |

**The chain of authority after it:** PLATFORM_CORE v1.16 + the amendments in their order (01, 02…) → PROOF_SPEC v1.3 → the code → the guides. A conflict between 1.16 and an amendment outside the paragraphs the amendment names as amended = an error in the amendment, until the project owner decides otherwise. This line is added to `CLAUDE.md`.

**What it does not touch (fixed by the text of 1.16):**

- The tenant is the only unit of isolation (§4.8); the second axis is a narrowing, not a unit.
- The five roles, no membership between roles, `BYPASSRLS` for migrator alone, no `SECURITY DEFINER` (§3.4).
- The two standard policy templates, `NULLIF`, `AS RESTRICTIVE` for narrowing, a composite FK between two tables carrying `tenant_id` (§3.3).
- The application generates every key and timestamp; no `DEFAULT`/identity/generated (§3.5).
- The consent principle: no membership without acceptance (§0). **The migration (§6) does not breach it:** the migrated users accepted their membership in the live system, and the migration is a move, not a recruitment — so their memberships are created `active` directly, with no invitation.
- `job_runner` in its 1.16 role (§3.4, §8): reading the active tenants to start the fan-out, and no writing.
- The ten checks and their inputs; the grants the amendment adds enter `grants.json` in the code PR. The policies of §4.1 enter the manifest when they are built (§4.1).

**The amendment's scope:** what the stage "receiving the subscription system" needs, per the approved roadmap — no more. The billing engine, the gateways, public self-registration, brands/partners, §9, SSO, the customer portal: outside it (§7).

## §1 Recorded decisions

The project owner's decisions of 2026-10-17. Each one has a corresponding item in `OPEN_ITEMS.md`, with the number in the last column: D5 and D8 update two existing items, and D6's item is a cross-reference to 36 and 16.

| # | The decision | Effect on 1.16 | OPEN_ITEMS |
| --- | --- | --- | --- |
| D1 | **Succession:** the platform succeeds the live subscription system (SYSTEM_DESIGN 2.0). Only the data is migrated; no code is carried over. The old system is read-only until the cutover, then switched off | Settles the open condition in §0 | 42 |
| D2 | **Creating tenants in production** by an operations procedure as `provisioner` (bootstrap from the templates + an `owner` invitation the owner accepts). No public self-registration | A third path for `provisioner` — **amends §4.4/3, §4.4/5 and §3.10 item a** (§0) | 43 |
| D3 | **Delivery channels:** email, WhatsApp (a hosted gateway such as GreenAPI), SMS (API), Telegram (Bot). Email + WhatsApp first. **Option A:** built inside M1, before the cutover | New tables (§2), grants (§3) | 44 |
| D3′ | **Three layers of delivery:** the platform's provider account by default, with a possible per-tenant override later; enabling the channels and their timing per tenant; the contact details per client. **Provider secrets are not in the database** (§2.6) | — | 45 |
| D4 | **Audit of the migrated rows — settled 2026-10-17:** **no `audit_log` entry for the import**; `migrator` does not become a fourth writer (1.16 §7). The record is the row-count report the command writes outside the database, and `legacy_id` for tracing (§6) | — (§7 as it stands) | 46 |
| D5 | **OPEN_ITEMS 27 is deferred:** measure again after 10 real tenants; the threshold and the two alternatives are fixed | — | 27 |
| D6 | **Password recovery and change** through three paths and two units of work (authenticator then app_user), with a `security_stamp` that ends every session when the password changes. **Closes OPEN_ITEMS 36 in part:** a copy of the cookie stays valid after logout | §2.1, §2.2, §3, §5 — **amends §4.3-a/2, §4.3-a/4, §4.3-b** (§0) | 47 (a cross-reference to 36 and 16) |
| D7 | **The platform subscription register (layer 1):** plans without prices, one subscription per tenant with `paid_until`, suspension by a scheduled operations command, read access for the owner and the admin (`core.billing.read`, §4.2). The engine and the gateways are deferred; they are built on top of this, not instead of it | §2.3, §2.4, §3, §4, §5 | 48 |
| D8 | **OPEN_ITEMS 25 is deferred** until the second module | — | 25 |
| D9 | **The amendment stands apart** from 1.16; merged into 2.0 when amendments accumulate or the second module starts | The chain of authority (§0) | 49 |

## §2 New tables and columns (additions to §4 and §5)

Every table carrying `tenant_id` follows the standard template (`tenant_isolation`), and one carrying `scope_ref_id` follows both templates. Keys are `app_id` and timestamps `app_ts`, as in 1.16. Column names are provisional until the migration.

### 2.1 `users.security_stamp` — a new column (to §4.2)

`security_stamp app_id NOT NULL`. The application generates it when the user is created and changes it on every password change. It enters the cookie; `UnitOfWorkMiddleware` compares it with the row on every request (one read, under the existing `user_self_read`); a mismatch = an expired session (401). Writing it through `user_self_update` is a self-identity write, so it joins the exception list of automatic auditing (1.16 §7) beside `last_login_at, language, theme` — so a password change leaves no audit trace until the identity audit log exists (OPEN_ITEMS 52). **The migration:** a `NOT NULL` column on a table that has rows → a backfill with a value for every row inside the same migration, then a row-count check (1.16 §3.4, condition 2).

### 2.2 `password_reset_tokens` — a global table (to §4.2, beside `auth_attempts`)

```sql
password_reset_tokens(id, user_id → users, token_hash, requested_at, expires_at, used_at NULL, request_ip NULL)
-- No tenant_id. RLS enabled. Only the token's hash is stored (SHA-256 of a 32-byte token).
-- A new request sets used_at on the same user's earlier unused tokens.
-- Retention: the same policy as auth_attempts (§13 item in 1.16) — operational cleanup as migrator.
-- authenticator's writes on it are identity writes, not tenant data: outside automatic auditing, like auth_attempts (1.16 §7),
-- and token_hash is already on the masking list.
```

### 2.3 `plans` — a global catalog (to §5)

`plans(id, code, name_ar, name_en, is_active, UNIQUE (code))`. Seeded by `migrator` only. **No prices** — the price is an agreement outside the platform at this stage. The first plans: [`trial`, `standard`] — an assumption.

### 2.4 `platform_subscriptions` — per tenant (to §4.1)

```sql
platform_subscriptions(id, tenant_id, plan_code → plans(code), status, period_start, paid_until date, grace_days, created_at, updated_at,
  UNIQUE (tenant_id))                       -- one row per tenant at this stage
-- paid_until is a date in UTC; suspend-unpaid compares against (now() AT TIME ZONE 'UTC')::date (§5.4) — not current_date, which follows the session TimeZone. A deadline with no time zone is a defect waiting for a tenant in another country.
-- status ∈ {active, suspended} — the grace is carried by grace_days, so no intermediate status. The standard template.
-- Created at bootstrap (provisioner) with the trial plan and paid_until = [today + 30] — an assumption.
-- paid_until/status change through the operations procedure (provisioner) alone: mark-paid and suspend-unpaid (§5.4).
```

**The explicit limit:** this is not an invoice and not a payment. No `invoices` and no `payments` in this amendment.

### 2.5 `tenant_notification_settings` — per tenant (to §4.1; owned by the module)

`(id, tenant_id, channel, is_enabled, reminder_days_before int[], sender_display_name, created_at, updated_at, UNIQUE (tenant_id, channel))`. `channel ∈ {email, whatsapp, sms, telegram}`. The standard template. The rows are created when the module is enabled for the tenant, with values from the live system [—].

### 2.6 `provider_accounts` — the platform, with an optional per-tenant override (to §4.1)

```sql
provider_accounts(id, tenant_id NULL, channel, provider_code, sender_id, credential_ref, is_active, created_at,
  UNIQUE NULLS NOT DISTINCT (tenant_id, channel))   -- the platform's row: tenant_id IS NULL, one per channel
-- credential_ref = the name of the key in the Worker/Api settings (env/secret store). **No secret in the database.**
-- At this stage only the platform's rows are seeded (migrator). Tenant rows: a reserved slot, no path writes them.
```

Why: `app_user` reads this table in every delivery transaction; had it carried the providers' keys, every tenant member would be one vulnerability away from the platform's keys. The reference to the settings keeps the secret where the connection strings live. **`NULLS NOT DISTINCT`:** without it, `UNIQUE` does not prevent two platform rows for the same channel, because `NULL` does not equal `NULL`. **The read policy** needs a wording outside the template: `tenant_id = app.tenant_id OR tenant_id IS NULL` — a named policy that enters the manifest (Check 2) when it is built (§4.1).

### 2.7 `notification_log` — per tenant, and scoped (to the module, beside `subscriptions`)

```sql
notification_log(id, tenant_id, subscription_id, scope_ref_id NOT NULL, channel, reminder_day date, provider_message_id NULL, status, attempted_at, sent_at NULL, error NULL,
  FOREIGN KEY (tenant_id, subscription_id) REFERENCES subscriptions (tenant_id, id),
  UNIQUE (tenant_id, subscription_id, channel, reminder_day))   -- the de-duplication key
-- reminder_day = the planned send date.
-- scope_ref_id = the subscription's client → both templates, and NOT NULL (rule 1.8).
-- status ∈ {pending, sent, failed}.
-- Written pending in the computing transaction (outbox); the send step moves it to sent|failed (§5.5).
-- No DELETE for any application role; the status only moves forward.
```

### 2.8 Columns on `clients` (to the module)

`email NULL`, `phone_e164 NULL`, `telegram_chat_id NULL`, `is_active NOT NULL` (disabling, not deleting). All under the current grants of `clients`. **`is_active` on a table that has rows** → the same rule as §2.1: a backfill for every row inside the same migration, then a row-count check.

### 2.9 Columns on `subscriptions` (to the module)

`status ∈ {active, cancelled, renewed}` (`NOT NULL`, under the same backfill rule as §2.1), `renewed_from_id NULL → subscriptions(tenant_id, id)` (composite). Renewal = a new subscription pointing to the old one; cancellation = a status change. **No DELETE** — it stays as in 1.16 for modules, but the module does not use it.

### 2.10 `legacy_id` — temporary

A `legacy_id text NULL` column on `clients`, `subscriptions`, `tenants` and `users`, added by a numbered schema migration before the import, filled by the import command (§6), and dropped by a later migration after the cutover. Its presence after the switch-off is an error.

### 2.11 `invitations.invited_by` accepts `NULL` (amends the §4.1 definition — §0)

`NULL` = a bootstrap invitation, with no human inviter: the `owner` invitation that `create-tenant` writes (§5.4) for a tenant that has no user yet. Every other invitation carries its inviter: `invitations_insert` (1.16 §3.9) requires `invited_by = app.user_id`, so `app_user` writes no invitation without an inviter, and `invitations_provisioner_insert` (§4.1) requires `invited_by IS NULL`, so the `provisioner` path forges no inviter.

### 2.12 The migrated user with no password — a marked assumption

The import (§6/3) creates for every migrated user a `user_password_credentials` row in a "no password" state: **no input matches it, and the verifier rejects it without throwing.** This is a rule, not an implementation; and it is **an assumption to be proven on PostgreSQL 18 in the code PR** before anything is built on it, like the other marked assumptions.

## §3 The complete updated grants matrix (replaces §3.8 when merged)

The rows from 1.16 are carried over verbatim. What is new is **in bold** and marked "(Amendment 01)". What is not in the matrix is not granted. It is carried into `checks/core/grants.json` in the code PR, and Check 6 fails until they match.

| Table | app_user | authenticator | provisioner | job_runner |
|---|---|---|---|---|
| tenants | SELECT | — | INSERT, **SELECT, UPDATE (status) (Amendment 01 — §5.4)** | SELECT |
| persons | SELECT | — | **SELECT**, INSERT | — |
| users | SELECT, UPDATE (columns: last_login_at, language, theme only) **+ security_stamp (Amendment 01 — §2.1)** | SELECT | **SELECT**, INSERT | — |
| user_password_credentials | UPDATE (password_hash, updated_at — **1.12**) **with no SELECT** | SELECT | INSERT | — |
| memberships | SELECT, UPDATE (status) — **the policies confine the transitions (1.16, 3.10)** | — | **SELECT**, INSERT, **UPDATE (status) — return only (1.16, D9)** | — |
| membership_roles | SELECT, INSERT, DELETE — **with core.members.manage, never on oneself (1.16)** | — | INSERT, **SELECT, DELETE (1.16 — return only)** | — |
| membership_auth | SELECT | SELECT | INSERT | — |
| membership_scope | SELECT, UPDATE (scope_mode) — **and the policy requires `can_manage_scope` and forbids editing one's own membership** (4.8) | — | INSERT, **SELECT, UPDATE (scope_mode) (1.16 — return only)** | — |
| scope_assignments | SELECT, INSERT, UPDATE (active) — **no DELETE**: an assignment is history that gets disabled, never erased | — | **SELECT, UPDATE (active) (1.16 — disabling on return only)** | — |
| invitations | SELECT, INSERT, UPDATE (status) | — | **INSERT (Amendment 01 — the `owner` invitation in `create-tenant`, §5.4)**, SELECT, UPDATE (status) | — |
| auth_attempts | — | SELECT, INSERT | — | — |
| **password_reset_tokens (Amendment 01)** | — | **SELECT, INSERT, UPDATE (used_at)** | — | — |
| audit_log | SELECT (**conditional on `scope_all` — 1.9, Section 7**), INSERT — **no UPDATE/DELETE for any application role** | — | INSERT | — |
| roles | SELECT, INSERT, UPDATE, DELETE (within RLS; **and system-seeded `is_system` roles are immune via a restrictive database policy — 1.8, Section 5**) | — | SELECT, **INSERT (1.13 — base roles only, bootstrap)** | — |
| role_permissions | SELECT, INSERT, DELETE | — | **INSERT (1.13 — bootstrap)** | — |
| tenant_modules | SELECT, UPDATE (is_active) | — | INSERT | — |
| modules (global catalog) | SELECT only — writes reserved for migrator (seeding) | — | SELECT | — |
| permissions (global catalog) | SELECT only — writes reserved for migrator (seeding) | — | SELECT | — |
| role_templates (global catalog — 1.13) | — | — | SELECT only — writes reserved for migrator (seeding) | — |
| role_template_permissions (global catalog — 1.13) | — | — | SELECT only — writes reserved for migrator (seeding) | — |
| **plans (global catalog — Amendment 01)** | **SELECT only — writes reserved for migrator (seeding)** | — | **SELECT** | **SELECT** |
| **platform_subscriptions (Amendment 01)** | **SELECT** (with `core.billing.read` on the path) | — | **INSERT, SELECT, UPDATE (status, paid_until, period_start, plan_code)** | — |
| **provider_accounts (Amendment 01)** | **SELECT** — writes reserved for migrator (seeding) | — | — | — |
| **tenant_notification_settings (Amendment 01)** | **SELECT, INSERT, UPDATE (is_enabled, reminder_days_before, sender_display_name)** | — | **INSERT** (initializing the module at bootstrap) | — |
| **notification_log (Amendment 01)** | **SELECT, INSERT, UPDATE (status, provider_message_id, error, sent_at)** — **no DELETE**; the status only moves forward (outbox) | — | — | — |
| module tables | SELECT, INSERT, UPDATE, DELETE (within RLS) | — | — | — |

### Justification of the new rows

- **`provisioner` on `tenants` (SELECT, UPDATE status):** suspending, and restoring a suspended tenant, through the operations procedure (§5.4), one tenant per transaction, with its policies on the row `id = app.tenant_id` (§4.1). SELECT because `UPDATE … WHERE` reads the row. This surface is the third path named in §0 (amends §4.4/3 and §4.4/5).
- **`provisioner` on `invitations` (INSERT):** `create-tenant` writes an `owner` invitation for the new tenant, with `invited_by IS NULL` — a bootstrap invitation with no human inviter (§2.11), never a session user. The tenant has no active owner until the invitation is accepted (amends §3.10 item a — §0).
- **`job_runner` on `plans` (SELECT):** reading a global catalog. Decision 1 removed `job_runner`'s writes, not its reads; and a later job reading the catalog does not reopen the matrix.
- **`authenticator` on `password_reset_tokens`:** recovery starts with no session, and it is the only role that works without a session. **This amends §4.3-a/2 and §4.3-a/4** (§0); it does not add to them. The write is confined to the token itself; it does not touch the password — the update happens as `app_user` in the context of the token's owner, through the existing `password_self_update` (the §4.3-b exception, §0).
- **`app_user` on `provider_accounts` (SELECT only):** the background job runs per tenant as `app_user` (1.16 §8) and needs to know which provider. No secrets in the table (§2.6).
- **`notification_log` (outbox):** the job writes the row `pending` and closes the transaction; the send step updates it to `sent|failed` in a short transaction. No DELETE; a restrictive policy allows the transition `pending → sent|failed` only. A retry = a new row.
- **`provisioner` on `tenant_notification_settings` (INSERT):** it initializes the default settings with bootstrap, as it initializes the roles from the templates. No SELECT: it does not need to read them.

**What is deliberately not granted:** `app_user` does not write `platform_subscriptions` (no tenant extends its own subscription); `job_runner` writes nothing — its 1.16 role as it stands, its only new item reading the `plans` catalog; no new role.

## §4 New policies and new permissions

### 4.1 Policies outside the two templates (they enter the manifest for Check 2 when built)

Every policy with explicit `FOR` and `TO` and `NULLIF` on `current_setting`, and the text below is the text extracted into the manifest verbatim. The two tables that enter the manifest with a policy outside the templates (`tenant_notification_settings`, `notification_log`) state their templates explicitly, because a manifest table's policy set is checked whole.

```sql
-- ===== authenticator — the recovery tokens (§2.2) =====
CREATE POLICY password_reset_tokens_authenticator_select ON password_reset_tokens
  FOR SELECT TO authenticator
  USING (true);   -- the role sees every token; the match is by the hash in the query

CREATE POLICY password_reset_tokens_authenticator_insert ON password_reset_tokens
  FOR INSERT TO authenticator
  WITH CHECK (true);

CREATE POLICY password_reset_tokens_authenticator_use ON password_reset_tokens
  FOR UPDATE TO authenticator
  USING (used_at IS NULL)
  WITH CHECK (used_at IS NOT NULL);   -- the only write is consumption

-- ===== the catalog (§2.3) =====
CREATE POLICY plans_read ON plans
  FOR SELECT TO app_user, provisioner, job_runner
  USING (true);

-- ===== platform_subscriptions (§2.4) =====
CREATE POLICY platform_subscriptions_read ON platform_subscriptions
  FOR SELECT TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));   -- the template's condition, FOR SELECT alone: app_user is granted SELECT only

CREATE POLICY platform_subscriptions_provisioner_insert ON platform_subscriptions
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY platform_subscriptions_provisioner_select ON platform_subscriptions
  FOR SELECT TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY platform_subscriptions_provisioner_update ON platform_subscriptions
  FOR UPDATE TO provisioner
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

-- ===== provisioner — the operations procedure (§5.4) =====
CREATE POLICY tenants_provisioner_select ON tenants
  FOR SELECT TO provisioner
  USING (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenants_provisioner_update ON tenants
  FOR UPDATE TO provisioner
  USING (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY invitations_provisioner_insert ON invitations
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              AND status = 'pending'
              AND invited_by IS NULL);   -- a bootstrap invitation: the provisioner path forges no inviter (§2.11)

-- ===== delivery (§2.5–§2.7) =====
CREATE POLICY provider_accounts_read ON provider_accounts
  FOR SELECT TO app_user
  USING (tenant_id IS NULL
         OR tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));   -- the platform's row, or the current tenant's (§2.6)

CREATE POLICY tenant_isolation ON tenant_notification_settings
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenant_notification_settings_provisioner_insert ON tenant_notification_settings
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY tenant_isolation ON notification_log
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY client_scope ON notification_log
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = notification_log.tenant_id
        AND sa.scope_ref_id  = notification_log.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active))
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = notification_log.tenant_id
        AND sa.scope_ref_id  = notification_log.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));

CREATE POLICY notification_log_status_forward ON notification_log
  AS RESTRICTIVE FOR UPDATE TO app_user
  USING (status = 'pending')
  WITH CHECK (status IN ('sent', 'failed'));   -- the status only moves forward: pending → sent|failed
```

**Enforcement:** `checks/core/generate-manifest.py` reads 1.16, then its amendments in the order of their numbers, and requires the text of every `CREATE POLICY` to match between the Arabic and the translation (comments aside); the English text is what enters the manifest. **A policy from this section enters the manifest when it is built:** the code PR that creates it adds its name (`table.policy`) to `checks/core/amendment-policies-built.json` and regenerates the manifest in the same PR; what is not built yet is reported as "pending" and required by neither Check 2 nor Check 3. And a name that enters that file never leaves it except by an explicit project-owner decision, recorded in the PR. The local check `check-manifest-extraction` refuses a manifest that does not match the extraction.

### 4.2 New permissions (to §5)

| Code | Module | Meaning | Templates |
| --- | --- | --- | --- |
| `core.billing.read` | core | Reading the platform subscription of the current tenant | owner, admin |
| `subscriptions.manage_settings` | subscriptions | Changing the notification channels and their timing | owner, admin |
| `subscriptions.clients.write` | subscriptions | Creating/editing a client | owner, admin, operator |
| `subscriptions.clients.manage` | subscriptions | Disabling a client and enabling it again | owner, admin |
| `subscriptions.notifications.read` | subscriptions | Reading the delivery log | owner, admin, operator, viewer |

Inserted by the same recipe as the guide's §11: the permission, the templates, upgrading the existing tenants' roles, a row-count check — in one migration.

### 4.3 What stays without a new permission

`subscriptions.write` covers creating a subscription, renewing it and cancelling it. Password recovery and change have no permission — an identity path, not a tenant path.

## §5 The paths: endpoints, background jobs, the operations procedure

### 5.1 New endpoints (to the guide's §14 reference)

| Path | Kind | Permission | Note |
| --- | --- | --- | --- |
| `POST /api/auth/password/forgot` | own UoW (authenticator) | — | Always 204; rate limit (§5.2); email through the sender. The 204 is uniform; the response time is not (OPEN_ITEMS 51) |
| `POST /api/auth/password/reset` | own UoW ×2 | — | UoW-1 authenticator verifies the token and consumes it; UoW-2 app_user (`app.user_id` = the token's owner — the named exception to §4.3-b, §0) updates the hash and changes `security_stamp`; no cookie is issued |
| `PUT /api/me/password` | own UoW ×2 | session | Verifying the current one (authenticator), then the update (app_user); the cookie is issued again with the new stamp |
| `GET /api/tenant/subscription` | normal | `core.billing.read` | The plan, the status, `paid_until` |
| `GET/PUT /api/subscriptions/settings` | module | `subscriptions.read` / `subscriptions.manage_settings` | The tenant's channels and their timing |
| `POST /api/subscriptions/clients`, `PUT …/{id}` | module | `subscriptions.clients.write` | Client: create, edit |
| `PUT /api/subscriptions/clients/{id}/status` | module | `subscriptions.clients.manage` | Client: disable, enable again |
| `POST /api/subscriptions/{id}/renew`, `POST …/{id}/cancel` | module | `subscriptions.write` | Renewal is a new subscription with `renewed_from_id` |
| `GET /api/subscriptions/notifications` | module | `subscriptions.notifications.read` | ScopedList |

No path reads `provider_accounts` or writes `platform_subscriptions` from the API.

### 5.2 Login — two changes

- **`security_stamp`** enters the cookie; `UnitOfWorkMiddleware` compares it on every request with `users` (under `user_self_read`). A mismatch → 401 `session_expired` (a new code, the §14 recipe).
- **Attempt limiting** — in the direction recorded in OPEN_ITEMS 16, and no other: `Authenticator` reads the failed `auth_attempts` for the same (`username` + `ip`) and delays the response by an **escalating delay**, **with no account lockout** (a lockout lets anyone lock out a known user), with a **ceiling per `ip`** above which it refuses with 429 `too_many_attempts` **before** reading the password. The numbers (the delays, the windows, the ceiling) [—] are settled at launch. The same mechanism for `forgot`, by (the email + `ip`).

### 5.3 Background jobs (to §8)

| Job | The role, then the context | What it does |
| --- | --- | --- |
| `ExpiringSubscriptionsJob` (existing, extended) | job_runner lists the tenants → app_user per tenant in a system context | Computes what is due for each `reminder_days_before`, writes `notification_log` with status `pending`, and closes the transaction; a second step sends, and moves each row to `sent`/`failed` in a short transaction (outbox — §5.5); a channel failing = a `failed` row, and it does not stop the others; a tenant failing does not stop the rest |

**Suspension is not a background job:** it is the `suspend-unpaid` command of the operations procedure, as `provisioner` (§5.4). `job_runner` stays in its 1.16 role: it reads the active tenants and does not write.

### 5.4 The operations procedure (D2)

`src/Ops` — a project carrying the `provisioner` and `job_runner` strings alone, with no HTTP path. **It is the third path for `provisioner` (amends §4.4/3 and §4.4/5 — §0):** one tenant per transaction, with `app.tenant_id` set to it, and every command that writes records an `audit_log` entry in its own transaction, like any `provisioner` operation (§4.4/4); what does not write records nothing. Its commands:

- `create-tenant --name --owner-email [--plan trial]`: bootstrap from the templates + `platform_subscriptions` + the default `tenant_notification_settings` + an `owner` invitation by email (`invited_by IS NULL` — a bootstrap invitation with no human inviter, §2.11). The owner accepts in person; and the tenant has no active owner until the acceptance (§0, §3.10 item a).
- `mark-paid --tenant --until [--plan]`: updates `paid_until`, and `platform_subscriptions.status` to `active`, and `tenants.status` to `active`. The only way to restore a suspended tenant.
- `suspend-unpaid`: scheduled from outside the platform. Lists the active tenants as `job_runner` through `tenants_for_jobs` (read only — its 1.16 role), then for each tenant a separate `provisioner` transaction: `paid_until + grace_days < (now() AT TIME ZONE 'UTC')::date` (§2.4) → `platform_subscriptions.status = suspended` and `tenants.status = suspended`, two critical writes (`CriticalWrite`) under the §4.1 policies. A tenant failing does not stop the rest.
- `announce-migration`: run once **after the cutover (M3)**, not before it and not during the import. Sends every migrated user (`users.legacy_id IS NOT NULL`) a plain announcement email carrying a link to the "forgot password" page (§6/7). Read only — `persons` and `users` under `provisioner`'s existing grants — and no writing; and the sender and its settings (`credential_ref`) are in `src/Ops`, not in `src/Ops.Import`.
- `src/Ops` refuses to run if it finds a `migrator` string in its environment (the same guard as Api). The import command is in a separate project, `src/Ops.Import`, which carries `migrator` alone (§6).

Suspension takes effect immediately, by the nature of 1.16: `POST /tenants/{id}/select` refuses the suspended tenant on the next request.

### 5.5 The sender

`INotificationSender` in `Core` (not in the module — invitations and passwords use it). One implementation per channel; the choice from `provider_accounts`, then `credential_ref` from the settings. **The outbox pattern (decision of 2026-10-17):** no sending inside the computing transaction. (1) The job computes, writes `notification_log` with status `pending`, and closes the transaction — the UNIQUE prevents a duplicate row here. (2) The send step, in the same run, reads `pending`, calls the provider with a 5 s timeout, and updates the row to `sent` (with `provider_message_id`) or `failed` (with `error`) in a short transaction per row. A `pending` row left after the run = a retry in the next run, not a new row. Invitations and passwords use the sender directly (one request, not a batch).

**Delivery at least once (at-least-once), not exactly once:** the UNIQUE prevents a duplicate **row**, not a duplicate **send** — if the provider accepted the message and `sent` was not saved, the row stayed `pending` and was sent again in the next run. And the status update is a critical write (`CriticalWrite`): zero rows affected = an error thrown, not a silent success.

## §6 Migration from the live system — binding rules

An application of the conditions of 1.16 §3.4 (the `BYPASSRLS` exception) to the only import case allowed. **The import is a command in a separate project, `src/Ops.Import`, as `migrator` — not a numbered migration:** a numbered migration runs in CI, in dev and in every environment, so it would have needed production data in the repository.

**The three conditions of 1.16 §3.4 on this command:**

- **Condition 1 — the `migrator` connection for a migration tool only:** the `import-legacy` command in `src/Ops.Import` is a data-migration tool for a single purpose documented here; it carries the `migrator` string alone, and neither Api, nor Worker, nor `src/Ops` reaches it (amends the `migrator` row of §3.4 — §0).
- **Condition 2 — the row counts:** item 4 below.
- **Condition 3 — the two rejected alternatives:** (a) **a numbered migration in `src/Migrations`** — rejected: it runs in every environment, so either production data in the repository or a migration that fails in CI. (b) **the import through the `provisioner` path, tenant by tenant** — rejected: `provisioner` has no grant on the module's tables (`clients`, `subscriptions`, `notification_log`) nor on `legacy_id`, and opening them to it widens its permanent surface for the sake of a single run; and `migrator` is confined to a tool that runs once.

1. **Data, not code** (D1). The input is a CSV/JSON export from the live system, given to the command from outside the repository; no production data in the repository. The command runs on production once, after the dry run (8).
2. **Every key and timestamp from the application:** written explicitly in every row; no `DEFAULT` and no `RETURNING`; `legacy_id` is kept for matching.
3. **A fixed map:** a live account → `tenants` + `platform_subscriptions`(active, `paid_until` = [—]) + `tenant_notification_settings`; a live user → `persons` + `users` (a new `security_stamp`) + `user_password_credentials` (in a "no password" state — §2.12 — so `password_self_update` stays valid and `provisioner` needs no new grant) + `memberships`(**active directly, with no invitation** — §0, the consent principle) + `membership_roles`(the owner is named explicitly in the export file, not inferred from the order: [—]; the others' roles [—]) + `membership_scope`(`all` unless otherwise specified — a missing row is a loud error in 1.16); a live client → `clients`; a live subscription → `subscriptions`(active).
4. **It ends with row-count checks** for every table against the export, and with a check: for every `subscriptions` row, a `clients` row with the same `tenant_id` (the composite FK guarantees it, and the check names it).
5. **Auditing** per D4: **no `audit_log` entry for the import** — `migrator` is not an audit writer (1.16 §7). The record is the row-count report the command writes outside the database (item 4), and `legacy_id` for tracing.
6. **No sending during the migration:** `notification_log` is seeded with `sent` rows for what the old system sent in the last [30] days — otherwise the platform would send a second reminder on the first night. This row is the only guard against duplication.
7. **Logging in after the migration:** **the import sends nothing**, issues no tokens, and does not call `forgot`: it creates the "no password" row (item 3, §2.12) and stops. The announcement is a later, separate step: the `announce-migration` command in `src/Ops` (§5.4), after the cutover (M3) alone — so no email arrives while the old system is still the live one, and `src/Ops.Import` carries no provider settings. It sends every migrated user a plain announcement email carrying a link to the "forgot password" page, and each user starts the path themselves (§5.1) — the same token and the same lifetime; no new kind of token and no new lifetime. So the announcement does not reach the `ip` ceiling of §5.2, and the token's lifetime (30 min) starts from the user's own request, not from the migration. Then they set it with `POST /api/auth/password/reset`, which updates the `user_password_credentials` row created in item 3. No invitation (the membership exists, so no collision with `UNIQUE (tenant_id, user_id)`), and no hashes are carried over.
8. **The dry run is mandatory**, on a copy of the production data before the cutover, and the "due within 30 days" report matches the old system row for row.
9. **`legacy_id` is dropped** by a later migration after the old system is switched off; its remaining is an error.

## §7 Explicitly outside the amendment, and the open questions

### Outside the amendment (nothing is built under it)

| Item | Needs |
| --- | --- |
| Invoices, payments, PDF, proration, dunning | Amendment 02, with a new webhook write surface |
| Payment gateways (local, Stripe) — implementing `IPaymentGateway` | Amendment 02 + settling the contracting entity and the currency |
| Public self-registration | With billing |
| `provider_accounts` rows for a tenant (the office's number) | A product decision + a write path |
| Module activation (25), the home of the scoped entity (§9) | With the second module |
| Brands/domains/the partner | A separate amendment |
| SSO, step-up (35), the customer portal | An actual request |
| Removing the server-rendered `GET /subscriptions/screen` (29) | A separate decision |

### The six questions — answered and approved 2026-10-17

1. **D4** — auditing the migrated rows: one entry per tenant, or per row?
2. **Lifetimes and limits** (all marked assumptions): the recovery token 30 min; the login limit 10 attempts/15 min; the provider timeout 5 s; the default `grace_days` [7]; `trial` [30] days.
3. **The templates**: `core.billing.read` for the admin too, or for the owner alone?
4. **`subscriptions.clients.write` for the operator**: does it create clients, or write subscriptions only?
5. **Sending inside the transaction** (§5.5): is it acceptable for COMMIT to grow by the provider's timeout? The alternative (outbox + a send job) is sounder in engineering terms and heavier to build — it was proposed for Amendment 02 if a problem appears.
6. **`provider_accounts` for the platform**: seeded by migrator (fixed in the migration), or by the operations procedure?

**The approved answers:**

1. **D4** (amended 2026-10-17; it was one entry per tenant): no `audit_log` entry for the import; the row-count report outside the database, and `legacy_id` for tracing (§6/5).
2. **The limits:** 30 min for the token; 5 s for the provider; `grace_days` 7; `trial` 30 days — all settings, not constants. **The login limit** (amended 2026-10-17; it was 10 attempts/15 min): in the direction recorded in OPEN_ITEMS 16, and its numbers are settled at launch (§5.2).
3. **`core.billing.read`:** owner and admin.
4. **The operator** creates and edits clients (`clients.write`); disabling is `clients.manage`, for owner/admin.
5. **Outbox** instead of sending inside the transaction (§5.5, §3, §4.1 updated).
6. **`provider_accounts` for the platform** is seeded by migrator, with the catalogs.

### What follows the amendment's approval

- A docs PR: `docs/PLATFORM_CORE_AMENDMENT_01.md` + `_EN.md`, the authority line in `CLAUDE.md`, the `OPEN_ITEMS.md` items with their numbers in §1.
- Then the code PRs, in the roadmap's order, each on its own branch, and every migration regenerates `docs/schema.sql` and updates `DATABASE.md`.
