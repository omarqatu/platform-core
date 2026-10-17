# PLATFORM_CORE — Amendment 02: The reminder engine

> **This file is a translation.** The authoritative text is the Arabic, `docs/PLATFORM_CORE_AMENDMENT_02.md`; where
> the two differ, the Arabic governs. Section numbers, table rows and marked assumptions follow the Arabic exactly.

**Status:** Approved by the project owner — 2026-10-17. Attached to PLATFORM_CORE v1.16 (frozen) and Amendment 01: it adds to them, and amends only the paragraphs of them named in §0. `_EN.md` is a translation; the Arabic governs.

## §0 Status and authority

**In force:** from the date this file is merged. The corresponding items in `OPEN_ITEMS.md`: 65–73.

**What it attaches to:** PLATFORM_CORE v1.16 + Amendment 01. It adds to them, and amends only the paragraphs listed below. The chain of authority becomes: 1.16 + Amendment 01 + Amendment 02 → PROOF_SPEC v1.3 → the code → the guides.

**What it withdraws from Amendment 01 — the project owner's decision of 2026-10-17:**

| The paragraph | Its new state |
| --- | --- |
| Amendment 01 §6 in full (the migration from the live system) | **Withdrawn.** No automated import: the data is entered by hand through the interface (4 clients, 25 subscriptions). With it fall `src/Ops.Import`, the `import-legacy` command, the dry run, the `legacy_id` column (§2.10), and the seeding of `notification_log` with `sent` rows |
| Amendment 01 §0, the `migrator` row from 1.16 §3.4 | **Returns to what it was in 1.16:** "migrations only". The amendment existed for the import command alone, and that has fallen |
| Amendment 01 §5.4, the `announce-migration` command | **Withdrawn.** There are no migrated users; the only user is created by a `create-tenant` invitation and accepts it in person |
| Amendment 01 §2.12, the "no password" state | **Withdrawn.** It existed for migrated users alone, and there are none. The only user is created by a `create-tenant` invitation and sets their password on acceptance. **And the marked assumption falls unproven** — nothing is built on it any more |
| Amendment 01 §1 D4 (auditing the migrated rows) | **Withdrawn.** There is no import, so there are no migrated rows to audit — governing text with no subject. And `migrator` did not become a fourth audit writer and will not (1.16 §7 as it stands). Manual entry goes through the `app_user` path and is audited automatically, which is better than the import could have given |
| Amendment 01 §1 D1 (succession), the decision | **Stands, and its description is corrected:** the live system is not SYSTEM_DESIGN 2.0 (TypeScript/PG16) as described, but **an Excel workbook + n8n + GreenAPI**. The succession itself does not change; the switch-off becomes disabling a workflow in n8n |
| Amendment 01 §2.5, `tenant_notification_settings` | **Stays, and becomes the default** that the client's own setting overrides (§3.3) |

**What it does not touch:** everything Amendment 01 §0 fixed — the tenant as the unit of isolation, the five roles, the two templates, the composite FKs, key generation in the application, the consent principle, the ten checks and their inputs, and `NULLIF`.

**A substantive correction to the data model (§1):** Amendment 01 §6/3 wrote "each live account = a tenant". That is wrong: the three accounts in the live system are **clients**, not tenants. The truth: **one tenant** (Al-Amin for Technologies), **four clients** inside it, and one `owner` (the project owner) as **one member**.

**Two structural corrections to the text of this amendment, by the project owner's decision of 2026-10-17 after a first review:** its first draft defined `categories`/`subcategories` with a `tenant_id NULL` column, and defined `client_notification_settings` with a `client_id` column. The first breaks the rule of 1.16 §3.3 (Check 7 is structural: any FK whose two ends carry `tenant_id` must be composite — and a composite FK toward a `tenant_id IS NULL` row never matches, so the platform's categories become unusable). The second declares `client_scope` on a table that does not carry the column the template reads, so the policy is never created and Check 8 never sees it. What is approved here is the corrected text (§3.1, §3.3), and items 65 and 66 in `OPEN_ITEMS.md`.

## §1 The inventory — what was extracted from the live system

Its source is the actual working file (the sheets `clients`, `settings_client`, `subscriptions`, `log_notifications`, `settings_global`) as of 2026-10-17. This closes the six inventory questions in `ROADMAP_SUBSCRIPTIONS.md` M1, and every `[—]` in Amendment 01 tied to them.

| The question | The answer from the live system |
| --- | --- |
| The tenant's owner | The project owner alone. One tenant, one member with the `owner` role, scope mode `all` |
| The other users' roles | There are no other users |
| The channels and their providers | **Email** (Gmail) and **WhatsApp** (GreenAPI). Telegram exists to alert **the administrator** (`telegram_admin_chat_id`), not the clients |
| The reminder schedule | 1, 7, 14, 30, 60 days before expiry (`reminder_intervals`), and after expiry a repeat every 24 hours for up to 30 days (`expired_repeat_hours`, `max_expired_days`) |
| `paid_until` for the tenant | A year from the cutover — the tenant is the project owner himself, plan `trial`, and the field carries no value that settles until the billing settles |
| The window for seeding `notification_log` | **Moot** — there is no import (§0). And `dedup_window_days = 45` in the live system is recorded here as a reference, not as a setting to be migrated |

### The project owner's recorded decisions (2026-10-17)

| # | The decision |
| --- | --- |
| E1 | **Per-client settings** — `settings_client` in the live system is an existing feature, and it is not lost in the move (§3.3) |
| E2 | **The send window and the time zone are to be designed** — they exist in no document until now (§4) |
| E3 | **Classification is two independent columns:** a category and a subcategory, a closed catalog the platform manages (§3.1). **A tenant adding its own categories is deferred** — see §3.1 and item 67 |
| E4 | **The subscription's financial and operational fields are to be built:** the provider, the cost, the currency, the billing cycle, auto-renewal, the login link (§3.2) |
| E5 | **The notice states the expense when there is one** — a reminder with no amount is not a reminder, and the cost is **optional**: in the live system the `cloudflare` subscription (management) costs zero and still needs a reminder |
| E6 | **The engine is general: a reminder by date**, and the subscription is one kind of it (§2). Appointments (a clinic, maintenance) wait for a real request, and the door is open with no retrofit |
| E7 | **No import:** entry is by hand, and §6 of Amendment 01 is withdrawn (§0) |
| E8 | **Test data is synthetic** — the same shape as the file, not its content. Real client data does not enter the repository |

### Notes from the file, left to the implementation and not to the design

- **Ambiguous dates:** 13 rows marked `Ambiguous date format - Please review`. They are settled at manual entry, not carried over as they are.
- **Letter-case duplication:** `Monthly` and `monthly` in the same column — which is exactly what the closed catalog in E3 and §3.2 prevents.
- **A message a minute:** 14 messages (2026-09-15) to the same recipient about seven subscriptions expiring together — the reason the groups are deferred (§6), not cancelled.

## §2 The general engine: a reminder by date

**Decision E6.** What was built in Amendment 01 is not specific to subscriptions: the scheduling, the channels, the de-duplication, the delivery log — all of it is a **reminder-by-date engine**. And the subscription is one kind of it — the outbox pattern.

**Why not force everything into one entity:** a subscription and an appointment behave differently, and mixing them gives empty columns and an ambiguous meaning.

| | The subscription | The appointment |
| --- | --- | --- |
| The date | expires and is renewed | falls due and passes |
| What follows it | a renewal extends the same thing | a new, separate appointment |
| Repetition | periodic by nature | may be one-off |
| The amount | usually yes, and optional (E5) | usually not |
| On expiry | "expired — renew" | "missed" |

**What is built now:** a `reminder_type` column on the entity being reminded about, with one seeded value: `subscription`. And the financial fields (`cost`, `currency`, `billing_cycle`, `auto_renew`) are **optional** at the level of `subscription` — so a kind that carries no amount needs no schema change when it comes.

**What is not built now:** the appointment entity, and no second value in `reminder_type`. The single value is not a premature abstraction — it is one column that prevents reshaping the tables later (§6).

**And its effect on naming:** the `Modules.Subscriptions` module keeps its name at this stage; renaming it to "reminders" is a product decision taken when a second paid kind exists, not before.

**Where what this amendment adds lives:** everything in it lives in `Modules.Subscriptions`, and nothing is moved to `Core`. The rule the project follows — no horizontal is built until two paid verticals ask for it — applies here: we have one vertical, so moving anything into the core today is guesswork, while extracting it later from a module that works is work of known bounds. Excepted is what is already in `Core` by an earlier decision: `INotificationSender` and `provider_accounts` (Amendment 01 §5.5, §2.6). **The candidates for extraction when a second vertical exists:** the classification, the client settings, the send window and its time zone, and the home of `clients` (§9, open since 1.16). This is to be read as a deferred decision and not an oversight, and its answer comes from the second vertical and not from our guess today.

**E5's effect on the message:** the reminder's text states the amount and the currency when the cost exists and is greater than zero, and drops them with no trace when it does not. This is a condition on the data, not on the template — and the templates are settled with the message texts in the delivery PR.

## §3 The new tables and columns

Every table carrying `tenant_id` follows the standard template, and one carrying `scope_ref_id` follows both. Keys are `app_id` and timestamps `app_ts`. The names are provisional until the migration.

### 3.1 The classification — a global catalog the platform manages (E3, corrected)

```sql
categories(id, code, name_ar, name_en, is_active, created_at,
  UNIQUE (code))
subcategories(id, category_id → categories(id), code, name_ar, name_en, is_active, created_at,
  UNIQUE (category_id, code))
-- No tenant_id column on either: a global catalog in the same pattern as plans, modules and permissions
-- (Amendment 01 §2.3, 1.16 §5). Seeded by migrator, read by everyone.
-- The platform's seeded categories (decision of 2026-10-17): domains, hosting, security, software, backup, ai, other.
-- hosting covers servers and shared hosting together; and other prevents inventing a category for every odd case, and without it the report dies.
-- And the subcategory: a category needs one when it describes a detail (VPS, Dedicated…), so it is optional.
-- The subcategory is optional: a domain needs no detail, and servers do.
```

**Why not `tenant_id NULL` — a correction to the first draft:** the first draft defined both tables with a `tenant_id NULL` column, in the `provider_accounts` pattern (Amendment 01 §2.6), so the platform's categories would sit in the `NULL` rows and a tenant would add its own beside them. That falls foul of the rule of 1.16 §3.3: `subscriptions` carries `tenant_id`, so if `categories` carried it too the FK between them would be between two `tenant_id` tables, and **Check 7 is structural** — it does not look at whether the column is nullable — so it demands a composite key; and a composite key from a row with a non-null `tenant_id` toward a row with `tenant_id IS NULL` never matches under MATCH SIMPLE, so the platform's seven categories become unreferenceable from any subscription. And an exception to §3.3 for the sake of a classification table costs far more than it is worth. The answer is the same global-catalog pattern `plans` follows: **no `tenant_id` at all**, so Check 7 does not apply in the first place.

**And its consequence: E3 is built in part, and its second part is deferred explicitly.** E3 reads "a closed catalog the platform manages **with the tenant able to add its own categories**". What is built here is the first part alone. **A tenant adding its own categories is deferred** until we know its shape — a separate table for the tenant's categories, most likely, and not a `tenant_id` column on this one. The seven categories serve the first vertical, and the shape of what a tenant adds is decided by the first real request. **This is to be read as a deferral and not an oversight** (item 67), and it is not built today on a guess at its shape.

### 3.2 Columns on `subscriptions` (E4, E5, E6)

| The column | Note |
| --- | --- |
| `reminder_type` | `NOT NULL`, one seeded value: `subscription` (§2) |
| `category_id` | `NOT NULL` → `categories(id)` — a single-column key, and `categories` carries no `tenant_id`, so Check 7 does not apply |
| `subcategory_id NULL` | → `subcategories(id)` |
| `provider` | text — the provider being paid |
| `cost NULL`, `currency NULL` | **optional** (E5). `cost` accepts zero |
| `billing_cycle` | a closed catalog: `monthly`, `yearly` — no free text (§1, the letter-case duplication) |
| `auto_renew` | `NOT NULL` — it tells a "renew" message apart from a "you will be charged automatically" one |
| `login_url NULL` | the service's management link |

**The `NOT NULL` columns on a table that has rows** follow the rule of Amendment 01 §2.1: a backfill inside the same migration, then a row-count check.

### 3.3 `client_notification_settings` — overriding the tenant's setting (E1, corrected)

```sql
client_notification_settings(id, tenant_id, scope_ref_id NOT NULL, channel, is_enabled,
  reminder_days_before int[] NULL, language NULL, tone NULL,
  send_days NULL, send_from NULL, send_to NULL, timezone NULL, do_not_disturb,
  created_at, updated_at,
  FOREIGN KEY (tenant_id, scope_ref_id) REFERENCES clients (tenant_id, id),
  UNIQUE (tenant_id, scope_ref_id, channel))
-- Both templates: tenant_isolation + client_scope. scope_ref_id = the client's id, in the same pattern as
-- subscriptions and notification_log (Amendment 01 §2.7, 1.16 §4.8) — not a client_id column.
-- Every NULL column means "take the tenant's value" — the override is partial, not total.
-- do_not_disturb NOT NULL: no sending to this client at all (C004 in the live system).
```

**Why `scope_ref_id` and not `client_id` — a correction to the first draft:** the first draft named the column `client_id` and wrote in its comment "`client_scope` (scope_ref_id = client_id)". The `client_scope` template in 1.16 §4.8 reads `<t>.scope_ref_id` literally, so the policy declared in §5.1 refers to a column that does not exist and is never created; and **Check 8 does not catch it**, because it demands the template on the tables that **carry** the column, and this one does not — so it passes silently. The practical result: an `operator` in `assigned` mode reads and edits the settings of any client in the tenant. The precedent in the schema is clear: `subscriptions_client_fkey FOREIGN KEY (tenant_id, scope_ref_id) REFERENCES clients(tenant_id, id)`, which is what the text above follows. The Check 8 gap itself is a separate item (item 66).

**Resolution at send time:** the client's setting first, then the tenant's (`tenant_notification_settings`), then the seeded default in `provider_accounts`. Three layers, in the same pattern as Amendment 01 §2.6.

### 3.4 `tone` and `language`

`tone ∈ {formal, friendly}` and `language ∈ {ar, en}`, in the same shape at the tenant level and the client level alike. The same override rule. They decide which message template is used, not the text of the message itself.

## §4 The send window and the time zone (E2)

**The problem this section solves:** Amendment 01 designed the job to run **once a day** and send everything that is due. And the live system sends between 09:00 and 20:00, Sunday–Thursday, in `Asia/Amman`. A reminder arriving at three in the morning loses a client.

### 4.1 The timing is not `paid_until` — and here it differs from UTC

Amendment 01 §2.4 settled `paid_until` in UTC, and that is right: an absolute due date. The **send window**, though, is local by its nature — "09:00 Amman time" is not "09:00 Riyadh time". So the time zone is stored **with the setting** (§3.3), and the calculation is done in it for each client.

And the storage: `timezone` an IANA zone name (`Asia/Amman`), and `send_from`/`send_to` two times with no zone (`time`). Not a fixed offset — the offset changes with daylight saving and the name does not.

### 4.2 The job runs every hour, not once a day

`ExpiringSubscriptionsJob` (Amendment 01 §5.3) changes its schedule: a run every hour, and in each run it sends **only to whoever entered their window in this hour**. The schedule stays external as it is (`DEVELOPER_GUIDE` §12).

And this does not break de-duplication: the key `UNIQUE (tenant_id, subscription_id, channel, reminder_day)` in Amendment 01 §2.7 works on the **day** of the planned send, so the eleven o'clock run does not resend what the ten o'clock one sent. And `pending` rows from an earlier run are picked up by the next, as designed (outbox).

**And `reminder_day` in which zone?** In the client's, not UTC — otherwise "today" splits at UTC midnight in the middle of the client's day. This is a constraint on how the column is computed, and it is proven on PostgreSQL 18 in the code PR.

### 4.3 Operational robustness — five constraints that enter the build (decision of 2026-10-17)

An external review on 2026-10-17 pointed out that running every hour moves the burden onto the constraints rather than the code. The five items below are **not extra quality**: each one prevents the service stopping, the number being blocked, or a reminder being lost, so they enter with the delivery in stage (d) and are not deferred.

**(1) An administrator alert on failure — it completes `notification_log` and adds nothing to it.** An outbox with a `failed` state was built, and nothing reads it: a log with no reader is half a mechanism. And the live system is like it: the last `whatsapp failed` row in its log sat `failed` and nobody knew. **The minimum form:** every run ends with a tally (`X sent, Y failed`), and when `Y > 0` the administrator is alerted on their channel (`telegram_admin_chat_id` exists in the live system) — Telegram or email. No dashboard and no escalation.

**The three marked assumptions in this section have their own items** — the spacing (71), the retry ceiling (72), and the daylight-saving proof (73): **stage (d) is not built before they are settled.**

**(2) Spacing between messages to the same recipient.** On 2026-09-15 the live system sent 14 messages in one minute to the same number. That is behaviour a number gets blocked for — a **stoppage** of the service, not an annoyance. On unofficial WhatsApp (GreenAPI) especially. **The minimum:** a gap of [2 s — an assumption] between two consecutive sends to the same recipient on the same channel. And this is not the grouping (§6): the grouping improves the experience, and the spacing prevents the block.

**(3) One run at a time.** The `UNIQUE` does not protect against this: a run that overruns meets the next hour's run, and the `pending` row is picked up twice. **The minimum:** an advisory lock at the database level (`pg_try_advisory_lock`); a run that finds the lock taken withdraws and records it. It prevents a duplicate row, not a parallel run, and the `pending` row stays and is retried in the next run (Amendment 01 §5.5).

**(4) A ceiling on retries.** A `pending` row is retried in every run — and that becomes 24 attempts a day **with no ceiling** for the same message when a provider is down, which is exactly what a number gets blocked for in (2). **The minimum:** an `attempt_count` column on `notification_log`, an escalating gap between attempts, and a ceiling of [5 — an assumption] after which the status becomes `failed` for good and the alert in (1) fires.

**(5) Daylight saving, before the build.** On the night the clocks change an hour repeats or is removed, so a client's window is seen twice or not at all — a duplicated or a missed reminder, twice a year in the zones that transition. **A factual note:** `Asia/Amman` is **not** one of them — Jordan has been fixed at UTC+3 since 2022, with no transitions. But the zone is a per-client setting, and not every client will be in Jordan. **What is proven on PostgreSQL 18 before this is adopted:** computing `reminder_day` in the zone of a client that does transition (`Europe/Berlin`, say) across both transitions, and the behaviour of `send_from`/`send_to` in the repeated hour and the removed one. The proof is written against a zone where it matters, not against one where it does not.

**A correction to the external review's argument:** it said "an error in computing the day breaks the `UNIQUE` constraint, so duplicate alerts are sent". That is backwards: the constraint is not broken by a computation error — if the computation yields a different day the constraint is simply not violated and prevents nothing. The constraint works as it was designed; the fault is in the input, and that is what item (5) addresses. And the real danger is the overlap, which is what (3) addresses.

### 4.4 Send days and do-not-disturb

- `send_days`: a set of weekdays (the default: Sunday–Thursday). What falls due on a day outside it is not sent — **it waits for the first allowed day and is not dropped**.
- `do_not_disturb`: no sending to this client at all; and no `pending` row is written for them in the first place.
- **An expired subscription** (`expired_repeat_hours = 24` in the live system): it follows the same window — a daily repeat inside working hours up to `max_expired_days`, then it stops.

**The tenant's default numbers** (seeded when the module is enabled, from the live system): the channels `email, whatsapp`; the schedule `1, 7, 14, 30, 60`; the window `09:00–20:00`; the days Sunday–Thursday; the zone `Asia/Amman`; the expired repeat every 24 hours for up to 30 days.

## §5 Grants, policies and permissions

They are added to the matrix of Amendment 01 §3, and carried into `checks/core/grants.json` in the code PR. And no new role.

| The table | app_user | authenticator | provisioner | job_runner |
| --- | --- | --- | --- | --- |
| `categories` (a global catalog) | SELECT only — writes reserved for migrator (seeding) | — | SELECT | SELECT |
| `subcategories` (a global catalog) | SELECT only — writes reserved for migrator (seeding) | — | SELECT | SELECT |
| `client_notification_settings` | SELECT, INSERT, UPDATE, DELETE | — | — | — |

As with the other catalogs (`plans`, `modules`, `permissions`): the rows of `categories` and `subcategories` are seeded by `migrator`; no application role writes them. This is what changed from the first draft, which granted `app_user` writes on both for the sake of a tenant's own categories — and those are deferred (§3.1).

### 5.1 Policies outside the two templates

The code that creates them records them in `checks/core/amendment-policies-built.json` under `"02"`, and they enter the manifest (Amendment 01 §4.1 and item 50).

```sql
-- ===== the catalog (§3.1) =====
CREATE POLICY categories_read ON categories
  FOR SELECT TO app_user, provisioner, job_runner
  USING (true);   -- a global catalog with no tenant_id, in the same text as plans_read (Amendment 01 §4.1)

CREATE POLICY subcategories_read ON subcategories
  FOR SELECT TO app_user, provisioner, job_runner
  USING (true);

-- ===== the client's settings (§3.3) =====
CREATE POLICY tenant_isolation ON client_notification_settings
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY client_scope ON client_notification_settings
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = client_notification_settings.tenant_id
        AND sa.scope_ref_id  = client_notification_settings.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active))
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = client_notification_settings.tenant_id
        AND sa.scope_ref_id  = client_notification_settings.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));
```

**Why `categories_read` is inside the template this time:** the first draft wrote a condition outside the template (`tenant_id IS NULL OR tenant_id = NULLIF(app.tenant_id)`), in the `provider_accounts_read` pattern. Once `tenant_id` fell from the table (§3.1) the condition became `USING (true)` literally, exactly as `plans_read` — a global catalog, with no exception and nothing to explain.

### 5.2 New permissions

| The code | The module | The meaning | The templates |
| --- | --- | --- | --- |
| `subscriptions.clients.settings` | subscriptions | Editing a client's notification settings | owner, admin, operator |

`subscriptions.read` covers reading: reading the categories and the client's settings needs no new permission. And entry is under the existing `subscriptions.write`. The recipe: the guide's §11, in one migration.

**A note on `operator`:** they edit the settings of their assigned clients alone — that is enforced by `client_scope` (§5.1), not by the rule and not by the code.

**And a permission dropped from the first draft:** it carried `subscriptions.categories.manage` (adding and editing a tenant's categories). It fell with the deferral of the second part of E3 (§3.1): no application role writes a global catalog, so the permission governs nothing. It returns with the separate table if that is built (item 67).

## §6 What is deferred explicitly

Three items were discussed and their idea adopted, and they are not built in this stage. The columns they need are built in §3, so there is no retrofit when they come.

| The item | Why it is deferred | What opens it |
| --- | --- | --- |
| **Appointments** (a clinic, maintenance) as a second reminder kind | No real request. `reminder_type` exists with one value, so adding the kind does not reshape the tables (§2) | The first client from outside the technical sector who asks for it |
| **Groups** — one reminder for subscriptions that expire together | Their operational danger (the number being blocked) is addressed by the spacing in §4.3 (2), and what remains is an improvement to the experience. And grouping changes the de-duplication key and the template together | After one reminder works in production |
| **The expense report** — what the subscriptions cost monthly, and their spread across providers | The columns are built now (§3.2), and the report is a screen and an aggregation. And it is **the strongest thing the product has**, so it is not built on data that has not been entered yet | After the manual entry and a first month of use |
| **A tenant adding its own categories** (the second part of E3) | Its shape is unknown: a separate table, most likely, and not a `tenant_id` on a global catalog (§3.1) | The first tenant that asks for a category outside the seven |

**And what stays outside Amendments 01 and 02 together** (as Amendment 01 §7 states): the billing engine and its gateways, public self-registration, SSO, the customer portal, brands and partners, and §9, the home of the scoped entity.

**A standing warning from the roadmap (M4):** the dashboard of metrics aggregated across tenants still has no read surface in either document, and it needs a decision and an amendment before it is built. **The expense report is not one of them:** it is inside a single tenant, under `tenant_isolation`, and it does not cross tenants.

## §7 The effect on the documents

### What changes when this amendment is adopted

| The file | The change |
| --- | --- |
| `PLATFORM_CORE_AMENDMENT_01.md` + `_EN` | §6 is marked **withdrawn** with a pointer here; the `migrator` row leaves the §0 table; `announce-migration` leaves §5.4; `legacy_id` (§2.10) is removed; and the description of the live system is corrected in D1 |
| `ROADMAP_SUBSCRIPTIONS.md` | M1: the inventory questions close with their answers (§1), and the completion criterion gains the send window. M3 is rewritten: no import, no dry run, no seeding — manual entry and disabling a workflow in n8n. And "the three accounts" is corrected to one tenant and four clients wherever it appears |
| `OPEN_ITEMS.md` | New items for what §6 defers and for the two corrections in §0; and the Amendment 01 items tied to the import close as **withdrawn**, not as done |
| `CLAUDE.md` | The chain of authority becomes 1.16 + both amendments |
| `DEVELOPER_GUIDE.md` | §12 (the job hourly, not daily), and §10 and §15 at build time — in the code PR, not here |

### The build order — settled 2026-10-17

(a) The classification and the subscription's fields **with their screens**, (b) client CRUD, renewal and cancellation **with their screens**, (c) the tenant's and the client's settings, (d) the delivery with its window and its five constraints (§4.3). Each one a branch and a PR, and **no tables without a screen**: a PR whose effect cannot be seen cannot really be tested.

**Entering your data by hand** happens after (b) — by then the client and subscription screens exist. And the notification settings are set after (c).

### A note on the order

M1 is wider than the roadmap estimated: what was "closing the module's gap" now carries the classification, the settings and the window. And that is what the roadmap itself warned of in its risks — "the inventory reveals more functions than we expected" — and it has. And none of this delays anything: showing it in the roadmap is more honest than hiding it.
